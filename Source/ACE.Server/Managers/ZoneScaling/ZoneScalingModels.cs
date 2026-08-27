using System;
using System.Collections.Generic;

namespace ACE.Server.Managers.ZoneScaling
{
    /// <summary>
    /// RESERVED — not read by anything. Left over from the pre-rewrite Zone Scaler design, which resolved
    /// LandblockVariation &gt; Landblock &gt; Zone &gt; Global. The live model layers
    /// <c>VariationDefault -&gt; zone -&gt; wcid</c> instead (see ZoneControlManager) and never consults this.
    /// Kept deliberately (owner, 2026-07-30) in case scoped profiles come back; do NOT assume it works.
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
        // min_attack_skill REMOVED 2026-08-02 (owner): redundant with attack_skill's absolute replace.
        // The prestige-gated v11_min_attack_skill server config floor still exists independently.
        public const string MeleeDefense = "melee_defense";
        public const string MissileDefense = "missile_defense";
        public const string MagicDefense = "magic_defense";
        public const string DamageRating = "damage_rating";
        public const string DamageResistRating = "damage_resist_rating";
        public const string ArmorLevel = "armor_level";
        // damage_taken_mult REMOVED 2026-08-03 (owner): redundant with damage_resist_rating's
        // replace (and server-clamped to <= 1.0 anyway). The prestige-gated v11_mob_dmg_taken_*
        // config path still exists independently.
        public const string VulnCap = "vuln_cap";
        public const string PercentHpBase = "percent_hp_base";

        // B1b. crit ratings (REPLACE the creature's base rating props at spawn; engine reads them
        // generically - 313/314 shape the mob's outgoing crits, 315/316 blunt incoming player crits.
        // 313 also drives %HP-floor crit frequency since it feeds the IsCritical roll).
        public const string CritRating = "crit_rating";
        public const string CritDamageRating = "crit_damage_rating";
        public const string CritResistRating = "crit_resist_rating";
        public const string CritDamageResistRating = "crit_damage_resist_rating";

        // B2. offense (REPLACE the weenie's body-part DVal/DVar/DType AND weapon damage — one number for
        // "what this monster hits for"). attack_damage_type is a DamageType flag int (random pick per hit).
        public const string AttackDamage = "attack_damage";
        public const string AttackVariance = "attack_variance";
        public const string AttackDamageType = "attack_damage_type";
        // spell_damage is RETAIL-path (since 2026-08-02): the authored value is the base per-cast damage and
        // runs the normal retail mitigation pipeline (resists/prots/ratings); crits multiply by
        // crit_damage_rating, and the floor still enforces a minimum. spell_variance spreads each cast
        // down from that value (0 = flat, like attack_variance); spell_damage_mult multiplies on top
        // (kept in code but hidden from the plugin UI since 2026-07-26 - owner retired it).
        public const string SpellDamage = "spell_damage";
        public const string SpellVariance = "spell_variance";
        public const string SpellDamageMult = "spell_damage_mult";

        // B2b. v11+ relief-curve anchors (2026-07-27, owner design): per-zone player-progression
        // damage reduction on AUTHORED damage (the %HP floor + WYSIWYG spell_damage). Each axis:
        // 0% reduction at *_start, rising to *_cap (fraction 0-1) at *_max, clamped both ends.
        // *_bend shapes the rise: relief = cap * t^bend where t = progress start->max; 1 = straight
        // line, <1 = strong early relief that tapers off, >1 = slow start that ramps late. aug and
        // dr axes MULTIPLY; critdr shrinks only the crit BONUS. Unset = server defaults
        // (v11_relief_* config). aug = defender life augs; dr = defender aggregate Damage Resist
        // rating; critdr = defender Crit Damage Resist rating.
        public const string ReliefAugStart = "relief_aug_start";
        public const string ReliefAugMax = "relief_aug_max";
        public const string ReliefAugCap = "relief_aug_cap";
        public const string ReliefAugBend = "relief_aug_bend";
        public const string ReliefDrStart = "relief_dr_start";
        public const string ReliefDrMax = "relief_dr_max";
        public const string ReliefDrCap = "relief_dr_cap";
        public const string ReliefDrBend = "relief_dr_bend";
        public const string ReliefCritDrStart = "relief_critdr_start";
        public const string ReliefCritDrMax = "relief_critdr_max";
        public const string ReliefCritDrCap = "relief_critdr_cap";
        public const string ReliefCritDrBend = "relief_critdr_bend";
        // Optional relief mid-points (owner 2026-07-27: "multiple bends"): up to 4 per axis, each an
        // (x = stat value, y = reduction fraction 0-1) pair. A point counts only when BOTH x and y
        // are authored. Any defined points REPLACE the bend shape: the curve runs piecewise-linear
        // through (start,0) -> sorted points -> (max,cap). Points outside (start,max) are ignored.
        public const string ReliefAugX1 = "relief_aug_x1";
        public const string ReliefAugY1 = "relief_aug_y1";
        public const string ReliefAugX2 = "relief_aug_x2";
        public const string ReliefAugY2 = "relief_aug_y2";
        public const string ReliefAugX3 = "relief_aug_x3";
        public const string ReliefAugY3 = "relief_aug_y3";
        public const string ReliefAugX4 = "relief_aug_x4";
        public const string ReliefAugY4 = "relief_aug_y4";
        public const string ReliefDrX1 = "relief_dr_x1";
        public const string ReliefDrY1 = "relief_dr_y1";
        public const string ReliefDrX2 = "relief_dr_x2";
        public const string ReliefDrY2 = "relief_dr_y2";
        public const string ReliefDrX3 = "relief_dr_x3";
        public const string ReliefDrY3 = "relief_dr_y3";
        public const string ReliefDrX4 = "relief_dr_x4";
        public const string ReliefDrY4 = "relief_dr_y4";
        public const string ReliefCritDrX1 = "relief_critdr_x1";
        public const string ReliefCritDrY1 = "relief_critdr_y1";
        public const string ReliefCritDrX2 = "relief_critdr_x2";
        public const string ReliefCritDrY2 = "relief_critdr_y2";
        public const string ReliefCritDrX3 = "relief_critdr_x3";
        public const string ReliefCritDrY3 = "relief_critdr_y3";
        public const string ReliefCritDrX4 = "relief_critdr_x4";
        public const string ReliefCritDrY4 = "relief_critdr_y4";

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
        // (loot_tier_bonus / loot_quantity_mult / rare_chance_mult / loot_quality_mult removed 2026-08-23:
        //  tier = zone floor, quantity = per-slot counts, quality = grade model)
        public const string BonusCurrency = "bonus_currency";

