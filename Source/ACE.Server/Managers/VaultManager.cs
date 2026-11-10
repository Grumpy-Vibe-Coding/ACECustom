using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using log4net;

using ACE.Common;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.DatLoader;
using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Account Vault - players see "Storage", /storage (owner 2026-10-28; /vault is the Vaulted Dungeons roster). Plan C:\AI\Vault_Build_Plan_2026-10-28.md): pages of 100 items per account (and a few
    /// attuned pages per character), kept in the shard and placed in the world only while their owner has one open.
    ///
    /// Rules that keep items from doubling:
    /// - a page exists once in memory (Instances): once loaded it is reused until it has been closed for a while, so a reopen
    ///   never reads a page from the database while a save of it (or of an item that just left it) is still on its way;
    /// - one open page per account (OpenByAccount), opened only for a character of that account (or of that attuned page);
    /// - a page is never Destroy()ed (that deletes the row and recycles the guid): closing saves it and takes it out of the world.
    /// Everything that touches a page runs on the world queue.
    /// </summary>
    public static partial class VaultManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public enum PageKind { Account, Attuned }

        /// <summary>The Vault Page weenie (owner item range 7777xxxxx, block 77773xxxx).</summary>
        public const uint VaultPageWcid = 777730001;

        /// <summary>A page left closed this long is dropped from memory (its saves are long done); it loads again on its next open.</summary>
        private const double KeepClosedPageSeconds = 1800;

        private static readonly object Sync = new();
        private static readonly Dictionary<uint, VaultPage> Instances = new();
        private static readonly Dictionary<uint, double> ClosedAt = new();
        private static readonly Dictionary<uint, VaultPage> OpenByAccount = new();
        private static readonly Dictionary<uint, double> LastOpenByAccount = new();
        private static readonly Dictionary<uint, List<uint>> PendingGets = new();

        /// <summary>Every storage reply is a "[Storage] ..." chat line: an answer to a typed command, and readable by a client plugin.</summary>
        public static void Say(Player player, string text) => player.SendMessage("[Storage] " + text);

        // ---- seen by their player only (owner 10-28: "All ethereal, all only visible to the player that summons") ------------

        /// <summary>Object guid -> the one player whose client is sent it (Player.TrackObject). Read from the landblock threads.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, uint> PrivateTo = new();

        /// <summary>Player.TrackObject: a strongbox, the Ledger or a summoned Quartermaster is sent only to its own player (and to an
        /// admin with Adminvision). A strongbox nobody owns right now (one a landblock put back after a crash) is sent to nobody.</summary>
        public static bool HiddenFrom(Player viewer, WorldObject wo)
        {
            if (viewer.Adminvision)
                return false;
            if (PrivateTo.TryGetValue(wo.Guid.Full, out var owner))
                return owner != viewer.Guid.Full;
            return wo is VaultPage || wo is VaultIndex;
        }

        /// <summary>Before EnterWorld: the object is private to the player and ethereal (the create message is built from both).</summary>
        private static void MakePrivate(WorldObject wo, Player player)
        {
            PrivateTo[wo.Guid.Full] = player.Guid.Full;
            wo.Ethereal = true;
        }

        /// <summary>After EnterWorld: Ethereal again and sent (InitPhysicsObj reads the state at EnterWorld; setting it alone only
        /// changes the server's physics - TEST 10-28).</summary>
        private static void ResendEthereal(WorldObject wo)
        {
            wo.Ethereal = true;
            wo.EnqueueBroadcastPhysicsState();
        }

        // ---- page objects ----------------------------------------------------------------------------------------------------

        /// <summary>Every VaultPage object registers here when it is made (VaultPage ctor).</summary>
        public static void Track(VaultPage page)
        {
            lock (Sync)
            {
                if (Instances.TryGetValue(page.Biota.Id, out var other) && other != page)
                    log.Error($"[VAULT] a second copy of vault page 0x{page.Biota.Id:X8} was made - the first copy stays in use");
                else
                    Instances[page.Biota.Id] = page;
            }
        }

        private static bool IsOpenPage(VaultPage page)
        {
            lock (Sync)
                return IsOpenPageLocked(page);
        }

        private static bool IsOpenPageLocked(VaultPage page) => OpenByAccount.ContainsValue(page);

        // ---- open ------------------------------------------------------------------------------------------------------------

        /// <summary>/storage, /storage page N, /storage attuned N: open that page for the player (closing the account's open one).</summary>
        public static void OpenPage(Player player, PageKind kind, int number, uint getItem = 0, bool fromIndex = false)
        {
            if (!CanUseVault(player, out var why))
            {
                Say(player, why);
                return;
            }

            var max = kind == PageKind.Account ? ServerConfig.vault_max_pages.Value : ServerConfig.vault_max_attuned_pages.Value;
            if (number < 1 || number > max)
            {
                Say(player, kind == PageKind.Account ? $"You have strongboxes 1 to {max}." : $"You have Bound Strongboxes 1 to {max}.");
                return;
            }

            var accountId = player.Account.AccountId;
            var now = Time.GetUnixTime();

            lock (Sync)
            {
                var sameOpen = false;
                if (OpenByAccount.TryGetValue(accountId, out var open))
                {
                    if (open.Viewer != 0 && open.Viewer != player.Guid.Full)
                    {
                        var other = PlayerManager.GetOnlinePlayer(open.Viewer)?.Name ?? "another character";
                        Say(player, $"Your storage is open on {other}. Close it there first.");
                        return;
                    }
                    sameOpen = open.VaultPageNumber == number && open.IsAttunedPage == (kind == PageKind.Attuned) && open.IsOpen;
                    if (sameOpen && getItem == 0)
                    {
                        Say(player, $"{open.Name} is already open.");
                        return;
                    }
                }

                // the gap is between page loads: taking another item off the page already open needs none
                // (a page picked from the index is one click after the index: no gap)
                if (!sameOpen && !fromIndex && LastOpenByAccount.TryGetValue(accountId, out var last) && now - last < ServerConfig.vault_open_gap_seconds.Value)
                {
                    Say(player, "Give the Quartermaster a moment between strongboxes.");
                    return;
                }
                LastOpenByAccount[accountId] = now;

                SweepClosedPages(now);
            }

            LookupPage(player, kind, number, (pageId, biota) => FinishOpen(player, kind, accountId, number, pageId, biota, getItem));
        }

        /// <summary>The page's id (and its row, when it is not in memory) on a worker thread; then <paramref name="then"/> on the world queue.</summary>
        private static void LookupPage(Player player, PageKind kind, int number, Action<uint?, Biota> then)
        {
            var accountId = player.Account.AccountId;
            var characterId = player.Guid.Full;
            Task.Run(() =>
            {
                uint? pageId = null;
                Biota biota = null;
                try
                {
                    pageId = FindPageId(kind, accountId, characterId, number);
                    bool inMemory;
                    lock (Sync)
                        inMemory = pageId.HasValue && Instances.ContainsKey(pageId.Value);
                    if (pageId.HasValue && !inMemory)
                        biota = DatabaseManager.Shard.BaseDatabase.GetBiota(pageId.Value);
                }
                catch (Exception e)
                {
                    log.Error($"[VAULT] page lookup for {player.Name} failed: {e}");
                }
                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => then(pageId, biota)));
            });
        }

        private static bool CanUseVault(Player player, out string why)
        {
            why = null;
            if (!ServerConfig.vault_enabled.Value) { why = "Storage is closed right now."; return false; }
            if (player.Account == null || player.CurrentLandblock == null) { why = "Your storage cannot be reached from here."; return false; }
            if (IsSorting(player.Account.AccountId)) { why = "The Quartermaster is sorting your strongboxes - one moment."; return false; }
            if (player.IsOlthoiPlayer) { why = "Olthoi have no use for storage."; return false; }
            if (player.Teleporting) { why = "Wait until you have arrived."; return false; }
            if (!ServerConfig.vault_anywhere.Value && FindButler(player) == null)
            {
                why = $"There is no Quartermaster within {ServerConfig.vault_butler_range.Value:0} meters.";
                return false;
            }
            return true;
        }

        private static bool IsMineOrPlaced(WorldObject qm, Player player)
        {
            lock (Sync)
                return !SummonedBy.TryGetValue(qm.Guid.Full, out var owner) || owner == player.Guid.Full;
        }

        private static WorldObject FindButler(Player player)
        {
            var range = ServerConfig.vault_butler_range.Value;

            // the player's own summoned Quartermaster - known here at once (the landblock lists it only from its next tick)
            Creature mine;
            lock (Sync)
                SummonByPlayer.TryGetValue(player.Guid.Full, out mine);
            if (mine != null && !mine.IsDestroyed && mine.Location != null && mine.Location.Variation == player.Location.Variation
                && mine.Location.DistanceTo(player.Location) <= range)
                return mine;

            return player.CurrentLandblock?.GetAllWorldObjectsForDiagnostics()
                .FirstOrDefault(o => o is Creature && !o.IsDestroyed && o.GetProperty(PropertyBool.VaultButler) == true && o.Location != null && IsMineOrPlaced(o, player)
                    && o.Location.Variation == player.Location.Variation && o.Location.DistanceTo(player.Location) <= range);
        }

        private static void FinishOpen(Player player, PageKind kind, uint accountId, int number, uint? pageId, Biota biota, uint getItem)
        {
            if (player.Session == null || player.CurrentLandblock == null || player.Location == null)
                return;   // logged out on the way

            CloseIndex(player);

            // the account's open page closes first - one open page per account
            VaultPage previous;
            lock (Sync)
                OpenByAccount.TryGetValue(accountId, out previous);
            if (previous != null)
            {
                if (previous.Viewer != 0 && previous.Viewer != player.Guid.Full)
                {
                    Say(player, "Your storage was opened on another character.");
                    return;
                }
                if (previous.VaultPageNumber == number && previous.IsAttunedPage == (kind == PageKind.Attuned) && previous.IsOpen)
                {
                    if (getItem != 0)
                        TakeItem(player, previous, getItem);
                    return;
                }
                ClosePage(previous, player);
            }

            var page = ResolvePage(player, kind, accountId, number, pageId, biota);
            if (page == null)
                return;

            page.Name = PageName(page);

            page.Location = BoxSpot(player, page);

            lock (Sync)
            {
                OpenByAccount[accountId] = page;
                ClosedAt.Remove(page.Biota.Id);
                if (getItem != 0)
                {
                    if (!PendingGets.TryGetValue(page.Biota.Id, out var list))
                        PendingGets[page.Biota.Id] = list = new List<uint>();
                    list.Add(getItem);
                }
            }
            page.PendingViewer = player;
            page.LastActivity = Time.GetUnixTime();

            MakePrivate(page, player);
            if (!page.EnterWorld())
            {
                Say(player, "Your storage could not be set down here. Try another spot.");
                Retire(page);
                return;
            }

            ResendEthereal(page);

            // the client must know the page before its contents: show it once the landblock has sent it
            var chain = new ActionChain();
            chain.AddDelaySeconds(0.75);
            chain.AddAction(WorldManager.ActionQueue, ActionType.Vault_Page, () => ShowPage(page));
            chain.EnqueueChain();
        }

        /// <summary>Where a strongbox or the Ledger is set down: vault_box_distance from the player, vault_box_angle to their right, its
        /// front facing the player. The Gauntlet Vault model (0x020016A2, read from the dat 10-29) is 1.6 m wide, 1.16 m deep (0.66 m of
        /// it toward the player), 3 m tall, with two 1.0 m collision spheres; setup Radius 1.41, a human's 0.68. At the old 1.2 m the
        /// player stood inside those spheres. The distance is capped so ACE's use check (the gap between the two bodies' setup radii,
        /// IsWithinUseRadiusOf / CreateMoveToChain) stays within the box's UseRadius with 0.25 m to spare: no walk-to on a drag.</summary>
        private static Position BoxSpot(Player player, WorldObject box)
        {
            var loc = player.Location;

            float boxRadius;
            try { boxRadius = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.SetupModel>(box.SetupTableId).Radius * (box.ObjScale ?? 1f); }
            catch { boxRadius = 1.4f; }
            var playerRadius = player.PhysicsObj?.GetRadius() ?? 0.68f;
            var reach = (box.UseRadius ?? 0.5f) + boxRadius + playerRadius - 0.25f;
            var distance = Math.Clamp((float)ServerConfig.vault_box_distance.Value, 0.5f, Math.Max(0.5f, reach));

            // turn the player's heading to the right (a positive Z turn is to the left), then step out along it; the box keeps that
            // heading, so a chest's front faces back at the player
            var angle = (float)(Math.Clamp(ServerConfig.vault_box_angle.Value, -180.0, 180.0) * Math.PI / 180.0);
            var turned = Quaternion.Normalize(new Quaternion(0, 0, loc.RotationZ, loc.RotationW) * Quaternion.CreateFromYawPitchRoll(0, 0, -angle));
            var facing = new Position(loc.LandblockId.Raw, loc.PositionX, loc.PositionY, loc.PositionZ, 0f, 0f, turned.Z, turned.W, false, loc.Variation);

            var spot = facing.InFrontOf(distance, false);
            spot.LandblockId = new LandblockId(spot.GetCell(), spot.Variation);
            return spot;
        }

        /// <summary>The page object: the copy in memory, else one made from its row, else a new page (world queue). Null = refused
        /// (the player has been told).</summary>
        private static VaultPage ResolvePage(Player player, PageKind kind, uint accountId, int number, uint? pageId, Biota biota)
        {
            VaultPage page = null;
            if (pageId.HasValue)
            {
                lock (Sync)
                    Instances.TryGetValue(pageId.Value, out page);

                if (page == null && biota != null)
                    page = WorldObjectFactory.CreateWorldObject(biota) as VaultPage;

                if (page == null)
                {
                    log.Error($"[VAULT] page 0x{pageId.Value:X8} ({kind} {number}) of {player.Name} could not be loaded");
                    Say(player, "That strongbox could not be opened. Please tell a GM.");
                    return null;
                }

                // a copy a landblock spawned after a crash: take it back out of the world before placing it here
                if (page.CurrentLandblock != null && !IsOpenPage(page))
                    page.CurrentLandblock.RemoveWorldObject(page.Guid, false);
            }
            else
            {
                page = WorldObjectFactory.CreateNewWorldObject(VaultPageWcid) as VaultPage;
                if (page == null)
                {
                    log.Error($"[VAULT] weenie {VaultPageWcid} is missing or not a Storage weenie - the vault cannot make pages");
                    Say(player, "Storage is not set up on this server yet.");
                    return null;
                }
                if (kind == PageKind.Account)
                    page.VaultAccount = accountId;
                else
                    page.VaultCharacter = player.Guid.Full;
                page.VaultPageNumber = number;
                page.SaveBiotaToDatabase();
                log.Info($"[VAULT] new {(kind == PageKind.Account ? $"account {accountId}" : $"attuned ({player.Name})")} page {number} = 0x{page.Biota.Id:X8}");
            }

            if (!page.BelongsTo(player))
            {
                log.Error($"[VAULT] {player.Name} was handed page 0x{page.Biota.Id:X8} that is not theirs - refused");
                Say(player, "That strongbox could not be opened. Please tell a GM.");
                return null;
            }

            return page;
        }

        /// <summary>"Strongbox 3", "Strongbox 3 - Bows", "Bound Strongbox 1" (owner 10-28: not "page").</summary>
        public static string PageName(VaultPage page)
        {
            var name = page.IsAttunedPage ? $"Bound Strongbox {page.VaultPageNumber}" : $"Strongbox {page.VaultPageNumber}";
            var label = page.GetProperty(PropertyString.VaultPageLabel);
            return string.IsNullOrEmpty(label) ? name : $"{name} - {label}";
        }

        /// <summary>/storage name 3 Bows (or with no label: clears it). The page is made if it does not exist yet.</summary>
        public static void NamePage(Player player, PageKind kind, int number, string label)
        {
            if (!CanUseVault(player, out var why))
            {
                Say(player, why);
                return;
            }
            var max = kind == PageKind.Account ? ServerConfig.vault_max_pages.Value : ServerConfig.vault_max_attuned_pages.Value;
            if (number < 1 || number > max)
            {
                Say(player, kind == PageKind.Account ? $"You have strongboxes 1 to {max}." : $"You have Bound Strongboxes 1 to {max}.");
                return;
            }
            label = (label ?? "").Trim();
            if (label.Length > MaxLabel || label.Any(c => c < ' ' || c > '~' || c == '|'))
            {
                Say(player, $"A strongbox name is up to {MaxLabel} plain letters, digits and spaces.");
                return;
            }
            var accountId = player.Account.AccountId;
            LookupPage(player, kind, number, (pageId, biota) =>
            {
                if (player.Session == null)
                    return;
                var page = ResolvePage(player, kind, accountId, number, pageId, biota);
                if (page == null)
                    return;
                if (label.Length == 0)
                    page.RemoveProperty(PropertyString.VaultPageLabel);
                else
                    page.SetProperty(PropertyString.VaultPageLabel, label);
                page.SaveBiotaToDatabase();
                lock (Sync)
                    if (!IsOpenPageLocked(page) && !ClosedAt.ContainsKey(page.Biota.Id))
                        ClosedAt[page.Biota.Id] = Time.GetUnixTime();   // a page loaded only to name it leaves memory like a closed one
                Say(player, label.Length == 0 ? $"{PageName(page)} has no name now." : $"Strongbox {number} is now {PageName(page)}.");
            });
        }

        public const int MaxLabel = 24;

        /// <summary>The page's items finished loading (VaultPage.OnInitialInventoryLoadCompleted).</summary>
        public static void OnPageLoaded(VaultPage page)
        {
            if (page.PendingViewer != null && page.CurrentLandblock != null)
                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => ShowPage(page)));
        }

        private static void ShowPage(VaultPage page)
        {
            var player = page.PendingViewer;
            if (player == null || page.IsOpen)
                return;
            if (!page.InventoryLoaded)
                return;   // OnPageLoaded shows it

            page.PendingViewer = null;

            if (player.Session == null || player.CurrentLandblock == null || page.CurrentLandblock == null)
            {
                Retire(page);
                return;
            }

            // a chest the player still has open closes (as Container.ActOnUse does)
            if (player.LastOpenedContainerId != ObjectGuid.Invalid && player.LastOpenedContainerId != page.Guid
                && player.CurrentLandblock.GetObject(player.LastOpenedContainerId) is Container other && other.IsOpen && other.Viewer == player.Guid.Full)
                other.Close(player);

            page.Open(player);
            page.LastActivity = Time.GetUnixTime();
            log.Info($"[VAULT] {player.Name} opened {(page.IsAttunedPage ? "attuned" : $"account {page.VaultAccount}")} page {page.VaultPageNumber} (0x{page.Biota.Id:X8}), {page.Inventory.Count} item(s)");
            player.SendMessage($"[Storage] {page.Name} open - {page.Inventory.Count} of {VaultPage.PageItems} slots used.");

            List<uint> gets = null;
            lock (Sync)
            {
                if (PendingGets.Remove(page.Biota.Id, out var list))
                    gets = list;
            }
            if (gets != null)
                foreach (var g in gets)
                    TakeItem(player, page, g);

            ScheduleWatch(page);
        }

        /// <summary>Closes a page whose viewer walked off, logged out, or moved nothing for vault_idle_close_seconds.</summary>
        private static void ScheduleWatch(VaultPage page)
        {
            var chain = new ActionChain();
            chain.AddDelaySeconds(15);
            chain.AddAction(WorldManager.ActionQueue, ActionType.Vault_Page, () =>
            {
                if (!page.IsOpen || !IsOpenPage(page))
                    return;

                var viewer = PlayerManager.GetOnlinePlayer(page.Viewer);
                var idle = Time.GetUnixTime() - page.LastActivity > ServerConfig.vault_idle_close_seconds.Value;
                var away = viewer?.Location == null || page.Location == null || viewer.Location.Variation != page.Location.Variation
                    || viewer.Location.DistanceTo(page.Location) > 15;

                if (viewer == null || idle || away)
                {
                    if (viewer != null)
                        viewer.SendMessage($"[Storage] {page.Name} closed ({(idle ? "nothing moved for a while" : "you walked away")}).");
                    ClosePage(page, viewer);
                }
                else
                    ScheduleWatch(page);
            });
            chain.EnqueueChain();
        }

        /// <summary>/storage get: the page is open for the player - move the item into their packs through the normal move (burden and
        /// free slots checked, nothing ever dropped on the ground).</summary>
        private static void TakeItem(Player player, VaultPage page, uint itemGuid)
        {
            if (!page.Inventory.ContainsKey(new ObjectGuid(itemGuid)))
            {
                Say(player, $"That item is no longer in {page.Name}.");
                return;
            }
            QueueMove(player, itemGuid, player.Guid.Full);
        }

        // A move plays the pickup motion; one asked for while the character is busy only replaces the waiting "next" move
        // (Player.NextPickup). So storage moves wait their turn here, one at a time, each through the normal move.
        private const int MaxQueuedMoves = 200;
        private static readonly Dictionary<uint, Queue<(uint Item, uint To)>> MoveQueues = new();
        private static readonly HashSet<uint> Pumping = new();

        private static void QueueMove(Player player, uint itemGuid, uint toGuid)
        {
            bool start;
            lock (Sync)
            {
                if (!MoveQueues.TryGetValue(player.Guid.Full, out var q))
                    MoveQueues[player.Guid.Full] = q = new Queue<(uint, uint)>();
                if (q.Count >= MaxQueuedMoves)
                {
                    Say(player, $"{MaxQueuedMoves} moves are already waiting - try again when they are done.");
                    return;
                }
                q.Enqueue((itemGuid, toGuid));
                start = Pumping.Add(player.Guid.Full);   // one pump per player
                if (ServerConfig.vault_debug_log.Value)
                    log.Info($"[VAULT] move queued {player.Name}: 0x{itemGuid:X8} -> 0x{toGuid:X8}, {q.Count} waiting, pump {(start ? "started" : "already running")}");
            }
            if (start)
                PumpMoves(player);
        }

        private static void PumpMoves(Player player)
        {
            (uint Item, uint To) next;
            lock (Sync)
            {
                if (!MoveQueues.TryGetValue(player.Guid.Full, out var q) || q.Count == 0 || player.Session == null || player.CurrentLandblock == null)
                {
                    MoveQueues.Remove(player.Guid.Full);
                    Pumping.Remove(player.Guid.Full);
                    return;
                }
                next = player.IsBusy ? default : q.Dequeue();
                if (ServerConfig.vault_debug_log.Value)
                    log.Info($"[VAULT] move queue {player.Name}: busy={player.IsBusy} pickup={player.PickupState} next={(next.Item == 0 ? "wait" : $"0x{next.Item:X8} -> 0x{next.To:X8}")} left={q.Count}");
            }
            if (next.Item != 0)
            {
                var to = next.To;
                if (to == player.Guid.Full)
                {
                    // a take: the main pack if it has room, else the first side pack with room (a full target would only put it back)
                    to = PackWithRoom(player);
                    if (to == 0)
                        Say(player, "Your packs are full - make room, then /storage get again.");
                }
                if (to != 0)
                    player.HandleActionPutItemInContainer(next.Item, to, 0);
            }

            var chain = new ActionChain();
            chain.AddDelaySeconds(0.3);
            chain.AddAction(player, ActionType.Vault_Page, () => PumpMoves(player));
            chain.EnqueueChain();
        }

        private static uint PackWithRoom(Player player)
        {
            if (player.GetFreeInventorySlots(false) > 0)
                return player.Guid.Full;
            foreach (var pack in player.Inventory.Values.OfType<Container>())
                if (pack.GetFreeInventorySlots(false) > 0)
                    return pack.Guid.Full;
            return 0;
        }

        /// <summary>/storage put: an item the player carries onto their open page, through the normal move (every check of a drag).</summary>
        public static void PutItem(Player player, uint itemGuid)
        {
            VaultPage page = null;
            lock (Sync)
                if (player.Account != null)
                    OpenByAccount.TryGetValue(player.Account.AccountId, out page);

            if (page == null || !page.IsOpen || page.Viewer != player.Guid.Full)
            {
                Say(player, "Open a strongbox first (/storage).");
                return;
            }
            var item = player.FindObject(itemGuid, Player.SearchLocations.MyInventory, out _, out _, out var wielded);
            if (item == null || wielded)
            {
                Say(player, "You are not carrying that item (worn items have to come off first).");
                return;
            }
            if (item is Container)
            {
                Say(player, $"Packs cannot go into storage - store what is in {item.Name} instead.");
                return;
            }
            if (item.IsAttunedOrContainsAttuned && !page.AcceptsAttunedFrom(player))
            {
                Say(player, $"{item.Name} is attuned - it can only go in your Bound Strongbox (/storage bound).");
                return;
            }
            if (page.Inventory.Count >= VaultPage.PageItems)
            {
                Say(player, $"{page.Name} is full.");
                return;
            }
            QueueMove(player, itemGuid, page.Guid.Full);
        }

        // ---- the index: a chest of page tokens -------------------------------------------------------------------------------

        public const uint PageTokenWcid = 777730002;
        public const uint IndexWcid = 777730003;

        private static readonly Dictionary<uint, VaultIndex> OpenIndexByPlayer = new();

        public static bool IsPageToken(WorldObject wo) => wo != null && wo.WeenieClassId == PageTokenWcid;

        /// <summary>The Ledger's "Sort my strongboxes" line: a token with this page number.</summary>
        private const int SortToken = -1;

        /// <summary>/storage, the NPC: a chest beside the player with one token per page (and one for the next new page).</summary>
        public static void OpenIndex(Player player)
        {
            if (!CanUseVault(player, out var why))
            {
                Say(player, why);
                return;
            }
            var accountId = player.Account.AccountId;
            var now = Time.GetUnixTime();
            lock (Sync)
            {
                if (OpenByAccount.TryGetValue(accountId, out var open) && open.Viewer != 0 && open.Viewer != player.Guid.Full)
                {
                    Say(player, $"Your storage is open on {PlayerManager.GetOnlinePlayer(open.Viewer)?.Name ?? "another character"}. Close it there first.");
                    return;
                }
                if (LastOpenByAccount.TryGetValue(accountId, out var last) && now - last < ServerConfig.vault_open_gap_seconds.Value)
                {
                    Say(player, "Give the Quartermaster a moment between strongboxes.");
                    return;
                }
                LastOpenByAccount[accountId] = now;
            }
            Query(player, (pages, items) => WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => BuildIndex(player, accountId, pages, items))));
        }

        private static void BuildIndex(Player player, uint accountId, List<PageRow> pages, List<ItemRow> items)
        {
            if (player.Session == null || player.CurrentLandblock == null || player.Location == null)
                return;

            VaultPage openPage;
            lock (Sync)
                OpenByAccount.TryGetValue(accountId, out openPage);
            if (openPage != null)
            {
                if (openPage.Viewer != 0 && openPage.Viewer != player.Guid.Full)
                {
                    Say(player, "Your storage was opened on another character.");
                    return;
                }
                ClosePage(openPage, player);
            }
            CloseIndex(player);

            var index = WorldObjectFactory.CreateNewWorldObject(IndexWcid) as VaultIndex;
            if (index == null)
            {
                log.Error($"[VAULT] weenie {IndexWcid} is missing or not a Storage weenie - no storage index");
                Say(player, "Storage is not set up on this server yet.");
                return;
            }

            // the pages there are, then the next new one of each kind (while under the cap)
            var rows = new List<(PageKind Kind, int Number, string Name)>();
            foreach (var kind in new[] { PageKind.Account, PageKind.Attuned })
            {
                var mine = pages.Where(p => p.Attuned == (kind == PageKind.Attuned)).OrderBy(p => p.Number).ToList();
                foreach (var p in mine)
                {
                    var n = items.Count(i => i.Page.Id == p.Id);
                    rows.Add((kind, p.Number, $"{(kind == PageKind.Attuned ? "Bound Strongbox" : "Strongbox")} {p.Number} - {n} item{(n == 1 ? "" : "s")}{(string.IsNullOrEmpty(p.Label) ? "" : " - " + p.Label)}"));
                }
                var max = kind == PageKind.Account ? ServerConfig.vault_max_pages.Value : ServerConfig.vault_max_attuned_pages.Value;
                var next = Enumerable.Range(1, (int)max).FirstOrDefault(n => mine.All(p => p.Number != n));
                if (next != 0)
                    rows.Add((kind, next, $"{(kind == PageKind.Attuned ? "Bound Strongbox" : "Strongbox")} {next} - new"));
            }

            // the last line of the Ledger: sort them (retail players click it, as a strongbox)
            if (pages.Any(p => !p.Attuned))
                rows.Add((PageKind.Account, SortToken, "Sort my strongboxes"));

            foreach (var r in rows.Take(VaultPage.PageItems))
            {
                var token = WorldObjectFactory.CreateNewWorldObject(PageTokenWcid);
                if (token == null)
                {
                    log.Error($"[VAULT] weenie {PageTokenWcid} is missing - no storage index");
                    Say(player, "Storage is not set up on this server yet.");
                    index.Destroy();
                    return;
                }
                token.SuppressShardPersistence = true;
                token.Name = r.Name;
                token.SetProperty(PropertyInt.VaultPageNumber, r.Number);
                if (r.Kind == PageKind.Attuned)
                    token.SetProperty(PropertyInstanceId.VaultCharacter, player.Guid.Full);
                index.TryAddToInventory(token);
            }

            index.Name = "Quartermaster's Ledger";
            index.Location = BoxSpot(player, index);
            MakePrivate(index, player);
            if (!index.EnterWorld())
            {
                Say(player, "Your storage could not be set down here. Try another spot.");
                index.Destroy();
                return;
            }
            ResendEthereal(index);
            lock (Sync)
                OpenIndexByPlayer[player.Guid.Full] = index;

            var chain = new ActionChain();
            chain.AddDelaySeconds(0.75);
            chain.AddAction(WorldManager.ActionQueue, ActionType.Vault_Page, () =>
            {
                if (index.IsDestroyed || player.Session == null)
                    return;
                index.Open(player);
                Say(player, $"Your storage: {pages.Count(p => !p.Attuned)} strongbox(es), {items.Count(i => !i.Page.Attuned)} item(s). Take a strongbox from the Ledger to open it.");
                WatchIndex(index, player);
            });
            chain.EnqueueChain();
        }

        /// <summary>Closes an index its player walked away from, left for two minutes, or logged out on.</summary>
        private static void WatchIndex(VaultIndex index, Player player)
        {
            var opened = Time.GetUnixTime();
            void Check()
            {
                if (index.IsDestroyed)
                    return;
                var away = player.Session == null || player.Location == null || index.Location == null || player.Location.Variation != index.Location.Variation
                    || player.Location.DistanceTo(index.Location) > 15;
                if (!index.IsOpen || away || Time.GetUnixTime() - opened > 120)
                {
                    CloseIndex(player);
                    return;
                }
                var again = new ActionChain();
                again.AddDelaySeconds(10);
                again.AddAction(WorldManager.ActionQueue, ActionType.Vault_Page, Check);
                again.EnqueueChain();
            }
            Check();
        }

        /// <summary>Takes the player's index away (a page opening, the watch, a new index).</summary>
        public static void CloseIndex(Player player)
        {
            VaultIndex index;
            lock (Sync)
                OpenIndexByPlayer.Remove(player.Guid.Full, out index);
            if (index == null || index.IsDestroyed)
                return;
            if (index.IsOpen)
                index.FinishClose(player.Session != null ? player : null);   // -> OnIndexClosed
            else
                OnIndexClosed(index);
        }

        /// <summary>VaultIndex.FinishClose: the index (and its tokens) are gone - it was never saved.</summary>
        public static void OnIndexClosed(VaultIndex index)
        {
            PrivateTo.TryRemove(index.Guid.Full, out _);
            lock (Sync)
                foreach (var k in OpenIndexByPlayer.Where(kv => kv.Value == index).Select(kv => kv.Key).ToList())
                    OpenIndexByPlayer.Remove(k);
            if (!index.IsDestroyed)
                index.Destroy();
        }

        /// <summary>A page token taken or double-clicked (Player_Inventory / Player_Use): open that page. Tokens never move.</summary>
        public static void OnTokenTaken(Player player, WorldObject token)
        {
            VaultIndex index;
            lock (Sync)
                OpenIndexByPlayer.TryGetValue(player.Guid.Full, out index);
            if (index == null || !index.Inventory.ContainsKey(token.Guid))
                return;
            var number = token.GetProperty(PropertyInt.VaultPageNumber) ?? 0;
            if (number == SortToken)
            {
                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => Sort(player)));
                return;
            }
            var kind = (token.GetProperty(PropertyInstanceId.VaultCharacter) ?? 0) != 0 ? PageKind.Attuned : PageKind.Account;
            WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => OpenPage(player, kind, number, 0, fromIndex: true)));
        }

        // ---- the summoned Quartermaster (owner 10-28: a command for now, anywhere, right in front of the player, ethereal) --------

        public const uint QuartermasterWcid = 739999979;
        private const float SummonWalkAwayMeters = 15;   // owner 10-28: no timer - they stay until you walk away (as an open page)

        /// <summary>Quartermaster's Bell (owner 10-28 "an item that summons, alongside the command"): using it = /storage summon
        /// (Gem.ActOnUse). Bonded + attuned. How players get one is the owner's call; for now /storage bell (admins).</summary>
        public const uint QuartermasterBellWcid = 777730004;

        /// <summary>/storage bell (admins, for testing): a Quartermaster's Bell in the pack, unless one is carried already.</summary>
        public static void GiveBell(Player player)
        {
            if (player.GetInventoryItemsOfWCID(QuartermasterBellWcid).Count > 0)
            {
                Say(player, "You already have a Quartermaster's Bell.");
                return;
            }
            var bell = WorldObjectFactory.CreateNewWorldObject(QuartermasterBellWcid);
            if (bell == null)
            {
                log.Error($"[VAULT] weenie {QuartermasterBellWcid} is missing - no Quartermaster's Bell");
                Say(player, "The Quartermaster's Bell is not on this server yet.");
                return;
            }
            if (!player.TryCreateInInventoryWithNetworking(bell))
            {
                bell.Destroy();
                Say(player, "Your packs are full - make room for the Quartermaster's Bell and try again.");
                return;
            }
            log.Info($"[VAULT] {player.Name} was given a Quartermaster's Bell 0x{bell.Guid.Full:X8}");
            Say(player, "A Quartermaster's Bell is in your pack. Use it to summon your Quartermaster, like /storage summon.");
        }

        private static readonly Dictionary<uint, Creature> SummonByPlayer = new();
        private static readonly Dictionary<uint, uint> SummonedBy = new();          // creature guid -> its player

        /// <summary>/storage summon: the player's own Quartermaster, right in front of them, until they walk away from it (no timer). One
        /// per player: a second summon nearby is told "right here"; farther off it comes to the player again.</summary>
        public static void Summon(Player player)
        {
            if (!ServerConfig.vault_enabled.Value) { Say(player, "Storage is closed right now."); return; }
            if (player.Account == null || player.CurrentLandblock == null || player.Location == null) { Say(player, "Your Quartermaster cannot reach you here."); return; }
            if (player.Teleporting) { Say(player, "Wait until you have arrived."); return; }

            Creature qm;
            lock (Sync)
                SummonByPlayer.TryGetValue(player.Guid.Full, out qm);
            if (qm != null && !qm.IsDestroyed && qm.Location != null && qm.Location.Variation == player.Location.Variation && qm.Location.DistanceTo(player.Location) <= 5)
            {
                Say(player, "Your Quartermaster is right here.");
                return;
            }
            if (qm != null)
                Dismiss(player, false);

            qm = WorldObjectFactory.CreateNewWorldObject(QuartermasterWcid) as Creature;
            if (qm == null)
            {
                log.Error($"[VAULT] weenie {QuartermasterWcid} is missing or not a creature - no Quartermaster to summon");
                Say(player, "Storage is not set up on this server yet.");
                return;
            }
            qm.SuppressShardPersistence = true;   // a summoned helper is never saved
            qm.Name = $"{player.Name}'s Quartermaster";
            var spot = player.Location.InFrontOf(2.0f, true);   // facing the player
            spot.LandblockId = new LandblockId(spot.GetCell(), spot.Variation);
            qm.Location = spot;
            MakePrivate(qm, player);   // seen by its player only, ethereal - before EnterWorld: the create message is built from it

            if (!qm.EnterWorld())
            {
                Say(player, "Your Quartermaster cannot reach you here.");
                qm.Destroy();
                return;
            }
            // as Pet.Init: InitPhysicsObj reads the physics state at EnterWorld; set again and send it - setting it alone only
            // changes the server's physics (TEST 10-28: the first create went out without Ethereal)
            ResendEthereal(qm);

            lock (Sync)
            {
                SummonByPlayer[player.Guid.Full] = qm;
                SummonedBy[qm.Guid.Full] = player.Guid.Full;
            }
            log.Info($"[VAULT] {player.Name} summoned their Quartermaster 0x{qm.Guid.Full:X8}");
            Say(player, "Your Quartermaster is here. Use them to open your storage; they stay until you walk away.");
            WatchSummon(player, qm);
        }

        /// <summary>/storage dismiss, the watch, a new summon.</summary>
        public static void Dismiss(Player player, bool tell = true)
        {
            Creature qm;
            lock (Sync)
            {
                SummonByPlayer.Remove(player.Guid.Full, out qm);
                if (qm != null)
                    SummonedBy.Remove(qm.Guid.Full);
                if (qm != null)
                    PrivateTo.TryRemove(qm.Guid.Full, out _);
            }
            if (qm == null)
            {
                if (tell)
                    Say(player, "You have no Quartermaster with you.");
                return;
            }
            if (!qm.IsDestroyed)
                qm.Destroy();   // never saved: nothing in the database to remove
            if (tell && player.Session != null)
                Say(player, "Your Quartermaster has gone.");
        }

        private static void WatchSummon(Player player, Creature qm)
        {
            var chain = new ActionChain();
            chain.AddDelaySeconds(3);
            chain.AddAction(WorldManager.ActionQueue, ActionType.Vault_Page, () =>
            {
                Creature current;
                lock (Sync)
                    SummonByPlayer.TryGetValue(player.Guid.Full, out current);
                if (current != qm || qm.IsDestroyed)
                    return;   // dismissed or replaced
                var loggedOut = player.Session == null || player.CurrentLandblock == null || player.Location == null;
                var walkedAway = !loggedOut && (qm.Location == null || player.Location.Variation != qm.Location.Variation
                    || player.Location.DistanceTo(qm.Location) > SummonWalkAwayMeters);
                if (loggedOut || walkedAway)
                {
                    if (walkedAway)
                        Say(player, "You walked away from your Quartermaster - they have gone back to the stores.");
                    Dismiss(player, false);
                    return;
                }
                WatchSummon(player, qm);
            });
            chain.EnqueueChain();
        }

        /// <summary>Creature.ActOnUse on a Quartermaster: a summoned one serves only its own player.</summary>
        public static void UseQuartermaster(Player player, Creature qm)
        {
            uint owner;
            lock (Sync)
                if (!SummonedBy.TryGetValue(qm.Guid.Full, out owner))
                    owner = 0;
            if (owner != 0 && owner != player.Guid.Full)
            {
                Say(player, $"This Quartermaster serves {PlayerManager.GetOnlinePlayer(owner)?.Name ?? "someone else"}.");
                return;
            }
            OpenIndex(player);
        }

        // ---- close -----------------------------------------------------------------------------------------------------------

        /// <summary>/vault close, a page change, the watch: close and take the page out of the world.</summary>
        public static void ClosePage(VaultPage page, Player player)
        {
            if (page.IsOpen)
                page.FinishClose(player);   // no close animation wait: the page leaves the world at once
            else
                Retire(page);
        }

        /// <summary>The page was closed (VaultPage.FinishClose: the client closed it, a logout, the watch, a page change).</summary>
        public static void OnPageClosed(VaultPage page, Player player) => Retire(page);

        private static void Retire(VaultPage page)
        {
            if (page.CurrentLandblock != null)
                log.Info($"[VAULT] page 0x{page.Biota.Id:X8} ({(page.IsAttunedPage ? "attuned" : $"account {page.VaultAccount}")} page {page.VaultPageNumber}) closed, {page.Inventory.Count} item(s)");

            lock (Sync)
            {
                foreach (var k in OpenByAccount.Where(kv => kv.Value == page).Select(kv => kv.Key).ToList())
                    OpenByAccount.Remove(k);
                PendingGets.Remove(page.Biota.Id);
                ClosedAt[page.Biota.Id] = Time.GetUnixTime();
            }
            page.PendingViewer = null;

            if (page.CurrentLandblock != null)
                page.CurrentLandblock.RemoveWorldObject(page.Guid, false);
            PrivateTo.TryRemove(page.Guid.Full, out _);

            page.Location = null;   // a page never rests in the world: no landblock spawns it
            page.SaveBiotaToDatabase();
        }

        /// <summary>VaultPage.Destroy: never. The page is put away (out of the world, saved) and the attempt is logged.</summary>
        public static void RefuseDestroy(VaultPage page, bool fromLandblockUnload)
        {
            log.Error($"[VAULT] refused to destroy storage page 0x{page.Biota.Id:X8} ({(page.IsAttunedPage ? "attuned" : $"account {page.VaultAccount}")} " +
                $"page {page.VaultPageNumber}, {page.Inventory.Count} item(s)){(fromLandblockUnload ? " on landblock unload" : "")} - put away instead{Environment.NewLine}{Environment.StackTrace}");
            if (page.IsOpen)
                page.FinishClose(PlayerManager.GetOnlinePlayer(page.Viewer));
            else
                Retire(page);
        }

        /// <summary>A page a landblock spawned from a crash-time save (VaultPage ctor): out of the world again, unless its owner opened it.</summary>
        public static void RetireStray(VaultPage page)
        {
            if (IsOpenPage(page) || page.PendingViewer != null)
                return;
            if (page.CurrentLandblock != null || page.Location != null)
            {
                log.Warn($"[VAULT] page 0x{page.Biota.Id:X8} was left in the world (server stopped while it was open) - taken back out");
                Retire(page);
            }
        }

        private static void SweepClosedPages(double now)
        {
            foreach (var id in ClosedAt.Where(kv => now - kv.Value > KeepClosedPageSeconds).Select(kv => kv.Key).ToList())
            {
                ClosedAt.Remove(id);
                if (Instances.TryGetValue(id, out var p) && !p.IsOpen && p.CurrentLandblock == null && !OpenByAccount.ContainsValue(p))
                    Instances.Remove(id);
            }
        }

        /// <summary>/storage close.</summary>
        public static void CloseFor(Player player)
        {
            VaultPage page = null;
            lock (Sync)
                if (player.Account != null)
                    OpenByAccount.TryGetValue(player.Account.AccountId, out page);

            if (page == null || page.Viewer != player.Guid.Full)
            {
                Say(player, "You have no strongbox open.");
                return;
            }
            ClosePage(page, player);
            player.SendMessage($"[Storage] {page.Name} closed.");
        }

        public static void LogMove(VaultPage page, WorldObject item, bool deposit)
        {
            if (item == null)
                return;
            var who = PlayerManager.GetOnlinePlayer(page.Viewer)?.Name ?? $"0x{page.Viewer:X8}";
            log.Info($"[VAULT] {who} {(deposit ? "stored" : "took")} {item.Name} 0x{item.Guid.Full:X8} x{item.StackSize ?? 1} " +
                $"{(deposit ? "on" : "from")} {(page.IsAttunedPage ? "attuned" : $"account {page.VaultAccount}")} page {page.VaultPageNumber} (0x{page.Biota.Id:X8})");
        }

        // ---- the database side: pages and their items without loading them ---------------------------------------------------

        public sealed record PageRow(uint Id, int Number, bool Attuned, string Label);
        public sealed record ItemRow(uint Guid, string Name, int Tier, int Stack, PageRow Page);

        private static uint? FindPageId(PageKind kind, uint accountId, uint characterId, int number)
        {
            var p = Pages(accountId, characterId).FirstOrDefault(r => r.Number == number && r.Attuned == (kind == PageKind.Attuned));
            return p?.Id;
        }

        /// <summary>The account's pages and this character's attuned pages.</summary>
        public static List<PageRow> Pages(uint accountId, uint characterId)
        {
            using var ctx = new ShardDbContext();
            var account = ctx.BiotaPropertiesInt64.Where(r => r.Type == (ushort)PropertyInt64.VaultAccount && r.Value == accountId).Select(r => r.ObjectId).ToList();
            var attuned = ctx.BiotaPropertiesIID.Where(r => r.Type == (ushort)PropertyInstanceId.VaultCharacter && r.Value == characterId).Select(r => r.ObjectId).ToList();
            var ids = account.Concat(attuned).ToList();
            var numbers = ctx.BiotaPropertiesInt.Where(r => r.Type == (ushort)PropertyInt.VaultPageNumber && ids.Contains(r.ObjectId))
                .ToDictionary(r => r.ObjectId, r => r.Value);
            var labels = ctx.BiotaPropertiesString.Where(r => r.Type == (ushort)PropertyString.VaultPageLabel && ids.Contains(r.ObjectId))
                .ToDictionary(r => r.ObjectId, r => r.Value);
            return ids.Where(numbers.ContainsKey).Select(id => new PageRow(id, numbers[id], attuned.Contains(id), labels.GetValueOrDefault(id)))
                .OrderBy(r => r.Attuned).ThenBy(r => r.Number).ToList();
        }

        /// <summary>The items of the pages that are in memory (their database rows may still be on their way) - world queue only, the
        /// pages' inventories change there. The pages left over are read from the database.</summary>
        private static List<ItemRow> MemoryItems(List<PageRow> pages, List<PageRow> notLoaded)
        {
            var rows = new List<ItemRow>();
            foreach (var p in pages)
            {
                VaultPage page;
                lock (Sync)
                    Instances.TryGetValue(p.Id, out page);
                if (page != null && page.InventoryLoaded)
                    rows.AddRange(page.Inventory.Values.Select(i => new ItemRow(i.Guid.Full, i.Name, i.GetProperty(PropertyInt.ZcTier) ?? 0, i.StackSize ?? 1, p)));
                else
                    notLoaded.Add(p);
            }
            return rows;
        }

        /// <summary>The stored items of pages that are not in memory, from the database (worker thread).</summary>
        private static List<ItemRow> DatabaseItems(List<PageRow> pages)
        {
            var rows = new List<ItemRow>();
            if (pages.Count == 0)
                return rows;

            using var ctx = new ShardDbContext();
            var pageIds = pages.Select(p => p.Id).ToList();
            var inPage = ctx.BiotaPropertiesIID.Where(r => r.Type == (ushort)PropertyInstanceId.Container && pageIds.Contains(r.Value))
                .Select(r => new { r.ObjectId, r.Value }).ToList();
            var itemIds = inPage.Select(r => r.ObjectId).ToList();
            var names = ctx.BiotaPropertiesString.Where(r => r.Type == (ushort)PropertyString.Name && itemIds.Contains(r.ObjectId)).ToDictionary(r => r.ObjectId, r => r.Value);
            var tiers = ctx.BiotaPropertiesInt.Where(r => r.Type == (ushort)PropertyInt.ZcTier && itemIds.Contains(r.ObjectId)).ToDictionary(r => r.ObjectId, r => r.Value);
            var stacks = ctx.BiotaPropertiesInt.Where(r => r.Type == (ushort)PropertyInt.StackSize && itemIds.Contains(r.ObjectId)).ToDictionary(r => r.ObjectId, r => r.Value);
            var byId = pages.ToDictionary(p => p.Id);
            foreach (var r in inPage)
                rows.Add(new ItemRow(r.ObjectId, names.GetValueOrDefault(r.ObjectId, "?"), tiers.GetValueOrDefault(r.ObjectId), stacks.GetValueOrDefault(r.ObjectId, 1), byId[r.Value]));
            return rows;
        }

        /// <summary>/storage list, find, pages, get: the page list from the database (worker), the in-memory pages' items on the world
        /// queue, the rest from the database (worker), the answer on the player's own queue.</summary>
        public static void Query(Player player, Action<List<PageRow>, List<ItemRow>> answer)
        {
            if (!CanUseVault(player, out var why))
            {
                Say(player, why);
                return;
            }
            var accountId = player.Account.AccountId;
            var characterId = player.Guid.Full;

            void Fail(Exception e) => log.Error($"[VAULT] query for {player.Name} failed: {e}");

            Task.Run(() =>
            {
                List<PageRow> pages;
                try { pages = Pages(accountId, characterId); }
                catch (Exception e) { Fail(e); return; }

                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () =>
                {
                    var notLoaded = new List<PageRow>();
                    var items = MemoryItems(pages, notLoaded);

                    Task.Run(() =>
                    {
                        try { items.AddRange(DatabaseItems(notLoaded)); }
                        catch (Exception e) { Fail(e); return; }

                        log.Info($"[VAULT] {player.Name} query: {pages.Count} page(s), {items.Count} item(s)");
                        player.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => answer(pages, items)));
                    });
                }));
            });
        }

        public static string Line(ItemRow r) =>
            $"[Storage] item 0x{r.Guid:X8} {(r.Page.Attuned ? "bound " : "")}box {r.Page.Number} tier {r.Tier} stack {r.Stack} | {r.Name}";
    }
}
