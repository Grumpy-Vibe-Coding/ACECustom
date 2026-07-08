using System;
using System.Collections.Generic;

namespace ACE.Server.Managers.ZoneScaling
{
    /// <summary>
    /// Granularity of a zone-scaling profile. Resolution is most-specific-wins:
    /// LandblockVariation &gt; Landblock &gt; Zone &gt; Global.
    /// </summary>
    public enum ZoneScopeType
    {
        Global = 0,
        Zone = 1,
        Landblock = 2,
        LandblockVariation = 3,
    }

    /// <summary>Which stat variant a curve belongs to (bosses carry PropertyBool.IsEmpowerSource).</summary>
    public enum ZoneVariant
    {
        Minion = 0,
        Boss = 1,
    }

    /// <summary>
    /// Canonical stat keys a zone profile can define. Kept as string constants (not an enum) so the JSON
    /// store stays forward-compatible if new keys are added without a migration.
    /// </summary>
    public static class ZoneStat
    {
        // A. spawn-snapshot / vital
        public const string MaxHealth = "max_health";

        // B. live per-hit
        public const string AttackSkill = "attack_skill";
        public const string MeleeDefense = "melee_defense";
        public const string MissileDefense = "missile_defense";
        public const string MagicDefense = "magic_defense";
        public const string DamageRating = "damage_rating";
        public const string DamageResistRating = "damage_resist_rating";
        public const string ArmorLevel = "armor_level";
        public const string DamageTakenMult = "damage_taken_mult";
        public const string VulnCap = "vuln_cap";
        public const string PercentHpBase = "percent_hp_base";

        // C. loot (rolled at corpse creation)
        public const string LootTierBonus = "loot_tier_bonus";
        public const string LootQuantityMult = "loot_quantity_mult";
        public const string RareChanceMult = "rare_chance_mult";
        public const string BonusCurrency = "bonus_currency";

        public static readonly string[] All =
        {
            MaxHealth, AttackSkill, MeleeDefense, MissileDefense, MagicDefense, DamageRating,
            DamageResistRating, ArmorLevel, DamageTakenMult, VulnCap, PercentHpBase,
            LootTierBonus, LootQuantityMult, RareChanceMult, BonusCurrency,
        };
    }

    /// <summary>
    /// One stat's value across tiers: a default curve (base at tier 1, grown per-tier) plus optional
    /// per-tier pinned overrides. Additive =&gt; base + growth*(tier-1); otherwise base * growth^(tier-1).
    /// </summary>
    public class StatCurve
    {
        public double Base { get; set; }
        public double Growth { get; set; } = 1.0;
        public bool Additive { get; set; }

        /// <summary>tier -&gt; pinned value; when present for a tier, replaces the curve for that tier only.</summary>
        public Dictionary<int, double> Overrides { get; set; }

        public double Evaluate(int tier)
        {
            if (Overrides != null && Overrides.TryGetValue(tier, out var pinned))
                return pinned;

            var t = Math.Max(1, tier);
            return Additive
                ? Base + Growth * (t - 1)
                : Base * Math.Pow(Growth, t - 1);
        }
    }

    /// <summary>The set of stat curves for one variant (minion or boss) of a zone profile.</summary>
    public class ZoneVariantProfile
    {
        public Dictionary<string, StatCurve> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string statKey, out StatCurve curve) => Stats.TryGetValue(statKey, out curve);
    }

    /// <summary>
    /// A zone-scaling profile bound to a scope. Holds a minion + boss variant, each a bundle of stat curves.
    /// Persisted as JSON in the shard config store; mutated live by /zonescale and the plugin.
    /// </summary>
    public class ZoneScalingProfile
    {
        public ZoneScopeType ScopeType { get; set; }
        public int? Landblock { get; set; }        // ushort landblock id (e.g. 0xF559)
        public int? Variation { get; set; }        // for LandblockVariation scope
        public string ZoneName { get; set; }       // for Zone scope (e.g. "tou_tou")
        public bool Enabled { get; set; } = true;
        public string Notes { get; set; }

        public ZoneVariantProfile Minion { get; set; } = new();
        public ZoneVariantProfile Boss { get; set; } = new();

        public ZoneVariantProfile Variant(ZoneVariant v) => v == ZoneVariant.Boss ? Boss : Minion;

        /// <summary>Canonical scope key used for the registry and memo cache.</summary>
        public string ScopeKey() => MakeScopeKey(ScopeType, Landblock, Variation, ZoneName);

        public static string MakeScopeKey(ZoneScopeType type, int? landblock, int? variation, string zoneName)
        {
            switch (type)
            {
                case ZoneScopeType.Global: return "global";
                case ZoneScopeType.Zone: return "zone:" + (zoneName ?? "").ToLowerInvariant();
                case ZoneScopeType.Landblock: return "lb:" + (landblock ?? 0).ToString("X4");
                case ZoneScopeType.LandblockVariation:
                    return "lbvar:" + (landblock ?? 0).ToString("X4") + ":v" + (variation ?? 0);
                default: return "global";
            }
        }
    }

    /// <summary>
    /// A profile resolved for a specific creature: the winning scope evaluated at the creature's tier and variant.
    /// Consumers read stats from here. Null return from the manager means "not scaled" (leave weenie stats).
    /// </summary>
    public class EvaluatedProfile
    {
        public string ScopeKey { get; set; }
        public int Tier { get; set; }
        public ZoneVariant Variant { get; set; }
        private readonly Dictionary<string, double> _values;

        public EvaluatedProfile(string scopeKey, int tier, ZoneVariant variant, Dictionary<string, double> values)
        {
            ScopeKey = scopeKey;
            Tier = tier;
            Variant = variant;
            _values = values ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>True if the winning profile actually defines this stat (otherwise the consumer keeps its own value).</summary>
        public bool Has(string statKey) => _values.ContainsKey(statKey);

        public double Get(string statKey, double fallback = 0.0)
            => _values.TryGetValue(statKey, out var v) ? v : fallback;

        public IReadOnlyDictionary<string, double> Values => _values;
    }
}
