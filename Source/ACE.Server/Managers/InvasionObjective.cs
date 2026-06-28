using System.Collections.Generic;
using System.Text;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Base class for an invasion's win/fail rules. <see cref="InvasionManager"/> owns the
    /// shared state (town, species, participation trackers, rewards, sync, broadcasts) and
    /// delegates the type-specific behavior — what spawns, what counts as a boss, and when the
    /// invasion is won or failed — to the active objective.
    ///
    /// Phase 1 ships a single implementation, <see cref="SingleBossObjective"/>, that exactly
    /// reproduces the original one-boss flow. New invasion types (e.g. a 3-boss burn) are added
    /// as additional subclasses without touching the shared manager plumbing.
    ///
    /// All members are invoked from <see cref="InvasionManager"/> while it holds its lock, so
    /// implementations must not lock themselves.
    /// </summary>
    public abstract class InvasionObjective
    {
        /// <summary>Stable short id used in the <c>/dev invasion start</c> command and the
        /// <c>[[IH]]</c> sync feed (e.g. "boss", "3boss").</summary>
        public abstract string TypeId { get; }

        /// <summary>Human-readable name for status output.</summary>
        public abstract string DisplayName { get; }

        /// <summary>Spawn the objective's creatures and arm any timers. Called once from
        /// StartInvasion after the shared state has been reset.</summary>
        public abstract void OnStart();

        /// <summary>A tracked invasion creature has died (it already passed the manager's
        /// name/proximity filter). Implementations update their own won/fail state here.</summary>
        public abstract void OnCreatureDeath(Creature creature);

        /// <summary>Per-second update for time-based rules (burn windows, encounter timers).
        /// Default is a no-op.</summary>
        public virtual void Tick(double now) { }

        /// <summary>True once the win condition is met — the manager then runs the shared
        /// reward + portal + success path.</summary>
        public abstract bool IsWon { get; }

        /// <summary>True once the objective has failed — the manager then fails the invasion.</summary>
        public abstract bool IsFailed { get; }

        /// <summary>True if <paramref name="creature"/> is one of this objective's bosses.
        /// Drives boss damage credit and the live combat-tuning hooks.</summary>
        public abstract bool IsBoss(Creature creature);

        /// <summary>The objective's live bosses (0..N), used for sync HP bars and status.</summary>
        public abstract IReadOnlyList<Creature> Bosses { get; }

        /// <summary>Append type-specific fields to the <c>[[IH]]</c> sync payload. Default no-op.
        /// Keep keys short and free of the <c>|</c> and <c>=</c> framing characters.</summary>
        public virtual void BuildSyncFields(StringBuilder sb) { }

        /// <summary>Destroy any spawned creatures and release references. Called from
        /// StopInvasion / FailInvasion.</summary>
        public abstract void Cleanup();
    }
}