        // C2. loot post-roll mutations (applied per dropped item AFTER the factory rolls it — enhance, never replace)
        // (weapon_stat_mult / weapon_damage_* / weapon_*_elem_* / weapon_workmanship_* / coin_mult / value_*
        //  removed 2026-08-23: weapon damage is owned by the weapon aug-scaling system)
        public const string WeaponAttuned = "weapon_attuned";        // nonzero = rolled weapons drop Attuned (can't be traded/dropped)
        public const string WeaponBonded = "weapon_bonded";          // nonzero = rolled weapons drop Bonded (stay on death)
        public const string WeaponUnenchantable = "weapon_unenchantable"; // nonzero = rolled weapons drop unenchantable (ResistMagic 9999)

        // C3. loot special-property rolls (independent 0..1 chance per eligible dropped item — "fun stuff")
        // Cast on Strike, REWRITTEN 2026-08-27 (owner). The card is now TWO INDEPENDENT slots - an ARC
        // and a RING - and both are picked from the weapon's OWN damage type, the same field the rend
        // reads. The old weapon_proc_chance / _rate / _spell trio is GONE along with ProcSpellPool and
        // its tier clamp; there is deliberately no back-compat shim (test shard, no migration).
        //
        // WHY MATCHED TO THE WEAPON: GetWeaponResistanceModifier resolves the rend off the SPELL's
        // damage type (SpellProjectile.cs:752 -> WorldObject_Weapon.cs:641), so a proc only benefits
        // from the weapon's rend when the two elements agree. Mismatched, the proc loses a 2.50-3.13x
        // multiplier at T11 that the melee swing keeps. Matching is worth 4-5x and is not cosmetic.
        public const string WeaponProcArcChance = "weapon_proc_arc_chance";   // per-drop odds the ARC slot is stamped
        public const string WeaponProcArcRate = "weapon_proc_arc_rate";       // the arc's per-hit proc rate (engine ProcSpellRate)
        public const string WeaponProcRingChance = "weapon_proc_ring_chance"; // per-drop odds the RING slot is stamped
        public const string WeaponProcRingRate = "weapon_proc_ring_rate";     // the ring's per-hit proc rate (ProcRate2)
        // The damage lever. B REPLACES the spell's rolled base AND the flat War/Void aug term at
        // SpellProjectile.cs:704-722 - it must never multiply it: EffectiveWarAugCount is added 1:1 and
        // the live shard ranges 0..10,000 War augs, a 60x spread coming from the WIELDER, not the weapon.
        // A BAND PER SLOT, not one shared value (owner 2026-08-27, GUI layout B - two separate cards).
        // Separate bands are what let the two be priced for what they actually are: the arc is one
        // projectile at the target, the ring is 9 projectiles centred on the PLAYER (owner: rings keep
        // the default behaviour and ring the caster, not the monster). At an identical B the ring would
        // be strictly better on any melee weapon, since it hits everything around you for the same
        // number. 440 = one melee hit is the anchor for BOTH; the ring's is expected to move first.
        public const string WeaponProcArcDmgMin = "weapon_proc_arc_dmg_min";   // arc B floor (T11 anchor 440)
        public const string WeaponProcArcDmgMax = "weapon_proc_arc_dmg_max";   // arc B ceiling (T11 660, PROVISIONAL)
        public const string WeaponProcRingDmgMin = "weapon_proc_ring_dmg_min"; // ring B floor
        public const string WeaponProcRingDmgMax = "weapon_proc_ring_dmg_max"; // ring B ceiling
        // Without this the card is a LIE on a melee character: the proc's resist check falls back to the
        // WIELDER's skill in the spell's school (TryResistSpell:140-161), and untrained War Magic ~600 vs
        // a T11 mob's magic_defense 1100 is resisted ~100 pct of the time. Stamping ItemSpellcraft moves
        // the check onto the WEAPON. This is the prerequisite for everything else on the card.
        public const string WeaponProcSpellcraft = "weapon_proc_spellcraft";  // stamped ItemSpellcraft
        // FLAT aug fold-in (owner 2026-08-27, "Flat"). The player's Melee + Missile + War + Void
        // effective aug counts are SUMMED and added to B, so a proc scales with what the wielder has
        // actually invested in - including a pure melee character, whose proc read exactly zero augs
        // before, since the stock path only ever added War/Void.
        //
        // THE CAP IS THE WHOLE SAFETY MECHANISM. Each count reaches 10,000 on the live shard, so an
        // uncapped sum is a 0..40,000 flat addition on top of a 440 base - the same uncontrolled,
        // wielder-driven spread that B was introduced to remove. UNSET = UNCAPPED; there is no
        // defensible default to invent here, so it is a knob the owner must set.
        public const string WeaponProcAugCap = "weapon_proc_aug_cap";        // max TOTAL aug contribution; unset/0 = uncapped
        public const string WeaponImbueChance = "weapon_imbue_chance";   // random imbue (rends / Critical Strike / Crippling Blow / Armor Rending)
        public const string WeaponSlayerChance = "weapon_slayer_chance"; // slayer vs the killed monster's own creature type
        public const string WeaponSlayerMin = "weapon_slayer_min";       // SlayerDamageBonus min (raw multiplier, floor 1.5, cap 10.0)
        public const string WeaponSlayerMax = "weapon_slayer_max";       // SlayerDamageBonus max
        public const string WeaponCantripChance = "weapon_cantrip_chance"; // one EXTRA cantrip on a rolled weapon, from the zone's custom pool ONLY
        public const string ArmorCantripChance = "armor_cantrip_chance";   // one EXTRA cantrip on rolled armor/clothing/jewelry, custom pool ONLY
        // Armor v2 (2026-08-21, Cantrip_Band_Ladder v2; the old per-bucket cantrip_draws_bN keys were
        // removed 2026-08-23). The mutator rolls a
        // per-piece LINE COUNT ladder: lines_min guaranteed, extra slots up to lines_max roll
        // chance_1/2/3 IN ORDER and the first miss stops. Keys fill slots weighted by Def.Class
        // (trash/mid/chase); key 33 Crit Rating has its own weight. armor_cantrip_chance stays the
        // master on/off gate.
        public const string CantripLinesMin = "cantrip_lines_min";         // guaranteed lines per piece (default 0)
        public const string CantripLinesMax = "cantrip_lines_max";         // hard cap per piece (default 2)
        public const string CantripLinesChance1 = "cantrip_lines_chance_1"; // 0..1 chance for extra slot 1 (default .40)
        public const string CantripLinesChance2 = "cantrip_lines_chance_2"; // extra slot 2 (default .10)
        public const string CantripLinesChance3 = "cantrip_lines_chance_3"; // extra slot 3 (default .01)
        public const string CantripWeightTrash = "cantrip_weight_trash";   // pick weight per Trash-class key (default 10)
        public const string CantripWeightMid = "cantrip_weight_mid";       // per Mid-class key (default 6)
        public const string CantripWeightChase = "cantrip_weight_chase";   // per Chase-class key (default 1)
        // Guaranteed core four anchors (SET totals at T25; per piece = anchor/18 x f(t)); see
        // LootGenerationFactory.ApplyT11GearStats
        public const string CoreAnchorDr = "core_anchor_dr";               // Damage Resist worn-set anchor (ladder 1250, authored on Default 11; ZoneFallback.AnchorDr 92 when off)
        public const string CoreAnchorCdr = "core_anchor_cdr";             // CritDmgResist / CritResist / NetherResist worn-set anchor (ladder 750, authored on Default 11; ZoneFallback.AnchorCdr 73 when off)
        // Slot specials: ONE roll per KILL (retail-rare model), 1-in-odds; boss/leader divide the odds
        public const string SpecialOdds = "special_odds";                  // denominator (default 750000)
        public const string SpecialBossMult = "special_boss_mult";         // IsZcBoss: odds / this (default 3.0)
        public const string SpecialLeaderMult = "special_leader_mult";     // IsZcLeader: odds / this (default 2.0)
        // Special behaviour knobs (read by the combat side)
        public const string BattleMendThreshold = "battlemend_threshold";  // HP fraction below which Battle Mending fires (default .25)
        public const string BattleMendCooldown = "battlemend_cooldown";    // seconds (default 60)
        public const string PctHpCooldown = "pcthp_cooldown";              // pct-HP special cooldown, seconds (default 2)
        public const string CheatDeathCooldown = "cheatdeath_cooldown";    // seconds per character (default 600)
        public const string CheatDeathImmunity = "cheatdeath_immunity";    // immunity window, seconds (default 5)
        public const string LifeOnHitCap = "lifeonhit_cap";                 // key 48: worn-total cap in pct of max HP per hit (default 25)
        public const string LifeOnHitCooldown = "lifeonhit_cooldown";       // key 48: per-character seconds between heals (default 3)
        // Worn-gear HARD caps (owner 2026-08-21: "everything we set at 2500 must cap at EXACTLY 2500, not
        // around 2500"). The flat per-piece bands overshoot the anchor by rounding (18 x 139 = 2502 at T25),
        // so the cap is enforced on the EQUIPPED SUM at read time - equipment term only, never enchantments /
        // augs / enlightenment. Read for PLAYERS via the zone default the player stands in; no zone = the C#
        // default, so the caps apply everywhere. Creature.GetGearCap / GetEquippedItemsRatingSumCapped /
        // GetZoneCantripBonus are the read sites.
        public const string GearCapDr = "gear_cap_dr";                     // worn Damage Resist sum (ladder ceiling 2500; ZoneFallback.CapDr 92 when zonecontrol_enabled is off)
        public const string GearCapCdr = "gear_cap_cdr";                   // worn CritDmgResist / CritResist / NetherResist sums, EACH (ladder ceiling 1500; ZoneFallback.CapCdr 73 when off)
        public const string GearCapLine = "gear_cap_line";                 // every anchored cantrip line: Dmg / CritDmg / MaxHP / MaxStam / MaxMana / HealBoost / each aug track / each attribute (ladder ceiling 2500; ZoneFallback.CapLine 211 when off)
        // Kill rewards (2026-08-23): authored per zone, by rank. xp_minion is the MASTER key - when set,
        // weenie XpOverride is ignored for every mob the zone governs (boss/leader fall back to minion
        // when their own key is unset; unranked random mobs pay minion). lum_award set = LuminanceAward
        // ignored; 0 = no luminance. Values are doubles and may exceed int range (read as long).
        public const string XpMinion = "xp_minion";
        public const string XpLeader = "xp_leader";
        public const string XpBoss = "xp_boss";
        public const string LumAward = "lum_award";
        public const string XpDefault = "xp_default";   // unranked/uncategorized spawns; unset = pay xp_minion

