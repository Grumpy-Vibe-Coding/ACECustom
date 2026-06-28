using System;
using System.Collections.Generic;
using System.Text;
using ACE.Common;
using ACE.Server.WorldObjects;
using Position = ACE.Entity.Position;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Three bosses spawn together and must be killed within a short "burn window".
    ///   • The burn window (<see cref="BurnWindowSeconds"/>) OPENS when the FIRST boss dies and
    ///     does NOT reset when subsequent bosses die inside it.
    ///   • If the window expires with any boss still alive, the burn phase resets: downed bosses
    ///     are revived at full HP and surviving bosses are full-healed (a true burn-phase wipe),
    ///     a warning is broadcast, and the window closes so it can re-open on the next first death.
    ///   • Win  = all three bosses are down at once (inside a window).
    ///   • Fail = the overall encounter timer (<see cref="EncounterSeconds"/>) expires with any
    ///     boss still alive.
    /// Bosses are the custom "Invasion Empyrean" weenies and use their own weenie stats — the
    /// single-golem live-tuning overrides are not applied here.
    /// </summary>
    public sealed class ThreeBossBurnObjective : InvasionObjective
    {
        public override string TypeId => "3boss";
        public override string DisplayName => "Empyrean 3-Boss Burn";

        // Custom WCIDs of the three Empyrean bosses (Content/sql/weenies/7200001{0,1,2}_*.sql).
        private static readonly uint[] BossWcids = { 72000010, 72000011, 72000012 };

        private const double BurnWindowSeconds = 30.0;
        private const double EncounterSeconds  = 600.0; // 10-minute overall timer

        // Small triangular offsets (world units) from the town center so the bosses don't stack.
        private static readonly (float dx, float dy)[] SpawnOffsets =
        {
            ( 3.0f,  0.0f),
            (-1.5f,  2.6f),
            (-1.5f, -2.6f),
        };

        private sealed class BossSlot
        {
            public uint Wcid;
            public Position SpawnPos;   // base+offset spawn position (terrain-Z snapped at spawn time)
            public Creature Creature;   // current live instance (replaced on revive); null/dead when down
            public string Name = "";    // last known name, so sync can still label a downed slot
            public long MaxHealth;      // last known max, so sync can show a full bar for a downed slot
        }

        private readonly List<BossSlot> _slots = new();

        private bool _burnWindowOpen;
        private double _burnWindowEndsAt;
        private double _encounterEndsAt;

        private bool _won;
        private bool _failed;

        public override void OnStart()
        {
            var basePos = InvasionManager.GetActiveTownBasePosition();
            if (basePos == null)
            {
                // No spawn position — fail fast so the manager doesn't run a boss-less invasion.
                _failed = true;
                return;
            }

            for (int i = 0; i < BossWcids.Length; i++)
            {
                var pos = new Position(basePos);
                pos.PositionX += SpawnOffsets[i].dx;
                pos.PositionY += SpawnOffsets[i].dy;

                var slot = new BossSlot { Wcid = BossWcids[i], SpawnPos = pos };
                SpawnSlot(slot);
                _slots.Add(slot);
            }

            _encounterEndsAt = Time.GetUnixTime() + EncounterSeconds;

            int alive = CountAlive();
            InvasionManager.BroadcastInvasion(
                $"[Invasion] {alive} Empyrean bosses have risen! Once the first falls, kill the rest within {(int)BurnWindowSeconds}s or they return!");
        }

        private void SpawnSlot(BossSlot slot)
        {
            // applyBossOverrides: the shared invasion_boss_* tuning applies to all 3 bosses (and to
            // revived ones), so Bosses-tab edits affect this encounter. No-op while config is default.
            var boss = InvasionManager.SpawnInvasionCreatureAt(slot.Wcid, slot.SpawnPos, applyBossOverrides: true);
            slot.Creature = boss;
            if (boss != null)
            {
                slot.Name = boss.Name ?? slot.Name;
                if (boss.Health != null)
                    slot.MaxHealth = boss.Health.MaxValue;
            }
        }

        private static bool IsSlotAlive(BossSlot s)
            => s.Creature != null && !s.Creature.IsDestroyed && s.Creature.IsAlive;

        private int CountAlive()
        {
            int n = 0;
            foreach (var s in _slots)
                if (IsSlotAlive(s)) n++;
            return n;
        }

        public override void OnCreatureDeath(Creature creature)
        {
            if (!IsBoss(creature)) return;

            // Open the burn window on the FIRST death; never reset it while already open.
            if (!_burnWindowOpen)
            {
                _burnWindowOpen = true;
                _burnWindowEndsAt = Time.GetUnixTime() + BurnWindowSeconds;
                InvasionManager.BroadcastInvasion(
                    $"[Invasion] Burn window OPEN — kill the remaining Empyrean bosses within {(int)BurnWindowSeconds} seconds!");
            }

            // Win the instant all three are down together.
            if (CountAlive() == 0)
                _won = true;
        }

        public override void Tick(double now)
        {
            if (_won || _failed) return;

            // Overall encounter timer.
            if (now >= _encounterEndsAt)
            {
                _failed = true;
                return;
            }

            // Burn window expiry with at least one boss still alive -> reset the burn phase.
            if (_burnWindowOpen && now >= _burnWindowEndsAt)
            {
                if (CountAlive() == 0)
                {
                    _won = true; // safety: all fell exactly at the deadline
                    return;
                }
                ResetBurnPhase();
            }
        }

        /// <summary>Revive any downed bosses at full HP, full-heal the survivors, and re-arm so the
        /// window opens again on the next first death.</summary>
        private void ResetBurnPhase()
        {
            foreach (var s in _slots)
            {
                if (IsSlotAlive(s))
                    s.Creature.SetMaxVitals();  // full-heal survivor
                else
                    SpawnSlot(s);               // revive a fresh full-HP instance at its spawn position
            }

            _burnWindowOpen = false;
            _burnWindowEndsAt = 0;

            InvasionManager.BroadcastInvasion(
                "[Invasion] The burn window closed with bosses still standing — the Empyrean bosses have fully recovered! Burn them together!");
        }

        public override bool IsWon => _won;
        public override bool IsFailed => _failed;

        public override bool IsBoss(Creature creature)
        {
            if (creature == null) return false;
            // Index loop (not foreach): slot.Creature may be reassigned on the world thread while
            // a combat thread calls this. The list itself is only structurally modified in OnStart.
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Creature == creature) return true;
            return false;
        }

        public override IReadOnlyList<Creature> Bosses
        {
            get
            {
                var list = new List<Creature>(_slots.Count);
                foreach (var s in _slots)
                    if (IsSlotAlive(s)) list.Add(s.Creature);
                return list;
            }
        }

        public override void BuildSyncFields(StringBuilder sb)
        {
            var now = Time.GetUnixTime();
            sb.Append("|bcount=").Append(_slots.Count);
            sb.Append("|burn=").Append(_burnWindowOpen ? (int)Math.Max(0, _burnWindowEndsAt - now) : -1);
            sb.Append("|enc=").Append((int)Math.Max(0, _encounterEndsAt - now));

            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                bool alive = IsSlotAlive(s);
                long cur = alive ? (s.Creature.Health?.Current ?? 0) : 0;
                long max = alive ? (s.Creature.Health?.MaxValue ?? s.MaxHealth) : s.MaxHealth;
                sb.Append("|b").Append(i).Append("n=").Append(SanitizeField(s.Name));
                sb.Append("|b").Append(i).Append("w=").Append(s.Wcid);
                sb.Append("|b").Append(i).Append("cur=").Append(cur);
                sb.Append("|b").Append(i).Append("max=").Append(max);
            }
        }

        // Keep values from breaking the pipe/equals framing of the [[IH]] payload.
        private static string SanitizeField(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("|", " ").Replace("=", " ");

        public override void Cleanup()
        {
            foreach (var s in _slots)
            {
                if (IsSlotAlive(s))
                    s.Creature.Destroy();
                s.Creature = null;
            }
            _slots.Clear();
        }
    }
}
