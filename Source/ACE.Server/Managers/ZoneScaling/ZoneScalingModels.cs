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
        // A. spawn-snapshot / attributes + vitals
        public const string Strength = "strength";
        public const string Endurance = "endurance";
        public const string Coordination = "coordination";
        public const string Quickness = "quickness";
        public const string Focus = "focus";
        public const string Self = "self";
        public const string MaxHealth = "max_health";
        public const string MaxStamina = "max_stamina";
        public const string MaxMana = "max_mana";

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

        // B2. offense (REPLACE the weenie's body-part DVal/DVar/DType AND weapon damage — one number for
        // "what this monster hits for"). attack_damage_type is a DamageType flag int (random pick per hit).
        public const string AttackDamage = "attack_damage";
        public const string AttackVariance = "attack_variance";
        public const string AttackDamageType = "attack_damage_type";
        public const string SpellDamageMult = "spell_damage_mult";

        // B3. incoming resists (REPLACE the creature-level ResistX multiplier; 1.0 neutral, <1 resists, >1 vuln).
        // Applies to melee AND magic damage of that element (same read point the weenie floats use).
        public const string ResistSlash = "resist_slash";
        public const string ResistPierce = "resist_pierce";
        public const string ResistBludgeon = "resist_bludgeon";
        public const string ResistFire = "resist_fire";
        public const string ResistCold = "resist_cold";
        public const string ResistAcid = "resist_acid";
        public const string ResistElectric = "resist_electric";
        public const string ResistNether = "resist_nether";

        // B4. per-element armor multiplier (REPLACE the creature-level ArmorModVsX; scales base armor vs that element).
        public const string ArmorVsSlash = "armor_vs_slash";
        public const string ArmorVsPierce = "armor_vs_pierce";
        public const string ArmorVsBludgeon = "armor_vs_bludgeon";
        public const string ArmorVsFire = "armor_vs_fire";
        public const string ArmorVsCold = "armor_vs_cold";
        public const string ArmorVsAcid = "armor_vs_acid";
        public const string ArmorVsElectric = "armor_vs_electric";
        public const string ArmorVsNether = "armor_vs_nether";

        // C. loot (rolled at corpse creation)
        public const string LootTierBonus = "loot_tier_bonus";
        public const string LootQuantityMult = "loot_quantity_mult";
        public const string RareChanceMult = "rare_chance_mult";     // multiplies the ACTUAL rare roll (Corpse.TryGenerateRare)
        public const string BonusCurrency = "bonus_currency";
        public const string LootQualityMult = "loot_quality_mult";   // multiplies TreasureDeath.LootQualityMod (better rolls within tier)

        // C2. loot post-roll mutations (applied per dropped item AFTER the factory rolls it — enhance, never replace)
        public const string WeaponStatMult = "weapon_stat_mult";     // scales rolled weapon damage/offense/defense bonuses
        public const string WeaponDamageMin = "weapon_damage_min";   // MELEE displayed hit range: Damage = max, variance derived so min hit = min
        public const string WeaponDamageMax = "weapon_damage_max";   //   (wins over the mult; set one of the pair = flat damage)
        public const string WeaponDamageRoll = "weapon_damage_roll"; // nonzero = each drop rolls a random sub-range WITHIN [min,max] instead
        public const string WeaponCasterElemMin = "weapon_caster_elem_min"; // CASTER elemental damage bonus OVERRIDE, rolled per drop; wire = bonus fraction (0.5 = +50%), ElementalDamageMod = 1 + this; only casters that already have a bonus
        public const string WeaponCasterElemMax = "weapon_caster_elem_max";
        public const string WeaponMissileElemMin = "weapon_missile_elem_min"; // MISSILE flat elemental damage bonus OVERRIDE (ElementalDamageBonus, a whole number), rolled per drop; only bows/xbows/atlatls that already have one
        public const string WeaponMissileElemMax = "weapon_missile_elem_max";
        public const string WeaponAttuned = "weapon_attuned";        // nonzero = rolled weapons drop Attuned (can't be traded/dropped)
        public const string WeaponBonded = "weapon_bonded";          // nonzero = rolled weapons drop Bonded (stay on death)
        public const string WeaponUnenchantable = "weapon_unenchantable"; // nonzero = rolled weapons drop unenchantable (ResistMagic 9999)
        public const string WeaponWorkmanshipMin = "weapon_workmanship_min"; // workmanship SET range on rolled WEAPONS only, clamp 1..10
        public const string WeaponWorkmanshipMax = "weapon_workmanship_max"; //   (set one of the pair = exact value, both = roll per drop)
        public const string ArmorAlBonus = "armor_al_bonus";         // flat AL added to rolled armor
        public const string ArmorAlMult = "armor_al_mult";           // AL multiplier on rolled armor
        public const string ArmorWorkmanshipMin = "armor_workmanship_min"; // workmanship SET range on rolled AL items only, clamp 1..10
        public const string ArmorWorkmanshipMax = "armor_workmanship_max"; //   (set one of the pair = exact value, both = roll per drop)
        public const string CoinMult = "coin_mult";                  // multiplies rolled pyreal stacks
        public const string ValueMult = "value_mult";                // multiplies vendor value of rolled loot
        public const string ValueMin = "value_min";                  // flat re-roll range for rolled loot value, clamped 0..1,000,000
        public const string ValueMax = "value_max";                  //   (wins over value_mult; set one of the pair = exact value)

        // C3. loot special-property rolls (independent 0..1 chance per eligible dropped item — "fun stuff")
        public const string WeaponProcChance = "weapon_proc_chance";     // Cast on Strike: stamp ProcSpell on a rolled melee/missile weapon
        public const string WeaponProcRate = "weapon_proc_rate";         // stamped on-hit ProcSpellRate (default 0.15)
        public const string WeaponProcSpell = "weapon_proc_spell";       // explicit SpellId to stamp; unset/0 = random elemental bolt leveled by loot tier
        public const string WeaponImbueChance = "weapon_imbue_chance";   // random imbue (rends / Critical Strike / Crippling Blow / Armor Rending)
        public const string WeaponSlayerChance = "weapon_slayer_chance"; // slayer vs the killed monster's own creature type
        public const string WeaponSlayerMin = "weapon_slayer_min";       // SlayerDamageBonus min (raw multiplier, floor 1.5, cap 10.0)
        public const string WeaponSlayerMax = "weapon_slayer_max";       // SlayerDamageBonus max
        public const string WeaponParagonChance = "weapon_paragon_chance"; // drops pre-Paragoned (+1 ItemMaxLevel, levels from use)
        public const string ArmorGemChance = "armor_gem_chance";         // one random Paragon gem spell stamped on rolled armor (tier weighted low)
        public const string WeaponCantripChance = "weapon_cantrip_chance"; // one EXTRA cantrip on a rolled weapon, from the zone's custom pool ONLY
        public const string ArmorCantripChance = "armor_cantrip_chance";   // one EXTRA cantrip on rolled armor/clothing/jewelry, custom pool ONLY
        // Card amounts are min/max PAIRS: set one = exact value, set both = each drop rolls uniformly
        // in the range, reversed bounds auto-swap (same semantics as weapon_damage_min/max).
        public const string WeaponCleaveChance = "weapon_cleave_chance";   // melee: swing hits extra targets in an arc
        public const string WeaponCleaveMin = "weapon_cleave_min";         //   extra targets, clamp 1..10 (default 1)
        public const string WeaponCleaveMax = "weapon_cleave_max";
        public const string WeaponSplitChance = "weapon_split_chance";     // bows: arrows split to hit extra targets
        public const string WeaponSplitMin = "weapon_split_min";           //   splits, clamp 1..10 (default 1)
        public const string WeaponSplitMax = "weapon_split_max";
        public const string WeaponSplitRange = "weapon_split_range";       //   split seek range meters, clamp 0..50 (default 8; >=11 trips the bowstring already-strung guard)
        public const string WeaponSplitDmg = "weapon_split_dmg";           //   damage fraction per split 0..1 (default 1)
        public const string WeaponBiteChance = "weapon_bite_chance";       // Biting Strike: crit chance override
        public const string WeaponBiteMin = "weapon_bite_min";             //   crit chance 0..1 (default 0.5; base is 0.1)
        public const string WeaponBiteMax = "weapon_bite_max";
        public const string WeaponCrushChance = "weapon_crush_chance";     // Crushing Blow: crit damage multiplier override
        public const string WeaponCrushMin = "weapon_crush_min";           //   multiplier, clamp 1..10 (default 3)
        public const string WeaponCrushMax = "weapon_crush_max";
        public const string WeaponArmorRendChance = "weapon_armor_rend_chance"; // stamps the REAL ArmorRending imbue + tunable amount
        public const string WeaponArmorRendMin = "weapon_armor_rend_min";  //   fraction of armor ignored 0..1 (default 0.5; skill imbue caps at 0.6)
        public const string WeaponArmorRendMax = "weapon_armor_rend_max";
        public const string WeaponShieldCleaveChance = "weapon_shield_cleave_chance"; // Shield Cleaving
        public const string WeaponShieldCleaveMin = "weapon_shield_cleave_min"; //   fraction of shield ignored 0..1 (default 0.5)
        public const string WeaponShieldCleaveMax = "weapon_shield_cleave_max";
        public const string WeaponPhantomChance = "weapon_phantom_chance"; // Phantom (hollow): hits ignore BOTH magic armor (impen/banes) and magic resist (prots)
        public const string WeaponRendPowerMin = "weapon_rend_power_min";  // rend strength as a DIRECT vuln bonus, rolled per drop; wire 1.5..10.0 = +150%..+1000% (rendingMod = 1 + this)
        public const string WeaponRendPowerMax = "weapon_rend_power_max";
        public const string WeaponHiltChance = "weapon_hilt_chance";       // melee drops with a Fine Bandit Blade Hilt pre-attached (full recipe minus ManaStoneDestroyChance)
        public const string WeaponBowstringChance = "weapon_bowstring_chance"; // bow drops restrung with a Finely Oiled Bowstring (full recipe)

        // C4. structured loot set + QB scaling (T11+ endgame loot; see ACE_Loot_Systems_DeepDive doc §12-13)
        public const string LootSetEnabled = "loot_set_enabled";           // nonzero = every governed kill drops the structured BLANK gear set
        public const string LootSetArmor = "loot_set_armor";               // armor pieces per kill (default 3)
        public const string LootSetJewelry = "loot_set_jewelry";           // jewelry/trinket pieces per kill (default 2)
        public const string LootSetCloaks = "loot_set_cloaks";             // cloaks per kill (default 1)
        public const string QbStepSize = "qb_step_size";                   // killer QB per progression step (default 1000)
        public const string QbQualityPerStep = "qb_quality_per_step";      // LootQualityMod added per QB step (setting this ENABLES QB scaling; suggested 0.05)
        public const string QbMaxSteps = "qb_max_steps";                   // QB step cap (default 20)
        public const string QbQuantityPerStep = "qb_quantity_per_step";    // RESERVED: no effect yet (quantity semantics undecided)

        public static readonly string[] All =
        {
            Strength, Endurance, Coordination, Quickness, Focus, Self, MaxHealth, MaxStamina, MaxMana,
            AttackSkill, MeleeDefense, MissileDefense, MagicDefense, DamageRating,
            DamageResistRating, ArmorLevel, DamageTakenMult, VulnCap, PercentHpBase,
            AttackDamage, AttackVariance, AttackDamageType, SpellDamageMult,
            ResistSlash, ResistPierce, ResistBludgeon, ResistFire, ResistCold, ResistAcid, ResistElectric, ResistNether,
            ArmorVsSlash, ArmorVsPierce, ArmorVsBludgeon, ArmorVsFire, ArmorVsCold, ArmorVsAcid, ArmorVsElectric, ArmorVsNether,
            LootTierBonus, LootQuantityMult, RareChanceMult, BonusCurrency, LootQualityMult,
            WeaponStatMult, WeaponDamageMin, WeaponDamageMax, WeaponDamageRoll, WeaponCasterElemMin, WeaponCasterElemMax,
            WeaponMissileElemMin, WeaponMissileElemMax,
            WeaponAttuned, WeaponBonded, WeaponUnenchantable, WeaponWorkmanshipMin, WeaponWorkmanshipMax,
            ArmorAlBonus, ArmorAlMult, ArmorWorkmanshipMin, ArmorWorkmanshipMax,
            CoinMult, ValueMult, ValueMin, ValueMax,
            WeaponProcChance, WeaponProcRate, WeaponProcSpell, WeaponImbueChance,
            WeaponSlayerChance, WeaponSlayerMin, WeaponSlayerMax, WeaponParagonChance, ArmorGemChance,
            WeaponCantripChance, ArmorCantripChance,
            WeaponCleaveChance, WeaponCleaveMin, WeaponCleaveMax,
            WeaponSplitChance, WeaponSplitMin, WeaponSplitMax, WeaponSplitRange, WeaponSplitDmg,
            WeaponBiteChance, WeaponBiteMin, WeaponBiteMax,
            WeaponCrushChance, WeaponCrushMin, WeaponCrushMax,
            WeaponArmorRendChance, WeaponArmorRendMin, WeaponArmorRendMax,
            WeaponShieldCleaveChance, WeaponShieldCleaveMin, WeaponShieldCleaveMax,
            WeaponPhantomChance, WeaponRendPowerMin, WeaponRendPowerMax,
            WeaponHiltChance, WeaponBowstringChance,
            LootSetEnabled, LootSetArmor, LootSetJewelry, LootSetCloaks,
            QbStepSize, QbQualityPerStep, QbMaxSteps, QbQuantityPerStep,
        };
    }

    /// <summary>
    /// Per-body-part overrides for a zone variant. Body-part collections are SHARED between all live
    /// instances and the weenie (WeenieConverter references them), so these are consumed by READ-TIME
    /// hooks only — the weenie data is never mutated. All fields nullable = "not overridden".
    /// Precedence at read time: per-part override &gt; all-parts scalar (armor_level / attack_*) &gt; weenie.
    /// </summary>
    public class ZoneBodyPart
    {
        public double? Armor { get; set; }        // per-part base armor (base_Armor)
        public double? Damage { get; set; }       // DVal; 0 stops this part from attacking
        public double? Variance { get; set; }     // DVar (0..1)
        public int? DamageType { get; set; }      // DamageType flag int (random pick per hit if multi-flag)

        public bool IsEmpty => Armor == null && Damage == null && Variance == null && DamageType == null;

        public ZoneBodyPart Clone() => new ZoneBodyPart { Armor = Armor, Damage = Damage, Variance = Variance, DamageType = DamageType };
    }

    /// <summary>
    /// One entry in a zone's bonus-currency drop table: a stack of Wcid x Amount injected onto every governed
    /// corpse with an independent per-kill Chance (0..1]. Entries are additive with each other and with the
    /// legacy single-token bonus_currency stat. Loot-table independent — the weenie is never touched.
    /// </summary>
    public class ZoneCurrencyDrop
    {
        public uint Wcid { get; set; }
        public int Amount { get; set; } = 1;
        public double Chance { get; set; } = 1.0;

        /// <summary>true = deliver straight into the killing player's inventory (with a chat message);
        /// false (default) = drop onto the corpse. Direct delivery falls back to the corpse when the
        /// killer isn't a player or their inventory is full.</summary>
        public bool Direct { get; set; }

        public ZoneCurrencyDrop Clone() => new ZoneCurrencyDrop { Wcid = Wcid, Amount = Amount, Chance = Chance, Direct = Direct };
    }

    /// <summary>
    /// Guard rails for the generic prop-stamping system: property ids that must never be stamped onto a live
    /// monster because they are structural/identity values (would corrupt resolution or object behavior) rather
    /// than tuning knobs. Enforced both at command time and at stamp time.
    /// </summary>
    public static class ZonePropGuard
    {
        // PropertyInt ids
        private static readonly HashSet<int> BlockedInts = new()
        {
            1,      // ItemType — object identity
            9007,   // WeenieType (reserved id) — object identity
            9043,   // PrestigeLevel — owned by PrestigeManager's scaling bookkeeping
        };

        // PropertyBool ids
        private static readonly HashSet<int> BlockedBools = new()
        {
            50047,  // ExemptFromZoneScaling — stamping this from a zone profile is a resolve paradox
        };

        public static bool IsBlockedInt(int id) => BlockedInts.Contains(id);
        public static bool IsBlockedInt64(int id) => false;
        public static bool IsBlockedFloat(int id) => false;
        public static bool IsBlockedBool(int id) => BlockedBools.Contains(id);
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

        /// <summary>Per-body-part overrides, keyed by (int)CombatBodyPart. Missing on deserialize of older
        /// profiles = empty dict (backward compatible). Consumed by read-time hooks.</summary>
        public Dictionary<int, ZoneBodyPart> BodyParts { get; set; } = new();

        /// <summary>Custom cantrip SpellIds this zone can roll as the EXTRA loot cantrip (alongside the
        /// retail tables; see ZoneStat.CustomCantripWeight). Owner-authored spell ids — stamped as-is.
        /// Missing on deserialize of older profiles = empty list (backward compatible).</summary>
        public List<int> CustomCantrips { get; set; } = new();

        /// <summary>Bonus-currency drop table: each entry rolls independently on every governed kill and
        /// injects a stack onto the corpse. Missing on deserialize of older profiles = empty list.</summary>
        public List<ZoneCurrencyDrop> CurrencyDrops { get; set; } = new();

        /// <summary>Generic property overrides STAMPED onto each governed monster at (re)spawn
        /// (ApplyZoneSnapshot). Int/Float/Bool/Int64 biota collections are per-instance clones, so
        /// stamping is safe and reverts on respawn. Keyed by raw property id.</summary>
        public Dictionary<int, long> PropInts { get; set; } = new();
        public Dictionary<int, long> PropInt64s { get; set; } = new();
        public Dictionary<int, double> PropFloats { get; set; } = new();
        public Dictionary<int, bool> PropBools { get; set; } = new();

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

        /// <summary>Per-WCID overrides: if a mob's WeenieClassId has an entry here, it's used as a
        /// COMPLETE standalone stat set (NOT layered on Minion/Boss) — a full replacement, not a delta.
        /// Any WCID without an entry falls back to the Minion/Boss resolution as before. Missing on
        /// deserialize of older profiles = empty dict (backward compatible, no migration needed).</summary>
        public Dictionary<uint, ZoneVariantProfile> WcidOverrides { get; set; } = new();

        public ZoneVariantProfile Variant(ZoneVariant v) => v == ZoneVariant.Boss ? Boss : Minion;

        /// <summary>Resolves the WCID override if one exists for this creature, else falls back to the
        /// normal Minion/Boss variant. Auto-creates the override bucket on first access when `create`.</summary>
        public ZoneVariantProfile VariantForWcid(uint wcid, ZoneVariant fallback, bool create = false)
        {
            if (WcidOverrides.TryGetValue(wcid, out var v))
                return v;
            if (create)
            {
                v = new ZoneVariantProfile();
                WcidOverrides[wcid] = v;
                return v;
            }
            return Variant(fallback);
        }

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
        private readonly Dictionary<int, ZoneBodyPart> _bodyParts;

        public EvaluatedProfile(string scopeKey, int tier, ZoneVariant variant, Dictionary<string, double> values,
            Dictionary<int, ZoneBodyPart> bodyParts = null,
            Dictionary<int, long> propInts = null, Dictionary<int, long> propInt64s = null,
            Dictionary<int, double> propFloats = null, Dictionary<int, bool> propBools = null,
            List<int> customCantrips = null, List<ZoneCurrencyDrop> currencyDrops = null)
        {
            ScopeKey = scopeKey;
            Tier = tier;
            Variant = variant;
            _values = values ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _bodyParts = bodyParts;
            PropInts = propInts;
            PropInt64s = propInt64s;
            PropFloats = propFloats;
            PropBools = propBools;
            CustomCantrips = customCantrips;
            CurrencyDrops = currencyDrops;
        }

        /// <summary>Custom cantrip SpellIds for the extra-loot-cantrip roll (may be null = none defined).</summary>
        public IReadOnlyList<int> CustomCantrips { get; }

        /// <summary>Bonus-currency drop table entries (may be null = none defined).</summary>
        public IReadOnlyList<ZoneCurrencyDrop> CurrencyDrops { get; }

        /// <summary>Per-part override for a CombatBodyPart key, or null. Read-time hot path: one dict lookup.</summary>
        public ZoneBodyPart GetBodyPart(int combatBodyPart)
            => _bodyParts != null && _bodyParts.TryGetValue(combatBodyPart, out var p) ? p : null;

        public bool HasBodyParts => _bodyParts != null && _bodyParts.Count > 0;
        public IReadOnlyDictionary<int, ZoneBodyPart> BodyParts => _bodyParts;

        /// <summary>Spawn-time prop stamps (may be null = none defined).</summary>
        public IReadOnlyDictionary<int, long> PropInts { get; }
        public IReadOnlyDictionary<int, long> PropInt64s { get; }
        public IReadOnlyDictionary<int, double> PropFloats { get; }
        public IReadOnlyDictionary<int, bool> PropBools { get; }

        /// <summary>True if the winning profile actually defines this stat (otherwise the consumer keeps its own value).</summary>
        public bool Has(string statKey) => _values.ContainsKey(statKey);

        public double Get(string statKey, double fallback = 0.0)
            => _values.TryGetValue(statKey, out var v) ? v : fallback;

        public IReadOnlyDictionary<string, double> Values => _values;
    }
}