        // PropertyBool ids the loot side reads by cast (the enum entries live with the combat work):
        // 50048 IsZcBoss / 50049 IsZcLeader / 50050 IsZcMinion / 50051 ZcPctHpImmune (owner 50000+ block)
        public const int BoolIsZcBoss = 50048;
        public const int BoolIsZcLeader = 50049;
        public const int BoolIsZcMinion = 50050;
        public const int BoolZcPctHpImmune = 50051;
        // Card amounts are min/max PAIRS: set one = exact value, set both = each drop rolls uniformly
        // in the range, reversed bounds auto-swap.
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
        // REMOVED 2026-08-25 (owner): weapon_phantom_chance, the Phantom (hollow) card's chance stat.
        // The card is gone from ZoneLootMutator; the retail IgnoreMagicArmor / IgnoreMagicResist
        // properties it used to stamp are untouched. Zones that authored the key keep a dead store row
        // until it is cleared with `/zonecontrol default <var> clearstat weapon_phantom_chance`.
        // Rend Power (2026-08-25): this card used to be the ONE special with no chance stat of its own -
        // its gate was a PRESENCE test on the min/max pair below, which meant "authored = 100 pct". That
        // made its T11 -> T25 ladder unreachable, because the presence test and WeaponDropBand's
        // "is a pin authored?" test were the SAME condition: the gate only opened when a pin existed,
        // and a pin always wins over the ladder. It now gates on this chance like the other five.
        // Consequence, accepted by the owner: a zone that authors only min/max and no chance no longer
        // rolls Rend Power at all, because Won() treats an UNDEFINED stat as NEVER (not as "0 pct").
        // Re-author those zones with a chance; there is deliberately no back-compat shim.
        public const string WeaponRendPowerChance = "weapon_rend_power_chance"; // Rend Power: per-drop odds on a rend-carrying weapon
        public const string WeaponRendPowerMin = "weapon_rend_power_min";  // rend strength as a DIRECT vuln bonus, rolled per drop; wire 1.5..10.0 = +150%..+1000% (rendingMod = 1 + this)
        public const string WeaponRendPowerMax = "weapon_rend_power_max";

