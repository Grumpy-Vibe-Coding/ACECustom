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
        /// Mutations for an item rolled from the death-treasure table: provenance, forced weapon
        /// properties, plus the low-chance special-property rolls.
        /// <paramref name="killed"/> is the dying monster (slayer type source); <paramref name="lootTier"/>
        /// is the effective treasure tier (levels the default proc spell). <paramref name="forceMax"/> = this
        /// piece won the per-kill slot special (Armor v2): every cantrip line rolls at band MAX.
        /// </summary>
        public static void MutateLootItem(WorldObject wo, EvaluatedProfile p, Creature killed = null, int lootTier = 1, bool forceMax = false)
        {
            if (wo == null || p == null)
                return;

            // currency: coin stacks are never mutated (coin_mult removed 2026-08-23)
            if (wo.WeenieType == WeenieType.Coin)
                return;

            // Zone Control origin: record where this item dropped as a readable sentence appended to the
            // item's description (p.ScopeKey = the winning zone's name; the variation the zone matched on =
            // the creature's effective variation; killed = the dropping monster). Every non-coin drop gets it.
            if (!string.IsNullOrEmpty(p.ScopeKey))
            {
                // Two-line provenance (owner 2026-08-01). FinalizeT11LongDesc and the AppraiseInfo
                // projection-insert anchor on the "Dropped by"/"Location:" prefixes - the three
                // move together.
                var variation = killed != null ? ZoneControlManager.GetEffectiveVariation(killed) : 0;
                var origin = killed != null
                    ? $"Dropped by: {killed.Name}\nLocation: {p.ScopeKey} v{variation}"
                    : $"Location: {p.ScopeKey} v{variation}";
                wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? origin : wo.LongDesc + "\n\n" + origin;
            }

            // (weapon_stat_mult, weapon_damage_min/max/roll, weapon_caster_elem_*, weapon_missile_elem_*
            //  removed 2026-08-23 — weapon damage is owned by the weapon aug-scaling system)

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

            // (weapon_workmanship_min/max and value_mult/min/max removed 2026-08-23)

            TrySpecialRolls(wo, p, killed, lootTier, forceMax);
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

        /// <summary>
        /// 🔴 THE WEAPON CARD WRITE SITE - all six of them, one method (2026-08-25, weapon/armour parity).
        ///
        /// WHAT CHANGED AND WHY. Until this method existed each card computed its FINAL number inline
        /// and wrote it straight onto the item. That number was then the only record of the roll: the
        /// weapon carried no grade, so ZoneStatResolver.ApplyIfStale returned at its HasRecord guard and
        /// a band retune could never reach a weapon already in someone's pack. Armour has not worked
        /// that way since 2026-08-22 - an armour line records a GRADE 0-1000 and the property is a cache
        /// resolved from that grade against the LIVE ladder. The owner's instruction was to make the two
        /// identical ("full parity with weapons and armor"), so this does for a weapon card exactly what
        /// ZoneCantrips.StampGraded does for an armour line:
        ///     roll a grade -> resolve it inside the effective band -> write the property -> RECORD THE GRADE.
        /// The grade is the truth; the property is its projection. RollBanded, which used to be the
        /// number producer, is now only consulted through ZoneStatResolver.WeaponDropBand.
        ///
        /// PRECEDENCE at drop is unchanged and lives in ZoneStatResolver.WeaponDropBand: an authored
        /// weapon_&lt;card&gt;_min/_max on the zone (which already has the tier Default merged into it)
        /// wins outright, including the "one box = EXACT value, not a range" rule; otherwise the card's
        /// T11-&gt;T25 ladder rung. The RESOLVE side reads the tier Default only - see WeaponResolveBand.
        ///
        /// TWO DELIBERATE BEHAVIOUR CHANGES, both of them the parity the owner asked for:
        ///   - the roll inside an AUTHORED band is now the tier-weighted grade (T11 uniform, climbing to
        ///     10/30/60 at T25) instead of RollRange's flat uniform draw. Armour lines have rolled that
        ///     way since 2026-08-22; "push a tier and your gear also ROLLS better" now holds for a
        ///     hand-tuned zone too, not only for a zone on the ladder fallback.
        ///   - forceMax (this drop won the per-kill slot special) now reaches an authored band as well.
        ///     It only ever reached the ladder fallback before, because RollRange had no notion of it.
        ///
        /// This does NOT change which cards roll. Every caller is still behind its own Won(...) gate (or,
        /// for Rend Power, a presence test on its min/max pair), and Won() returns FALSE for an UNDEFINED
        /// stat - unset means NEVER, not "0 pct". A zone that authors nothing rolls nothing, as before.
        /// </summary>
        private static void StampWeaponCard(WorldObject wo, EvaluatedProfile p,
            ZoneStatResolver.WeaponSpecial ws, int tier, bool forceMax)
        {
            var (lo, hi) = ZoneStatResolver.WeaponDropBand(p, ws, tier);
            var grade = ZoneStatResolver.RollGrade(tier, forceMax);
            var display = Math.Clamp(ZoneStatResolver.ValueForD(lo, hi, grade), ws.Band.Lo, ws.Band.Hi);
            // EngineValue is the ONE display -> engine conversion in the server (Crushing Blow's
            // "- 1.0"). Do not subtract anything here and do not pre-convert before calling: the
            // resolver calls the same method on every equip, and a second subtraction anywhere would
            // walk a 7.5x weapon down to 6.5x, then 5.5x, on every login, silently.
            wo.SetProperty(ws.Prop, ZoneStatResolver.EngineValue(ws, display));
            ZoneStatResolver.AddLine(wo, ws.Key, grade);
        }

        // ── pre-applied craft deltas that land on a CARD's own property ────────────────────────────
        // These two mirror the live Bandit Hilt recipe (527870063) and are stamped by the hilt block at
        // the bottom of TrySpecialRolls, AFTER the cards, so they ADD on top of Biting Strike and
        // Crushing Blow. They are consts rather than inline literals because ZoneStatResolver.Compute
        // has to add the same amounts back when it re-resolves a hilted weapon - if these two numbers
        // ever disagree, a hilted weapon's crit stats move every time it is equipped. One number, two
        // readers.
        public const double BanditHiltCritFrequencyBonus = 0.25;
        public const double BanditHiltCritMultiplierBonus = 0.175;

        /// <summary>
        /// True when this weapon carries a Bandit Hilt - ours from the drop path, or one a player
        /// applied later with the real recipe (both stamp the same marks, and both need the same
        /// treatment at re-resolve time).
        ///
        /// The test is deliberately FOUR conditions rather than one. ManaStoneDestroyChance alone is a
        /// real retail property and would false-positive on any weenie that happens to carry it, which
        /// would hand that weapon a free +0.25 crit chance on its next equip. All four together -
        /// melee, ivoryable, the hilt's completion marker, and the hilt's Two Handed Combat training
        /// gate on the SECOND wield slot - are what the hilt recipe stamps and effectively nothing else does.
        /// (The recipe REQUIRES ManaStoneDestroyChance &lt; 0.01 to apply, which is why stamping 0.01 is
        /// the marker that blocks a second hilt.)
        /// </summary>
        public static bool HasBanditHilt(WorldObject wo)
        {
            if (wo == null || !(wo is MeleeWeapon))
                return false;
            if ((wo.GetProperty(PropertyFloat.ManaStoneDestroyChance) ?? 0.0) < 0.01)
                return false;
            if (wo.GetProperty(PropertyBool.Ivoryable) != true)
                return false;
            return (wo.GetProperty(PropertyInt.WieldSkillType2) ?? 0) == 46
                && (wo.GetProperty(PropertyInt.WieldRequirements2) ?? 0) == 8;
        }

        // RollBanded (the old "authored min/max, else the tier ladder, else clamp" number producer)
        // was DELETED 2026-08-25. Its two jobs were split so that a weapon can carry a GRADE the way an
        // armour piece does: the PRECEDENCE half moved to ZoneStatResolver.WeaponDropBand (and gained a
        // resolve-time twin, WeaponResolveBand), and the ROLL + WRITE half moved to StampWeaponCard
        // above, which also records the grade. Nothing about which cards roll, or about what an
        // authored min/max means, changed in the move - see StampWeaponCard for the two deliberate
        // exceptions (grade-weighted rolls and forceMax now reach an authored band too).

        // Split-arrow props (already in ACE.Entity — the custom bowstring system)
        // public: the weapon forge (/wsforge cards) stamps the same split-arrow properties
        public const int SplitArrowsBoolId = 9030;      // PropertyBool.SplitArrows
        public const int SplitArrowCountIntId = 9031;   // PropertyInt.SplitArrowCount
        public const int SplitArrowRangeFloatId = 9032; // PropertyFloat.SplitArrowRange
        public const int SplitArrowDmgFloatId = 9033;   // PropertyFloat.SplitArrowDamageMultiplier

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

        private static void TrySpecialRolls(WorldObject wo, EvaluatedProfile p, Creature killed, int lootTier, bool forceMax)
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
            //
            // NOTE on the gate (2026-08-25): unlike the other five cards this one has NO chance stat -
            // it is a PRESENCE test on the min/max pair, so a zone that authors either key gives Rend
            // Power to every rend-carrying weapon it drops, at 100 pct. Because the gate and
            // RollBanded's "is anything authored?" test are the same condition, the tier band below is
            // currently UNREACHABLE for this card: the gate only opens when something is authored, and
            // authored always wins. That is deliberate. Making the band reachable means gating on a new
            // weapon_rend_power_chance instead, and Won() treats an undefined stat as NEVER - so that
            // switch would silently turn Rend Power OFF in every zone that authors a min/max today. It
            // needs an owner ruling plus a migration (author the chance as 1.0 on those zones), so it
            // is deliberately NOT part of this pass. The row exists here so the ladder is ready the day
            // the gate changes, and so the plugin's WpnBands table has a server-side counterpart.
            if (isWeapon && (p.Has(ZoneStat.WeaponRendPowerMin) || p.Has(ZoneStat.WeaponRendPowerMax))
                && (wo.GetImbuedEffects() & AllRends) != 0)
                StampWeaponCard(wo, p, ZoneStatResolver.SpecRendPower, lootTier, forceMax);

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

            // Biting Strike: crit chance override (base 0.1). Unauthored magnitude follows the
            // T11 -> T25 ladder (0.58-0.65 -> 0.78-0.88); the value is now the projection of a
            // recorded grade, so a band retune reaches weapons already in the world.
            if (isWeapon && Won(p, ZoneStat.WeaponBiteChance))
                StampWeaponCard(wo, p, ZoneStatResolver.SpecBite, lootTier, forceMax);

            // Crushing Blow: the card value IS the final crit damage multiplier (2 = 2x, the floor),
            // and the engine computes CriticalDamageMod = 1 + CriticalMultiplier, so the STORED number
            // is (multiplier - 1). That subtraction used to sit inline right here.
            // 🔴 IT NO LONGER DOES, AND MUST NOT COME BACK. The number is produced from a recorded
            // grade now, and produced again on every equip re-stamp, so a subtraction at this site as
            // well would apply the "- 1" twice on the very first login and once more on every ladder
            // apply after that - a 7.5x weapon walking down to 6.5x, 5.5x, 4.5x with nothing logged.
            // The one and only conversion lives in ZoneStatResolver.EngineValue, which both this
            // write site (via StampWeaponCard) and the resolver call. The band table stays in display
            // space; do not bake the offset into it either.
            if (isWeapon && Won(p, ZoneStat.WeaponCrushChance))
                StampWeaponCard(wo, p, ZoneStatResolver.SpecCrush, lootTier, forceMax);

            // Armor Rend: stamps the REAL ArmorRending imbue (shows with the rend family on the item) plus
            // a tunable amount = fraction of armor ignored; the override prop replaces the skill formula
            // (which caps at 0.6) at hit time. OR'd in so it coexists with an elemental rend from above.
            // MELEE/MISSILE ONLY: armor rending is a physical-armor effect and does nothing for magic, so
            // casters (wands/orbs/staves) never roll it, regardless of the card's chance.
            if ((isMelee || isMissile) && Won(p, ZoneStat.WeaponArmorRendChance))
            {
                wo.ImbuedEffect |= ImbuedEffectType.ArmorRending;
                StampWeaponCard(wo, p, ZoneStatResolver.SpecArmorRend, lootTier, forceMax);
            }

            // Shield Cleaving: fraction of shield AL ignored (engine reads the value directly)
            if (isWeapon && Won(p, ZoneStat.WeaponShieldCleaveChance))
                StampWeaponCard(wo, p, ZoneStatResolver.SpecShieldCleave, lootTier, forceMax);

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
                // cap 10x (=1000%). One box = exact, both = roll in range; neither = the tier ladder
                // (1.80-2.10 at T11 -> 2.40-3.00 at T25) instead of the old flat 1.5.
                StampWeaponCard(wo, p, ZoneStatResolver.SpecSlayer, lootTier, forceMax);
            }

            // pre-Paragoned: levels from use (same properties the Paragon Weapons recipe stamps)
            if (isWeapon && Won(p, ZoneStat.WeaponParagonChance))
            {
                wo.ItemMaxLevel = (wo.ItemMaxLevel ?? 0) + 1;
                wo.ItemBaseXp = 2000000000;
                wo.ItemTotalXp = wo.ItemTotalXp ?? 0;
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

            // the zone-cantrip LINES on top of whatever the roll produced — the zone's pool only
            // (prop-based ZoneCantrips catalog; retail cantrips deliberately excluded)
            TryExtraCantrip(wo, p, isWeapon, forceMax, lootTier);

            // WEAPON RESOLVE IDENTITY, last, once the record is final (2026-08-25).
            //
            // Armour gets this from LootGenerationFactory.ApplyT11GearStats -> StampIdentity, but that
            // method returns at its `default:` case for weapons and casters, so nothing ever stamped a
            // weapon's ZcResolvedVersion. An unstamped weapon reads 0, which is a legitimate stamp
            // value (tier ladder v0, Zone Control on), so a weapon that dropped on a v0 tier would look
            // ALREADY RESOLVED for ever and skip the very first re-resolve it was owed.
            //
            // Only the version is stamped, never ZcTier: ZcTier is the armour-shaped tier property, and
            // ZoneStatResolver.TierOf / ZoneCraftGate.TierOf both expect a weapon's row to arrive via
            // WeaponAugScaleTier instead. That property is written a few lines later in the same
            // Creature_Death sweep (ApplyWeaponAugScaleStamp), which is also why lootTier has to be
            // passed in here rather than read off the item - at this instant the weapon has no tier
            // property at all.
            //
            // Guarded on HasRecord, not on isWeapon, so it also covers a weapon carrying only armour-
            // style cantrip lines (weapons have always been able to roll those through
            // weapon_cantrip_chance, and they were never stamped either).
            if (isWeapon && ZoneStatResolver.HasRecord(wo))
                ZoneStatResolver.StampWeaponResolve(wo, lootTier);
        }

        /// <summary>
        /// Armor v2 line roll (Cantrip_Band_Ladder v2, 2026-08-21). Replaces the per-bucket draws:
        ///   (a) line COUNT = cantrip_lines_min guaranteed, then extra slots up to cantrip_lines_max roll
        ///       cantrip_lines_chance_1/2/3 IN ORDER - the first miss stops (slots past 3 reuse chance_3);
        ///   (b) each slot picks a DISTINCT key from the zone pool, weighted by Def.Class
        ///       (cantrip_weight_trash/mid/chase);
        ///       SlotSpecial defs never enter; the per-line slot rule decides which piece kinds may roll it;
        ///   (c) bands: the zone override (CustomCantripBands) wins over the catalog, unchanged;
        ///   (d) forceMax (the piece carries the per-kill slot special) = every line at band MAX.
        /// armor_cantrip_chance / weapon_cantrip_chance stay the master on/off gate.
        /// </summary>
        private static void TryExtraCantrip(WorldObject wo, EvaluatedProfile p, bool isWeapon, bool forceMax, int lootTier = 11)
        {
            if (!Won(p, isWeapon ? ZoneStat.WeaponCantripChance : ZoneStat.ArmorCantripChance))
                return;

            var pool = p.CustomCantrips;
            if (pool == null || pool.Count == 0)
                return;

            // (a) how many lines this piece gets
            var fb = ZoneCantrips.LinesFallback(lootTier);   // tier-scaled fallback (owner 2026-08-23); authored stats win
            var linesMin = Math.Clamp((int)Math.Round(p.Get(ZoneStat.CantripLinesMin, fb.Min)), 0, 8);
            var linesMax = Math.Clamp((int)Math.Round(p.Get(ZoneStat.CantripLinesMax, fb.Max)), linesMin, 8);
            var lines = linesMin;
            for (int slot = 1; lines < linesMax; slot++)
            {
                var chanceStat = slot switch
                {
                    1 => ZoneStat.CantripLinesChance1,
                    2 => ZoneStat.CantripLinesChance2,
                    _ => ZoneStat.CantripLinesChance3,
                };
                var chance = Math.Clamp(p.Get(chanceStat, slot == 1 ? fb.Chance1 : slot == 2 ? fb.Chance2 : fb.Chance3), 0.0, 1.0);
                if (chance <= 0 || ThreadSafeRandom.Next(0.0f, 1.0f) >= chance)
                    break;
                lines++;
            }
            if (lines <= 0)
                return;

            var hasArmor = !isWeapon && wo.ArmorLevel.HasValue && wo.ArmorLevel.Value > 0;
            // per-line slot rule (owner 2026-08-22): zone / Default override per key, else the catalog's ArmorOnly / JewelryOnly
            var pieceMask = ZoneCantrips.PieceMask(wo);

            // (b) zone pool -> weighted candidate list (retired / special / armor-only-on-armorless never enter)
            var weightTrash = Math.Max(0.0, p.Get(ZoneStat.CantripWeightTrash, 10.0));
            var weightMid = Math.Max(0.0, p.Get(ZoneStat.CantripWeightMid, 6.0));
            var weightChase = Math.Max(0.0, p.Get(ZoneStat.CantripWeightChase, 1.0));

            var candidates = new List<(ZoneCantrips.Def Def, double Weight)>();
            var seen = new HashSet<int>();
            foreach (var key in pool)
            {
                if (!seen.Add(key) || !ZoneCantrips.TryGet(key, out var def) || def.SlotSpecial)   // unknown keys (retired, removed from the catalog) are skipped
                    continue;
                if (!ZoneCantrips.SlotAllowed(ZoneCantrips.EffectiveSlotMask(def, p.CantripSlots), pieceMask))
                    continue;
                var weight = def.Class switch   // Crit Chance is a plain Chase line since 2026-08-23 (crit weight removed)
                {
                    ZoneCantrips.CantripClass.Trash => weightTrash,
                    ZoneCantrips.CantripClass.Mid => weightMid,
                    ZoneCantrips.CantripClass.Chase => weightChase,
                    _ => 0.0,
                };
                if (weight > 0)
                    candidates.Add((def, weight));
            }

            for (int n = 0; n < lines && candidates.Count > 0; n++)
            {
                var def = PickWeighted(candidates);
                candidates.RemoveAll(c => c.Def.Key == def.Key);   // distinct per piece

                var (min, max) = p.CantripBands.TryGetValue(def.Key, out var band)
                    ? (band.Min, band.Max) : ZoneCantrips.CatalogBandAt(def, lootTier);   // hardcoded fallback is tier-scaled (2026-08-23)

                // insurance against hand-edited store bands - an inverted band must not throw mid-loot
                if (min > max) (min, max) = (max, min);

                // Live stat resolution (owner 2026-08-22): roll a GRADE 0-1000 (tier-weighted thirds,
                // Option A: T11 uniform, climbing to 10/30/60 at T25) and stamp it through the record;
                // the prop value is ValueFor(grade) inside the effective band. Key 49 Reinforced routes
                // to the plain Stamp inside StampGraded (earned + frozen, never in the record).
                // Proc-shaped bands are gone from the catalog and from Def entirely (2026-08-24).
                var grade = ZoneStatResolver.RollGrade(lootTier, forceMax);
                ZoneCantrips.StampGraded(wo, def, grade, (min, max));
            }
        }

        /// <summary>Weighted pick over a (def, weight) list; weights are arbitrary positive doubles.</summary>
        private static ZoneCantrips.Def PickWeighted(List<(ZoneCantrips.Def Def, double Weight)> candidates)
        {
            var total = 0.0;
            foreach (var c in candidates)
                total += c.Weight;
            var roll = ThreadSafeRandom.Next(0.0f, (float)total);
            var sum = 0.0;
            foreach (var c in candidates)
            {
                sum += c.Weight;
                if (roll < sum)
                    return c.Def;
            }
            return candidates[candidates.Count - 1].Def;
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

        // MutateCreateListItem / MutateWeaponStats / ScaleBonusFraction / ScaleStack removed 2026-08-23
        // together with coin_mult and weapon_stat_mult (createlist items are no longer mutated at all).
    }
}
