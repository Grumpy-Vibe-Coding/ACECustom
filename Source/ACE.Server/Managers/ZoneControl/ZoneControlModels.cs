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

        /// <summary>COSMETIC appearance overrides, kept SEPARATE from Profile so they never touch a monster's
        /// stats/abilities. <see cref="AppearanceDefault"/> applies to every governed monster; the
        /// <see cref="AppearanceByWcid"/> entries LAYER on top of it per monster type (non-null fields win).
        /// Missing on deserialize of older stores = empty (backward compatible). See ZoneAppearance.</summary>
        public ZoneAppearance AppearanceDefault { get; set; } = new();
        public Dictionary<uint, ZoneAppearance> AppearanceByWcid { get; set; } = new();

        /// <summary>The cosmetic bucket to edit: the per-WCID overlay (auto-created when <paramref name="create"/>)
        /// or the zone default when <paramref name="wcid"/> is null. Mirrors Profile.VariantForWcid.</summary>
        public ZoneAppearance AppearanceFor(uint? wcid, bool create = false)
        {
            if (!wcid.HasValue) return AppearanceDefault ??= new ZoneAppearance();
            AppearanceByWcid ??= new Dictionary<uint, ZoneAppearance>();
            if (AppearanceByWcid.TryGetValue(wcid.Value, out var ap) && ap != null) return ap;
            if (!create) return null;
            AppearanceByWcid[wcid.Value] = ap = new ZoneAppearance();
            return ap;
        }
    }

    /// <summary>
    /// The DEFAULT layer for one world variation (2026-07-30): the baseline every zone at that variation
    /// inherits. Resolution is <c>VariationDefault -&gt; zone -&gt; wcid</c>, merged PER STAT — a zone that
    /// authors nothing IS its variation's Default, and a zone that authors one stat overrides one stat.
    ///
    /// Progression across v11-v25 is expressed as 15 of these, each explicitly authored. The server never
    /// derives a stat from the variation number (owner ruling: computed scaling is out).
    ///
    /// Runtime zones (rift runs) deliberately do NOT inherit — they live at negative variations and set
    /// their own stats at registration, so a rift can never pick up Tide combat numbers.
    /// </summary>
    public class VariationDefault
    {
        /// <summary>Stat/prop/body-part/list payload — the same shape a zone carries.</summary>
        public ZoneVariantProfile Profile { get; set; } = new();

        /// <summary>Player effects (DoT etc.) inherited by zones at this variation. Nullable per field.</summary>
        public ZoneEffects Effects { get; set; } = new();

        /// <summary>Cosmetic baseline, overlaid by the zone's own appearance then per-WCID.</summary>
        public ZoneAppearance Appearance { get; set; } = new();

        public string Notes { get; set; }

        public bool IsEmpty =>
            (Profile == null || Profile.IsEmpty)
            && (Effects == null || Effects.IsEmpty)
            && (Appearance == null || Appearance.IsEmpty);
    }

    /// <summary>
    /// Per-zone effects applied to PLAYERS inside the zone, evaluated each player heartbeat by
    /// <see cref="ZoneControl.ZoneEffectManager"/>. Only <see cref="DotEnabled"/> is wired today; the
    /// slow/charm fields are reserved placeholders so the wire format + store schema are forward-compatible.
    /// </summary>
    public class ZoneEffects
    {
        // Every field is NULLABLE (2026-07-30 Default layer): null means "not authored at THIS layer", so a
        // variation Default can supply the DoT and an individual zone can override just the damage number
        // without restating type/interval. The Effective* accessors below apply the defaults a caller needs.

        // ── Damage over time ("the floor is lava") ──
        /// <summary>When true, players in the zone take a periodic hit every <see cref="DotIntervalSeconds"/>.</summary>
        public bool? DotEnabled { get; set; }

        /// <summary>Amount applied PER TICK. Flat points normally, or a percent of the player's max health when
        /// <see cref="DotPercent"/> is true (e.g. 5 = 5% of max health per tick).</summary>
        public double? DotDamage { get; set; }

        /// <summary>When true, <see cref="DotDamage"/> is a percent of the player's max health (drains Health).</summary>
        public bool? DotPercent { get; set; }

        /// <summary>Seconds between ticks (min 1). Applied by a per-player timer, independent of the 5s heartbeat.</summary>
        public double? DotIntervalSeconds { get; set; }

        /// <summary>ACE.Entity.Enum.DamageType as int (default Fire = 0x10). Stored as int to keep the model enum-free.
        /// Stamina/Mana drain those pools; Health = "drained"; percent mode forces Health.</summary>
        public int? DotDamageType { get; set; }

        // ── Suppression (regen) ──
        /// <summary>Master switch for the Suppression card: regen suppression for players in the zone.</summary>
        public bool? SuppressEnabled { get; set; }

        /// <summary>When true (the default while suppression is on), the Prodigal regen enchantment line
        /// (Regeneration 3731 / Rejuvenation 3732 / Mana Renewal 3725) is excluded from the player's regen
        /// math — a retail regen buff underneath still applies. Computed per vital tick, never cached.</summary>
        public bool? SuppressProdigal { get; set; }

        /// <summary>Scales players' natural POSITIVE regen ticks (all three vitals). 1 = normal, 0 = no regen.
        /// Never scales a negative (degen) tick — suppression must not become a shield.</summary>
        public double? SuppressRegenMult { get; set; }

        // SlowEnabled / SlowPercent / CharmEnabled were reserved here from 2026-07-30 with no reader and
        // no command - dead knobs, removed 2026-09-09. Stored JSON that still carries them loads fine
        // (Newtonsoft ignores unknown members by default).

        // ── Effective reads (the defaults that used to be field initializers) ──
        public bool EffectiveDotEnabled => DotEnabled == true;
        public double EffectiveDotDamage => DotDamage ?? 0.0;
        public bool EffectiveDotPercent => DotPercent == true;
        public double EffectiveDotIntervalSeconds => DotIntervalSeconds ?? 5.0;
        public int EffectiveDotDamageType => DotDamageType ?? 0x10;

        public bool EffectiveSuppressEnabled => SuppressEnabled == true;
        public bool EffectiveSuppressProdigal => SuppressProdigal ?? true;
        public double EffectiveSuppressRegenMult => System.Math.Clamp(SuppressRegenMult ?? 1.0, 0.0, 1.0);

        /// <summary>True if any effect is active — used to skip zones that author no effects during resolution.</summary>
        public bool AnyActive => DotEnabled == true || SuppressEnabled == true;

        /// <summary>True when nothing at all is authored at this layer.</summary>
        public bool IsEmpty =>
            DotEnabled == null && DotDamage == null && DotPercent == null && DotIntervalSeconds == null
            && DotDamageType == null && SuppressEnabled == null && SuppressProdigal == null
            && SuppressRegenMult == null;

        public ZoneEffects Clone() => new ZoneEffects
        {
            DotEnabled = DotEnabled, DotDamage = DotDamage, DotPercent = DotPercent,
            DotIntervalSeconds = DotIntervalSeconds, DotDamageType = DotDamageType,
            SuppressEnabled = SuppressEnabled, SuppressProdigal = SuppressProdigal,
            SuppressRegenMult = SuppressRegenMult,
        };

        // ── Un-authoring (2026-09-09) ──
        // The fields are nullable so a layer can inherit, but until these existed nothing could put a
        // field BACK to null: once a zone touched DoT it authored every DoT field forever, and the
        // variation Default underneath was unreachable again. Each clears one group to "not authored".

        /// <summary>Un-author every DoT field at this layer; the layer below shows through again.</summary>
        public void ClearDot()
        {
            DotEnabled = null; DotDamage = null; DotPercent = null;
            DotIntervalSeconds = null; DotDamageType = null;
        }

        /// <summary>Un-author every suppression field at this layer.</summary>
        public void ClearSuppress()
        {
            SuppressEnabled = null; SuppressProdigal = null; SuppressRegenMult = null;
        }

        /// <summary>Un-author everything at this layer.</summary>
        public void ClearAll()
        {
            ClearDot();
            ClearSuppress();
        }

        /// <summary>Per-FIELD layered merge: any authored (non-null) field on <paramref name="upper"/> wins,
        /// everything else falls through to <paramref name="lower"/>. Returns a new instance.</summary>
        public static ZoneEffects Merge(ZoneEffects lower, ZoneEffects upper)
        {
            if (lower == null) return upper?.Clone() ?? new ZoneEffects();
            if (upper == null) return lower.Clone();
            return new ZoneEffects
            {
                DotEnabled = upper.DotEnabled ?? lower.DotEnabled,
                DotDamage = upper.DotDamage ?? lower.DotDamage,
                DotPercent = upper.DotPercent ?? lower.DotPercent,
                DotIntervalSeconds = upper.DotIntervalSeconds ?? lower.DotIntervalSeconds,
                DotDamageType = upper.DotDamageType ?? lower.DotDamageType,
                SuppressEnabled = upper.SuppressEnabled ?? lower.SuppressEnabled,
                SuppressProdigal = upper.SuppressProdigal ?? lower.SuppressProdigal,
                SuppressRegenMult = upper.SuppressRegenMult ?? lower.SuppressRegenMult,
            };
        }
    }
}