        // C4. structured loot set + QB scaling (T11+ endgame loot; see ACE_Loot_Systems_DeepDive doc §12-13)
        // PER-SLOT drop counts (owner 2026-07-20: no set enabler, each slot has its own count).
        // Default = 1 each at tier 11+, 0 below; a zone stat overrides just that slot. Armor slots
        // are coverage-aware: a multi-slot piece (coat) credits every slot it covers.
        public const string LootSlotWeapons = "loot_slot_weapons";         // weapon drops PER FAMILY (9 families)
        public const string LootSlotHelm = "loot_slot_helm";
        public const string LootSlotChest = "loot_slot_chest";
        public const string LootSlotShoulder = "loot_slot_shoulder";
        public const string LootSlotBracer = "loot_slot_bracer";
        public const string LootSlotGlove = "loot_slot_glove";
        public const string LootSlotGirth = "loot_slot_girth";
        public const string LootSlotUpperLeg = "loot_slot_upperleg";
        public const string LootSlotLowerLeg = "loot_slot_lowerleg";
        public const string LootSlotBoot = "loot_slot_boot";
        public const string LootSlotShield = "loot_slot_shield";
        public const string LootSlotAmulet = "loot_slot_amulet";
        public const string LootSlotRing = "loot_slot_ring";
        public const string LootSlotBracelet = "loot_slot_bracelet";
        public const string LootSlotTrinket = "loot_slot_trinket";
        public const string LootSlotCloak = "loot_slot_cloak";

        // OPTIONAL per-slot RANGE (owner 2026-08-24): the base loot_slot_<slot> key above is the MIN
        // and these are the MAX. When a max is defined AND above the min, the slot's count rolls
        // uniform-inclusive per slot, per kill ("1-2 Weapons, 3-5 Chest"); each slot rolls
        // INDEPENDENTLY. Max unset (the default) = an exact count, i.e. the pre-2026-08-24 behaviour.
        // Reversed pairs are auto-swapped at read time, matching every other min/max pair here.
        // Append-only: an older plugin ignores these, an older server never sends them.
        public const string LootSlotWeaponsMax = "loot_slot_weapons_max";
        public const string LootSlotHelmMax = "loot_slot_helm_max";
        public const string LootSlotChestMax = "loot_slot_chest_max";
        public const string LootSlotShoulderMax = "loot_slot_shoulder_max";
        public const string LootSlotBracerMax = "loot_slot_bracer_max";
        public const string LootSlotGloveMax = "loot_slot_glove_max";
        public const string LootSlotGirthMax = "loot_slot_girth_max";
        public const string LootSlotUpperLegMax = "loot_slot_upperleg_max";
        public const string LootSlotLowerLegMax = "loot_slot_lowerleg_max";
        public const string LootSlotBootMax = "loot_slot_boot_max";
        public const string LootSlotShieldMax = "loot_slot_shield_max";
        public const string LootSlotAmuletMax = "loot_slot_amulet_max";
        public const string LootSlotRingMax = "loot_slot_ring_max";
        public const string LootSlotBraceletMax = "loot_slot_bracelet_max";
        public const string LootSlotTrinketMax = "loot_slot_trinket_max";
        public const string LootSlotCloakMax = "loot_slot_cloak_max";

        // BUDGET MODE (owner 2026-08-24). Defining loot_max_drops switches the structured set from
        // "each slot drops its own count" to "roll this many ITEMS total, distributed by weight".
        // In budget mode the loot_slot_<slot> values above are reinterpreted as WEIGHTS WITHIN THEIR
        // CATEGORY (0 still = never); the _max range keys apply to LEGACY mode only, since the range
        // now lives on the budget. Unset = legacy, i.e. the pre-2026-08-24 behaviour, unchanged.
        // The budget is a CEILING, not a quota: armor coverage credit (one coat covering three slots)
        // can land under it. Slot specials are deliberately OUTSIDE the budget.
        // Renamed from loot_max_drops / loot_max_drops_max 2026-08-24: the first key is the LOWER
        // bound, so calling it "max" was backwards and the UI inherited the error. Renamed while only
        // a single test value existed - re-author it once.
        public const string LootDropsMin = "loot_drops_min";   // floor; DEFINING THIS is what enables budget mode
        public const string LootDropsMax = "loot_drops_max";   // optional ceiling; budget rolls uniform-inclusive between them
        public const string LootWeightWeapon = "loot_weight_weapon";       // relative category weights, normalized at roll time
        public const string LootWeightArmor = "loot_weight_armor";         //   (shield rides armor)
        public const string LootWeightJewelry = "loot_weight_jewelry";
        public const string LootWeightCloak = "loot_weight_cloak";

        // C5. ARMOR BASE VALUES (owner 2026-08-24, Armor_Base_Values_Plan_2026-08-24.md sections 2.1-2.3).
        // The three numbers a T11+ armour piece was built on that had no authoring surface at all.
        //
        // READ STAGE MATTERS, and the three do NOT share one:
        //   armor_base_level     -> RESOLVE. Read inside ZoneStatResolver.Compute, so authoring it
        //                           RE-PRICES EXISTING GEAR on its next equip / login (or at once via
        //                           Apply Ladder). Authored on the per-tier DEFAULT layer.
        //   armor_prot_base      -> DROP. Stamped once when the piece is created; existing pieces
        //                           never change. Zone layer wins, else the tier Default.
        //   armor_prot_equalize  -> DROP. Same as above.
        // If a change to one of the prot keys "does nothing", it is because the gear already dropped.
        public const string ArmorBaseLevel = "armor_base_level";       // per-tier armour floor; UNSET = 1100 + 100 x (tier - 11), i.e. today's numbers exactly
        public const string ArmorProtBase = "armor_prot_base";         // value written into every ArmorModVs* the weenie left absent (default 1.0 = the Average band)
        public const string ArmorProtEqualize = "armor_prot_equalize"; // nonzero (default) = average the present elements and write the mean back; 0 = elements keep what they rolled, so Poor and Unparalleled both survive

