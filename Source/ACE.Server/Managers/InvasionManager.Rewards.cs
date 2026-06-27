using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Invasion auto-loot rewards. Eligibility (meeting the damage/healing threshold) is claimed
    /// per ACCOUNT — the first eligible character on an account is the recipient — and capped per
    /// IP, to prevent alt/multi-account farming. On a successful invasion the earner is credited a
    /// pending reward (a custom character PropertyInt), which is delivered into their pack as an
    /// attuned/bonded treasure roll: immediately if online at boss death, otherwise on next login.
    /// Nothing is ever dropped on the ground; rewards that don't fit stay queued.
    /// </summary>
    public static partial class InvasionManager
    {
        // Per-invasion reward eligibility (in-memory; reset on StartInvasion).
        private static readonly HashSet<uint> _rewardedAccounts = new();
        private static readonly Dictionary<string, int> _rewardsPerIp = new();
        private static readonly Dictionary<uint, uint> _accountEarner = new(); // accountId -> earning char guid

        private static long MaxRewardsPerIp => ServerConfig.invasion_reward_max_per_ip.Value;

        /// <summary>TreasureDeath profile id rolled for auto-loot. 0 = auto-loot disabled.</summary>
        public static long TreasureId
        {
            get => ServerConfig.invasion_treasure_id.Value;
            set => ServerConfig.SetValue("invasion_treasure_id", value);
        }

        /// <summary>Fixed item WCID granted as a reward (0 = none). e.g. Invasion/Prestige coin.</summary>
        public static long RewardWcid
        {
            get => ServerConfig.invasion_reward_wcid.Value;
            set => ServerConfig.SetValue("invasion_reward_wcid", value);
        }

        /// <summary>Quantity/stack of the fixed reward item.</summary>
        public static long RewardAmount
        {
            get => ServerConfig.invasion_reward_amount.Value;
            set => ServerConfig.SetValue("invasion_reward_amount", value);
        }

        private static bool AutoLootEnabled => RewardWcid > 0 || TreasureId > 0;

        /// <summary>Clears reward eligibility state. Called at invasion start.</summary>
        internal static void ResetRewardState()
        {
            lock (_lock)
            {
                _rewardedAccounts.Clear();
                _rewardsPerIp.Clear();
                _accountEarner.Clear();
            }
        }

        /// <summary>
        /// Called when a player first meets the eligibility threshold. Claims the reward for their
        /// account (one per account per invasion), capped per IP. First eligible char on the
        /// account becomes the recipient.
        /// </summary>
        public static void TryClaimReward(Player player)
        {
            if (player?.Session == null) return;

            var acctId = player.Session.AccountId;
            var ip = player.Session.EndPoint?.Address?.ToString() ?? "unknown";

            lock (_lock)
            {
                if (_rewardedAccounts.Contains(acctId)) return;            // account already claimed
                int ipCount = _rewardsPerIp.TryGetValue(ip, out var c) ? c : 0;
                if (ipCount >= MaxRewardsPerIp) return;                    // per-IP cap reached

                _rewardedAccounts.Add(acctId);
                _rewardsPerIp[ip] = ipCount + 1;
                _accountEarner[acctId] = player.Guid.Full;
            }
        }

        /// <summary>
        /// On successful invasion (boss death): credit each eligible account's earner with one
        /// pending reward, delivering immediately to those online.
        /// </summary>
        internal static void GrantInvasionRewards()
        {
            List<uint> earners;
            lock (_lock) { earners = _accountEarner.Values.ToList(); }

            // Message participants who did not qualify for rewards
            var allParticipants = PlayerDamageTracker.Keys.Union(PlayerHealingTracker.Keys).ToList();
            foreach (var charGuidFull in allParticipants)
            {
                if (earners.Contains(charGuidFull))
                    continue;

                var guid = new ObjectGuid(charGuidFull);
                var player = PlayerManager.GetOnlinePlayer(guid);
                if (player == null) continue;

                PlayerDamageTracker.TryGetValue(charGuidFull, out var dmg);
                PlayerHealingTracker.TryGetValue(charGuidFull, out var heal);

                if (dmg >= DamageThreshold || heal >= HealingThreshold)
                {
                    var acctId = player.Session?.AccountId ?? 0;
                    bool accountClaimed = false;
                    lock (_lock) { accountClaimed = _accountEarner.ContainsKey(acctId) && _accountEarner[acctId] != charGuidFull; }

                    if (accountClaimed)
                    {
                        player.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                            "[Invasion] You met the threshold, but another character on your account already claimed the reward.",
                            ChatMessageType.System));
                    }
                    else
                    {
                        player.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                            $"[Invasion] You met the threshold, but the reward limit for your IP address ({MaxRewardsPerIp}) was reached.",
                            ChatMessageType.System));
                    }
                }
                else
                {
                    player.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                        $"[Invasion] You did not meet the reward eligibility requirements. Your contribution: {dmg:N0}/{DamageThreshold:N0} Damage, {heal:N0}/{HealingThreshold:N0} Healing.",
                        ChatMessageType.System));
                }
            }

            if (!AutoLootEnabled) return; // no fixed item and no treasure roll configured

            foreach (var charGuidFull in earners)
            {
                var guid = new ObjectGuid(charGuidFull);

                var online = PlayerManager.GetOnlinePlayer(guid);
                if (online != null)
                {
                    var pend = (online.GetProperty(PropertyInt.InvasionPendingRewards) ?? 0) + 1;
                    online.SetProperty(PropertyInt.InvasionPendingRewards, pend);
                    TryDeliverPending(online);
                }
                else
                {
                    var offline = PlayerManager.GetOfflinePlayer(guid);
                    if (offline != null)
                    {
                        var pend = (offline.GetProperty(PropertyInt.InvasionPendingRewards) ?? 0) + 1;
                        offline.SetProperty(PropertyInt.InvasionPendingRewards, pend);
                        offline.SaveBiotaToDatabase();
                    }
                }
            }
        }

        /// <summary>
        /// Delivers pending invasion rewards into the player's pack. Each pending unit is one
        /// reward PACKAGE = the configured fixed item (e.g. a coin) plus, if set, a fresh treasure
        /// roll. Items are set attuned/bonded; a unit is only consumed if the whole package fit
        /// (never dropped on the ground). Safe to call repeatedly (boss death online + on login).
        /// </summary>
        public static void TryDeliverPending(Player player)
        {
            if (player == null) return;

            int pending = player.GetProperty(PropertyInt.InvasionPendingRewards) ?? 0;
            if (pending <= 0) return;
            if (!AutoLootEnabled) return;

            TreasureDeath profile = null;
            if (TreasureId > 0)
            {
                profile = DatabaseManager.World.GetCachedDeathTreasure((uint)TreasureId);
                if (profile == null)
                    log.Error($"[Invasion] invasion_treasure_id {TreasureId} has no TreasureDeath profile — skipping treasure roll.");
            }

            int delivered = 0;
            for (int i = 0; i < pending; i++)
            {
                var package = BuildRewardPackage(profile);
                if (package.Count == 0) { delivered++; continue; } // nothing to give: consume the unit

                // Whole package must fit, else leave it (and the rest) queued — never drop on ground.
                if (player.GetFreeInventorySlots() < package.Count)
                {
                    foreach (var it in package) it.Destroy();
                    break;
                }

                foreach (var item in package)
                {
                    item.SetProperty(PropertyInt.Attuned, (int)AttunedStatus.Attuned);
                    item.SetProperty(PropertyInt.Bonded, (int)BondedStatus.Bonded);
                    player.TryCreateInInventoryWithNetworking(item);
                }
                delivered++;
            }

            if (delivered > 0)
            {
                player.SetProperty(PropertyInt.InvasionPendingRewards, pending - delivered);
                player.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                    $"[Invasion] You received {delivered} reward{(delivered > 1 ? "s" : "")}!", ChatMessageType.Broadcast));
            }
        }

        /// <summary>Builds one reward package: fixed item (RewardWcid x RewardAmount) + optional treasure roll.</summary>
        private static List<WorldObject> BuildRewardPackage(TreasureDeath profile)
        {
            var package = new List<WorldObject>();

            // Fixed item (e.g. coin), stacked where possible.
            if (RewardWcid > 0 && RewardAmount > 0)
            {
                long remaining = RewardAmount;
                var first = WorldObjectFactory.CreateNewWorldObject((uint)RewardWcid);
                if (first == null)
                {
                    log.Error($"[Invasion] invasion_reward_wcid {RewardWcid} could not be created.");
                }
                else
                {
                    int max = first.MaxStackSize ?? 1;
                    if (max > 1)
                    {
                        int s = (int)Math.Min(remaining, max);
                        first.SetStackSize(s);
                        package.Add(first);
                        remaining -= s;
                        while (remaining > 0)
                        {
                            var extra = WorldObjectFactory.CreateNewWorldObject((uint)RewardWcid);
                            if (extra == null) break;
                            int es = (int)Math.Min(remaining, max);
                            extra.SetStackSize(es);
                            package.Add(extra);
                            remaining -= es;
                        }
                    }
                    else
                    {
                        package.Add(first);
                        for (long n = 1; n < RewardAmount; n++)
                        {
                            var extra = WorldObjectFactory.CreateNewWorldObject((uint)RewardWcid);
                            if (extra != null) package.Add(extra);
                        }
                    }
                }
            }

            // Optional treasure roll.
            if (profile != null)
            {
                var rolled = LootGenerationFactory.CreateRandomLootObjects(profile);
                if (rolled != null) package.AddRange(rolled);
            }

            return package;
        }
    }
}
