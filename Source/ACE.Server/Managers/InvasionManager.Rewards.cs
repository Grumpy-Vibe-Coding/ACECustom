using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
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
            if (ServerConfig.invasion_treasure_id.Value <= 0) return; // auto-loot disabled

            List<uint> earners;
            lock (_lock) { earners = _accountEarner.Values.ToList(); }

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
        /// Delivers pending invasion rewards into the player's pack. Rolls treasure fresh per
        /// reward, sets it attuned/bonded, and only consumes a pending unit if the whole roll fit.
        /// Safe to call repeatedly (boss death for the online earner, and on login).
        /// </summary>
        public static void TryDeliverPending(Player player)
        {
            if (player == null) return;

            int pending = player.GetProperty(PropertyInt.InvasionPendingRewards) ?? 0;
            if (pending <= 0) return;

            long treasureId = ServerConfig.invasion_treasure_id.Value;
            if (treasureId <= 0) return;

            var profile = DatabaseManager.World.GetCachedDeathTreasure((uint)treasureId);
            if (profile == null)
            {
                log.Error($"[Invasion] invasion_treasure_id {treasureId} has no TreasureDeath profile — cannot deliver rewards.");
                return;
            }

            int delivered = 0;
            for (int i = 0; i < pending; i++)
            {
                var items = LootGenerationFactory.CreateRandomLootObjects(profile);
                if (items == null || items.Count == 0) { delivered++; continue; } // empty roll: consume it

                // Whole roll must fit, else leave this (and the rest) queued — never drop on ground.
                if (player.GetFreeInventorySlots() < items.Count)
                {
                    foreach (var it in items) it.Destroy();
                    break;
                }

                foreach (var item in items)
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
    }
}
