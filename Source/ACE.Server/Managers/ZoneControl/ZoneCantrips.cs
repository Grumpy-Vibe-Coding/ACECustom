using System.Collections.Generic;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Zone Control unique loot cantrips — a PROP-BASED system, deliberately OUTSIDE the spell/enchantment
    /// machinery: no dat entries, no SpellId, no SpellCategory stacking rules. Each cantrip stamps int props
    /// (custom block 50200-50399 in the shard's private prop space, plus the retail Gear* rating props) onto
    /// the dropped item; read hooks sum them across EQUIPPED items, so the bonuses always stack on top of
    /// every spell, gem and aug — and across multiple cantripped pieces worn at once.
    /// Consumers: CreatureAttribute.GetCurrent, CreatureSkill.GetAugBonus_Current, CreatureVital.GearBonus,
    /// Creature_Vitals.VitalHeartBeat (regen augMod), EnchantmentManager/AddEnchantmentResult (spell duration).
    /// </summary>
    public static class ZoneCantrips
    {
        // ── custom PropertyInt ids (ZC cantrip block) ────────────────────────
        public const int PropMin = 50200;
        public const int PropMax = 50399;

        public const int AttrBonusBase = 50200;       // + (int)PropertyAttribute (1..6) => 50201..50206
        public const int MaxStaminaBonus = 50207;     // flat max stamina (max health rides retail GearMaxHealth)
        public const int MaxManaBonus = 50208;        // flat max mana
        public const int HealthRegenAdd = 50209;      // adds into the regen augMod (24 => ~x25 tick)
        public const int StaminaRegenAdd = 50210;
        public const int ManaRegenAdd = 50211;
        public const int SpellDurationLevels = 50212; // each level = +20 pct spell duration (aug formula)
        public const int CreatureAugBonus = 50213;    // gear Creature Augs - the tier-gate stat. LIVE route
                                                      // (owner 2026-08-21): summed by zoneCantripCache and
                                                      // read by CreatureSkill.Current on every skill -
                                                      // deliberately NOT the Effective*AugCount/buff-bake
                                                      // path (freeze-at-cast swap exploit, relief-axis
                                                      // inflation). Raw wield checks never see it.
        public const int ItemAugBonus = 50220;        // gear Item Augs (key 34)
        public const int LifeAugBonus = 50221;        // gear Life Augs (key 36)
        public const int WarAugBonus = 50222;         // gear War Augs (key 37)
        public const int VoidAugBonus = 50223;        // gear Void Augs (key 38)
        public const int MeleeAugBonus = 50224;       // gear Melee Augs (key 39)
        public const int MissileAugBonus = 50225;     // gear Missile Augs (key 40)
        public const int FortifyVitalsPct = 50226;    // additive percentage POINTS across pieces - all three vitals (key 41)
        public const int BattleMendChancePct = 50227; // per-PIECE proc chance in % (key 42)
        public const int BattleMendHealAmount = 50228;// per-PIECE heal amount on proc (key 42) - LEGACY, no longer stamped
        // Armor v2 slot specials (2026-08-21): MAX-wins across worn pieces (Creature.GetZoneCantripMax), never summed
        public const int PctHpDamagePct = 50229;      // Gauntlets (key 44): pct of target max HP per hit, in TENTHS of a pct (45 = 4.5 pct)
        public const int CheatDeathFlag = 50230;      // Boots (key 45): stamp 1 - lethal hit -> 1 HP + immunity window
        public const int RegenSpecialMult = 50231;    // Bracers (key 46): natural regen multiplier (default 3)
        public const int SkillBonusBase = 50300;      // + (int)Skill => 50300+.. (flat skill, post-vitae)

        public class Def
        {
            public int Key;
            public int Bucket;                             // 1 attrs/vitals, 2 RETIRED masteries, 3 aug tracks, 4 regen/armor/utility, 5 ratings
            public string Name;                            // codename ("Empyrean Might")
            public string Effect;                          // legacy fixed-value text, kept for old callers/help
            public string ValFmt;                          // rolled-value format: "+{0}", "x{0}", "+{0} lvl", "+{0}% vitals", "heal {0}"
            public int Min, Max;                           // roll band, INCLUSIVE both bounds
            public int ProcMin, ProcMax;                   // proc-chance band in %; 0/0 = passive
            public bool ArmorOnly;                         // key 25 only - needs an ArmorLevel piece
            public bool Retired;                           // keys 7-18 - masteries cut, kept for saved zone pools
            public (int PropId, int Value)[] Ints;         // int props stamped on the item; PropId also = banded stamp target
            public int ArmorBonus;                         // key 25 legacy fixed AL bonus
            public int ProcChancePropId;                   // key 42: BattleMendChancePct (50227)
            public int ProcAmountPropId;                   // key 42: BattleMendHealAmount (50228) - legacy, unused since Armor v2
            public CantripClass Class;                     // Armor v2 pick weight class (Trash/Mid/Chase); None = never in the line pool
            public bool SlotSpecial;                       // Armor v2 slot special: rolls once per KILL outside the line count, MAX-wins when worn
            public CoverageMask SpecialSlot;               // the armor slot a SlotSpecial stamps onto (Head/OuterwearChest/Hands/Feet/OuterwearLowerArms)
        }

        /// <summary>Armor v2 random-pool weight class (Cantrip_Band_Ladder v2 section 2). The per-class
        /// weights are zone stats cantrip_weight_trash/mid/chase; key 33 Crit Rating reads cantrip_crit_weight.</summary>
        public enum CantripClass
        {
            None = 0,   // retired / specials - never drawn as a line
            Trash,      // 20 Max Stamina, 21 Max Mana, 32 Spell Duration, 25 Armor Level
            Mid,        // 28 Damage Rating, 29 Crit Damage Rating, 19 Max Health, 31 Healing Boost, 34-40 aug tracks
            Chase,      // 33 Crit Rating, 43 All Attributes
        }

        private static (int, int)[] Attr(PropertyAttribute a, int v) => new[] { (AttrBonusBase + (int)a, v) };
        private static (int, int)[] Sk(Skill s, int v) => new[] { (SkillBonusBase + (int)s, v) };
        private static (int, int)[] P(int propId, int v) => new[] { (propId, v) };

        /// <summary>The unique zone cantrip catalog. Keys are stable — they live in saved zone pools.
        /// Bands (Bucket/Min/Max/ValFmt) mirror the plugin CantripCatalog, the band authority.</summary>
        public static readonly SortedDictionary<int, Def> Catalog = new()
        {
            // Empyrean attribute line - RETIRED 2026-08-21 (Armor v2): key 43 All Attributes replaces the six
            { 1, new Def { Key = 1, Bucket = 1, Retired = true, Name = "Empyrean Might", Effect = "+400 Strength", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Attr(PropertyAttribute.Strength, 400) } },
            { 2, new Def { Key = 2, Bucket = 1, Retired = true, Name = "Empyrean Fortitude", Effect = "+400 Endurance", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Attr(PropertyAttribute.Endurance, 400) } },
            { 3, new Def { Key = 3, Bucket = 1, Retired = true, Name = "Empyrean Grace", Effect = "+400 Coordination", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Attr(PropertyAttribute.Coordination, 400) } },
            { 4, new Def { Key = 4, Bucket = 1, Retired = true, Name = "Empyrean Celerity", Effect = "+400 Quickness", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Attr(PropertyAttribute.Quickness, 400) } },
            { 5, new Def { Key = 5, Bucket = 1, Retired = true, Name = "Empyrean Clarity", Effect = "+400 Focus", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Attr(PropertyAttribute.Focus, 400) } },
            { 6, new Def { Key = 6, Bucket = 1, Retired = true, Name = "Empyrean Dominion", Effect = "+400 Willpower", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Attr(PropertyAttribute.Self, 400) } },
            // masteries (weapon + magic schools + summoning) — bucket 2 RETIRED, keys kept for saved pools
            { 7, new Def { Key = 7, Bucket = 2, Retired = true, Name = "Warlord's Edge", Effect = "+400 Heavy Weapons", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.HeavyWeapons, 400) } },
            { 8, new Def { Key = 8, Bucket = 2, Retired = true, Name = "Assassin's Edge", Effect = "+400 Finesse Weapons", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.FinesseWeapons, 400) } },
            { 9, new Def { Key = 9, Bucket = 2, Retired = true, Name = "Duelist's Tempo", Effect = "+400 Light Weapons", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.LightWeapons, 400) } },
            { 10, new Def { Key = 10, Bucket = 2, Retired = true, Name = "Juggernaut's Rhythm", Effect = "+400 Two Handed Combat", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.TwoHandedCombat, 400) } },
            { 11, new Def { Key = 11, Bucket = 2, Retired = true, Name = "Twinblade's Dance", Effect = "+400 Dual Wield", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.DualWield, 400) } },
            { 12, new Def { Key = 12, Bucket = 2, Retired = true, Name = "Deadeye's Mark", Effect = "+400 Missile Weapons", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.MissileWeapons, 400) } },
            { 13, new Def { Key = 13, Bucket = 2, Retired = true, Name = "Archmage's Authority", Effect = "+400 War Magic", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.WarMagic, 400) } },
            { 14, new Def { Key = 14, Bucket = 2, Retired = true, Name = "Voidcaller's Pact", Effect = "+400 Void Magic", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.VoidMagic, 400) } },
            { 15, new Def { Key = 15, Bucket = 2, Retired = true, Name = "Lifeweaver's Bond", Effect = "+400 Life Magic", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.LifeMagic, 400) } },
            { 16, new Def { Key = 16, Bucket = 2, Retired = true, Name = "Beastcaller's Command", Effect = "+400 Creature Enchantment", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.CreatureEnchantment, 400) } },
            { 17, new Def { Key = 17, Bucket = 2, Retired = true, Name = "Artificer's Insight", Effect = "+400 Item Enchantment", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.ItemEnchantment, 400) } },
            { 18, new Def { Key = 18, Bucket = 2, Retired = true, Name = "Archon's Covenant", Effect = "+400 Summoning", ValFmt = "+{0}", Min = 400, Max = 400, Ints = Sk(Skill.Summoning, 400) } },
            // vitals
            { 19, new Def { Key = 19, Bucket = 1, Class = CantripClass.Mid, Name = "Max Health", Effect = "+300 Max Health", ValFmt = "+{0}", Min = 50, Max = 100, Ints = P((int)PropertyInt.GearMaxHealth, 300) } },
            { 20, new Def { Key = 20, Bucket = 1, Class = CantripClass.Trash, Name = "Max Stamina", Effect = "+500 Max Stamina", ValFmt = "+{0}", Min = 50, Max = 100, Ints = P(MaxStaminaBonus, 500) } },
            { 21, new Def { Key = 21, Bucket = 1, Class = CantripClass.Trash, Name = "Max Mana", Effect = "+500 Max Mana", ValFmt = "+{0}", Min = 50, Max = 100, Ints = P(MaxManaBonus, 500) } },
            // regen lines - RETIRED 2026-08-21 (Armor v2): regen is the Bracers slot special (key 46) now
            { 22, new Def { Key = 22, Bucket = 4, Retired = true, Name = "Wellspring of Souls", Effect = "x25 health regen", ValFmt = "x{0}", Min = 1, Max = 3, Ints = P(HealthRegenAdd, 24) } },
            { 23, new Def { Key = 23, Bucket = 4, Retired = true, Name = "Marathon's Breath", Effect = "x25 stamina regen", ValFmt = "x{0}", Min = 1, Max = 3, Ints = P(StaminaRegenAdd, 24) } },
            { 24, new Def { Key = 24, Bucket = 4, Retired = true, Name = "Tides of Ether", Effect = "x25 mana regen", ValFmt = "x{0}", Min = 1, Max = 3, Ints = P(ManaRegenAdd, 24) } },
            // bulwark (Aegis is the ONLY line that modifies the item, not the player - armor/shield pieces only)
            { 25, new Def { Key = 25, Bucket = 4, Class = CantripClass.Trash, ArmorOnly = true, Name = "Armor Level", Effect = "+300 Armor Level on this piece", ValFmt = "+{0}", Min = 50, Max = 200, ArmorBonus = 300 } },
            // 26/27 RETIRED 2026-08-21 (Armor v2): DR/CDR are the guaranteed core rolled by ApplyT11GearStats - never double-stack past cap
            { 26, new Def { Key = 26, Bucket = 5, Retired = true, Name = "Bulwark of Ages", Effect = "+60 Damage Reduction Rating", ValFmt = "+{0}", Min = 10, Max = 50, Ints = P((int)PropertyInt.GearDamageResist, 60) } },
            { 27, new Def { Key = 27, Bucket = 5, Retired = true, Name = "Deathward", Effect = "+40 Critical Damage Reduction Rating", ValFmt = "+{0}", Min = 10, Max = 50, Ints = P((int)PropertyInt.GearCritDamageResist, 40) } },
            // slaughter
            { 28, new Def { Key = 28, Bucket = 5, Class = CantripClass.Mid, Name = "Damage Rating", Effect = "+25 Damage Rating", ValFmt = "+{0}", Min = 10, Max = 50, Ints = P((int)PropertyInt.GearDamage, 25) } },
            { 29, new Def { Key = 29, Bucket = 5, Class = CantripClass.Mid, Name = "Crit Damage Rating", Effect = "+40 Critical Damage Rating", ValFmt = "+{0}", Min = 10, Max = 50, Ints = P((int)PropertyInt.GearCritDamage, 40) } },
            // whimsy
            { 30, new Def { Key = 30, Bucket = 4, Retired = true, Name = "Fleet of the Zephyr", Effect = "+400 Run", ValFmt = "+{0}", Min = 25, Max = 100, Ints = Sk(Skill.Run, 400) } },
            { 31, new Def { Key = 31, Bucket = 4, Class = CantripClass.Mid, Name = "Healing Boost", Effect = "+40 Healing Boost Rating", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P((int)PropertyInt.GearHealingBoost, 40) } },
            { 32, new Def { Key = 32, Bucket = 4, Class = CantripClass.Trash, Name = "Spell Duration", Effect = "+100 pct spell duration", ValFmt = "+{0} lvl", Min = 1, Max = 3, Ints = P(SpellDurationLevels, 5) } },
            // ratings (banded-only lines - legacy fixed Value is 0, they never ship via the legacy overload)
            { 33, new Def { Key = 33, Bucket = 5, Class = CantripClass.Chase, Name = "Crit Chance", Effect = "+1-3 Crit Rating", ValFmt = "+{0}", Min = 1, Max = 3, Ints = P((int)PropertyInt.GearCrit, 0) } },
            // aug tracks (flat damage / enchantment power - key 35 stamps ADDITIVELY on top of the fixed base)
            { 34, new Def { Key = 34, Bucket = 3, Class = CantripClass.Mid, Name = "Item Augs", Effect = "+25-100 Item Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(ItemAugBonus, 0) } },
            { 35, new Def { Key = 35, Bucket = 3, Class = CantripClass.Mid, Name = "Creature Augs", Effect = "+25-100 Creature Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(CreatureAugBonus, 0) } },
            { 36, new Def { Key = 36, Bucket = 3, Class = CantripClass.Mid, Name = "Life Augs", Effect = "+25-100 Life Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(LifeAugBonus, 0) } },
            { 37, new Def { Key = 37, Bucket = 3, Class = CantripClass.Mid, Name = "War Augs", Effect = "+25-100 War Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(WarAugBonus, 0) } },
            { 38, new Def { Key = 38, Bucket = 3, Class = CantripClass.Mid, Name = "Void Augs", Effect = "+25-100 Void Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(VoidAugBonus, 0) } },
            { 39, new Def { Key = 39, Bucket = 3, Class = CantripClass.Mid, Name = "Melee Augs", Effect = "+25-100 Melee Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(MeleeAugBonus, 0) } },
            { 40, new Def { Key = 40, Bucket = 3, Class = CantripClass.Mid, Name = "Missile Augs", Effect = "+25-100 Missile Augs", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P(MissileAugBonus, 0) } },
            // ── Armor v2 SLOT SPECIALS (2026-08-21) ─────────────────────────────────────────────
            // Roll ONCE per KILL outside the line count (Creature_Death), stamp the dropped piece of
            // their slot, MAX-wins across worn pieces. Never drawn as a pool line (SlotSpecial).
            // Helm - fortify vitals, percentage POINTS, highest single value wins, all three pools
            { 41, new Def { Key = 41, Bucket = 1, SlotSpecial = true, SpecialSlot = CoverageMask.Head, Name = "Fortify Vitals", Effect = "+5-25 pct max vitals (Helm special)", ValFmt = "+{0}% vitals", Min = 5, Max = 25, Ints = P(FortifyVitalsPct, 0) } },
            // Chest - Battle Mending death save: after damage, HP > 0 and < 25 pct -> heal to max, 60 s CD; no magnitude.
            // The old proc PAIR is gone: only 50227 is stamped (= 1, presence flag); the combat side reads it MAX-wins.
            { 42, new Def { Key = 42, Bucket = 4, SlotSpecial = true, SpecialSlot = CoverageMask.OuterwearChest, Name = "Battle Mending", Effect = "death save: below 25 pct HP heal to full, 60 s cooldown (Chest special)", ValFmt = "", Min = 1, Max = 1, Ints = P(BattleMendChancePct, 0) } },
            // Gauntlets - flat pct of target max HP per hit, no crit/mults; value in TENTHS of a pct (40-60 = 4-6 pct at T11); 2 s CD; no-kill rule
            { 44, new Def { Key = 44, Bucket = 5, SlotSpecial = true, SpecialSlot = CoverageMask.Hands, Name = "Pct HP Damage", Effect = "4-6 pct of target max HP per hit (Gauntlets special)", ValFmt = "{0}", Min = 40, Max = 60, Ints = P(PctHpDamagePct, 0) } },
            // Boots - Cheat Death: lethal hit -> 1 HP + 5 s immunity, 10 min CD per character; no magnitude
            { 45, new Def { Key = 45, Bucket = 4, SlotSpecial = true, SpecialSlot = CoverageMask.Feet, Name = "Cheat Death", Effect = "lethal hit leaves 1 HP + 5 s immunity, 10 min cooldown (Boots special)", ValFmt = "", Min = 1, Max = 1, Ints = P(CheatDeathFlag, 0) } },
            // Bracers - natural regen multiplier (x3 default), Prodigal stays suppressed
            { 46, new Def { Key = 46, Bucket = 4, SlotSpecial = true, SpecialSlot = CoverageMask.OuterwearLowerArms, Name = "Regeneration", Effect = "x3 natural regen (Bracers special)", ValFmt = "x{0}", Min = 3, Max = 3, Ints = P(RegenSpecialMult, 0) } },
            // ── Armor v2 pool additions ─────────────────────────────────────────────────────────
            // All Attributes - six attrs behind ONE line (keys 1-6 retired); anchor 2500 at T25 = the Dmg/HP per-piece table
            { 43, new Def { Key = 43, Bucket = 1, Class = CantripClass.Chase, Name = "All Attributes", Effect = "+14-69 to ALL six attributes", ValFmt = "+{0}", Min = 14, Max = 69,
                Ints = new[] { (AttrBonusBase + (int)PropertyAttribute.Strength, 0), (AttrBonusBase + (int)PropertyAttribute.Endurance, 0), (AttrBonusBase + (int)PropertyAttribute.Coordination, 0),
                               (AttrBonusBase + (int)PropertyAttribute.Quickness, 0), (AttrBonusBase + (int)PropertyAttribute.Focus, 0), (AttrBonusBase + (int)PropertyAttribute.Self, 0) } } },
        };

        public static bool TryGet(int key, out Def def) => Catalog.TryGetValue(key, out def);

        /// <summary>Non-retired, non-special defs that carry a weight class — the Armor v2 line pool source.</summary>
        public static IEnumerable<Def> PoolDefs()
        {
            foreach (var def in Catalog.Values)
                if (!def.Retired && !def.SlotSpecial && def.Class != CantripClass.None)
                    yield return def;
        }

        /// <summary>The slot specials (Armor v2) — the per-kill special roll picks one of these at random.</summary>
        public static List<Def> SlotSpecials()
        {
            var list = new List<Def>();
            foreach (var def in Catalog.Values)
                if (!def.Retired && def.SlotSpecial)
                    list.Add(def);
            return list;
        }

        /// <summary>Non-retired defs of a bucket — the LEGACY roll pool source (bucket draws are vestigial since Armor v2).</summary>
        public static IEnumerable<Def> LiveBucket(int bucket)
        {
            foreach (var def in Catalog.Values)
                if (!def.Retired && def.Bucket == bucket)
                    yield return def;
        }

        private static void AddInt(WorldObject wo, int propId, int value)
        {
            var cur = wo.GetProperty((PropertyInt)propId) ?? 0;
            wo.SetProperty((PropertyInt)propId, cur + value);
        }

        // The pre-band fixed-value Stamp(wo, def) overload was DELETED 2026-08-21: it had zero
        // callers left, and for the banded-only keys 33-42 (Ints Value = 0 / no Ints at all) it
        // would stamp nothing while still marking the item. Roll a value and use the overload below.

        /// <summary>Banded stamp: writes the ROLLED value additively into each Ints PropId (the legacy fixed
        /// Value is ignored), handles the two special mechanisms (ArmorOnly item AL, proc chance/amount pair),
        /// and marks the item with the plugin-format drop line ("Name +73 [25-100]"). The optional band is the
        /// EFFECTIVE band the value was rolled from (zone override) — omitted, the catalog band is printed.</summary>
        public static void Stamp(WorldObject wo, Def def, int value, int proc = 0,
            (int Min, int Max, int ProcMin, int ProcMax)? band = null)
        {
            if (wo == null || def == null)
                return;

            if (def.SlotSpecial)
            {
                // Armor v2 slot special: the value is written to every Ints prop (magnitude-less keys
                // stamp 1 = "present"); the combat side reads it MAX-wins across worn pieces. The drop
                // line MUST start with "Zone Cantrip:" or FinalizeT11LongDesc deletes it; magnitude-less
                // keys (42/45) print bare, 41/44/46 print the rolled value.
                if (def.Ints != null)
                    foreach (var (propId, _) in def.Ints)
                        AddInt(wo, propId, value);

                var specialLine = string.IsNullOrEmpty(def.ValFmt)
                    ? $"Zone Cantrip: {def.Name}"
                    : $"Zone Cantrip: {def.Name} {string.Format(def.ValFmt, value)}";
                wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? specialLine : wo.LongDesc + "\n\n" + specialLine;
                return;
            }

            if (def.ProcChancePropId != 0)
            {
                // proc line (legacy key 42 shape): chance and amount are separate props, both additive across pieces
                AddInt(wo, def.ProcChancePropId, proc);
                if (def.ProcAmountPropId != 0)
                    AddInt(wo, def.ProcAmountPropId, value);
            }
            else if (def.ArmorOnly)
            {
                // key 25: modifies the item itself, no Ints — the rolled value, NOT the legacy 300
                if (wo.ArmorLevel.HasValue)
                    wo.ArmorLevel = wo.ArmorLevel.Value + value;
            }
            else if (def.Ints != null)
            {
                foreach (var (propId, _) in def.Ints)
                    AddInt(wo, propId, value);
            }

            var (min, max, procMin, procMax) = band ?? (def.Min, def.Max, def.ProcMin, def.ProcMax);
            var rolled = string.Format(def.ValFmt ?? "+{0}", value);
            var line = def.ProcChancePropId != 0
                ? $"Zone Cantrip: {def.Name} {proc}% to {rolled} [{procMin}-{procMax}% / {min}-{max}]"
                : $"Zone Cantrip: {def.Name} {rolled} [{min}-{max}]";
            wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? line : wo.LongDesc + "\n\n" + line;
        }
    }
}
