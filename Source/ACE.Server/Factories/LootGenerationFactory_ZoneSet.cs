using System.Collections.Generic;

using ACE.Common;
using ACE.Database.Models.World;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        // =====================================================================================
        // Zone Control structured loot set ("T11+" endgame loot)
        //
        // Every governed kill drops one weapon per FAMILY (subtype/skill randomized inside the
        // pick) plus a fixed gear allotment (armor / jewelry / cloak). All pieces roll BLANK
        // (isMagical = false -> no legacy spell sets); useful cantrips/spells/procs come from
        // the Zone Control post-roll mutation layer (plugin-tuned zone stats, ZoneLootMutator).
        // Quality/tier come from the (QB-scaled) treasure profile passed in.
        //
        // Design doc: C:\AI\ZoneControl\ACE_Loot_Systems_DeepDive_2026-07-17.md §12-13
        // =====================================================================================

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
        /// Generates the full structured loot set for one kill: one blank weapon per family
        /// plus armor / jewelry / cloak pieces, all mutated against <paramref name="profile"/>.
        /// </summary>
        public static List<WorldObject> CreateZoneLootSet(TreasureDeath profile, int armorCount, int jewelryCount, int cloakCount)
        {
            var items = new List<WorldObject>();

            foreach (var family in zoneSetFamilies)
            {
                var weapon = CreateZoneSetWeapon(profile, family);
                if (weapon != null)
                    items.Add(weapon);
                else
                    log.Warn($"[ZONELOOT] CreateZoneLootSet({profile.TreasureType}): failed to create {family} weapon");
            }

            for (var i = 0; i < armorCount; i++)
                AddZoneSetGearPiece(items, profile, TreasureItemType_Orig.Armor);

            for (var i = 0; i < jewelryCount; i++)
                AddZoneSetGearPiece(items, profile, TreasureItemType_Orig.Jewelry);

            for (var i = 0; i < cloakCount; i++)
                AddZoneSetGearPiece(items, profile, TreasureItemType_Orig.Cloak);

            return items;
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
