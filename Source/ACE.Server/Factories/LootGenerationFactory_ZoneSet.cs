using System.Collections.Concurrent;
using System.Collections.Generic;

using ACE.Common;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        // =====================================================================================
        // Structured tier-11+ loot set ("T11" endgame loot)
        //
        // Every kill from a tier-11+ treasure profile drops one weapon per FAMILY (subtype/skill
        // randomized inside the pick) plus a fixed gear allotment (armor / jewelry / cloak). All
        // pieces roll BLANK (isMagical = false -> no legacy spell sets); useful cantrips/spells/
        // procs come from the Zone Control post-roll mutation layer (ZoneLootMutator).
        // Quality/tier come from the (QB-scaled) treasure profile passed in.
        //
        // This set is the DEFAULT behaviour of a tier-11+ profile and does NOT require Zone
        // Control: the T11 profiles carry zero item/magic/mundane chances of their own, so this
        // generator is what makes them drop anything at all. Drops are PER-SLOT (owner decision
        // 2026-07-20, "no set enabler"): every equip slot has its own count, defaulting to 1 at
        // tier 11+ and 0 below. A resolved zone profile overrides individual slot counts via the
        // loot_slot_* stats; setting a slot to 0 turns just that slot off, and there is no
        // separate enable flag. With Zone Control absent, tier-11+ mobs drop one of everything.
        //
        // Design doc: C:\AI\ZoneControl\ACE_Loot_Systems_DeepDive_2026-07-17.md §12-13
        // =====================================================================================

        /// <summary>
        /// Lowest treasure tier that drops the structured set by default (no Zone Control needed).
        /// </summary>
        public const int ZoneLootSetMinTier = 11;

        /// <summary>
        /// Per-slot drop counts for one kill. Weapons = drops PER FAMILY (9 families). Each armor
        /// slot counts pieces whose coverage includes that slot; a multi-slot piece (coat) credits
        /// every slot it covers, so covered slots don't roll again.
        /// </summary>
        public class ZoneLootSetCounts
        {
            public int Weapons;
            public int Helm, Chest, Shoulder, Bracer, Glove, Girth, UpperLeg, LowerLeg, Boot;
            public int Shield;
            public int Amulet, Ring, Bracelet, Trinket;
            public int Cloak;

            public bool Any =>
                Weapons > 0 || Helm > 0 || Chest > 0 || Shoulder > 0 || Bracer > 0 || Glove > 0 ||
                Girth > 0 || UpperLeg > 0 || LowerLeg > 0 || Boot > 0 || Shield > 0 ||
                Amulet > 0 || Ring > 0 || Bracelet > 0 || Trinket > 0 || Cloak > 0;

            /// <summary>Tier default: one of everything at tier 11+, nothing below.</summary>
            public static ZoneLootSetCounts TierDefault(int tier)
            {
                var n = tier >= ZoneLootSetMinTier ? 1 : 0;
                return new ZoneLootSetCounts
                {
                    Weapons = n,
                    Helm = n, Chest = n, Shoulder = n, Bracer = n, Glove = n,
                    Girth = n, UpperLeg = n, LowerLeg = n, Boot = n,
                    Shield = n, Amulet = n, Ring = n, Bracelet = n, Trinket = n, Cloak = n,
                };
            }
        }

        // Jewelry pools by equip slot. Crowns/coronets are deliberately excluded: they roll down
        // the armor path (they have an armor level) and would duplicate the Head slot.
        private static readonly WeenieClassName[] zoneSetNeckWcids =
            { WeenieClassName.amulet, WeenieClassName.gorget, WeenieClassName.necklace, WeenieClassName.necklaceheavy };

        private static readonly WeenieClassName[] zoneSetWristWcids =
            { WeenieClassName.bracelet, WeenieClassName.braceletheavy };

        private static readonly WeenieClassName[] zoneSetFingerWcids =
            { WeenieClassName.ring, WeenieClassName.ringjeweled };

        private static readonly WeenieClassName[] zoneSetTrinketWcids =
        {
            WeenieClassName.ace41483_compass, WeenieClassName.ace41484_goggles,
            WeenieClassName.ace41487_mechanicalscarab, WeenieClassName.ace41486_puzzlebox,
            WeenieClassName.ace41485_pocketwatch, WeenieClassName.ace41488_top,
        };

        // wcid -> ClothingPriority, read from the cached weenie so candidate pieces can be
        // accepted/rejected by slot WITHOUT instantiating a WorldObject for each attempt.
        private static readonly ConcurrentDictionary<WeenieClassName, ACE.Entity.Enum.CoverageMask> zoneSetCoverage = new();

        private static ACE.Entity.Enum.CoverageMask GetZoneSetCoverage(WeenieClassName wcid)
        {
            return zoneSetCoverage.GetOrAdd(wcid, w =>
            {
                var weenie = DatabaseManager.World.GetCachedWeenie((uint)w);
                return (ACE.Entity.Enum.CoverageMask)(weenie?.GetProperty(PropertyInt.ClothingPriority) ?? 0);
            });
        }

        /// <summary>
        /// Removes any character-LEVEL wield requirement from a tier-11+ drop (owner decision
        /// 2026-07-20: T11 loot is never level-gated). SKILL requirements (RawSkill, e.g. a
        /// weapon's own skill or a cloak/armor MeleeDefense gate) are intentionally left alone.
        ///
        /// This is a final sweep rather than a fix at each producer, because a level requirement
        /// can arrive from three independent places: a mutation script, factory code (e.g.
        /// MutateCloak's ItemMaxLevel -> WieldDifficulty 30/60/90/120/150), or the BASE WEENIE
        /// itself -- cloaks in particular ship with WieldRequirements = Level already set, which
        /// no mutation ever clears. Patching producers one at a time misses that third case.
        /// </summary>
        public static void StripLevelWieldRequirements(WorldObject wo)
        {
            if (wo == null)
                return;

            if (wo.WieldRequirements == ACE.Entity.Enum.WieldRequirement.Level)
            {
                wo.WieldRequirements = ACE.Entity.Enum.WieldRequirement.Invalid;
                wo.WieldSkillType = null;
                wo.WieldDifficulty = null;
            }

            if (wo.WieldRequirements2 == ACE.Entity.Enum.WieldRequirement.Level)
            {
                wo.WieldRequirements2 = ACE.Entity.Enum.WieldRequirement.Invalid;
                wo.WieldSkillType2 = null;
                wo.WieldDifficulty2 = null;
            }
        }

        private enum ZoneSetFamily
        {
            Axe,        // skill: Heavy/Light/Finesse/TwoHanded
            Dagger,     // skill: Heavy/Light/Finesse; MS or non-MS
            Mace,       // skill: Heavy/Light/Finesse/TwoHanded; Mace or MaceJitte on 1H
            Spear,      // skill: Heavy/Light/Finesse/TwoHanded
            Staff,      // skill: Heavy/Light/Finesse
            Sword,      // skill: Heavy/Light/Finesse/TwoHanded; MS or non-MS on 1H
            Unarmed,    // skill: Heavy/Light/Finesse
            Missile,    // bow / crossbow / atlatl
            Caster,     // wand / orb / staff
        }

        private static readonly ZoneSetFamily[] zoneSetFamilies = (ZoneSetFamily[])System.Enum.GetValues(typeof(ZoneSetFamily));

        /// <summary>
        /// Generates the full structured loot set for one kill, one category per configured slot,
        /// all mutated against <paramref name="profile"/>.
        /// </summary>
        public static List<WorldObject> CreateZoneLootSet(TreasureDeath profile, ZoneLootSetCounts counts)
        {
            var items = new List<WorldObject>();

            for (var i = 0; i < counts.Weapons; i++)
            {
                foreach (var family in zoneSetFamilies)
                {
                    var weapon = CreateZoneSetWeapon(profile, family);
                    if (weapon != null)
                        items.Add(weapon);
                    else
                        log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): failed to create {family} weapon");
                }
            }

            AddZoneSetArmorSlots(items, profile, counts);

            for (var i = 0; i < counts.Shield; i++)
                AddZoneSetShield(items, profile);

            for (var i = 0; i < counts.Amulet; i++)
                AddZoneSetJewelryPiece(items, profile, zoneSetNeckWcids, "neck");
            for (var i = 0; i < counts.Bracelet; i++)
                AddZoneSetJewelryPiece(items, profile, zoneSetWristWcids, "wrist");
            for (var i = 0; i < counts.Ring; i++)
                AddZoneSetJewelryPiece(items, profile, zoneSetFingerWcids, "finger");
            for (var i = 0; i < counts.Trinket; i++)
                AddZoneSetJewelryPiece(items, profile, zoneSetTrinketWcids, "trinket");

            for (var i = 0; i < counts.Cloak; i++)
                AddZoneSetGearPiece(items, profile, TreasureItemType_Orig.Cloak);

            return items;
        }

        /// <summary>
        /// Walks the nine armor equip slots top-down, dropping pieces until every slot has met its
        /// configured count. A multi-slot piece (an Amuli coat covers chest + abdomen + upper arms)
        /// CREDITS every slot it covers, so covered slots don't roll again (owner decision
        /// 2026-07-20) -- with all counts at 1 this yields one clean wearable set, 4-9 pieces.
        /// </summary>
        private static void AddZoneSetArmorSlots(List<WorldObject> items, TreasureDeath profile, ZoneLootSetCounts counts)
        {
            // top-down slot order; each entry pairs the CoverageMask bit with its configured count
            var slots = new (ACE.Entity.Enum.CoverageMask mask, int target)[]
            {
                (ACE.Entity.Enum.CoverageMask.Head,               counts.Helm),
                (ACE.Entity.Enum.CoverageMask.OuterwearChest,     counts.Chest),
                (ACE.Entity.Enum.CoverageMask.OuterwearUpperArms, counts.Shoulder),
                (ACE.Entity.Enum.CoverageMask.OuterwearLowerArms, counts.Bracer),
                (ACE.Entity.Enum.CoverageMask.Hands,              counts.Glove),
                (ACE.Entity.Enum.CoverageMask.OuterwearAbdomen,   counts.Girth),
                (ACE.Entity.Enum.CoverageMask.OuterwearUpperLegs, counts.UpperLeg),
                (ACE.Entity.Enum.CoverageMask.OuterwearLowerLegs, counts.LowerLeg),
                (ACE.Entity.Enum.CoverageMask.Feet,               counts.Boot),
            };

            var credit = new int[slots.Length];

            for (var s = 0; s < slots.Length; s++)
            {
                while (credit[s] < slots[s].target)
                {
                    // reject-sample the normal armor roll until it yields a piece covering this
                    // slot. Coverage comes from the cached weenie, so rejects cost no object
                    // creation.
                    var armorType = TreasureArmorType.Undef;
                    var wcid = WeenieClassName.undef;
                    var coverage = default(ACE.Entity.Enum.CoverageMask);

                    for (var attempt = 0; attempt < 50; attempt++)
                    {
                        // same two-step as the stock armor path (LootGenerationFactory.cs:1130-1131):
                        // roll the armor TYPE for the tier, then a wcid within it
                        var candidateType = ArmorTypeChance.Roll(profile.Tier);
                        var candidate = ArmorWcids.Roll(profile, ref candidateType);

                        if (candidate == WeenieClassName.undef)
                            continue;

                        var candidateCoverage = GetZoneSetCoverage(candidate);

                        if ((candidateCoverage & slots[s].mask) == 0)
                            continue;   // wrong slot (or a shield / non-clothing entry)

                        armorType = candidateType;
                        wcid = candidate;
                        coverage = candidateCoverage;
                        break;
                    }

                    if (wcid == WeenieClassName.undef)
                    {
                        log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): no armor wcid found covering {slots[s].mask}");
                        break;
                    }

                    var roll = new TreasureRoll(TreasureItemType_Orig.Armor) { ArmorType = armorType, Wcid = wcid };
                    var wo = CreateAndMutateWcid(profile, roll, false);

                    if (wo == null)
                    {
                        log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): failed to create {slots[s].mask} armor ({wcid})");
                        break;
                    }

                    items.Add(wo);

                    // a piece credits EVERY slot it covers
                    for (var j = 0; j < slots.Length; j++)
                    {
                        if ((coverage & slots[j].mask) != 0)
                            credit[j]++;
                    }
                }
            }
        }

        /// <summary>
        /// Shields carry no ClothingPriority, so they can never satisfy an armor slot and are
        /// rolled separately here.
        /// </summary>
        private static void AddZoneSetShield(List<WorldObject> items, TreasureDeath profile)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var armorType = ArmorTypeChance.Roll(profile.Tier);
                var wcid = ArmorWcids.Roll(profile, ref armorType);

                if (wcid == WeenieClassName.undef)
                    continue;

                var weenie = DatabaseManager.World.GetCachedWeenie((uint)wcid);

                if ((weenie?.GetProperty(PropertyInt.CombatUse) ?? 0) != (int)ACE.Entity.Enum.CombatUse.Shield)
                    continue;

                var roll = new TreasureRoll(TreasureItemType_Orig.Armor) { ArmorType = armorType, Wcid = wcid };
                var wo = CreateAndMutateWcid(profile, roll, false);

                if (wo != null)
                {
                    items.Add(wo);
                    return;
                }
            }

            log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): failed to roll a shield");
        }

        /// <summary>
        /// Renames a tier-11+ drop to "T11 - [base weenie name]" (owner decision 2026-07-20).
        /// MaterialType is cleared because the CLIENT prepends the material adjective to the
        /// displayed name ("Iron T11 - Frost Ken" otherwise) -- the trade-off is these drops
        /// cannot be salvaged for material (acceptable: T11 gear is worn, not salvage fodder;
        /// ItemWorkmanship stays, so tinkering ONTO the item still works).
        /// </summary>
        public static void ApplyT11NamePrefix(WorldObject wo)
        {
            if (wo == null)
                return;

            var weenie = DatabaseManager.World.GetCachedWeenie(wo.WeenieClassId);
            var baseName = weenie?.GetProperty(ACE.Entity.Enum.Properties.PropertyString.Name) ?? wo.Name;

            if (string.IsNullOrWhiteSpace(baseName) || baseName.StartsWith("T11 - "))
                return;

            wo.Name = $"T11 - {baseName}";
            wo.MaterialType = null;
        }

        /// <summary>
        /// Corpse display order for tier-11+ loot (owner decision 2026-07-20): quest/create-list
        /// items are added to the corpse before treasure (see GenerateTreasure), and treasure
        /// itself sorts casters, then missile launchers, then unarmed, then sword, then the other
        /// melee families, then armor, shields, jewelry, cloaks. Lower = earlier. Stable-sorted,
        /// so generation order is preserved within a group.
        /// </summary>
        public static int GetZoneLootDisplayOrder(WorldObject wo)
        {
            if (wo is Caster)
                return 10;

            if (wo is MissileLauncher || wo is Missile)
                return 20;

            if (wo is MeleeWeapon)
            {
                return wo.W_WeaponType switch
                {
                    ACE.Entity.Enum.WeaponType.Unarmed => 30,
                    ACE.Entity.Enum.WeaponType.Sword => 40,
                    ACE.Entity.Enum.WeaponType.Axe => 50,
                    ACE.Entity.Enum.WeaponType.Dagger => 60,
                    ACE.Entity.Enum.WeaponType.Mace => 70,
                    ACE.Entity.Enum.WeaponType.Spear => 80,
                    ACE.Entity.Enum.WeaponType.Staff => 90,
                    _ => 95,
                };
            }

            if (wo.IsShield)
                return 110;

            if ((wo.ArmorLevel ?? 0) > 0)
                return 100;

            if (wo.ItemType == ACE.Entity.Enum.ItemType.Jewelry)
                return 120;

            if (ACE.Server.Entity.Cloak.IsCloak(wo))
                return 130;

            if (wo is Clothing)
                return 105;

            return 200;     // coins, gems, anything uncategorized -> last
        }

        /// <summary>
        /// Appends a formatted, persisted Ratings block to a tier-11+ drop's LongDesc:
        ///
        ///   Ratings:
        ///   * Damage 20 (min 16, max 20)
        ///   * Crit Damage 18 (min 16, max 20)
        ///
        /// Each line shows the rolled value plus the min/max the tier-11 table could have rolled.
        /// AppraiseInfo detects this block and suppresses the raw gear-rating ints in the appraisal
        /// profile, so the client does not also render its own compact "Ratings: Dam X, ..." line.
        /// ASCII only (old client cannot render anything else).
        /// </summary>
        public static void AppendRatingsAppraisalBlock(WorldObject wo)
        {
            if (wo == null)
                return;

            var ratings = new List<(string label, int value)>();

            void Collect(string label, int? value)
            {
                if (value.HasValue && value.Value > 0)
                    ratings.Add((label, value.Value));
            }

            Collect("Damage", wo.GearDamage);
            Collect("Damage Resist", wo.GearDamageResist);
            Collect("Crit", wo.GearCrit);
            Collect("Crit Resist", wo.GearCritResist);
            Collect("Crit Damage", wo.GearCritDamage);
            Collect("Crit Damage Resist", wo.GearCritDamageResist);
            Collect("Nether Resist", wo.GearNetherResistRating);
            Collect("Healing Boost", wo.GearHealingBoost);
            Collect("Max Health", wo.GearMaxHealth);

            if (ratings.Count == 0)
                return;

            // category determines which tier-11 table produced the rolls (mirrors the category
            // logic in GearRatingChance.RollT11 / TryMutateGearRatingT10)
            var category =
                wo is MeleeWeapon || wo is MissileLauncher || wo is Caster ? Tables.GearRatingChance.GearRatingCategory.Weapon :
                (wo.ArmorLevel ?? 0) > 0 || wo.IsShield ? Tables.GearRatingChance.GearRatingCategory.Armor :
                wo.ItemType == ACE.Entity.Enum.ItemType.Jewelry ? Tables.GearRatingChance.GearRatingCategory.Jewelry :
                Tables.GearRatingChance.GearRatingCategory.Clothing;

            var (min, max) = Tables.GearRatingChance.GetRangeT11(category);

            var sb = new System.Text.StringBuilder("Ratings:");

            foreach (var (label, value) in ratings)
            {
                // shield Max Health is a flat 100-200 roll, not a table roll (TryMutateGearRatingT10)
                var (lo, hi) = label == "Max Health" && wo.IsShield ? (100, 200) : (min, max);

                sb.Append($"\n* {label} {value} (min {lo}, max {hi})");
            }

            wo.LongDesc = string.IsNullOrWhiteSpace(wo.LongDesc)
                ? sb.ToString()
                : $"{wo.LongDesc}\n\n{sb}";
        }

        private static void AddZoneSetJewelryPiece(List<WorldObject> items, TreasureDeath profile, WeenieClassName[] pool, string slotName)
        {
            var wcid = pool[ThreadSafeRandom.Next(0, pool.Length - 1)];

            var roll = new TreasureRoll(TreasureItemType_Orig.Jewelry) { Wcid = wcid };
            var wo = CreateAndMutateWcid(profile, roll, false);

            if (wo != null)
                items.Add(wo);
            else
                log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): failed to create {slotName} jewelry ({wcid})");
        }

        private static void AddZoneSetGearPiece(List<WorldObject> items, TreasureDeath profile, TreasureItemType_Orig itemType)
        {
            // TreasureItemCategory.Item => isMagical = false throughout the mutation chain (blank gear)
            var wo = CreateRandomLootObjects_New(profile, TreasureItemCategory.Item, itemType);

            if (wo != null)
                items.Add(wo);
            else
                log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): failed to create {itemType} piece");
        }

        private static WorldObject CreateZoneSetWeapon(TreasureDeath profile, ZoneSetFamily family)
        {
            var treasureRoll = new TreasureRoll(TreasureItemType_Orig.Weapon);

            switch (family)
            {
                case ZoneSetFamily.Missile:
                    switch (ThreadSafeRandom.Next(0, 2))
                    {
                        case 0:
                            treasureRoll.WeaponType = TreasureWeaponType.Bow;
                            switch (ThreadSafeRandom.Next(0, 2))
                            {
                                case 0: treasureRoll.Wcid = BowWcids_Aluvian.Roll(profile.Tier); break;
                                case 1: treasureRoll.Wcid = BowWcids_Gharundim.Roll(profile.Tier); break;
                                default: treasureRoll.Wcid = BowWcids_Sho.Roll(profile.Tier); break;
                            }
                            break;
                        case 1:
                            treasureRoll.WeaponType = TreasureWeaponType.Crossbow;
                            treasureRoll.Wcid = CrossbowWcids.Roll(profile.Tier);
                            break;
                        default:
                            treasureRoll.WeaponType = TreasureWeaponType.Atlatl;
                            treasureRoll.Wcid = AtlatlWcids.Roll(profile.Tier);
                            break;
                    }
                    break;

                case ZoneSetFamily.Caster:
                    treasureRoll.WeaponType = TreasureWeaponType.Caster;
                    treasureRoll.Wcid = CasterWcids.Roll(profile.Tier);
                    break;

                default:
                    RollZoneSetMeleeWeapon(family, treasureRoll);
                    break;
            }

            if (treasureRoll.Wcid == WeenieClassName.undef)
                return null;

            // isMagical = false -> blank weapon (no spell sets); zone mutation layer adds the fun
            return CreateAndMutateWcid(profile, treasureRoll, false);
        }

        private static void RollZoneSetMeleeWeapon(ZoneSetFamily family, TreasureRoll treasureRoll)
        {
            var canTwoHand = family == ZoneSetFamily.Axe || family == ZoneSetFamily.Mace ||
                             family == ZoneSetFamily.Spear || family == ZoneSetFamily.Sword;

            // 1 = Heavy, 2 = Light, 3 = Finesse, 4 = TwoHanded (folded into the family skill roll)
            var skill = ThreadSafeRandom.Next(1, canTwoHand ? 4 : 3);

            if (skill == 4)
            {
                var twoHandType = family switch
                {
                    ZoneSetFamily.Axe => TreasureWeaponType.TwoHandedAxe,
                    ZoneSetFamily.Mace => TreasureWeaponType.TwoHandedMace,
                    ZoneSetFamily.Spear => TreasureWeaponType.TwoHandedSpear,
                    _ => TreasureWeaponType.TwoHandedSword,
                };

                treasureRoll.WeaponType = twoHandType;
                treasureRoll.Wcid = TwoHandedWeaponWcids.RollForWeaponType(twoHandType);
                return;
            }

            // subtype inner rolls: MS-vs-non for sword/dagger, jitte for mace
            var subType = family switch
            {
                ZoneSetFamily.Axe => TreasureWeaponType.Axe,
                ZoneSetFamily.Dagger => ThreadSafeRandom.Next(0, 1) == 0 ? TreasureWeaponType.Dagger : TreasureWeaponType.DaggerMS,
                ZoneSetFamily.Mace => ThreadSafeRandom.Next(0, 1) == 0 ? TreasureWeaponType.Mace : TreasureWeaponType.MaceJitte,
                ZoneSetFamily.Spear => TreasureWeaponType.Spear,
                ZoneSetFamily.Staff => TreasureWeaponType.Staff,
                ZoneSetFamily.Sword => ThreadSafeRandom.Next(0, 1) == 0 ? TreasureWeaponType.Sword : TreasureWeaponType.SwordMS,
                _ => TreasureWeaponType.Unarmed,
            };

            var wcid = RollSkillTableForWeaponType(skill, subType);

            // not every skill table carries every variant (e.g. a skill without jitte/MS tables):
            // fall back to the family's base subtype, then to any skill that has it
            if (wcid == WeenieClassName.undef && (subType == TreasureWeaponType.DaggerMS || subType == TreasureWeaponType.SwordMS || subType == TreasureWeaponType.MaceJitte))
            {
                subType = family switch
                {
                    ZoneSetFamily.Dagger => TreasureWeaponType.Dagger,
                    ZoneSetFamily.Mace => TreasureWeaponType.Mace,
                    _ => TreasureWeaponType.Sword,
                };
                wcid = RollSkillTableForWeaponType(skill, subType);
            }

            if (wcid == WeenieClassName.undef)
            {
                for (var altSkill = 1; altSkill <= 3 && wcid == WeenieClassName.undef; altSkill++)
                {
                    if (altSkill == skill) continue;
                    wcid = RollSkillTableForWeaponType(altSkill, subType);
                }
            }

            treasureRoll.WeaponType = subType;
            treasureRoll.Wcid = wcid;
        }

        private static WeenieClassName RollSkillTableForWeaponType(int skill, TreasureWeaponType weaponType)
        {
            return skill switch
            {
                1 => HeavyWeaponWcids.RollForWeaponType(weaponType),
                2 => LightWeaponWcids.RollForWeaponType(weaponType),
                _ => FinesseWeaponWcids.RollForWeaponType(weaponType),
            };
        }
    }
}
