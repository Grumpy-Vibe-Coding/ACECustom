using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ACE.Common;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity.Actions;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// /storage sort (owner 10-28: "Full sort command", retail first): every item in the account's strongboxes is re-filed by
    /// category - one run of strongboxes per category, a new category starting a new strongbox, each strongbox named for what is in
    /// it ("Bows", "Bows 2", "Armor"). A strongbox the player named themselves is left out (its items stay); Bound Strongboxes are
    /// not touched. Items move container to container on the server: each item is one row whose container changes, so a crash
    /// mid-sort leaves every item in exactly one strongbox.
    /// </summary>
    public static partial class VaultManager
    {
        private static readonly HashSet<uint> SortingAccounts = new();
        private static readonly Dictionary<uint, double> LastSortByAccount = new();

        public static bool IsSorting(uint accountId)
        {
            lock (Sync)
                return SortingAccounts.Contains(accountId);
        }

        /// <summary>/storage sort, the Ledger's "Sort my strongboxes".</summary>
        public static void Sort(Player player)
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
                var wait = LastSortByAccount.TryGetValue(accountId, out var last) ? last + ServerConfig.vault_sort_cooldown_minutes.Value * 60 - now : 0;
                if (wait > 0)
                {
                    var minutes = (int)Math.Ceiling(wait / 60);
                    Say(player, $"The Quartermaster sorted your strongboxes a moment ago. You can sort again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
                    return;
                }
                SortingAccounts.Add(accountId);
                LastSortByAccount[accountId] = now;
            }

            // the strongbox (and Ledger) this player has open close first - nothing is open while the sort runs
            CloseIndex(player);
            VaultPage mineOpen;
            lock (Sync)
                OpenByAccount.TryGetValue(accountId, out mineOpen);
            if (mineOpen != null)
                ClosePage(mineOpen, player);

            Say(player, "The Quartermaster is sorting your strongboxes...");
            var characterId = player.Guid.Full;

            Task.Run(() =>
            {
                List<PageRow> rows;
                var biotas = new Dictionary<uint, ACE.Database.Models.Shard.Biota>();
                try
                {
                    rows = Pages(accountId, characterId).Where(r => !r.Attuned).ToList();
                    foreach (var r in rows)
                    {
                        bool inMemory;
                        lock (Sync)
                            inMemory = Instances.ContainsKey(r.Id);
                        if (!inMemory)
                            biotas[r.Id] = DatabaseManager.Shard.BaseDatabase.GetBiota(r.Id);
                    }
                }
                catch (Exception e)
                {
                    log.Error($"[VAULT] sort lookup for {player.Name} failed: {e}");
                    EndSort(player, accountId, "The sort could not start. Nothing was moved.");
                    return;
                }
                WorldManager.EnqueueAction(new ActionEventDelegate(ActionType.Vault_Page, () => LoadForSort(player, accountId, rows, biotas)));
            });
        }

        private static void EndSort(Player player, uint accountId, string message)
        {
            lock (Sync)
                SortingAccounts.Remove(accountId);
            if (message != null && player.Session != null)
                Say(player, message);
        }

        private static void LoadForSort(Player player, uint accountId, List<PageRow> rows, Dictionary<uint, ACE.Database.Models.Shard.Biota> biotas)
        {
            var pages = new List<VaultPage>();
            foreach (var r in rows)
            {
                var page = ResolvePage(player, PageKind.Account, accountId, r.Number, r.Id, biotas.GetValueOrDefault(r.Id));
                if (page == null)
                {
                    EndSort(player, accountId, "A strongbox could not be opened, so nothing was moved. Please tell a GM.");
                    return;
                }
                lock (Sync)
                    if (!ClosedAt.ContainsKey(page.Biota.Id))
                        ClosedAt[page.Biota.Id] = Time.GetUnixTime();   // loaded for the sort: leaves memory like a closed strongbox
                pages.Add(page);
            }
            WaitForItems(player, accountId, pages, Time.GetUnixTime() + 60);
        }

        /// <summary>A strongbox made from its row loads its items in the background: the sort starts when every one is in.</summary>
        private static void WaitForItems(Player player, uint accountId, List<VaultPage> pages, double deadline)
        {
            if (pages.All(p => p.InventoryLoaded))
            {
                try
                {
                    SortNow(player, accountId, pages);
                }
                catch (Exception e)
                {
                    log.Error($"[VAULT] sort for {player.Name} failed: {e}");
                    EndSort(player, accountId, "The sort stopped part way. Every item is still in one of your strongboxes - please tell a GM.");
                }
                return;
            }
            if (Time.GetUnixTime() > deadline)
            {
                EndSort(player, accountId, "Your strongboxes took too long to load, so nothing was moved. Try again in a moment.");
                return;
            }
            var chain = new ActionChain();
            chain.AddDelaySeconds(0.5);
            chain.AddAction(WorldManager.ActionQueue, ActionType.Vault_Page, () => WaitForItems(player, accountId, pages, deadline));
            chain.EnqueueChain();
        }

        private static void SortNow(Player player, uint accountId, List<VaultPage> pages)
        {
            // a strongbox the player named stays as it is; the rest (unnamed, or named by an earlier sort) are re-filed
            bool PlayerNamed(VaultPage p) => !string.IsNullOrEmpty(p.GetProperty(PropertyString.VaultPageLabel)) && p.GetProperty(PropertyBool.VaultPageAutoLabel) != true;
            var kept = pages.Where(PlayerNamed).ToList();
            var sortable = pages.Where(p => !PlayerNamed(p)).OrderBy(p => p.VaultPageNumber).ToList();

            // 1. everything out (in memory only: until an item is saved, its row still names its old strongbox)
            var from = new Dictionary<WorldObject, VaultPage>();
            foreach (var p in sortable)
            {
                p.BulkMove = true;
                foreach (var guid in p.Inventory.Keys.ToList())
                    if (p.TryRemoveFromInventory(guid, out var item))
                        from[item] = p;
            }

            // 2. the order: category, then inside it
            var groups = from.Keys.GroupBy(Classify).OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Name)
                .Select(g => (g.Key.Name, Items: g.OrderBy(i => i, ItemOrder.Instance).ToList())).ToList();

            // 3. the strongboxes they go into: the sortable ones first, then new numbers no named strongbox uses
            var max = (int)ServerConfig.vault_max_pages.Value;
            var taken = kept.Select(p => p.VaultPageNumber).ToHashSet();
            var numbers = sortable.Select(p => p.VaultPageNumber)
                .Concat(Enumerable.Range(1, max).Where(n => !taken.Contains(n) && sortable.All(p => p.VaultPageNumber != n))).ToList();
            var needed = groups.Sum(g => (g.Items.Count + VaultPage.PageItems - 1) / VaultPage.PageItems);
            var chunks = new List<(string Name, List<WorldObject> Items)>();
            if (needed <= numbers.Count)
            {
                foreach (var g in groups)
                {
                    var parts = (g.Items.Count + VaultPage.PageItems - 1) / VaultPage.PageItems;
                    for (var k = 0; k < parts; k++)
                        chunks.Add((parts == 1 ? g.Name : $"{g.Name} {k + 1}", g.Items.Skip(k * VaultPage.PageItems).Take(VaultPage.PageItems).ToList()));
                }
            }
            else
            {
                // not enough strongboxes to start every category on its own: the same order, packed
                var all = groups.SelectMany(g => g.Items).ToList();
                for (var k = 0; k * VaultPage.PageItems < all.Count; k++)
                {
                    var part = all.Skip(k * VaultPage.PageItems).Take(VaultPage.PageItems).ToList();
                    var first = Classify(part[0]).Name;
                    var last = Classify(part[^1]).Name;
                    chunks.Add((first == last ? first : $"{first} - {last}", part));
                }
            }

            // 4. refill, strongbox by strongbox
            var used = new List<(VaultPage Page, string Name, int Count)>();
            var homeless = new List<WorldObject>();
            for (var c = 0; c < chunks.Count; c++)
            {
                var number = numbers[c];
                var page = sortable.FirstOrDefault(p => p.VaultPageNumber == number)
                    ?? ResolvePage(player, PageKind.Account, accountId, number, null, null);   // a new strongbox
                if (page == null)
                {
                    homeless.AddRange(chunks[c].Items);
                    continue;
                }
                page.BulkMove = true;
                if (!sortable.Contains(page))
                    sortable.Add(page);
                var placed = 0;
                foreach (var item in chunks[c].Items)
                {
                    if (page.TryAddToInventory(item, placed))
                        placed++;
                    else
                        homeless.Add(item);
                }
                var label = chunks[c].Name.Length > MaxLabel ? chunks[c].Name.Substring(0, MaxLabel) : chunks[c].Name;
                page.SetProperty(PropertyString.VaultPageLabel, label);
                page.SetProperty(PropertyBool.VaultPageAutoLabel, true);
                used.Add((page, label, placed));
            }

            // anything that did not fit goes back where it came from (should not happen: the items fitted before)
            foreach (var item in homeless)
            {
                var back = from.GetValueOrDefault(item);
                if (back == null || !back.TryAddToInventory(item))
                    log.Error($"[VAULT] sort for {player.Name}: {item.Name} 0x{item.Guid.Full:X8} found no strongbox - its saved row still names 0x{back?.Biota.Id ?? 0:X8}");
            }

            // 5. strongboxes left empty lose their sort name; then everything is saved once
            foreach (var p in sortable.Where(p => used.All(u => u.Page != p)))
            {
                if (p.GetProperty(PropertyBool.VaultPageAutoLabel) == true)
                {
                    p.RemoveProperty(PropertyString.VaultPageLabel);
                    p.RemoveProperty(PropertyBool.VaultPageAutoLabel);
                }
            }
            foreach (var item in from.Keys)
                item.SaveBiotaToDatabase();
            foreach (var p in sortable)
            {
                p.BulkMove = false;
                p.SaveBiotaToDatabase();
            }

            log.Info($"[VAULT] {player.Name} sorted account {accountId}: {from.Count} item(s) into {used.Count} strongbox(es), {kept.Count} named one(s) left as they were");
            EndSort(player, accountId, null);
            if (player.Session == null)
                return;
            Say(player, $"Sorted {from.Count} item{(from.Count == 1 ? "" : "s")} into {used.Count} strongbox{(used.Count == 1 ? "" : "es")}" +
                (kept.Count > 0 ? $"; {kept.Count} you named stayed as they were." : "."));
            foreach (var u in used.OrderBy(u => u.Page.VaultPageNumber))
                Say(player, $"Strongbox {u.Page.VaultPageNumber} - {u.Name}: {u.Count} item{(u.Count == 1 ? "" : "s")}");
        }

        // ---- the categories (retail-readable names, up to 24 characters with a number) ----------------------------------------

        private static readonly (WeaponType Type, string Name)[] WeaponNames =
        {
            (WeaponType.Bow, "Bows"), (WeaponType.Crossbow, "Crossbows"), (WeaponType.Thrown, "Thrown Weapons"), (WeaponType.Magic, "Casters"),
            (WeaponType.Sword, "Swords"), (WeaponType.Axe, "Axes"), (WeaponType.Mace, "Maces"), (WeaponType.Spear, "Spears"),
            (WeaponType.Dagger, "Daggers"), (WeaponType.Staff, "Staves"), (WeaponType.Unarmed, "Unarmed"), (WeaponType.TwoHanded, "Two-Handed"),
        };

        private static (int Order, string Name) Classify(WorldObject wo)
        {
            var t = wo.ItemType;
            if (IsSalvageBag(wo) || t.HasFlag(ItemType.TinkeringMaterial))
                return (60, "Salvage");
            if ((t & (ItemType.MeleeWeapon | ItemType.MissileWeapon | ItemType.Caster)) != 0)
            {
                var type = t.HasFlag(ItemType.Caster) ? WeaponType.Magic : wo.W_WeaponType;
                for (var i = 0; i < WeaponNames.Length; i++)
                    if (WeaponNames[i].Type == type)
                        return (10 + i, WeaponNames[i].Name);
                return (29, "Weapons");
            }
            if (t.HasFlag(ItemType.Armor))
                return (wo.ValidLocations.HasValue && wo.ValidLocations.Value.HasFlag(EquipMask.Shield)) ? (31, "Shields") : (30, "Armor");
            if (t.HasFlag(ItemType.Jewelry))
                return (40, "Jewelry");
            if (t.HasFlag(ItemType.Clothing))
                return (45, "Clothing");
            if (t.HasFlag(ItemType.Gem))
                return (65, "Gems");
            if ((t & (ItemType.Food | ItemType.ManaStone)) != 0)
                return (70, "Consumables");
            if (t.HasFlag(ItemType.SpellComponents))
                return (75, "Components");
            if ((t & (ItemType.Key | ItemType.PromissoryNote)) != 0)
                return (80, "Keys and Notes");
            return (90, "Other");
        }

        /// <summary>Salvage Bags (live 10-26): by WCID, as the client's loot filter knows them.</summary>
        private static bool IsSalvageBag(WorldObject wo) =>
            (wo.WeenieClassId >= 78780310 && wo.WeenieClassId <= 78780319) || wo.WeenieClassId == 78780323 || wo.WeenieClassId == 78780324;

        /// <summary>Inside a category: weapons by element, armor by slot, then the highest tier first, then the name.</summary>
        private sealed class ItemOrder : IComparer<WorldObject>
        {
            public static readonly ItemOrder Instance = new();

            public int Compare(WorldObject a, WorldObject b)
            {
                int c;
                if ((a.ItemType & ItemType.WeaponOrCaster) != 0)
                {
                    if ((c = ((int)a.W_DamageType).CompareTo((int)b.W_DamageType)) != 0) return c;
                }
                else if ((a.ItemType & (ItemType.Armor | ItemType.Clothing | ItemType.Jewelry)) != 0)
                {
                    if ((c = ((uint)(a.ValidLocations ?? 0)).CompareTo((uint)(b.ValidLocations ?? 0))) != 0) return c;
                }
                if ((c = (b.GetProperty(PropertyInt.ZcTier) ?? 0).CompareTo(a.GetProperty(PropertyInt.ZcTier) ?? 0)) != 0) return c;
                if ((c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)) != 0) return c;
                return a.Guid.Full.CompareTo(b.Guid.Full);
            }
        }
    }
}
