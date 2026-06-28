using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Real-time state sync for the "Invasion Helper" Decal plugin.
    ///
    /// Transport: messages are pushed over each player's existing game connection as a
    /// system-chat line with the sentinel prefix <see cref="SyncPrefix"/>. Only sessions
    /// that have handshaked via /ilt ihsync receive them, so non-plugin players never see
    /// the sentinel text. The plugin intercepts the line, suppresses display, and parses it.
    ///
    /// Cost: a single small EnqueueSend per plugin player, at most once/sec while an
    /// invasion is active and once/10s while idle. No DB access, no per-request computation.
    /// </summary>
    public static partial class InvasionManager
    {
        public const string SyncPrefix = "[[IH]]";
        private const int   SyncVersion = 1;

        // Guids of players whose client has the plugin (handshake via /ilt ihsync).
        private static readonly HashSet<uint> _pluginSessions = new();

        private static double _nextSyncPush;
        private const double SyncIntervalActive = 1.0;   // seconds, while an invasion runs
        private const double SyncIntervalIdle   = 10.0;  // seconds, while idle (cooldown ticking)

        /// <summary>Flag a session as plugin-enabled and push it the current state immediately.</summary>
        public static void RegisterPluginSession(Player player)
        {
            if (player == null) return;
            lock (_lock)
            {
                _pluginSessions.Add(player.Guid.Full);
            }
            ForceSyncPush(); // next tick (<=1s) pushes current state to everyone, incl. this player
        }

        public static void UnregisterPluginSession(Player player)
        {
            if (player == null) return;
            lock (_lock)
            {
                _pluginSessions.Remove(player.Guid.Full);
            }
        }

        /// <summary>
        /// Force the next Tick() to push state immediately (drops the throttle wait).
        /// Call after any state change so plugin clients update within ~1s instead of
        /// waiting out the idle interval. Just sets a field — safe to call under _lock.
        /// </summary>
        public static void ForceSyncPush() => _nextSyncPush = 0;

        /// <summary>
        /// Called from Tick(). Self-throttled; safe to call every tick.
        ///
        /// Built for scale: the shared portion of the payload (identical for every player)
        /// is composed once per cycle, and the damage/healing trackers are snapshotted under
        /// the lock a single time — so the per-player loop runs lock-free and only appends
        /// the few fields that differ. With 400 players this is one lock acquisition and one
        /// shared string build per cycle instead of 400 of each.
        /// </summary>
        private static void PushSync(double now)
        {
            if (now < _nextSyncPush) return;
            _nextSyncPush = now + (IsActive ? SyncIntervalActive : SyncIntervalIdle);

            uint[] guids;
            Dictionary<uint, long> dmgSnap, healSnap;
            bool active;
            string town, species, bossName, objType;
            uint bossCur, bossMax;
            long dThr, hThr;
            bool minOn, autoOn;

            // Single lock acquisition for the whole cycle.
            lock (_lock)
            {
                if (_pluginSessions.Count == 0) return;
                guids    = _pluginSessions.ToArray();
                dmgSnap  = new Dictionary<uint, long>(PlayerDamageTracker);
                healSnap = new Dictionary<uint, long>(PlayerHealingTracker);

                active  = IsActive;
                town    = ActiveTown;
                species = ActiveSpecies;
                objType = ActiveObjective?.TypeId ?? "";
                bossName = ""; bossCur = 0; bossMax = 0;
                if (ActiveBoss != null && ActiveBoss.IsAlive)
                {
                    bossName = ActiveBoss.Name ?? "";
                    bossCur = ActiveBoss.Health?.Current ?? 0;
                    bossMax = ActiveBoss.Health?.MaxValue ?? 0;
                }
                dThr = DamageThreshold; hThr = HealingThreshold;
                minOn = SpawnMinions;   autoOn = Enabled;
            }

            int elapsed  = active ? (int)(now - InvasionStartTime) : 0;
            int cooldown = !active && NextInvasionTime > now ? (int)(NextInvasionTime - now) : 0;
            int participants = dmgSnap.Keys.Union(healSnap.Keys).Count(); // admin-only field, computed once

            // Shared prefix — same bytes for everyone. Includes the sentinel + version.
            var sb = new StringBuilder(220);
            sb.Append(SyncPrefix);
            sb.Append("v=").Append(SyncVersion);
            sb.Append("|act=").Append(active ? 1 : 0);
            sb.Append("|town=").Append(Clean(town));
            sb.Append("|species=").Append(Clean(species));
            sb.Append("|type=").Append(Clean(objType));
            sb.Append("|boss=").Append(Clean(bossName));
            sb.Append("|bosscur=").Append(bossCur);
            sb.Append("|bossmax=").Append(bossMax);
            sb.Append("|elapsed=").Append(elapsed);
            sb.Append("|cd=").Append(cooldown);
            sb.Append("|min=").Append(minOn ? 1 : 0);
            sb.Append("|auto=").Append(autoOn ? 1 : 0);
            sb.Append("|dthr=").Append(dThr);
            sb.Append("|hthr=").Append(hThr);
            int sharedLen = sb.Length; // truncate back to here for each player

            foreach (var guid in guids)
            {
                var player = PlayerManager.GetOnlinePlayer(guid);
                if (player?.Session == null) continue;

                dmgSnap.TryGetValue(guid, out long yDmg);
                healSnap.TryGetValue(guid, out long yHeal);
                bool isAdmin = player.Session.AccessLevel >= AccessLevel.Developer;
                bool elig = yDmg >= dThr || yHeal >= hThr; // no dev bypass — devs test the real path

                sb.Length = sharedLen; // reuse the builder; drop previous player's tail
                sb.Append("|ydmg=").Append(yDmg);
                sb.Append("|yheal=").Append(yHeal);
                sb.Append("|elig=").Append(elig ? 1 : 0);
                if (isAdmin)
                {
                    sb.Append("|admin=1");
                    sb.Append("|parts=").Append(participants);
                }
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(sb.ToString(), ChatMessageType.System));
            }
        }

        // Strip delimiters from a value so it can't break the pipe/equals framing.
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", " ").Replace("=", " ");
        }
    }
}
