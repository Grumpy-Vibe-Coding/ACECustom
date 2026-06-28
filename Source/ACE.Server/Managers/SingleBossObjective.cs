using System;
using System.Collections.Generic;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// The classic invasion: one boss spawns at the town center.
    ///   Win  = the boss dies.
    ///   Fail = the boss is silently removed from the world (landblock cleanup — Destroy()
    ///          without Die()), detected on tick.
    /// This wraps the original single-boss flow unchanged; <see cref="InvasionManager.ActiveBoss"/>
    /// remains the one source of truth for the boss reference (combat hooks and live tuning still
    /// read it directly).
    /// </summary>
    public sealed class SingleBossObjective : InvasionObjective
    {
        public override string TypeId => "boss";
        public override string DisplayName => "Single Boss";

        private bool _won;
        private bool _failed;

        public override void OnStart()
        {
            // Spawns the boss at the town center and assigns InvasionManager.ActiveBoss.
            InvasionManager.SpawnBoss();
        }

        public override void OnCreatureDeath(Creature creature)
        {
            if (IsBoss(creature))
                _won = true;
        }

        public override void Tick(double now)
        {
            // Detect a boss silently removed by landblock cleanup (Destroy() without Die()).
            var boss = InvasionManager.ActiveBoss;
            if (boss != null && boss.IsDestroyed)
                _failed = true;
        }

        public override bool IsWon => _won;
        public override bool IsFailed => _failed;

        public override bool IsBoss(Creature creature)
            => creature != null && creature == InvasionManager.ActiveBoss;

        public override IReadOnlyList<Creature> Bosses
        {
            get
            {
                var boss = InvasionManager.ActiveBoss;
                return boss != null && boss.IsAlive
                    ? new[] { boss }
                    : Array.Empty<Creature>();
            }
        }

        public override void Cleanup()
        {
            var boss = InvasionManager.ActiveBoss;
            if (boss != null && boss.IsAlive)
                boss.Destroy();
        }
    }
}