        public static readonly string[] All =
        {
            Strength, Endurance, Coordination, Quickness, Focus, Self, MaxHealth, MaxStamina, MaxMana,
            AttackSkill, MeleeDefense, MissileDefense, MagicDefense, DamageRating,
            DamageResistRating, ArmorLevel, VulnCap, PercentHpBase,
            CritRating, CritDamageRating, CritResistRating, CritDamageResistRating,
            AttackDamage, AttackVariance, AttackDamageType, SpellDamage, SpellVariance, SpellDamageMult,
            ReliefAugStart, ReliefAugMax, ReliefAugCap, ReliefAugBend,
            ReliefDrStart, ReliefDrMax, ReliefDrCap, ReliefDrBend,
            ReliefCritDrStart, ReliefCritDrMax, ReliefCritDrCap, ReliefCritDrBend,
            ReliefAugX1, ReliefAugY1, ReliefAugX2, ReliefAugY2, ReliefAugX3, ReliefAugY3, ReliefAugX4, ReliefAugY4,
            ReliefDrX1, ReliefDrY1, ReliefDrX2, ReliefDrY2, ReliefDrX3, ReliefDrY3, ReliefDrX4, ReliefDrY4,
            ReliefCritDrX1, ReliefCritDrY1, ReliefCritDrX2, ReliefCritDrY2, ReliefCritDrX3, ReliefCritDrY3, ReliefCritDrX4, ReliefCritDrY4,
            ResistSlash, ResistPierce, ResistBludgeon, ResistFire, ResistCold, ResistAcid, ResistElectric, ResistNether,
            ArmorVsSlash, ArmorVsPierce, ArmorVsBludgeon, ArmorVsFire, ArmorVsCold, ArmorVsAcid, ArmorVsElectric, ArmorVsNether,
            BonusCurrency,
            WeaponAttuned, WeaponBonded, WeaponUnenchantable,
            WeaponProcArcChance, WeaponProcArcRate, WeaponProcRingChance, WeaponProcRingRate,
            WeaponProcArcDmgMin, WeaponProcArcDmgMax, WeaponProcRingDmgMin, WeaponProcRingDmgMax,
            WeaponProcSpellcraft, WeaponProcAugCap, WeaponImbueChance,
            WeaponSlayerChance, WeaponSlayerMin, WeaponSlayerMax,
            WeaponCantripChance, ArmorCantripChance,
            WeaponCleaveChance, WeaponCleaveMin, WeaponCleaveMax,
            WeaponSplitChance, WeaponSplitMin, WeaponSplitMax, WeaponSplitRange, WeaponSplitDmg,
            WeaponBiteChance, WeaponBiteMin, WeaponBiteMax,
            WeaponCrushChance, WeaponCrushMin, WeaponCrushMax,
            WeaponArmorRendChance, WeaponArmorRendMin, WeaponArmorRendMax,
            WeaponShieldCleaveChance, WeaponShieldCleaveMin, WeaponShieldCleaveMax,
            WeaponRendPowerChance, WeaponRendPowerMin, WeaponRendPowerMax,
            LootSlotWeapons,
            LootSlotHelm, LootSlotChest, LootSlotShoulder, LootSlotBracer, LootSlotGlove,
            LootSlotGirth, LootSlotUpperLeg, LootSlotLowerLeg, LootSlotBoot,
            LootSlotShield, LootSlotAmulet, LootSlotRing, LootSlotBracelet, LootSlotTrinket, LootSlotCloak,
            LootSlotWeaponsMax,
            LootSlotHelmMax, LootSlotChestMax, LootSlotShoulderMax, LootSlotBracerMax, LootSlotGloveMax,
            LootSlotGirthMax, LootSlotUpperLegMax, LootSlotLowerLegMax, LootSlotBootMax,
            LootSlotShieldMax, LootSlotAmuletMax, LootSlotRingMax, LootSlotBraceletMax, LootSlotTrinketMax, LootSlotCloakMax,
            LootDropsMin, LootDropsMax,
            LootWeightWeapon, LootWeightArmor, LootWeightJewelry, LootWeightCloak,
            // Armor v2 (2026-08-21). Order here is cosmetic ONLY: every wire payload that carries these
            // ([[ZC]] sync, [[ZCD]] Default reply) emits "<name>=<defined>,<value>" pairs, so the plugin
            // matches by NAME - adding, reordering or REMOVING an entry mid-list is safe. (The genuinely
            // positional lists are the bare comma payloads combatdefs= / diagdefs= in ZoneControlCommands.)
            CantripLinesMin, CantripLinesMax, CantripLinesChance1, CantripLinesChance2, CantripLinesChance3,
            CantripWeightTrash, CantripWeightMid, CantripWeightChase,
            CoreAnchorDr, CoreAnchorCdr,
            SpecialOdds, SpecialBossMult, SpecialLeaderMult,
            BattleMendThreshold, BattleMendCooldown, PctHpCooldown, CheatDeathCooldown, CheatDeathImmunity,
            LifeOnHitCap, LifeOnHitCooldown,
            GearCapDr, GearCapCdr, GearCapLine,
            // Kill rewards (2026-08-23): authored per zone, by rank - APPEND-ONLY
            XpMinion, XpLeader, XpBoss, LumAward, XpDefault,
            // Armor base values (2026-08-24) - APPEND-ONLY, added at the END. The wire matches by NAME
            // (see the note above), so appending is safe for an older plugin / older server.
            ArmorBaseLevel, ArmorProtBase, ArmorProtEqualize,
        };

        /// <summary>
        /// The weapon-card CHANCE stats, derived from <see cref="All"/> by shape (weapon_*_chance).
        ///
        /// WHY DERIVED AND NOT A HAND-WRITTEN LIST (2026-08-25). This is the identifier space of
        /// `/zonecontrol weaponcard &lt;stat&gt; on|off` and the only key set
        /// <see cref="ZoneVariantProfile.CustomWeaponCards"/> may ever hold. Armour's equivalent toggle
        /// keys off a NUMERIC catalog key, which cannot go stale because the catalog is the registry.
        /// A weapon card has no registry - its identity IS its chance stat - so the temptation is to
        /// paste a fifteen-entry list here and let it rot the next time a card is added or renamed.
        /// Deriving it means a new weapon_&lt;card&gt;_chance constant is toggleable the moment it lands
        /// in All, with no second place to remember.
        ///
        /// THE TRAP: this matches weapon_cantrip_chance too, which is not one of the fourteen combat
        /// cards but the "one EXTRA cantrip on a rolled weapon" roll. That is intentional and harmless -
        /// it also passes through ZoneLootMutator.Won, so the toggle works on it exactly as it does on a
        /// card. Its ARMOUR twin, armor_cantrip_chance, is deliberately NOT in this set (wrong prefix),
        /// so armour drops can never be silenced by a weapon verb.
        /// </summary>
        public static readonly string[] WeaponCardChances = BuildWeaponCardChances();

