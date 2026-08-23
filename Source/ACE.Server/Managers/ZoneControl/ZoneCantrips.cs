using System;
using System.Collections.Generic;
using ACE.Common;

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
        public const int SpellDurationLevels = 50212; // each level = +20 pct spell duration (aug formula)
        public const int FortifyVitalsPct = 50226;    // additive percentage POINTS across pieces - all three vitals (key 41)
        public const int BattleMendChancePct = 50227; // per-PIECE proc chance in % (key 42)
        public const int BattleMendHealAmount = 50228;// per-PIECE heal amount on proc (key 42) - LEGACY, no longer stamped
        // Armor v2 slot specials (2026-08-21): MAX-wins across worn pieces (Creature.GetZoneCantripMax), never summed
        public const int PctHpDamagePct = 50229;      // Gauntlets (key 44): pct of target max HP per hit, in TENTHS of a pct (45 = 4.5 pct)
        public const int CheatDeathFlag = 50230;      // Boots (key 45): stamp 1 - lethal hit -> 1 HP + immunity window
        public const int RegenSpecialMult = 50231;    // Bracers (key 46): natural regen multiplier (default 3)
        // 2026-08-22 additions (owner cantrip walkthrough)
        public const int PctMaxHealthPct = 50232;     // key 47 Pct Max Health: percentage POINTS of max HP, SUMMED across worn pieces
        public const int LifeOnHitPct = 50233;        // key 48 Life on Hit: pct of the wielder's MAX HP healed per landed hit, SUMMED, worn cap lifeonhit_cap (25)
        public const int ReinforcedRank = 50234;      // key 49 Reinforced: the protection rank stamped on the piece (1 Superior / 2 Excellent / 3 Unparalleled) - display/bookkeeping only
        public const int SkillBonusBase = 50300;      // + (int)Skill => 50300+.. (flat skill, post-vitae)

        /// <summary>
        /// Per-line slot rule (owner 2026-08-22, "a way to specify live in game which slots the cantrip
        /// can drop in"). Bit flags; 0 = Any. The catalog's ArmorOnly / JewelryOnly flags are the
        /// DEFAULT (DefaultSlotMask); a zone / Default-layer override (ZoneVariantProfile.CustomCantripSlots,
        /// `cantrip <scope> slots <key> ...`) replaces it per key. The mutator and the premade forge both ask
        /// EffectiveSlotMask, so what drops and what /asforge mints agree.
        /// </summary>
        [Flags]
        public enum SlotMask
        {
            Any = 0,
            Armor = 1,
            Shield = 2,
            Jewelry = 4,
            Clothing = 8,
            Cloak = 16,
        }

        public static SlotMask DefaultSlotMask(Def def)
            => def == null ? SlotMask.Any : def.ArmorOnly ? SlotMask.Armor | SlotMask.Shield : def.JewelryOnly ? SlotMask.Jewelry : SlotMask.Any;

        /// <summary>The mask the roll should use: the profile's per-key override when authored, else the catalog default.</summary>
        public static SlotMask EffectiveSlotMask(Def def, IReadOnlyDictionary<int, int> overrides)
            => def != null && overrides != null && overrides.TryGetValue(def.Key, out var m) ? (SlotMask)m : DefaultSlotMask(def);

        /// <summary>What kind of piece this is, for the slot rule. Armor = has an armor level and is not a shield;
        /// Shield = IsShield; Jewelry = ItemType Jewelry; Clothing = ItemType Clothing without an AL (undies);
        /// Cloak = ItemType Clothing covering the cloak slot. Weapons return 0 (they never roll lines here anyway).</summary>
        public static SlotMask PieceMask(WorldObject wo)
        {
            if (wo == null) return SlotMask.Any;
            if (wo.IsShield) return SlotMask.Shield;
            if (wo.ArmorLevel.HasValue && wo.ArmorLevel.Value > 0 && wo.ItemType != ItemType.Jewelry) return SlotMask.Armor;
            if (wo.ItemType == ItemType.Jewelry) return SlotMask.Jewelry;
            if (wo.ItemType == ItemType.Clothing)
                return (wo.ValidLocations ?? 0).HasFlag(EquipMask.Cloak) ? SlotMask.Cloak : SlotMask.Clothing;
            return SlotMask.Any;
        }

        public static bool SlotAllowed(SlotMask rule, SlotMask piece) => rule == SlotMask.Any || (rule & piece) != 0;

        /// <summary>"armor,shield" / "jewelry" / "any" / "clear" -> mask; null on a bad token. -1 = clear.</summary>
        public static bool TryParseSlotSpec(string spec, out int mask)
        {
            mask = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            if (spec.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase)) { mask = -1; return true; }
            if (int.TryParse(spec.Trim(), out var raw) && raw >= 0 && raw <= 31) { mask = raw; return true; }
            foreach (var tok in spec.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (tok.Trim().ToLowerInvariant())
                {
                    case "any": break;
                    case "armor": mask |= (int)SlotMask.Armor; break;
                    case "shield": mask |= (int)SlotMask.Shield; break;
                    case "jewelry": case "jewellery": mask |= (int)SlotMask.Jewelry; break;
                    case "clothing": case "undies": mask |= (int)SlotMask.Clothing; break;
                    case "cloak": mask |= (int)SlotMask.Cloak; break;
                    default: return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Where a SLOT SPECIAL lands (owner 2026-08-22: "drop down here, just with single slot"). One id per
        /// drop slot of the T11+ set. The catalog's SpecialSlot (CoverageMask) is the default; a zone / Default
        /// override in CustomCantripSlots (same dict as the line slot rules - for a special the value is a
        /// SpecialSlotId, not a mask) replaces it. Creature_Death matches and spawns by this id.
        /// </summary>
        public enum SpecialSlotId
        {
            Helm = 1, Chest, Shoulders, Bracers, Gauntlets, Girth, Tassets, Greaves, Boots,
            Shield = 10, Neck = 11, Trinket = 12, Ring = 13, Bracelet = 14, Cloak = 15,
        }

        public static SpecialSlotId DefaultSpecialSlot(Def def) => def?.SpecialSlot switch
        {
            CoverageMask.Head => SpecialSlotId.Helm,
            CoverageMask.OuterwearChest => SpecialSlotId.Chest,
            CoverageMask.OuterwearUpperArms => SpecialSlotId.Shoulders,
            CoverageMask.OuterwearLowerArms => SpecialSlotId.Bracers,
            CoverageMask.Hands => SpecialSlotId.Gauntlets,
            CoverageMask.OuterwearAbdomen => SpecialSlotId.Girth,
            CoverageMask.OuterwearUpperLegs => SpecialSlotId.Tassets,
            CoverageMask.OuterwearLowerLegs => SpecialSlotId.Greaves,
            CoverageMask.Feet => SpecialSlotId.Boots,
            _ => SpecialSlotId.Chest,
        };

        public static SpecialSlotId EffectiveSpecialSlot(Def def, IReadOnlyDictionary<int, int> overrides)
            => def != null && overrides != null && overrides.TryGetValue(def.Key, out var v) && Enum.IsDefined(typeof(SpecialSlotId), v)
                ? (SpecialSlotId)v : DefaultSpecialSlot(def);

        public static bool TryParseSpecialSlot(string spec, out int id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            var t = spec.Trim();
            if (t.Equals("clear", StringComparison.OrdinalIgnoreCase)) { id = -1; return true; }
            if (int.TryParse(t, out var raw) && Enum.IsDefined(typeof(SpecialSlotId), raw)) { id = raw; return true; }
            switch (t.ToLowerInvariant())
            {
                case "helm": case "head": id = (int)SpecialSlotId.Helm; return true;
                case "chest": id = (int)SpecialSlotId.Chest; return true;
                case "shoulders": case "shoulder": case "pauldrons": id = (int)SpecialSlotId.Shoulders; return true;
                case "bracers": case "bracer": id = (int)SpecialSlotId.Bracers; return true;
                case "gauntlets": case "gloves": case "glove": case "hands": id = (int)SpecialSlotId.Gauntlets; return true;
                case "girth": id = (int)SpecialSlotId.Girth; return true;
                case "tassets": case "upperleg": id = (int)SpecialSlotId.Tassets; return true;
                case "greaves": case "lowerleg": id = (int)SpecialSlotId.Greaves; return true;
                case "boots": case "boot": case "feet": id = (int)SpecialSlotId.Boots; return true;
                case "shield": id = (int)SpecialSlotId.Shield; return true;
                case "neck": case "amulet": case "necklace": id = (int)SpecialSlotId.Neck; return true;
                case "trinket": id = (int)SpecialSlotId.Trinket; return true;
                case "ring": id = (int)SpecialSlotId.Ring; return true;
                case "bracelet": case "wrist": id = (int)SpecialSlotId.Bracelet; return true;
                case "cloak": id = (int)SpecialSlotId.Cloak; return true;
            }
            return false;
        }

        public static string SpecialSlotName(int id) => Enum.IsDefined(typeof(SpecialSlotId), id) ? ((SpecialSlotId)id).ToString() : "slot " + id;

        private static CoverageMask SpecialCoverage(SpecialSlotId id) => id switch
        {
            SpecialSlotId.Helm => CoverageMask.Head,
            SpecialSlotId.Chest => CoverageMask.OuterwearChest,
            SpecialSlotId.Shoulders => CoverageMask.OuterwearUpperArms,
            SpecialSlotId.Bracers => CoverageMask.OuterwearLowerArms,
            SpecialSlotId.Gauntlets => CoverageMask.Hands,
            SpecialSlotId.Girth => CoverageMask.OuterwearAbdomen,
            SpecialSlotId.Tassets => CoverageMask.OuterwearUpperLegs,
            SpecialSlotId.Greaves => CoverageMask.OuterwearLowerLegs,
            SpecialSlotId.Boots => CoverageMask.Feet,
            _ => 0,
        };

        /// <summary>Does this dropped piece sit in the special's slot?</summary>
        public static bool SpecialPieceMatches(WorldObject wo, SpecialSlotId id)
        {
            if (wo == null) return false;
            switch (id)
            {
                case SpecialSlotId.Shield: return wo.IsShield;
                case SpecialSlotId.Cloak: return wo.ItemType == ItemType.Clothing && (wo.ValidLocations ?? 0).HasFlag(EquipMask.Cloak);
                case SpecialSlotId.Neck: return wo.ItemType == ItemType.Jewelry && (wo.ValidLocations ?? 0).HasFlag(EquipMask.NeckWear);
                case SpecialSlotId.Trinket: return wo.ItemType == ItemType.Jewelry && (wo.ValidLocations ?? 0).HasFlag(EquipMask.TrinketOne);
                case SpecialSlotId.Ring: return wo.ItemType == ItemType.Jewelry && ((wo.ValidLocations ?? 0) & (EquipMask.FingerWearLeft | EquipMask.FingerWearRight)) != 0;
                case SpecialSlotId.Bracelet: return wo.ItemType == ItemType.Jewelry && ((wo.ValidLocations ?? 0) & (EquipMask.WristWearLeft | EquipMask.WristWearRight)) != 0;
                default:
                    var cov = SpecialCoverage(id);
                    return cov != 0 && !wo.IsShield && wo.ClothingPriority.HasValue && (wo.ClothingPriority.Value & cov) != 0 && (wo.ArmorLevel ?? 0) > 0;
            }
        }

        public static string SlotMaskName(int mask)
        {
            if (mask <= 0) return "Any";
            var parts = new List<string>();
            if ((mask & 1) != 0) parts.Add("Armor");
            if ((mask & 2) != 0) parts.Add("Shield");
            if ((mask & 4) != 0) parts.Add("Jewelry");
            if ((mask & 8) != 0) parts.Add("Clothing");
            if ((mask & 16) != 0) parts.Add("Cloak");
            return string.Join("+", parts);
        }

        public class Def
        {
            public int Key;
            public int Bucket;                             // 1 attrs/vitals, 2 RETIRED masteries, 3 aug tracks, 4 regen/armor/utility, 5 ratings
            public string Name;                            // codename ("Empyrean Might")
            public string Effect;                          // legacy fixed-value text, kept for old callers/help
            public string ValFmt;                          // rolled-value format: "+{0}", "x{0}", "+{0} lvl", "+{0}% vitals", "heal {0}"
            public int Min, Max;                           // roll band, INCLUSIVE both bounds
            public int ProcMin, ProcMax;                   // proc-chance band in %; 0/0 = passive
            public bool ArmorOnly;                         // keys 25 / 49 - needs an ArmorLevel piece
            public bool JewelryOnly;                       // key 48 Life on Hit - rolls only on jewelry (no AL, ItemType Jewelry)
            public bool SetsProtection;                    // key 49 Reinforced - the rolled value is a RANK that SETS every ArmorModVs* on the piece
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
            Trash,      // 32 Spell Duration, 25 Armor Level
            Mid,        // 28 Damage Rating, 29 Crit Damage Rating, 19 Max Health, 31 Healing Boost, 49 Reinforced
            Chase,      // 33 Crit Chance, 43 All Attributes, 47 Pct Max Health, 48 Life on Hit
        }

        private static (int, int)[] P(int propId, int v) => new[] { (propId, v) };

        /// <summary>The unique zone cantrip catalog. Keys are stable — they live in saved zone pools.
        /// Bands (Bucket/Min/Max/ValFmt) mirror the plugin CantripCatalog, the band authority.</summary>
        public static readonly SortedDictionary<int, Def> Catalog = new()
        {
            // vitals
            { 19, new Def { Key = 19, Bucket = 1, Class = CantripClass.Mid, Name = "Max Health", Effect = "+300 Max Health", ValFmt = "+{0}", Min = 50, Max = 100, Ints = P((int)PropertyInt.GearMaxHealth, 300) } },
            // bulwark (Aegis is the ONLY line that modifies the item, not the player - armor/shield pieces only)
            { 25, new Def { Key = 25, Bucket = 4, Class = CantripClass.Trash, ArmorOnly = true, Name = "Armor Level", Effect = "+300 Armor Level on this piece", ValFmt = "+{0}", Min = 50, Max = 200, ArmorBonus = 300 } },
            // slaughter
            { 28, new Def { Key = 28, Bucket = 5, Class = CantripClass.Mid, Name = "Damage Rating", Effect = "+25 Damage Rating", ValFmt = "+{0}", Min = 10, Max = 50, Ints = P((int)PropertyInt.GearDamage, 25) } },
            { 29, new Def { Key = 29, Bucket = 5, Class = CantripClass.Mid, Name = "Crit Damage Rating", Effect = "+40 Critical Damage Rating", ValFmt = "+{0}", Min = 10, Max = 50, Ints = P((int)PropertyInt.GearCritDamage, 40) } },
            // whimsy
            { 31, new Def { Key = 31, Bucket = 4, Class = CantripClass.Mid, Name = "Healing Boost", Effect = "+40 Healing Boost Rating", ValFmt = "+{0}", Min = 25, Max = 100, Ints = P((int)PropertyInt.GearHealingBoost, 40) } },
            { 32, new Def { Key = 32, Bucket = 4, Class = CantripClass.Trash, Name = "Spell Duration", Effect = "+100 pct spell duration", ValFmt = "+{0} lvl", Min = 1, Max = 3, Ints = P(SpellDurationLevels, 5) } },
            // ratings (banded-only lines - legacy fixed Value is 0, they never ship via the legacy overload)
            { 33, new Def { Key = 33, Bucket = 5, Class = CantripClass.Chase, Name = "Crit Chance", Effect = "+1-3 Crit Rating", ValFmt = "+{0}", Min = 1, Max = 3, Ints = P((int)PropertyInt.GearCrit, 0) } },
            // 37-40 RETIRED 2026-08-22 (owner): four class-split copies of what Damage Rating (28) already does
            // for every class. They forced a melee/caster premade split and made 3 of 4 dead for any given
            // player. Keys kept (saved pools, old items still read), props 50222-50225 still summed on equip.
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
            // ── 2026-08-22 pool additions (owner cantrip walkthrough; design in Cantrip_Pool_Decisions_2026-08-22.md) ──
            // Pct Max Health - chase, pinned 1-3 like Crit Chance, SUMMED across pieces (18 x 3 = 54 pct at BiS),
            // stacks on top of the Helm special Fortify Vitals (max-wins). Read in CreatureVital (MaxHealth only).
            { 47, new Def { Key = 47, Bucket = 1, Class = CantripClass.Chase, Name = "Pct Max Health", Effect = "+1-3 pct max health", ValFmt = "+{0} pct", Min = 1, Max = 3, Ints = P(PctMaxHealthPct, 0) } },
            // Life on Hit - chase, JEWELRY ONLY (6 slots), 1-3 pct of the wielder's max HP per landed hit, summed,
            // worn cap lifeonhit_cap (25 pct), lifeonhit_cooldown (3 s). Read in Player.ZcTryLifeOnHit from both hit paths.
            { 48, new Def { Key = 48, Bucket = 4, Class = CantripClass.Chase, JewelryOnly = true, Name = "Life on Hit", Effect = "heal 1-3 pct max HP per hit", ValFmt = "{0} pct HP per hit", Min = 1, Max = 3, Ints = P(LifeOnHitPct, 0) } },
            // Reinforced - mid, ARMOR ONLY, the value is a protection RANK: +1 Superior 1.40 / +2 Excellent 1.60 /
            // +3 Unparalleled 1.80. Stamp SETS every ArmorModVs* on the piece (after EqualizeT11ArmorResists), so it
            // is item data, not an enchantment - hollow mobs cannot strip it. Tier-weighted toward +3 via TierThirds.
            { 49, new Def { Key = 49, Bucket = 4, Class = CantripClass.Mid, ArmorOnly = true, SetsProtection = true, Name = "Reinforced", Effect = "+1-3 protection rank (Superior / Excellent / Unparalleled)", ValFmt = "+{0}", Min = 1, Max = 3, Ints = P(ReinforcedRank, 0) } },
            // ── Armor v2 pool additions ─────────────────────────────────────────────────────────
            // All Attributes - six attrs behind ONE line (keys 1-6 retired); anchor 2500 at T25 = the Dmg/HP per-piece table
            { 43, new Def { Key = 43, Bucket = 1, Class = CantripClass.Chase, Name = "All Attributes", Effect = "+14-69 to ALL six attributes", ValFmt = "+{0}", Min = 14, Max = 69,
                Ints = new[] { (AttrBonusBase + (int)PropertyAttribute.Strength, 0), (AttrBonusBase + (int)PropertyAttribute.Endurance, 0), (AttrBonusBase + (int)PropertyAttribute.Coordination, 0),
                               (AttrBonusBase + (int)PropertyAttribute.Quickness, 0), (AttrBonusBase + (int)PropertyAttribute.Focus, 0), (AttrBonusBase + (int)PropertyAttribute.Self, 0) } } },
        };

        public static bool TryGet(int key, out Def def) => Catalog.TryGetValue(key, out def);

        /// <summary>
        /// Tier-weighted roll position (owner 2026-08-22, Option A): the carrot for pushing tiers is
        /// "better gear AND better-rolled gear". Every band splits into thirds (low / mid / high); the
        /// third is picked by these weights, then the value rolls uniform inside it. T11 = 33/33/34
        /// (today's uniform roll - T11 is deliberately UNCHANGED for ship), T25 = 10/30/60, linear
        /// between, clamped outside. Three-outcome lines (Reinforced +1/+2/+3, Life on Hit 1/2/3 pct)
        /// use the thirds directly. One curve for everything; Tempered's per-tier odds will ride it too.
        /// </summary>
        public static (int Lo, int Mid, int Hi) TierThirds(int tier)
        {
            var f = Math.Clamp((tier - 11) / 14.0, 0.0, 1.0);
            var lo = (int)Math.Round(33 + (10 - 33) * f);
            var mid = (int)Math.Round(33 + (30 - 33) * f);
            return (lo, mid, Math.Max(0, 100 - lo - mid));
        }

        /// <summary>Roll a value in [min,max] inclusive with the tier-weighted third. forceMax wins outright.</summary>
        public static int RollBanded(int min, int max, int tier, bool forceMax = false)
        {
            if (min > max) (min, max) = (max, min);
            if (forceMax || min == max) return max;
            var span = max - min + 1;
            if (span < 3)
                return ThreadSafeRandom.Next(min, max);
            var (wLo, wMid, wHi) = TierThirds(tier);
            var third = span / 3;                     // low and mid thirds get `third` values, the high third takes the remainder
            var loHi = min + third - 1;
            var midHi = loHi + third;
            var roll = ThreadSafeRandom.Next(0, wLo + wMid + wHi - 1);
            if (roll < wLo) return ThreadSafeRandom.Next(min, loHi);
            if (roll < wLo + wMid) return ThreadSafeRandom.Next(loHi + 1, midHi);
            return ThreadSafeRandom.Next(midHi + 1, max);
        }

        /// <summary>Reinforced rank -> the ArmorModVs* value it sets (client label thresholds: Superior 1.40,
        /// Excellent 1.60, Unparalleled 1.80).</summary>
        public static double ReinforcedMod(int rank) => rank >= 3 ? 1.80 : rank == 2 ? 1.60 : 1.40;

        private static readonly PropertyFloat[] ReinforcedMods =
        {
            PropertyFloat.ArmorModVsSlash, PropertyFloat.ArmorModVsPierce, PropertyFloat.ArmorModVsBludgeon, PropertyFloat.ArmorModVsFire,
            PropertyFloat.ArmorModVsCold, PropertyFloat.ArmorModVsAcid, PropertyFloat.ArmorModVsElectric, PropertyFloat.ArmorModVsNether,
        };

        /// <summary>Non-retired, non-special defs that carry a weight class — the Armor v2 line pool source.</summary>
        public static IEnumerable<Def> PoolDefs()
        {
            foreach (var def in Catalog.Values)
                if (!def.SlotSpecial && def.Class != CantripClass.None)
                    yield return def;
        }

        /// <summary>The slot specials (Armor v2) — the per-kill special roll picks one of these at random.</summary>
        public static List<Def> SlotSpecials()
        {
            var list = new List<Def>();
            foreach (var def in Catalog.Values)
                if (def.SlotSpecial)
                    list.Add(def);
            return list;
        }

        /// <summary>Non-retired defs of a bucket — the LEGACY roll pool source (bucket draws are vestigial since Armor v2).</summary>
        public static IEnumerable<Def> LiveBucket(int bucket)
        {
            foreach (var def in Catalog.Values)
                if (def.Bucket == bucket)
                    yield return def;
        }

        private static void AddInt(WorldObject wo, int propId, int value)
        {
            var cur = wo.GetProperty((PropertyInt)propId) ?? 0;
            wo.SetProperty((PropertyInt)propId, cur + value);
        }

        /// <summary>
        /// GRADED stamp (live stat resolution, 2026-08-22): records (key, grade) in the piece's ZcLines
        /// record, SETS the line's props to the value the grade resolves to inside the EFFECTIVE band
        /// (what a later ladder apply will recompute against the live band), and writes the same drop line
        /// text as <see cref="Stamp"/>. SET, not additive - one line per key per piece, the record is the
        /// truth and the props are its cache. Reinforced (SetsProtection) is NOT graded - it is earned and
        /// frozen (owner ruling) - so callers route key 49 through the plain Stamp. Returns the value.
        /// Callers must also <see cref="ZoneStatResolver.StampIdentity"/> the piece once (tier + version).
        /// </summary>
        public static int StampGraded(WorldObject wo, Def def, int grade, (int Min, int Max) band)
        {
            if (wo == null || def == null)
                return 0;
            if (def.SetsProtection)
            {
                var rank = ZoneStatResolver.ValueFor(band.Min, band.Max, grade);
                Stamp(wo, def, rank, 0, (band.Min, band.Max, def.ProcMin, def.ProcMax));
                return rank;
            }

            var value = ZoneStatResolver.ValueFor(band.Min, band.Max, grade);
            ZoneStatResolver.AddLine(wo, def.Key, grade);

            if (def.ArmorOnly && def.Ints == null)
            {
                // key 25 Armor Level: the piece's AL = tier base + the line; SET through the base so a
                // re-roll of the same key never stacks
                var tier = ZoneStatResolver.TierOf(wo);
                if (wo.ArmorLevel.HasValue)
                    wo.ArmorLevel = (tier > 0 ? ZoneStatResolver.BaseArmorLevel(tier) : wo.ArmorLevel.Value) + value;
            }
            else if (def.Ints != null)
            {
                foreach (var (propId, _) in def.Ints)
                    wo.SetProperty((PropertyInt)propId, value);
            }

            string line;
            if (def.SlotSpecial)
                line = string.IsNullOrEmpty(def.ValFmt) ? $"Zone Cantrip: {def.Name}" : $"Zone Cantrip: {def.Name} {string.Format(def.ValFmt, value)}";
            else
                line = $"Zone Cantrip: {def.Name} {string.Format(def.ValFmt ?? "+{0}", value)} [{band.Min}-{band.Max}]";
            wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? line : wo.LongDesc + "\n\n" + line;
            return value;
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
            else if (def.SetsProtection)
            {
                // key 49 Reinforced: the value is a RANK. SET every elemental armor mod on the piece to the
                // rank's value (this runs after ApplyT11GearStats equalized them at ~1.30). Base mods are
                // item data, so hollow mobs (IgnoreMagicArmor) cannot strip this the way they strip Banes.
                var mod = ReinforcedMod(value);
                if (wo.ArmorLevel.HasValue)
                    foreach (var prop in ReinforcedMods)
                        wo.SetProperty(prop, mod);
                if (def.Ints != null)
                    foreach (var (propId, _) in def.Ints)
                        wo.SetProperty((PropertyInt)propId, value);     // bookkeeping, not summed-on-equip semantics
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
