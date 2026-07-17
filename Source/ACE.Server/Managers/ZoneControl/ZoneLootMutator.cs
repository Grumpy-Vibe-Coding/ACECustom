using System;
using System.Collections.Generic;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Zone Control loot: post-roll mutations applied per dropped item AFTER LootGenerationFactory has finished
    /// rolling it. Enhances the monster's own loot, never replaces it — only the per-instance dropped object is
    /// touched (weenies and treasure tables never are) — enhance-don't-replace, per-drop instance only.
    /// </summary>
    public static class ZoneLootMutator
    {
        /// <summary>
        /// Mutations for an item rolled from the death-treasure table: weapon stat scaling, armor AL,
        /// workmanship, coin stacks, vendor value, plus the low-chance special-property rolls.
        /// <paramref name="killed"/> is the dying monster (slayer type source); <paramref name="lootTier"/>
        /// is the effective treasure tier (levels the default proc spell).
        /// </summary>
        public static void MutateLootItem(WorldObject wo, EvaluatedProfile p, Creature killed = null, int lootTier = 1)
        {
            if (wo == null || p == null)
                return;

            // currency first: coin stacks take only the coin knob (their Value tracks stack size)
            if (wo.WeenieType == WeenieType.Coin)
            {
                if (p.Has(ZoneStat.CoinMult))
                    ScaleStack(wo, p.Get(ZoneStat.CoinMult));
                return;
            }

            // Zone Control origin: record where this item dropped as a readable sentence appended to the
            // item's description (p.ScopeKey = the winning zone's name; the variation the zone matched on =
            // the creature's effective variation; killed = the dropping monster). Every non-coin drop gets it.
            if (!string.IsNullOrEmpty(p.ScopeKey))
            {
                var variation = killed != null ? ZoneControlManager.GetEffectiveVariation(killed) : 0;
                var origin = killed != null
                    ? $"Dropped by {killed.Name} in the {p.ScopeKey} zone (v{variation})."
                    : $"Dropped in the {p.ScopeKey} zone (v{variation}).";
                wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? origin : wo.LongDesc + "\n\n" + origin;
            }

            if (p.Has(ZoneStat.WeaponStatMult))
                MutateWeaponStats(wo, p.Get(ZoneStat.WeaponStatMult));

            // Caster elemental damage OVERRIDE: set the elemental damage bonus to a value rolled per drop,
            // only on casters that ALREADY have one (the > 1.0 guard — never injects a bonus into a blank
            // wand). Wire = bonus fraction (0.5 = +50%), so ElementalDamageMod = 1 + roll. Runs after
            // weapon_stat_mult so it's an authoritative set (wins over the broad scaler). Only the spell
            // element matching the wand benefits, and vs-players is auto-halved at hit time.
            if (wo is Caster && wo.ElementalDamageMod.HasValue && wo.ElementalDamageMod.Value > 1.0
                && (p.Has(ZoneStat.WeaponCasterElemMin) || p.Has(ZoneStat.WeaponCasterElemMax)))
                wo.ElementalDamageMod = 1.0 + RollRange(p, ZoneStat.WeaponCasterElemMin, ZoneStat.WeaponCasterElemMax, 0.15, 0.0, 1.0);

            // Missile elemental damage OVERRIDE: set the FLAT elemental damage bonus (ElementalDamageBonus,
            // a whole number added to the matching-element missile hit) to a value rolled per drop, only on
            // launchers that ALREADY have one (the > 0 guard — never injects into a plain bow). Runs after
            // weapon_stat_mult so it's an authoritative set. (Melee has no elemental bonus property.)
            if (wo is MissileLauncher && wo.ElementalDamageBonus.HasValue && wo.ElementalDamageBonus.Value > 0
                && (p.Has(ZoneStat.WeaponMissileElemMin) || p.Has(ZoneStat.WeaponMissileElemMax)))
                wo.ElementalDamageBonus = (int)Math.Round(
                    RollRange(p, ZoneStat.WeaponMissileElemMin, ZoneStat.WeaponMissileElemMax, 5, 0, 100));

            // flat damage range override — melee only (missile/caster damage is a 1.xx multiplier mod,
            // not a flat number; use weapon_stat_mult for those). Wins over the mult. The pair defines the
            // weapon's DISPLAYED hit range: Damage = max hit, DamageVariance derived so min hit = min —
            // every drop appraises exactly min - max. Reversed bounds always auto-swap (a 2000-1000 zone
            // still yields 1000-2000 weapons). weapon_damage_roll nonzero = each drop instead rolls a
            // random sub-range within [min,max] (two uniform picks, sorted).
            if (wo is MeleeWeapon && wo.Damage.HasValue && wo.Damage.Value > 0 &&
                (p.Has(ZoneStat.WeaponDamageMin) || p.Has(ZoneStat.WeaponDamageMax)))
            {
                var lo = p.Has(ZoneStat.WeaponDamageMin) ? p.Get(ZoneStat.WeaponDamageMin) : p.Get(ZoneStat.WeaponDamageMax);
                var hi = p.Has(ZoneStat.WeaponDamageMax) ? p.Get(ZoneStat.WeaponDamageMax) : lo;
                if (hi < lo)
                    (lo, hi) = (hi, lo);
                var dmgHi = Math.Max(1, (int)Math.Round(hi));
                var dmgLo = Math.Clamp((int)Math.Round(lo), 1, dmgHi);

                if (p.Get(ZoneStat.WeaponDamageRoll, 0) != 0 && dmgHi > dmgLo)
                {
                    var a = ThreadSafeRandom.Next(dmgLo, dmgHi);
                    var b = ThreadSafeRandom.Next(dmgLo, dmgHi);
                    dmgLo = Math.Min(a, b);
                    dmgHi = Math.Max(a, b);
                }

                wo.Damage = dmgHi;
                wo.DamageVariance = 1.0 - (double)dmgLo / dmgHi;
            }

            var isWeapon = wo is MeleeWeapon || wo is MissileLauncher || wo is Caster;

            // forced properties on rolled WEAPONS only (never other items)
            if (isWeapon)
            {
                if (p.Get(ZoneStat.WeaponAttuned, 0) != 0)
                    wo.Attuned = AttunedStatus.Attuned;
                if (p.Get(ZoneStat.WeaponBonded, 0) != 0)
                    wo.Bonded = BondedStatus.Bonded;
                if (p.Get(ZoneStat.WeaponUnenchantable, 0) != 0)
                    wo.ResistMagic = 9999;
            }

            if (wo.ArmorLevel.HasValue && wo.ArmorLevel.Value > 0 &&
                (p.Has(ZoneStat.ArmorAlMult) || p.Has(ZoneStat.ArmorAlBonus)))
            {
                var al = (double)wo.ArmorLevel.Value;
                var mult = p.Get(ZoneStat.ArmorAlMult, 1.0);
                if (mult > 0)
                    al *= mult;
                al += p.Get(ZoneStat.ArmorAlBonus, 0.0);
                wo.ArmorLevel = Math.Max(0, (int)Math.Round(al));
            }

            // workmanship SET range, split by item kind: the weapon pair touches only weapons, the armor
            // pair only AL-bearing non-weapons (armor, shields, AL clothing). Replaces the rolled value —
            // one of the pair set = exact, both = uniform roll per drop, reversed auto-swap, clamp 1..10.
            if (wo.ItemWorkmanship.HasValue)
            {
                if (isWeapon && (p.Has(ZoneStat.WeaponWorkmanshipMin) || p.Has(ZoneStat.WeaponWorkmanshipMax)))
                    wo.ItemWorkmanship = (int)Math.Round(
                        RollRange(p, ZoneStat.WeaponWorkmanshipMin, ZoneStat.WeaponWorkmanshipMax, 1, 1, 10));
                else if (!isWeapon && wo.ArmorLevel.HasValue && wo.ArmorLevel.Value > 0 &&
                         (p.Has(ZoneStat.ArmorWorkmanshipMin) || p.Has(ZoneStat.ArmorWorkmanshipMax)))
                    wo.ItemWorkmanship = (int)Math.Round(
                        RollRange(p, ZoneStat.ArmorWorkmanshipMin, ZoneStat.ArmorWorkmanshipMax, 1, 1, 10));
            }

            if (p.Has(ZoneStat.ValueMult) && wo.Value.HasValue)
            {
                var m = p.Get(ZoneStat.ValueMult);
                if (m > 0)
                    wo.Value = (int)Math.Round(wo.Value.Value * m);
            }

            // flat value range override, clamped 0..1,000,000 (wins over value_mult; coins already returned above)
            if (wo.Value.HasValue && (p.Has(ZoneStat.ValueMin) || p.Has(ZoneStat.ValueMax)))
            {
                var lo = p.Has(ZoneStat.ValueMin) ? p.Get(ZoneStat.ValueMin) : p.Get(ZoneStat.ValueMax);
                var hi = p.Has(ZoneStat.ValueMax) ? p.Get(ZoneStat.ValueMax) : lo;
                if (hi < lo)
                    (lo, hi) = (hi, lo);
                var valLo = Math.Clamp((int)Math.Round(lo), 0, 1_000_000);
                var valHi = Math.Clamp((int)Math.Round(hi), valLo, 1_000_000);
                wo.Value = ThreadSafeRandom.Next(valLo, valHi);
            }

            TrySpecialRolls(wo, p, killed, lootTier);
        }

        // ── special-property rolls ("fun stuff": independent 0..1 chance each; an item can win several) ──

        // ACE.Server-only custom float prop ids (no ACE.Entity enum change — same pattern as the
        // zone-cantrip prop block 50200-50399). Read hooks: WorldObject_Weapon.GetWeaponResistanceModifier
        // (rend power) and DamageEvent.DoCalculateDamage (armor rend amount).
        public const int RendingModOverridePropId = 9056;
        public const int ArmorRendOverridePropId = 9057;

        /// <summary>
        /// Card amount pairs: min only / max only = exact value, both = uniform roll in the range each
        /// drop, reversed bounds auto-swap, everything clamped to [lo, hi]. Neither set = def.
        /// </summary>
        private static double RollRange(EvaluatedProfile p, string minStat, string maxStat, double def, double lo, double hi)
        {
            var a = p.Has(minStat) ? p.Get(minStat) : (p.Has(maxStat) ? p.Get(maxStat) : def);
            var b = p.Has(maxStat) ? p.Get(maxStat) : a;
            if (b < a)
                (a, b) = (b, a);
            a = Math.Clamp(a, lo, hi);
            b = Math.Clamp(b, lo, hi);
            return a >= b ? a : ThreadSafeRandom.Next((float)a, (float)b);
        }

        // Split-arrow props (already in ACE.Entity — the custom bowstring system)
        private const int SplitArrowsBoolId = 9030;      // PropertyBool.SplitArrows
        private const int SplitArrowCountIntId = 9031;   // PropertyInt.SplitArrowCount
        private const int SplitArrowRangeFloatId = 9032; // PropertyFloat.SplitArrowRange
        private const int SplitArrowDmgFloatId = 9033;   // PropertyFloat.SplitArrowDamageMultiplier

        // Non-elemental imbues ALL excluded from the Rending card pool (owner 2026-07-13):
        // CripplingBlow/CriticalStrike compete with the Crushing Blow / Biting Strike cards via
        // Math.Max (6.0 crit mult / 50% crit chance at endgame skill), and ArmorRending has its own
        // card — the pool is rends matching the weapon's damage type, nothing else.
        private const ImbuedEffectType AllRends =
            ImbuedEffectType.SlashRending | ImbuedEffectType.PierceRending | ImbuedEffectType.BludgeonRending |
            ImbuedEffectType.AcidRending | ImbuedEffectType.ColdRending | ImbuedEffectType.ElectricRending |
            ImbuedEffectType.FireRending | ImbuedEffectType.NetherRending;

        /// <summary>Rend imbues that MATCH the weapon's own damage type (owner rule: a fire sword can only
        /// get Fire Rend — a rend for an element the weapon can't deal is dead weight). Multi-type weapons
        /// (e.g. slash/pierce) return every matching rend.</summary>
        private static List<ImbuedEffectType> GetMatchingRends(DamageType dt)
        {
            var rends = new List<ImbuedEffectType>();
            if (dt.HasFlag(DamageType.Slash)) rends.Add(ImbuedEffectType.SlashRending);
            if (dt.HasFlag(DamageType.Pierce)) rends.Add(ImbuedEffectType.PierceRending);
            if (dt.HasFlag(DamageType.Bludgeon)) rends.Add(ImbuedEffectType.BludgeonRending);
            if (dt.HasFlag(DamageType.Acid)) rends.Add(ImbuedEffectType.AcidRending);
            if (dt.HasFlag(DamageType.Cold)) rends.Add(ImbuedEffectType.ColdRending);
            if (dt.HasFlag(DamageType.Electric)) rends.Add(ImbuedEffectType.ElectricRending);
            if (dt.HasFlag(DamageType.Fire)) rends.Add(ImbuedEffectType.FireRending);
            if (dt.HasFlag(DamageType.Nether)) rends.Add(ImbuedEffectType.NetherRending);
            return rends;
        }

        // Default Cast-on-Strike pool: offensive bolts, leveled by the loot tier (index 0 = level I).
        private static readonly List<SpellId>[] ProcSpellPool =
        {
            SpellLevelProgression.FlameBolt, SpellLevelProgression.FrostBolt, SpellLevelProgression.AcidStream,
            SpellLevelProgression.LightningBolt, SpellLevelProgression.ShockWave, SpellLevelProgression.ForceBolt,
            SpellLevelProgression.WhirlingBlade, SpellLevelProgression.HarmOther,
        };

        // The Paragon gem spell lists (same spells the vendor gems add to armor), tiers I..V each.
        private static readonly List<SpellId>[] ParagonGemPool =
        {
            SpellLevelProgression.ParagonsDualWieldMastery, SpellLevelProgression.ParagonsFinesseWeaponMastery,
            SpellLevelProgression.ParagonsHeavyWeaponMastery, SpellLevelProgression.ParagonsLifeMagicMastery,
            SpellLevelProgression.ParagonsLightWeaponMastery, SpellLevelProgression.ParagonsMissileWeaponMastery,
            SpellLevelProgression.ParagonsRecklessnessMastery, SpellLevelProgression.ParagonsSneakAttackMastery,
            SpellLevelProgression.ParagonsTwoHandedCombatMastery, SpellLevelProgression.ParagonsVoidMagicMastery,
            SpellLevelProgression.ParagonsWarMagicMastery, SpellLevelProgression.ParagonsDirtyFightingMastery,
            SpellLevelProgression.ParagonsWillpower, SpellLevelProgression.ParagonsCoordination,
            SpellLevelProgression.ParagonsEndurance, SpellLevelProgression.ParagonsFocus,
            SpellLevelProgression.ParagonQuickness, SpellLevelProgression.ParagonsStrength,
            SpellLevelProgression.ParagonsStamina, SpellLevelProgression.ParagonsCriticalDamageBoost,
            SpellLevelProgression.ParagonsCriticalDamageReduction, SpellLevelProgression.ParagonsDamageBoost,
            SpellLevelProgression.ParagonsDamageReduction, SpellLevelProgression.ParagonsMana,
        };

        // Gem tier I..V weights (sum 100) — weighted toward the low tiers.
        private static readonly int[] GemTierWeights = { 40, 25, 17, 11, 7 };

        private static void TrySpecialRolls(WorldObject wo, EvaluatedProfile p, Creature killed, int lootTier)
        {
            var isMelee = wo is MeleeWeapon;
            var isMissile = wo is MissileLauncher;
            var isWeapon = isMelee || isMissile || wo is Caster;

            // Cast on Strike (melee/missile — procs fire from the swing path; never clobber an existing proc)
            if ((isMelee || isMissile) && wo.ProcSpell == null && Won(p, ZoneStat.WeaponProcChance))
            {
                var spellId = (uint)Math.Round(p.Get(ZoneStat.WeaponProcSpell, 0));
                if (spellId == 0)
                {
                    var list = ProcSpellPool[ThreadSafeRandom.Next(0, ProcSpellPool.Length - 1)];
                    spellId = (uint)list[Math.Clamp(lootTier, 1, list.Count) - 1];
                }
                wo.ProcSpell = spellId;
                wo.ProcSpellRate = Math.Clamp(p.Get(ZoneStat.WeaponProcRate, 0.15), 0.0, 1.0);
                wo.ProcSpellSelfTargeted = false;
            }

            // Rending card: a rend imbue matching the weapon's own damage type (fire sword or fire wand
            // -> Fire Rend; only if the natural roll didn't already produce an imbue). Casters ARE eligible
            // (elemental rends reduce the target's resistance, boosting magic damage). Weapons with no
            // resolvable damage type (e.g. plain bows — element comes from the ammo — or generic casters)
            // roll nothing via the empty-pool guard below.
            if (isWeapon && wo.ImbuedEffect == ImbuedEffectType.Undef && Won(p, ZoneStat.WeaponImbueChance))
            {
                var candidates = GetMatchingRends(wo.W_DamageType);
                if (candidates.Count > 0)
                    wo.ImbuedEffect = candidates[ThreadSafeRandom.Next(0, candidates.Count - 1)];
            }

            // rend power: per-weapon rend strength as a DIRECT vuln bonus, rolled per drop in [min, max]
            // on any rend-carrying weapon in the zone (whether from our roll above or the natural loot
            // roll). Wire value = vuln fraction (150% = 1.5 = the normal rend cap/floor, up to 1000% =
            // 10.0); the engine sets rendingMod = 1 + this, replacing the skill formula (and its 2.5 cap).
            if (isWeapon && (p.Has(ZoneStat.WeaponRendPowerMin) || p.Has(ZoneStat.WeaponRendPowerMax))
                && (wo.GetImbuedEffects() & AllRends) != 0)
                wo.SetProperty((PropertyFloat)RendingModOverridePropId,
                    RollRange(p, ZoneStat.WeaponRendPowerMin, ZoneStat.WeaponRendPowerMax, 1.5, 1.5, 10.0));

            // Cleaving (melee): swing hits extra targets in a 180-degree arc
            if (isMelee && Won(p, ZoneStat.WeaponCleaveChance))
            {
                var targets = (int)Math.Round(RollRange(p, ZoneStat.WeaponCleaveMin, ZoneStat.WeaponCleaveMax, 1, 1, 10));
                wo.SetProperty(PropertyInt.Cleaving, targets + 1); // engine: CleaveTargets = Cleaving - 1
            }

            // Split Arrows (bows): shots fork to hit extra targets (the custom bowstring system)
            if (isMissile && Won(p, ZoneStat.WeaponSplitChance))
            {
                var count = (int)Math.Round(RollRange(p, ZoneStat.WeaponSplitMin, ZoneStat.WeaponSplitMax, 1, 1, 10));
                wo.SetProperty((PropertyBool)SplitArrowsBoolId, true);
                wo.SetProperty((PropertyInt)SplitArrowCountIntId, count);
                wo.SetProperty((PropertyFloat)SplitArrowRangeFloatId,
                    Math.Clamp(p.Get(ZoneStat.WeaponSplitRange, 8.0), 0.0, 50.0));
                wo.SetProperty((PropertyFloat)SplitArrowDmgFloatId,
                    Math.Clamp(p.Get(ZoneStat.WeaponSplitDmg, 1.0), 0.0, 1.0));
            }

            // Biting Strike: crit chance override (base 0.1)
            if (isWeapon && Won(p, ZoneStat.WeaponBiteChance))
                wo.CriticalFrequency = RollRange(p, ZoneStat.WeaponBiteMin, ZoneStat.WeaponBiteMax, 0.5, 0.0, 1.0);

            // Crushing Blow: card value IS the final crit damage multiplier (2 = 2x, the floor). The
            // engine computes CriticalDamageMod = 1 + CriticalMultiplier, so store (mult - 1) to land
            // exactly on the configured Nx rather than 1 + N.
            if (isWeapon && Won(p, ZoneStat.WeaponCrushChance))
            {
                var crushMult = RollRange(p, ZoneStat.WeaponCrushMin, ZoneStat.WeaponCrushMax, 2.0, 2.0, 10.0);
                wo.SetProperty(PropertyFloat.CriticalMultiplier, crushMult - 1.0);
            }

            // Armor Rend: stamps the REAL ArmorRending imbue (shows with the rend family on the item) plus
            // a tunable amount = fraction of armor ignored; the override prop replaces the skill formula
            // (which caps at 0.6) at hit time. OR'd in so it coexists with an elemental rend from above.
            // MELEE/MISSILE ONLY: armor rending is a physical-armor effect and does nothing for magic, so
            // casters (wands/orbs/staves) never roll it, regardless of the card's chance.
            if ((isMelee || isMissile) && Won(p, ZoneStat.WeaponArmorRendChance))
            {
                wo.ImbuedEffect |= ImbuedEffectType.ArmorRending;
                wo.SetProperty((PropertyFloat)ArmorRendOverridePropId,
                    RollRange(p, ZoneStat.WeaponArmorRendMin, ZoneStat.WeaponArmorRendMax, 0.5, 0.0, 1.0));
            }

            // Shield Cleaving: fraction of shield AL ignored (engine reads the value directly)
            if (isWeapon && Won(p, ZoneStat.WeaponShieldCleaveChance))
                wo.IgnoreShield = RollRange(p, ZoneStat.WeaponShieldCleaveMin, ZoneStat.WeaponShieldCleaveMax, 0.5, 0.0, 1.0);

            // Phantom: a full "hollow" weapon — hits ignore BOTH the target's magic armor (impen/banes)
            // and magic resistance (Life prots). Always full hollow; no partial mode.
            if (isWeapon && Won(p, ZoneStat.WeaponPhantomChance))
            {
                wo.IgnoreMagicArmor = true;
                wo.IgnoreMagicResist = true;
            }

            // slayer attuned against the killed monster's own kind
            if (isWeapon && wo.SlayerCreatureType == null && killed?.CreatureType != null &&
                killed.CreatureType != ACE.Entity.Enum.CreatureType.Invalid && Won(p, ZoneStat.WeaponSlayerChance))
            {
                wo.SlayerCreatureType = killed.CreatureType;
                // damage multiplier vs that creature type, rolled per drop; floor 1.5x (a normal slayer),
                // cap 10x (=1000%). One box = exact, both = roll in range.
                wo.SlayerDamageBonus = RollRange(p, ZoneStat.WeaponSlayerMin, ZoneStat.WeaponSlayerMax, 1.5, 1.5, 10.0);
            }

            // pre-Paragoned: levels from use (same properties the Paragon Weapons recipe stamps)
            if (isWeapon && Won(p, ZoneStat.WeaponParagonChance))
            {
                wo.ItemMaxLevel = (wo.ItemMaxLevel ?? 0) + 1;
                wo.ItemBaseXp = 2000000000;
                wo.ItemTotalXp = wo.ItemTotalXp ?? 0;
            }

            // one random Paragon gem spell on rolled armor (as if pre-gemmed)
            if (!isWeapon && wo.ArmorLevel.HasValue && wo.ArmorLevel.Value > 0 && Won(p, ZoneStat.ArmorGemChance))
            {
                var list = ParagonGemPool[ThreadSafeRandom.Next(0, ParagonGemPool.Length - 1)];
                var spell = list[Math.Min(RollWeighted(GemTierWeights), list.Count) - 1];
                wo.Biota.GetOrAddKnownSpell((int)spell, wo.BiotaDatabaseLock, out _);
            }

            // ── pre-applied crafts — ALWAYS LAST (owner rule: hilts/strings go on after every other
            // tuner, so their bonuses ADD on top of whatever the cards above set). Numbers mirror the
            // live recipes; adds land on the item's EFFECTIVE value (engine default when no prop). ──

            // Bandit Hilt (melee): recipe 527870063 complete. ManaStoneDestroyChance 0.01 is NOT junk —
            // it is the hilt system's completion marker: the apply recipe REQUIRES it < 0.01 ("This
            // weapon has already been hilted!"), so stamping it blocks a second hilt on this drop.
            if (isMelee && Won(p, ZoneStat.WeaponHiltChance))
            {
                wo.Attuned = AttunedStatus.Attuned;
                wo.Bonded = BondedStatus.Bonded;
                wo.SetProperty(PropertyBool.Ivoryable, true);
                wo.SetProperty(PropertyInt.WieldRequirements2, 8);   // WieldRequirement.Training
                wo.SetProperty(PropertyInt.WieldSkillType2, 46);
                wo.SetProperty(PropertyInt.WieldDifficulty2, 3);     // specialized
                wo.Value = 0;
                wo.SetProperty(PropertyFloat.ManaStoneDestroyChance, 0.01);
                wo.SetProperty(PropertyFloat.DamageMod,
                    (wo.GetProperty(PropertyFloat.DamageMod) ?? 1.0) + 1.075);
                wo.SetProperty(PropertyFloat.CriticalFrequency,
                    (wo.GetProperty(PropertyFloat.CriticalFrequency) ?? 0.1) + 0.25);
                wo.SetProperty(PropertyFloat.CriticalMultiplier,
                    (wo.GetProperty(PropertyFloat.CriticalMultiplier) ?? 1.0) + 0.175);
            }

            // Oiled Bowstring (bows): recipe 527870116 complete
            if (isMissile && Won(p, ZoneStat.WeaponBowstringChance))
            {
                wo.SetProperty(PropertyInt.WieldRequirements2, 8);
                wo.SetProperty(PropertyInt.WieldSkillType2, 47);     // Missile Weapons
                wo.SetProperty(PropertyInt.WieldDifficulty2, 3);
                wo.SetProperty((PropertyBool)SplitArrowsBoolId, true);
                wo.SetProperty((PropertyInt)SplitArrowCountIntId,
                    (wo.GetProperty((PropertyInt)SplitArrowCountIntId) ?? 0) + 1);   // stacks with the Split Arrows card
                wo.SetProperty((PropertyFloat)SplitArrowRangeFloatId, 12.0);         // recipe SETS 12 — string goes on last
                wo.SetProperty(PropertyFloat.DamageMod,
                    (wo.GetProperty(PropertyFloat.DamageMod) ?? 1.0) + 0.05);
            }

            // one EXTRA unique zone cantrip on top of whatever the roll produced — the zone's pool only
            // (prop-based ZoneCantrips catalog; retail cantrips deliberately excluded)
            TryExtraCantrip(wo, p, isWeapon);
        }

        private static void TryExtraCantrip(WorldObject wo, EvaluatedProfile p, bool isWeapon)
        {
            if (!Won(p, isWeapon ? ZoneStat.WeaponCantripChance : ZoneStat.ArmorCantripChance))
                return;

            var pool = p.CustomCantrips;
            if (pool == null || pool.Count == 0)
                return;

            var key = pool[ThreadSafeRandom.Next(0, pool.Count - 1)];
            if (ZoneCantrips.TryGet(key, out var def))
                ZoneCantrips.Stamp(wo, def);
        }

        /// <summary>True when the profile defines the chance stat AND the 0..1 roll comes up a winner.</summary>
        private static bool Won(EvaluatedProfile p, string chanceStat)
        {
            if (!p.Has(chanceStat))
                return false;
            var chance = Math.Clamp(p.Get(chanceStat), 0.0, 1.0);
            return chance > 0 && ThreadSafeRandom.Next(0.0f, 1.0f) < chance;
        }

        /// <summary>Weighted 1-based index roll over a weight table summing to 100.</summary>
        private static int RollWeighted(int[] weights)
        {
            var roll = ThreadSafeRandom.Next(1, 100);
            var sum = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                sum += weights[i];
                if (roll <= sum)
                    return i + 1;
            }
            return weights.Length;
        }

        /// <summary>
        /// Mutations for an always-drop (createlist) item: coin stack scaling only — WHAT drops is the
        /// weenie's own table and stays untouched. (mat_drop_mult was removed 2026-07-12: the server has
        /// no stackable non-coin createlist items for it to act on.)
        /// </summary>
        public static void MutateCreateListItem(WorldObject wo, EvaluatedProfile p)
        {
            if (wo == null || p == null)
                return;

            if (wo.WeenieType == WeenieType.Coin && p.Has(ZoneStat.CoinMult))
                ScaleStack(wo, p.Get(ZoneStat.CoinMult));
        }

        /// <summary>
        /// Scales weapon combat stats by mult. Flat rolls (melee Damage, missile ElementalDamageBonus) scale
        /// directly; the 1.xx multiplier mods (DamageMod, ElementalDamageMod, WeaponOffense/Defense) scale
        /// only their bonus fraction so 1.20 @ x1.5 becomes 1.30, not 1.80.
        /// </summary>
        private static void MutateWeaponStats(WorldObject wo, double mult)
        {
            if (mult <= 0 || mult == 1.0)
                return;

            var isWeapon = wo is MeleeWeapon || wo is MissileLauncher || wo is Caster;
            if (!isWeapon)
                return;

            if (wo is MeleeWeapon && wo.Damage.HasValue && wo.Damage.Value > 0)
                wo.Damage = Math.Max(1, (int)Math.Round(wo.Damage.Value * mult));

            if (wo is MissileLauncher)
            {
                if (wo.DamageMod.HasValue && wo.DamageMod.Value > 1.0)
                    wo.DamageMod = ScaleBonusFraction(wo.DamageMod.Value, mult);

                if (wo.ElementalDamageBonus.HasValue && wo.ElementalDamageBonus.Value > 0)
                    wo.ElementalDamageBonus = (int)Math.Round(wo.ElementalDamageBonus.Value * mult);
            }

            if (wo is Caster && wo.ElementalDamageMod.HasValue && wo.ElementalDamageMod.Value > 1.0)
                wo.ElementalDamageMod = ScaleBonusFraction(wo.ElementalDamageMod.Value, mult);

            if (wo.WeaponOffense.HasValue && wo.WeaponOffense.Value > 1.0)
                wo.WeaponOffense = ScaleBonusFraction(wo.WeaponOffense.Value, mult);

            if (wo.WeaponDefense.HasValue && wo.WeaponDefense.Value > 1.0)
                wo.WeaponDefense = ScaleBonusFraction(wo.WeaponDefense.Value, mult);
        }

        private static double ScaleBonusFraction(double mod, double mult) => 1.0 + (mod - 1.0) * mult;

        private static void ScaleStack(WorldObject wo, double mult)
        {
            if (mult <= 0 || mult == 1.0)
                return;

            var stack = wo.StackSize ?? 1;
            if (stack < 1)
                return;

            var scaled = (int)Math.Round(stack * mult);
            var max = wo.MaxStackSize.HasValue ? (int)wo.MaxStackSize.Value : stack;
            wo.SetStackSize(Math.Clamp(scaled, 1, Math.Max(stack, max)));
        }
    }
}