        private static readonly HashSet<string> WeaponCardChanceSet =
            new HashSet<string>(WeaponCardChances, StringComparer.OrdinalIgnoreCase);

        private static string[] BuildWeaponCardChances()
        {
            var list = new List<string>();
            foreach (var s in All)
                if (s.StartsWith("weapon_", StringComparison.Ordinal) && s.EndsWith("_chance", StringComparison.Ordinal))
                    list.Add(s);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>True when <paramref name="statKey"/> names a weapon card's chance stat, i.e. is a legal
        /// identifier for the `weaponcard` verb and for the CustomWeaponCards map.</summary>
        public static bool IsWeaponCardChance(string statKey)
            => statKey != null && WeaponCardChanceSet.Contains(statKey);
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

        /// <summary>Per-FIELD layered merge: any non-null field on <paramref name="upper"/> wins, everything
        /// else falls through to <paramref name="lower"/>. Returns a new instance; inputs are untouched.</summary>
        public static ZoneBodyPart Merge(ZoneBodyPart lower, ZoneBodyPart upper)
        {
            if (lower == null) return upper?.Clone();
            if (upper == null) return lower.Clone();
            return new ZoneBodyPart
            {
                Armor = upper.Armor ?? lower.Armor,
                Damage = upper.Damage ?? lower.Damage,
                Variance = upper.Variance ?? lower.Variance,
                DamageType = upper.DamageType ?? lower.DamageType,
            };
        }
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
    /// An authored roll-band override for one zone-cantrip catalog key: the value band (Min..Max, inclusive
    /// both bounds) and, for proc lines, the proc-chance band in percent (0/0 = passive). A band is one
    /// VALUE — layers overwrite it whole per key (like PropInts), never merge its fields.
    /// </summary>
    public class CantripBand
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int ProcMin { get; set; }
        public int ProcMax { get; set; }

        public CantripBand Clone() => new CantripBand { Min = Min, Max = Max, ProcMin = ProcMin, ProcMax = ProcMax };
    }

    /// <summary>
    /// One spell-book rule for a zone-governed monster: disable a known spell, override its cast chance,
    /// or ADD a spell the weenie doesn't know. Consumed READ-TIME at the monster spell-selection choke
    /// point (Monster_Magic.TryRollSpell) - spell books are weenie-shared, so they are never mutated,
    /// and rule changes apply LIVE to already-spawned monsters.
    /// </summary>
    public class ZoneSpellRule
    {
        public int SpellId { get; set; }

        /// <summary>true = the monster never casts this spell (book spells only - an added rule with
        /// Disabled makes no sense but is harmlessly skipped).</summary>
        public bool Disabled { get; set; }

        /// <summary>Cast chance in PERCENT per cast opportunity (the book's 2.029 encodes 2.9). Null on a
        /// book spell = keep the book's own chance; null on an ADDED spell = default 2.0.</summary>
        public double? Chance { get; set; }

        public ZoneSpellRule Clone() => new ZoneSpellRule { SpellId = SpellId, Disabled = Disabled, Chance = Chance };
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
    /// One stat's value. Only <see cref="Base"/> is live: <c>EvaluateVariant</c> always calls
    /// <c>Evaluate(1)</c>, so a stat is a flat number.
    ///
    /// <see cref="Growth"/> / <see cref="Additive"/> / <see cref="Overrides"/> are RESERVED and not wired up.
    /// Reviving them would mean deriving a stat from the variation number, which is exactly the computed
    /// scaling the owner ruled out on 2026-07-30 — progression is expressed as 15 explicitly AUTHORED
    /// per-variation Defaults (v11-v25) instead, because those are visible and editable in a way a growth
    /// exponent is not. The fields stay because the store already serializes them.
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

        /// <summary>Roll-band overrides for zone cantrip keys, keyed by catalog key. A key absent here rolls
        /// the catalog's own band. Missing on deserialize of older profiles = empty dict (backward compatible).</summary>
        public Dictionary<int, CantripBand> CustomCantripBands { get; set; } = new();

        /// <summary>Per-key SLOT RULE overrides (ZoneCantrips.SlotMask bits; 0 = Any), keyed by catalog key. A key
        /// absent here uses the catalog's ArmorOnly / JewelryOnly default. Authored by `cantrip <scope> slots`,
        /// merged OVERWRITE per key like the bands. Missing on deserialize of older profiles = empty dict.</summary>
        public Dictionary<int, int> CustomCantripSlots { get; set; } = new();

        /// <summary>Per-SPECIAL on/off (owner 2026-08-23): catalog key -> enabled. A key absent here is ON. Authored by
        /// `cantrip <scope> special <key> on|off`, merged OVERWRITE per key (a zone can re-enable what the Default
        /// turned off). Consulted by the per-kill special roll only. Missing on older profiles = empty dict.</summary>
        public Dictionary<int, bool> CustomSpecials { get; set; } = new();

