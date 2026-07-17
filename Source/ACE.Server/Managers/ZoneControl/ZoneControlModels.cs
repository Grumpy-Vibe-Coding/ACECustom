using System.Collections.Generic;
using ACE.Server.Managers.ZoneScaling;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// One controlled Zone: a named set of landblocks governed at a specific world Variation, with an on/off
    /// toggle and a stat profile. A monster is governed by a zone when it stands on one of the zone's landblocks
    /// AND its variation equals the zone's <see cref="Variation"/> (0 = the normal world; 11+ = variant instances).
    ///
    /// No prestige/tier/boss concepts: the stat payload is a single DEFAULT set applied to every monster in the
    /// zone (the profile's default variant), plus optional per-monster (WCID) overrides.
    /// </summary>
    public class ControlledArea
    {
        /// <summary>Unique key (case-insensitive) — e.g. "tusker_barracks".</summary>
        public string Name { get; set; }

        /// <summary>Member landblocks (a dungeon's landblock, or every block of an overworld region).</summary>
        public HashSet<ushort> Landblocks { get; set; } = new();

        /// <summary>The world variation this zone governs. 0 = the normal (base) world; 11+ = variant instances.</summary>
        public int Variation { get; set; }

        /// <summary>Master switch. Off ⇒ the zone resolves to null (monsters revert to baseline; live stats instantly, HP on respawn).</summary>
        public bool Enabled { get; set; }

        /// <summary>When true (and the zone is Enabled), players at this zone's Variation may only roam the
        /// landblocks of bounded zones at that variation (the union across such zones forms the variation's
        /// player allowlist). Enforced by the boundary punishment loop, guide wisp and perimeter markers.
        /// Only meaningful at variations 11+ (the command refuses retail variations); runtime zones never bound.</summary>
        public bool Bounded { get; set; }

        public string Notes { get; set; }

        /// <summary>Manual terrain overrides for the Territory map, keyed by landblock → terrain tag
        /// (water|beach|obsidian|snow|ice|swamp|grass|dirt|rock). The survey reports this tag instead of the
        /// DAT-derived dominant terrain wherever present. Display-only: terrain drives nothing but map color, so
        /// an admin can re-tag mixed grass/rock/obsidian blocks to whatever reads best for planning generators.</summary>
        public Dictionary<ushort, string> TerrainOverrides { get; set; } = new();

        /// <summary>Stat payload: the default set (profile default variant) for all monsters + per-WCID overrides.</summary>
        public ZoneScalingProfile Profile { get; set; } = new();

        /// <summary>Zone-wide rules applied to PLAYERS standing in the zone (independent of monster stats).</summary>
        public ZoneEffects Effects { get; set; } = new();
    }

    /// <summary>
    /// Per-zone effects applied to PLAYERS inside the zone, evaluated each player heartbeat by
    /// <see cref="ZoneControl.ZoneEffectManager"/>. Only <see cref="DotEnabled"/> is wired today; the
    /// slow/charm fields are reserved placeholders so the wire format + store schema are forward-compatible.
    /// </summary>
    public class ZoneEffects
    {
        // ── Damage over time ("the floor is lava") ──
        /// <summary>When true, players in the zone take a periodic hit every <see cref="DotIntervalSeconds"/>.</summary>
        public bool DotEnabled { get; set; }

        /// <summary>Amount applied PER TICK. Flat points normally, or a percent of the player's max health when
        /// <see cref="DotPercent"/> is true (e.g. 5 = 5% of max health per tick).</summary>
        public double DotDamage { get; set; }

        /// <summary>When true, <see cref="DotDamage"/> is a percent of the player's max health (drains Health).</summary>
        public bool DotPercent { get; set; }

        /// <summary>Seconds between ticks (min 1). Applied by a per-player timer, independent of the 5s heartbeat.</summary>
        public double DotIntervalSeconds { get; set; } = 5.0;

        /// <summary>ACE.Entity.Enum.DamageType as int (default Fire = 0x10). Stored as int to keep the model enum-free.
        /// Stamina/Mana drain those pools; Health = "drained"; percent mode forces Health.</summary>
        public int DotDamageType { get; set; } = 0x10;

        // ── Reserved for later slices (NOT applied yet) ──
        public bool SlowEnabled { get; set; }
        public double SlowPercent { get; set; }
        public bool CharmEnabled { get; set; }

        /// <summary>True if any effect is active — used to skip zones that author no effects during resolution.</summary>
        public bool AnyActive => DotEnabled || SlowEnabled || CharmEnabled;
    }
}
