using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using log4net;

using ACE.Common;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Player Traders (owner 2026-10-28, plan C:\AI\Trader_Plan_2026-10-28.md): /trader. One trader per account, placed where its
    /// owner stands; it stays there through logouts and restarts until it is moved or put away (14 days without a login on the
    /// account puts it away, stock kept). Its stock is the owner's real items in the trader's inventory, each with a TraderPrice in
    /// the trader's currency - Enlightened Coins or Luminance (1 = trader_luminance_unit), taken from the buyer's BANK and paid into
    /// the seller's bank, online or offline. The owner stocks it through the vendor window's Sell tab (nothing is paid) and takes
    /// items back by "buying" them (free). Every buy asks Yes / No first.
    ///
    /// Rules that keep items from doubling: a trader exists once in memory (Instances; a second copy a landblock makes while the
    /// first is live is a ghost - it sells nothing, saves nothing and leaves); an item moves as the same object (never a copy);
    /// a trader is never Destroy()ed. Everything that touches a trader runs on the world queue.
    /// </summary>
    public static class TraderManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The trader NPC weenie (owner item range 7399xxxxx).</summary>
        public const uint TraderWcid = 739999978;
        /// <summary>"Billion Luminance": the currency shown in a Luminance trader's window (never made as an item).</summary>
        public const uint LumTokenWcid = 777730010;
        public const uint EnlightenedCoinWcid = 300004;

        public const int CurrencyCoins = 1;
        public const int CurrencyLuminance = 2;

        /// <summary>The most items a trader holds (a container's item count is a byte).</summary>
        public const int MaxItems = 255;
        /// <summary>A buyer must stand this close to the trader when they say Yes.</summary>
        private const float BuyRange = 10f;

        private static readonly object Sync = new();
        private static readonly Dictionary<uint, PlayerTrader> Instances = new();

        public static void Say(Player player, string text) => player.SendMessage("[Trader] " + text);

        public static string CurrencyName(int currency) => currency == CurrencyLuminance ? "Billion Luminance" : "Enlightened Coins";

        // ---- the trader objects ----------------------------------------------------------------------------------------------

        /// <summary>Every PlayerTrader registers here when it is made. False = a copy of a trader that is live in the world right
        /// now: the new copy is a ghost (PlayerTrader.BecomeGhost). A copy whose first one is out of the world (its landblock
        /// unloaded, or put away) replaces it.</summary>
        public static bool Track(PlayerTrader trader)
        {
            lock (Sync)
            {
                if (Instances.TryGetValue(trader.Guid.Full, out var other) && other != trader && !other.IsDestroyed && other.CurrentLandblock != null)
                {
                    log.Error($"[TRADER] a second copy of trader 0x{trader.Guid.Full:X8} was made while the first is in the world - the second is dropped");
                    return false;
                }
                Instances[trader.Guid.Full] = trader;
                return true;
            }
        }

        private static PlayerTrader InMemoryFor(uint accountId)
        {
            lock (Sync)
                return Instances.Values.FirstOrDefault(t => !t.IsGhost && t.OwnerAccount == accountId);
        }

        /// <summary>The account's trader: the copy in memory, else the one in the database (loaded, out of the world), else null.
        /// <paramref name="then"/> runs on the world queue.</summary>
        private static void FindTrader(uint accountId, Action<PlayerTrader> then)
        {
            var mine = InMemoryFor(accountId);
            if (mine != null)
            {
                then(mine);
                return;
            }
            Task.Run(() =>
            {
                Biota biota = null;
                uint? id = null;
                try
                {
                    using var ctx = new ShardDbContext();
                    id = ctx.BiotaPropertiesInt64.Where(r => r.Type == (ushort)PropertyInt64.TraderAccount && r.Value == accountId)
                        .Select(r => (uint?)r.ObjectId).FirstOrDefault();
                    bool inMemory;
                    lock (Sync)
                        inMemory = id.HasValue && Instances.ContainsKey(id.Value);
                    if (id.HasValue && !inMemory)
                        biota = DatabaseManager.Shard.BaseDatabase.GetBiota(id.Value);
                }
                catch (Exception e)
                {
                    log.Error($"[TRADER] lookup for account {accountId} failed: {e}");
                }
                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vendor_Approach, () =>
                {
                    PlayerTrader trader = null;
                    if (id.HasValue)
                    {
                        lock (Sync)
                            Instances.TryGetValue(id.Value, out trader);
                        if (trader == null && biota != null)
                            trader = WorldObjectFactory.CreateWorldObject(biota) as PlayerTrader;
                    }
                    then(trader);
                }));
            });
        }

        private static bool CanUse(Player player)
        {
            if (!ServerConfig.trader_enabled.Value) { Say(player, "Traders are closed right now."); return false; }
            if (player.Account == null || player.CurrentLandblock == null || player.Location == null) { Say(player, "Not from here."); return false; }
            if (player.Teleporting) { Say(player, "Wait until you have arrived."); return false; }
            return true;
        }

        // ---- /trader place, summon, dismiss ----------------------------------------------------------------------------------

        /// <summary>/trader place [coins|lum]: a new trader where the player stands (one per account - an existing one is moved here).</summary>
        public static void Place(Player player, int currency)
        {
            if (!CanUse(player))
                return;
            var accountId = player.Account.AccountId;
            FindTrader(accountId, existing =>
            {
                if (player.Session == null || player.CurrentLandblock == null)
                    return;
                if (existing != null)
                {
                    Say(player, "Your account already has a trader - it comes to you now (/trader currency changes what it sells for).");
                    MoveTo(player, existing);
                    return;
                }
                var trader = WorldObjectFactory.CreateNewWorldObject(TraderWcid) as PlayerTrader;
                if (trader == null)
                {
                    log.Error($"[TRADER] weenie {TraderWcid} is missing or not a vendor - no trader to place");
                    Say(player, "Traders are not set up on this server yet.");
                    return;
                }
                trader.OwnerCharacter = player.Guid.Full;
                trader.OwnerAccount = accountId;
                trader.Currency = currency;
                trader.Name = $"{player.Name}'s Trader";
                log.Info($"[TRADER] {player.Name} (account {accountId}) placed a new trader 0x{trader.Guid.Full:X8} selling for {CurrencyName(currency)}");
                MoveTo(player, trader);
            });
        }

        /// <summary>/trader summon: the account's trader comes to where the player stands.</summary>
        public static void Summon(Player player)
        {
            if (!CanUse(player))
                return;
            FindTrader(player.Account.AccountId, trader =>
            {
                if (player.Session == null || player.CurrentLandblock == null)
                    return;
                if (trader == null)
                {
                    Say(player, "You have no trader yet - /trader place sets one down here.");
                    return;
                }
                MoveTo(player, trader);
            });
        }

        /// <summary>/trader dismiss: the trader leaves the world; its stock stays with it (/trader summon brings it back).</summary>
        public static void Dismiss(Player player)
        {
            if (player.Account == null)
                return;
            FindTrader(player.Account.AccountId, trader =>
            {
                if (trader == null || trader.Location == null)
                {
                    Say(player, "Your trader is not out.");
                    return;
                }
                PutAway(trader, $"dismissed by {player.Name}");
                Say(player, "Your trader has packed up. Its stock is kept - /trader summon brings it back.");
            });
        }

        private static void MoveTo(Player player, PlayerTrader trader)
        {
            if (trader.IsGhost)
                return;
            var spot = player.Location.InFrontOf(2.0f, true);   // facing the player
            spot.LandblockId = new LandblockId(spot.GetCell(), spot.Variation);

            if (trader.CurrentLandblock != null)
            {
                // out of its old spot first; the removal is applied on the landblock's next tick
                trader.CurrentLandblock.RemoveWorldObject(trader.Guid, false);
                var chain = new ActionChain();
                chain.AddDelaySeconds(1.0);
                chain.AddAction(WorldManager.ActionQueue, ActionType.Vendor_Approach, () => EnterAt(player, trader, spot));
                chain.EnqueueChain();
            }
            else
                EnterAt(player, trader, spot);
        }

        private static void EnterAt(Player player, PlayerTrader trader, Position spot)
        {
            trader.Location = spot;
            trader.Ethereal = true;   // never blocks a door (owner 10-28) - before EnterWorld: the create message is built from it
            if (!trader.EnterWorld())
            {
                trader.Location = null;
                trader.SaveBiotaToDatabase();
                Say(player, "Your trader cannot stand here. Try another spot.");
                return;
            }
            // InitPhysicsObj reads the physics state at EnterWorld; set again and send it (Storage lesson, TEST 10-28)
            trader.Ethereal = true;
            trader.EnqueueBroadcastPhysicsState();
            trader.SaveBiotaToDatabase();
            log.Info($"[TRADER] trader 0x{trader.Guid.Full:X8} ({trader.Name}) set down by {player.Name} at {spot}, {trader.Inventory.Count} item(s)");
            Say(player, $"Your trader is here, selling for {CurrencyName(trader.Currency)}. Open it and use the Sell tab to stock it; /trader price sets prices.");
        }

        /// <summary>Out of the world, saved with no location (no landblock spawns it); the stock stays in it.</summary>
        private static void PutAway(PlayerTrader trader, string why)
        {
            log.Info($"[TRADER] trader 0x{trader.Guid.Full:X8} ({trader.Name}) put away ({why}), {trader.Inventory.Count} item(s)");
            trader.CurrentLandblock?.RemoveWorldObject(trader.Guid, false);
            trader.Location = null;
            trader.SaveBiotaToDatabase();
        }

        /// <summary>PlayerTrader.Destroy: never. The trader is put away instead (stock kept) and the attempt is logged.</summary>
        public static void RefuseDestroy(PlayerTrader trader, bool fromLandblockUnload)
        {
            log.Error($"[TRADER] refused to destroy trader 0x{trader.Guid.Full:X8} ({trader.Name}, {trader.Inventory.Count} item(s))" +
                $"{(fromLandblockUnload ? " on landblock unload" : "")} - put away instead{Environment.NewLine}{Environment.StackTrace}");
            PutAway(trader, "a Destroy was refused");
        }

        // ---- idle accounts (owner 10-28: put away after 14 days without a login) ---------------------------------------------

        /// <summary>A trader whose account has had no login for trader_dismiss_days is put away. Checked when a landblock loads it
        /// and every hour while it stands.</summary>
        public static void CheckIdle(PlayerTrader trader)
        {
            if (trader.IsGhost || trader.IsDestroyed || trader.CurrentLandblock == null)
                return;

            var players = Player.GetAccountPlayers((uint)trader.OwnerAccount);
            var online = players.Any(p => p is Player);
            var lastLogin = players.Select(p => p.GetProperty(PropertyFloat.LoginTimestamp) ?? 0).DefaultIfEmpty(0).Max();
            var idleDays = (Time.GetUnixTime() - lastLogin) / 86400.0;

            if (!online && (players.Count == 0 || idleDays > ServerConfig.trader_dismiss_days.Value))
            {
                PutAway(trader, players.Count == 0 ? "its account has no characters" : $"no login on its account for {idleDays:0.0} days");
                return;
            }

            var chain = new ActionChain();
            chain.AddDelaySeconds(3600);
            chain.AddAction(trader, ActionType.Vendor_CheckClose, () => CheckIdle(trader));
            chain.EnqueueChain();
        }

        // ---- the window ------------------------------------------------------------------------------------------------------

        /// <summary>The prices written as Value while a trader's window is built (WorldObject_Networking) - this thread only.</summary>
        [ThreadStatic] private static Dictionary<uint, int> windowPrices;

        public static int? WindowPrice(WorldObject wo)
        {
            var prices = windowPrices;
            return prices != null && prices.TryGetValue(wo.Guid.Full, out var price) ? price : null;
        }

        public static long PriceOf(WorldObject item) => item.GetProperty(PropertyInt64.TraderPrice) ?? 0;

        /// <summary>The trader's items in a fixed order (the numbers in /trader list).</summary>
        public static List<WorldObject> Stock(PlayerTrader trader) =>
            trader.Inventory.Values.OrderBy(i => i.Name).ThenBy(i => i.Guid.Full).ToList();

        /// <summary>The buyer's banked amount in the trader's currency (Luminance in units of trader_luminance_unit).</summary>
        private static long Purse(Player player, int currency)
        {
            if (currency == CurrencyLuminance)
                return (player.BankedLuminance ?? 0) / Math.Max(1, ServerConfig.trader_luminance_unit.Value);
            return player.BankedEnlightenedCoins ?? 0;
        }

        /// <summary>Use on a trader (PlayerTrader.ActOnUse): the vendor window. The owner sees every item (unpriced ones at 0) and
        /// its Sell tab stocks the trader; a buyer sees the priced items.</summary>
        public static void OpenWindow(Player player, PlayerTrader trader)
        {
            if (!ServerConfig.trader_enabled.Value) { Say(player, "Traders are closed right now."); return; }
            if (trader.IsGhost || trader.CurrentLandblock == null)
                return;
            if (player.IsBusy)
            {
                player.SendWeenieError(WeenieError.YoureTooBusy);
                return;
            }
            if (!trader.InventoryLoaded)
            {
                Say(player, $"{trader.Name} is still setting out the stock - try again in a moment.");
                return;
            }
            SendWindow(player, trader);
            trader.Rotate(player);
        }

        private static void SendWindow(Player player, PlayerTrader trader)
        {
            if (player.Session == null)
                return;
            var owner = trader.IsOwnedBy(player);
            var items = Stock(trader).Where(i => owner || PriceOf(i) > 0).ToList();

            // what appraisal finds through the window (Player.FindObject -> Vendor.TryGetItemForSale): exactly what is shown
            trader.UniqueItemsForSale.Clear();
            foreach (var item in items)
                trader.UniqueItemsForSale[item.Guid] = item;

            var currency = trader.Currency;
            var have = (uint)Math.Clamp(Purse(player, currency), 0, uint.MaxValue);

            windowPrices = items.ToDictionary(i => i.Guid.Full, i => (int)Math.Min(PriceOf(i), int.MaxValue));
            try
            {
                player.Session.Network.EnqueueSend(new GameEventTraderWindow(player.Session, trader, owner, items,
                    currency == CurrencyLuminance ? LumTokenWcid : EnlightenedCoinWcid, have, CurrencyName(currency)));
            }
            finally
            {
                windowPrices = null;
            }
            player.LastOpenedContainerId = trader.Guid;
        }

        // ---- stocking (the owner's Sell tab) ---------------------------------------------------------------------------------

        /// <summary>The window's category an item is counted in (trader_listings_per_category): its item type.</summary>
        private static ItemType Category(WorldObject item) => item.ItemType;

        public static void HandleStock(Player player, PlayerTrader trader, List<ItemProfile> profiles)
        {
            WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vendor_Approach, () => Stock(player, trader, profiles)));
        }

        private static void Stock(Player player, PlayerTrader trader, List<ItemProfile> profiles)
        {
            if (player.Session == null)
                return;
            if (!ServerConfig.trader_enabled.Value || trader.IsGhost || trader.CurrentLandblock == null || !trader.InventoryLoaded)
            {
                Say(player, "This trader cannot take stock right now.");
                player.Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(player.Session, player.Guid.Full));
                return;
            }
            if (!trader.IsOwnedBy(player))
            {
                Say(player, $"{trader.Name} buys nothing - only its owner can stock it.");
                player.Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(player.Session, player.Guid.Full));
                return;
            }

            var perCategory = (int)Math.Clamp(ServerConfig.trader_listings_per_category.Value, 1, MaxItems);
            var stocked = new List<WorldObject>();
            foreach (var profile in profiles.GroupBy(p => p.ObjectGuid).Select(g => g.First()))
            {
                var item = player.FindObject(profile.ObjectGuid, Player.SearchLocations.MyInventory, out _, out _, out var wielded);
                if (item == null || wielded)
                {
                    Say(player, "Only items in your packs can be stocked (take worn items off first).");
                    continue;
                }
                if (item is Container)
                {
                    Say(player, $"{item.Name}: packs cannot be stocked.");
                    continue;
                }
                if (item.IsAttunedOrContainsAttuned)
                {
                    Say(player, $"{item.Name} is attuned - it cannot be sold.");
                    continue;
                }
                if (item.Retained)
                {
                    Say(player, $"{item.Name} is retained - un-retain it to sell it.");
                    continue;
                }
                if (item.Stuck || !item.Guid.IsDynamic())
                {
                    Say(player, $"{item.Name} cannot be sold.");
                    continue;
                }
                if (player.IsTrading && item.IsBeingTradedOrContainsItemBeingTraded(player.ItemsInTradeWindow))
                {
                    Say(player, $"{item.Name} is in a trade window.");
                    continue;
                }
                if (trader.Inventory.Count >= MaxItems)
                {
                    Say(player, $"{trader.Name} is full ({MaxItems} items).");
                    break;
                }
                var category = Category(item);
                if (trader.Inventory.Values.Count(i => Category(i) == category) >= perCategory)
                {
                    Say(player, $"{trader.Name} already holds {perCategory} items of that kind ({category}).");
                    continue;
                }

                if (!player.TryRemoveFromInventoryWithNetworking(item.Guid, out _, Player.RemoveFromInventoryAction.SellItem))
                    continue;
                player.Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(player.Session, item, trader));
                item.RemoveProperty(PropertyInt64.TraderPrice);
                if (!trader.TryAddToInventory(item))
                {
                    log.Error($"[TRADER] {trader.Name} 0x{trader.Guid.Full:X8} would not take {item.Name} 0x{item.Guid.Full:X8} - given back to {player.Name}");
                    if (!player.TryCreateInInventoryWithNetworking(item))
                        log.Error($"[TRADER] {item.Name} 0x{item.Guid.Full:X8} could not go back to {player.Name} either - it is unhomed, check it");
                    item.SaveBiotaToDatabase();
                    continue;
                }
                item.SaveBiotaToDatabase();
                stocked.Add(item);
                log.Info($"[TRADER] {player.Name} stocked {item.Name} 0x{item.Guid.Full:X8} x{item.StackSize ?? 1} in {trader.Name} 0x{trader.Guid.Full:X8}");
            }

            if (stocked.Count > 0)
            {
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.EncumbranceVal, player.EncumbranceVal ?? 0));
                Say(player, $"Stocked {stocked.Count} item(s) - not for sale until priced: /trader list, then /trader price <n> <amount>.");
            }
            else
                player.Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(player.Session, player.Guid.Full));

            SendWindow(player, trader);
        }

        // ---- buying (Yes / No) -----------------------------------------------------------------------------------------------

        public static void HandleBuy(Player player, PlayerTrader trader, List<ItemProfile> profiles)
        {
            WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vendor_Approach, () => AskBuy(player, trader, profiles)));
        }

        private sealed record Line(WorldObject Item, long Price);

        private static void AskBuy(Player player, PlayerTrader trader, List<ItemProfile> profiles)
        {
            if (player.Session == null)
                return;
            if (!ServerConfig.trader_enabled.Value || trader.IsGhost || trader.CurrentLandblock == null)
            {
                Say(player, "This trader is closed.");
                return;
            }

            var owner = trader.IsOwnedBy(player);
            var lines = new List<Line>();
            foreach (var guid in profiles.Select(p => p.ObjectGuid).Distinct())
            {
                if (!trader.Inventory.TryGetValue(new ObjectGuid(guid), out var item))
                    continue;
                var price = PriceOf(item);
                if (!owner && price <= 0)
                    continue;
                lines.Add(new Line(item, owner ? 0 : price));
            }
            if (lines.Count == 0)
            {
                Say(player, "Those items are no longer for sale.");
                SendWindow(player, trader);
                return;
            }

            var names = string.Join(", ", lines.Select(l => (l.Item.StackSize ?? 1) > 1 ? $"{l.Item.StackSize} {l.Item.GetPluralName()}" : l.Item.Name));
            string question;
            if (owner)
                question = $"Take back {names} from {trader.Name}?";
            else
            {
                var total = lines.Sum(l => l.Price);
                question = $"Buy {names} for {total:N0} {CurrencyName(trader.Currency)} from {trader.Name}?\n(paid from your bank: you have {Purse(player, trader.Currency):N0})";
            }

            var confirmation = new Confirmation_Custom(player.Guid, (yes, timedOut) =>
                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vendor_Approach, () =>
                {
                    if (player.Session == null)
                        return;
                    if (yes && !timedOut)
                        FinishBuy(player, trader, lines, owner);
                    else if (trader.CurrentLandblock != null)
                        SendWindow(player, trader);
                })));

            if (!player.ConfirmationManager.EnqueueSend(confirmation, question))
                Say(player, "Answer the question you already have open first.");
        }

        private static void FinishBuy(Player player, PlayerTrader trader, List<Line> lines, bool owner)
        {
            // everything again: the world may have moved on while the question was open
            if (trader.IsGhost || trader.CurrentLandblock == null || player.CurrentLandblock == null || player.Location == null || trader.Location == null
                || player.Location.Variation != trader.Location.Variation || player.Location.DistanceTo(trader.Location) > BuyRange)
            {
                Say(player, $"You are no longer at {trader.Name}.");
                return;
            }
            if (owner != trader.IsOwnedBy(player))
                return;
            foreach (var line in lines)
            {
                if (!trader.Inventory.ContainsKey(line.Item.Guid) || (!owner && PriceOf(line.Item) != line.Price))
                {
                    Say(player, $"{line.Item.Name} has been sold or repriced - nothing was bought.");
                    SendWindow(player, trader);
                    return;
                }
            }
            if (owner)
            {
                var taken = 0;
                foreach (var line in lines)
                    if (MoveToBuyer(player, trader, line.Item))
                        taken++;
                Say(player, taken == lines.Count ? $"Took back {taken} item(s)." : $"Took back {taken} of {lines.Count} item(s) - your packs are full.");
                trader.SaveBiotaToDatabase();
                SendWindow(player, trader);
                return;
            }

            var currency = trader.Currency;
            var seller = Payee(trader);
            if (seller == null)
            {
                Say(player, $"{trader.Name}'s owner can no longer be paid - nothing was bought.");
                return;
            }

            long total;
            try { total = checked(lines.Sum(l => l.Price)); }
            catch (OverflowException) { Say(player, "That total is too large."); return; }

            if (!Debit(player, currency, total))
            {
                Say(player, $"You do not have {total:N0} {CurrencyName(currency)} in your bank.");
                SendWindow(player, trader);
                return;
            }

            long paid = 0;
            var bought = new List<WorldObject>();
            foreach (var line in lines)
            {
                if (MoveToBuyer(player, trader, line.Item))
                {
                    paid += line.Price;
                    bought.Add(line.Item);
                }
            }
            if (paid < total)
                Credit(player, currency, total - paid);   // what did not fit in the packs is not paid for

            if (bought.Count == 0)
            {
                Say(player, "Your packs are full - nothing was bought.");
                player.SavePlayerToDatabase();
                SendWindow(player, trader);
                return;
            }

            CreditSeller(seller, currency, paid, player, bought);
            player.SavePlayerToDatabase();
            trader.SaveBiotaToDatabase();

            var names = string.Join(", ", bought.Select(i => i.Name));
            Say(player, $"Bought {names} for {paid:N0} {CurrencyName(currency)}{(bought.Count < lines.Count ? " (the rest did not fit in your packs)" : "")}.");
            foreach (var item in bought)
                TransferLogger.LogBankTransfer(player, seller.Name, item.Name, lines.First(l => l.Item == item).Price, TransferLogger.TransferTypeTraderSale);
            log.Info($"[TRADER] {player.Name} bought {string.Join(", ", bought.Select(i => $"{i.Name} 0x{i.Guid.Full:X8}"))} from {trader.Name} 0x{trader.Guid.Full:X8} " +
                $"for {paid:N0} {CurrencyName(currency)}, paid to {seller.Name}");

            SendWindow(player, trader);
        }

        /// <summary>The same object from the trader's stock into the player's packs (never a copy). False = it stays in the stock.</summary>
        private static bool MoveToBuyer(Player player, PlayerTrader trader, WorldObject item)
        {
            var price = item.GetProperty(PropertyInt64.TraderPrice);
            if (!trader.TryRemoveFromInventory(item.Guid, out _))
                return false;
            item.RemoveProperty(PropertyInt64.TraderPrice);
            trader.UniqueItemsForSale.Remove(item.Guid);
            if (player.TryCreateInInventoryWithNetworking(item))
            {
                item.SaveBiotaToDatabase();
                return true;
            }
            // no room: back in the stock as it was
            if (price.HasValue)
                item.SetProperty(PropertyInt64.TraderPrice, price.Value);
            if (!trader.TryAddToInventory(item))
                log.Error($"[TRADER] {item.Name} 0x{item.Guid.Full:X8} did not fit {player.Name}'s packs nor go back into {trader.Name} 0x{trader.Guid.Full:X8} - check it");
            item.SaveBiotaToDatabase();
            return false;
        }

        // ---- the bank ----------------------------------------------------------------------------------------------------------

        /// <summary>Who a sale pays: the character that placed the trader, else another character of its account.</summary>
        private static IPlayer Payee(PlayerTrader trader)
        {
            var placed = PlayerManager.FindByGuid(trader.OwnerCharacter);
            if (placed != null && placed.Account?.AccountId == trader.OwnerAccount)
                return placed;
            return Player.GetAccountPlayers((uint)trader.OwnerAccount).FirstOrDefault();
        }

        private static long ToBankUnits(int currency, long amount) =>
            currency == CurrencyLuminance ? checked(amount * Math.Max(1, ServerConfig.trader_luminance_unit.Value)) : amount;

        /// <summary>Takes the amount from the player's bank if it is all there (checked and taken under the bank lock).</summary>
        private static bool Debit(Player player, int currency, long amount)
        {
            long units;
            try { units = ToBankUnits(currency, amount); }
            catch (OverflowException) { return false; }

            lock (player.BalanceLock)
            {
                if (currency == CurrencyLuminance)
                {
                    var have = player.BankedLuminance ?? 0;
                    if (have < units) return false;
                    player.BankedLuminance = have - units;
                }
                else
                {
                    var have = player.BankedEnlightenedCoins ?? 0;
                    if (have < units) return false;
                    player.BankedEnlightenedCoins = have - units;
                }
            }
            return true;
        }

        private static void Credit(Player player, int currency, long amount)
        {
            var units = ToBankUnits(currency, amount);
            lock (player.BalanceLock)
            {
                if (currency == CurrencyLuminance)
                    player.BankedLuminance = checked((player.BankedLuminance ?? 0) + units);
                else
                    player.BankedEnlightenedCoins = checked((player.BankedEnlightenedCoins ?? 0) + units);
            }
        }

        private static void CreditSeller(IPlayer seller, int currency, long amount, Player buyer, List<WorldObject> bought)
        {
            var units = ToBankUnits(currency, amount);
            var what = string.Join(", ", bought.Select(i => i.Name));
            if (seller is Player online)
            {
                Credit(online, currency, amount);
                online.SavePlayerToDatabase();
                Say(online, $"{buyer.Name} bought {what} for {amount:N0} {CurrencyName(currency)} - paid into your bank.");
            }
            else if (seller is OfflinePlayer offline)
            {
                if (currency == CurrencyLuminance)
                    offline.BankedLuminance = checked((offline.BankedLuminance ?? 0) + units);
                else
                    offline.BankedEnlightenedCoins = checked((offline.BankedEnlightenedCoins ?? 0) + units);
                offline.SaveBiotaToDatabase();
            }
        }

        // ---- owner commands --------------------------------------------------------------------------------------------------

        /// <summary>The account's trader for an owner command, loaded with its stock - else the player has been told why not.</summary>
        public static void WithOwnTrader(Player player, Action<PlayerTrader> then)
        {
            if (player.Account == null)
                return;
            FindTrader(player.Account.AccountId, trader =>
            {
                if (player.Session == null)
                    return;
                if (trader == null)
                {
                    Say(player, "You have no trader yet - /trader place sets one down where you stand.");
                    return;
                }
                if (!trader.InventoryLoaded)
                {
                    Say(player, "Your trader is still setting out its stock - try again in a moment.");
                    return;
                }
                then(trader);
            });
        }

        public static void List(Player player, PlayerTrader trader)
        {
            var stock = Stock(trader);
            var where = trader.Location != null ? trader.Location.ToString() : "packed away (/trader summon)";
            Say(player, $"{trader.Name}: {stock.Count}/{MaxItems} items, sells for {CurrencyName(trader.Currency)}, {where}");
            for (var n = 0; n < stock.Count; n++)
            {
                var item = stock[n];
                var price = PriceOf(item);
                Say(player, $"{n + 1}. {item.Name}{((item.StackSize ?? 1) > 1 ? $" x{item.StackSize}" : "")} - {(price > 0 ? $"{price:N0}" : "not for sale")} | item 0x{item.Guid.Full:X8}");
            }
        }

        /// <summary>The items a price / unprice / take names: "all", a list number, or none (the item last appraised).</summary>
        public static List<WorldObject> Pick(Player player, PlayerTrader trader, string which)
        {
            var stock = Stock(trader);
            if (string.Equals(which, "all", StringComparison.OrdinalIgnoreCase))
                return stock;
            if (string.IsNullOrEmpty(which))
            {
                var target = player.CurrentAppraisalTarget;
                var item = target.HasValue ? stock.FirstOrDefault(i => i.Guid.Full == target.Value) : null;
                if (item == null)
                    Say(player, "Appraise one of your trader's items first, or give its number from /trader list.");
                return item == null ? new List<WorldObject>() : new List<WorldObject> { item };
            }
            if (int.TryParse(which, out var n) && n >= 1 && n <= stock.Count)
                return new List<WorldObject> { stock[n - 1] };
            Say(player, $"No item {which} - see /trader list.");
            return new List<WorldObject>();
        }

        public static void SetPrice(Player player, PlayerTrader trader, List<WorldObject> items, long price)
        {
            foreach (var item in items)
            {
                if (price > 0)
                    item.SetProperty(PropertyInt64.TraderPrice, price);
                else
                    item.RemoveProperty(PropertyInt64.TraderPrice);
                item.SaveBiotaToDatabase();
            }
            if (items.Count > 0)
            {
                log.Info($"[TRADER] {player.Name} {(price > 0 ? $"priced {items.Count} item(s) at {price:N0}" : $"took {items.Count} item(s) off sale")} in {trader.Name} 0x{trader.Guid.Full:X8}");
                Say(player, price > 0
                    ? $"{(items.Count == 1 ? items[0].Name : $"{items.Count} items")}: {price:N0} {CurrencyName(trader.Currency)}{(items.Count > 1 ? " each" : "")}."
                    : $"{(items.Count == 1 ? items[0].Name : $"{items.Count} items")}: not for sale.");
            }
        }

        public static void SetCurrency(Player player, PlayerTrader trader, int currency)
        {
            if (trader.Currency == currency)
            {
                Say(player, $"Your trader already sells for {CurrencyName(currency)}.");
                return;
            }
            if (trader.Inventory.Values.Any(i => PriceOf(i) > 0))
            {
                Say(player, "Take every price off first (/trader unprice all) - a price means something else in the other currency.");
                return;
            }
            trader.Currency = currency;
            trader.SaveBiotaToDatabase();
            Say(player, $"Your trader now sells for {CurrencyName(currency)}.");
        }

        /// <summary>/trader take: items back into the owner's packs (the trader may be anywhere, even packed away).</summary>
        public static void Take(Player player, PlayerTrader trader, List<WorldObject> items)
        {
            if (trader.IsGhost)
                return;
            var taken = 0;
            foreach (var item in items)
            {
                if (!MoveToBuyer(player, trader, item))
                {
                    Say(player, "Your packs are full.");
                    break;
                }
                taken++;
                log.Info($"[TRADER] {player.Name} took back {item.Name} 0x{item.Guid.Full:X8} from {trader.Name} 0x{trader.Guid.Full:X8}");
            }
            if (taken > 0)
            {
                trader.SaveBiotaToDatabase();
                Say(player, $"Took back {taken} item(s).");
            }
        }
    }
}