        /// <summary>
        /// Per-WEAPON-CARD on/off (owner 2026-08-25), keyed by the card's CHANCE STAT NAME
        /// (weapon_bite_chance, weapon_rend_power_chance, ...) -> enabled. A key absent here is ON.
        /// Authored by `/zonecontrol weaponcard`, merged OVERWRITE per key exactly like CustomSpecials.
        ///
        /// WHY THIS EXISTS AT ALL, since a card's odds already live in weapon_&lt;card&gt;_chance.
        /// Before this map, "off" was the ABSENCE of the chance stat, which had two defects:
        ///   1. turning a card off destroyed its tuned chance value - the number had to be retyped from
        ///      memory to turn it back on;
        ///   2. 🔴 at ZONE scope, clearing the key means INHERIT, not OFF. The zone layer is merged on
        ///      top of the tier Default (ZoneVariantProfile.Merge), so a cleared zone key falls straight
        ///      through to the Default's chance and the card keeps rolling while the UI shows it off.
        ///      A zone simply could not switch off a card its tier Default enabled. Closing that is the
        ///      entire point of this map.
        /// It therefore stores an EXPLICIT true/false, never just an off-list: a zone must be able to
        /// re-enable a card the Default turned off as well as disable one the Default turned on, and
        /// only a three-state (true / false / absent) can express both against a merged parent.
        ///
        /// Consulted in exactly one place - ZoneLootMutator.Won - which is the single gate every
        /// chance-gated weapon card already passes through. Missing on older profiles = empty dict.
        /// </summary>
        public Dictionary<string, bool> CustomWeaponCards { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bonus-currency drop table: each entry rolls independently on every governed kill and
        /// injects a stack onto the corpse. Missing on deserialize of older profiles = empty list.</summary>
        public List<ZoneCurrencyDrop> CurrencyDrops { get; set; } = new();

        /// <summary>Spell-book rules (disable / reweight / add spells) consumed read-time at monster
        /// spell selection. Missing on deserialize of older profiles = empty list.</summary>
        public List<ZoneSpellRule> SpellRules { get; set; } = new();

        /// <summary>Generic property overrides STAMPED onto each governed monster at (re)spawn
        /// (ApplyZoneSnapshot). Int/Float/Bool/Int64 biota collections are per-instance clones, so
        /// stamping is safe and reverts on respawn. Keyed by raw property id.</summary>
        public Dictionary<int, long> PropInts { get; set; } = new();
        public Dictionary<int, long> PropInt64s { get; set; } = new();
        public Dictionary<int, double> PropFloats { get; set; } = new();
        public Dictionary<int, bool> PropBools { get; set; } = new();

        public bool TryGet(string statKey, out StatCurve curve) => Stats.TryGetValue(statKey, out curve);

        /// <summary>
        /// Layered merge (2026-07-30 Default layer): later layers win, PER KEY — never wholesale. Feed it
        /// <c>VariationDefault -&gt; zone -&gt; wcid</c>; a key absent from a layer falls through to the one
        /// above it. Returns a NEW profile; every input is left untouched (they are the live admin-mutable
        /// objects) and every nested value is cloned, so the result is safe to hand to a lock-free reader.
        ///
        /// Null layers are skipped, so a zone with no Default and no WCID bucket merges to just itself.
        ///
        /// List-valued fields UNION rather than replace (owner ruling): a zone can add one boss-specific
        /// currency drop without restating the variation's standard drops. Collisions are keyed —
        /// cantrip key / currency wcid / spell id — with the most specific layer winning.
        /// </summary>
        public static ZoneVariantProfile Merge(params ZoneVariantProfile[] layers)
        {
            var result = new ZoneVariantProfile();
            if (layers == null)
                return result;

            foreach (var layer in layers)
            {
                if (layer == null)
                    continue;

                if (layer.Stats != null)
                    foreach (var kv in layer.Stats)
                        result.Stats[kv.Key] = kv.Value;

                if (layer.BodyParts != null)
                    foreach (var kv in layer.BodyParts)
                    {
                        result.BodyParts.TryGetValue(kv.Key, out var lower);
                        var merged = ZoneBodyPart.Merge(lower, kv.Value);
                        if (merged != null)
                            result.BodyParts[kv.Key] = merged;
                    }

                if (layer.PropInts != null)
                    foreach (var kv in layer.PropInts) result.PropInts[kv.Key] = kv.Value;
                if (layer.PropInt64s != null)
                    foreach (var kv in layer.PropInt64s) result.PropInt64s[kv.Key] = kv.Value;
                if (layer.PropFloats != null)
                    foreach (var kv in layer.PropFloats) result.PropFloats[kv.Key] = kv.Value;
                if (layer.PropBools != null)
                    foreach (var kv in layer.PropBools) result.PropBools[kv.Key] = kv.Value;

                // union, deduped by key, most specific wins
                if (layer.CustomCantrips != null)
                    foreach (var key in layer.CustomCantrips)
                        if (!result.CustomCantrips.Contains(key))
                            result.CustomCantrips.Add(key);

                // OVERWRITE per key like PropInts: a band is one value, the most specific layer wins it whole
                if (layer.CustomCantripBands != null)
                    foreach (var kv in layer.CustomCantripBands)
                        if (kv.Value != null)
                            result.CustomCantripBands[kv.Key] = kv.Value.Clone();

                // slot rules: same OVERWRITE-per-key semantics
                if (layer.CustomCantripSlots != null)
                    foreach (var kv in layer.CustomCantripSlots)
                        result.CustomCantripSlots[kv.Key] = kv.Value;

                if (layer.CustomSpecials != null)
                    foreach (var kv in layer.CustomSpecials)
                        result.CustomSpecials[kv.Key] = kv.Value;

                // weapon-card on/off: same OVERWRITE-per-key semantics as CustomSpecials. This is what
                // lets a ZONE win against its tier Default in BOTH directions - an explicit false here
                // beats an inherited true, which a merely-absent chance stat never could.
                if (layer.CustomWeaponCards != null)
                    foreach (var kv in layer.CustomWeaponCards)
                        result.CustomWeaponCards[kv.Key] = kv.Value;

                if (layer.CurrencyDrops != null)
                    foreach (var drop in layer.CurrencyDrops)
                    {
                        if (drop == null) continue;
                        var at = result.CurrencyDrops.FindIndex(d => d.Wcid == drop.Wcid);
                        if (at >= 0) result.CurrencyDrops[at] = drop.Clone();
                        else result.CurrencyDrops.Add(drop.Clone());
                    }

                if (layer.SpellRules != null)
                    foreach (var rule in layer.SpellRules)
                    {
                        if (rule == null) continue;
                        var at = result.SpellRules.FindIndex(r => r.SpellId == rule.SpellId);
                        if (at >= 0) result.SpellRules[at] = rule.Clone();
                        else result.SpellRules.Add(rule.Clone());
                    }
            }

            return result;
        }

        /// <summary>True when this layer carries nothing at all (used to prune empty buckets).</summary>
        public bool IsEmpty =>
            (Stats == null || Stats.Count == 0)
            && (BodyParts == null || BodyParts.Count == 0)
            && (PropInts == null || PropInts.Count == 0)
            && (PropInt64s == null || PropInt64s.Count == 0)
            && (PropFloats == null || PropFloats.Count == 0)
            && (PropBools == null || PropBools.Count == 0)
            && (CustomCantrips == null || CustomCantrips.Count == 0)
            && (CustomCantripBands == null || CustomCantripBands.Count == 0)
            && (CustomCantripSlots == null || CustomCantripSlots.Count == 0)
            && (CustomSpecials == null || CustomSpecials.Count == 0)
            && (CustomWeaponCards == null || CustomWeaponCards.Count == 0)
            && (CurrencyDrops == null || CurrencyDrops.Count == 0)
            && (SpellRules == null || SpellRules.Count == 0);
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

        /// <summary>The ZONE-level layer: stats authored on this zone specifically. Sits between its
        /// variation's Default and any per-WCID bucket. (JSON key stays "Minion" — the pre-2026-07-30 name
        /// from when this was the minion slot of a minion/boss pair — so existing stores keep loading.)</summary>
        [Newtonsoft.Json.JsonProperty("Minion")]
        public ZoneVariantProfile Minion { get; set; } = new();

        // Boss slot REMOVED 2026-07-30 (owner ruling): nothing read it post-decouple, and bosses are now
        // ordinary mobs tuned ~2x their minions via per-WCID overrides, which are strictly more precise.
        // Any "Boss" key still present in a stored profile simply deserializes to nothing.

        /// <summary>Per-WCID overrides: a monster's own layer, merged ON TOP of its variation's Default and
        /// the zone layer, PER STAT (2026-07-30 — it used to REPLACE the whole profile wholesale). A bucket
        /// that sets one stat overrides one stat. Missing on deserialize of older profiles = empty dict.</summary>
        public Dictionary<uint, ZoneVariantProfile> WcidOverrides { get; set; } = new();

        /// <summary>The per-WCID bucket to EDIT, or null when absent and <paramref name="create"/> is false.
        /// Callers that want the RESOLVED stats for a monster must not use this — resolution layers
        /// Default -&gt; zone -&gt; wcid and happens in ZoneControlManager.</summary>
        public ZoneVariantProfile VariantForWcid(uint wcid, bool create = false)
        {
            if (WcidOverrides.TryGetValue(wcid, out var v))
                return v;
            if (!create)
                return null;
            v = new ZoneVariantProfile();
            WcidOverrides[wcid] = v;
            return v;
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
            List<int> customCantrips = null, List<ZoneCurrencyDrop> currencyDrops = null,
            List<ZoneSpellRule> spellRules = null, Dictionary<int, CantripBand> cantripBands = null,
            Dictionary<int, int> cantripSlots = null, Dictionary<int, bool> specialToggles = null,
            Dictionary<string, bool> weaponCardToggles = null)
        {
            CantripSlots = cantripSlots is { Count: > 0 } ? new Dictionary<int, int>(cantripSlots) : EmptyCantripSlots;
            SpecialToggles = specialToggles is { Count: > 0 } ? new Dictionary<int, bool>(specialToggles) : EmptySpecialToggles;
            WeaponCardToggles = weaponCardToggles is { Count: > 0 }
                ? new Dictionary<string, bool>(weaponCardToggles, StringComparer.OrdinalIgnoreCase)
                : EmptyWeaponCardToggles;
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
            SpellRules = spellRules;

            if (cantripBands is { Count: > 0 })
            {
                var bands = new Dictionary<int, (int Min, int Max, int ProcMin, int ProcMax)>(cantripBands.Count);
                foreach (var kv in cantripBands)
                    if (kv.Value != null)
                        bands[kv.Key] = (kv.Value.Min, kv.Value.Max, kv.Value.ProcMin, kv.Value.ProcMax);
                CantripBands = bands;
            }
            else
                CantripBands = EmptyCantripBands;
        }

        private static readonly IReadOnlyDictionary<int, (int Min, int Max, int ProcMin, int ProcMax)> EmptyCantripBands
            = new Dictionary<int, (int Min, int Max, int ProcMin, int ProcMax)>();
        private static readonly IReadOnlyDictionary<int, int> EmptyCantripSlots = new Dictionary<int, int>();

        /// <summary>Per-key slot rule overrides (ZoneCantrips.SlotMask bits), merged view. Empty = catalog defaults everywhere.</summary>
        public IReadOnlyDictionary<int, int> CantripSlots { get; }
        private static readonly IReadOnlyDictionary<int, bool> EmptySpecialToggles = new Dictionary<int, bool>();
        /// <summary>Per-special on/off (absent = on). See ZoneVariantProfile.CustomSpecials.</summary>
        public IReadOnlyDictionary<int, bool> SpecialToggles { get; }
        public bool SpecialEnabled(int key) => !SpecialToggles.TryGetValue(key, out var on) || on;

        private static readonly IReadOnlyDictionary<string, bool> EmptyWeaponCardToggles
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Per-weapon-card on/off keyed by CHANCE STAT NAME (absent = on). Merged view, so this is
        /// the tier Default's toggles with the zone's (and any per-WCID bucket's) laid over them per key.
        /// See ZoneVariantProfile.CustomWeaponCards.</summary>
        public IReadOnlyDictionary<string, bool> WeaponCardToggles { get; }

        /// <summary>
        /// May the card behind <paramref name="chanceStat"/> roll at this scope? Absent = YES (sparse, same
        /// rule as SpecialEnabled), so a scope that authors no toggle behaves exactly as it did before the
        /// map existed.
        ///
        /// Deliberately keyed by the raw stat name and NOT filtered to weapon cards here: the map can only
        /// ever contain names ZoneStat.IsWeaponCardChance accepted at authoring time, so any other chance
        /// stat asked about - armor_cantrip_chance above all - misses the lookup and comes back ON. That is
        /// the scoping that keeps this weapon-only; do not "improve" it by adding a prefix test on the hot
        /// path, and do not let anything write an unvalidated key into the map.
        /// </summary>
        public bool WeaponCardEnabled(string chanceStat)
            => chanceStat == null || !WeaponCardToggles.TryGetValue(chanceStat, out var on) || on;

        /// <summary>Custom cantrip SpellIds for the extra-loot-cantrip roll (may be null = none defined).</summary>
        public IReadOnlyList<int> CustomCantrips { get; }

        /// <summary>Authored roll-band overrides per zone-cantrip catalog key (never null; EMPTY when none
        /// authored). A key absent here rolls the catalog's own Min/Max/ProcMin/ProcMax.</summary>
        public IReadOnlyDictionary<int, (int Min, int Max, int ProcMin, int ProcMax)> CantripBands { get; }

        /// <summary>Bonus-currency drop table entries (may be null = none defined).</summary>
        public IReadOnlyList<ZoneCurrencyDrop> CurrencyDrops { get; }

        /// <summary>Spell-book rules (may be null = none defined). Read at monster spell selection.</summary>
        public IReadOnlyList<ZoneSpellRule> SpellRules { get; }

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
