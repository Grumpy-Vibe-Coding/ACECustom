using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects
{
    public static class TestCharacterGem
    {
        public const uint Tier11GemWcid = 777902011;

        public static bool IsTestCharacterGem(WorldObject gem)
        {
            return gem.WeenieClassId == Tier11GemWcid;
        }

        public static void UseGem(Player player, WorldObject gem)
        {
            if (player.Account.AccessLevel < (uint)AccessLevel.Developer)
            {
                player.SendTransientError("This item is restricted to Developers and above.");
                return;
            }

            // 1. Set Base Attributes
            // Base Attributes: Strength 460, Endurance 460, Coordination 580, Quickness 550, Focus 550, Self 510
            var attributeTargets = new Dictionary<PropertyAttribute, uint>()
            {
                { PropertyAttribute.Strength, 460 },
                { PropertyAttribute.Endurance, 460 },
                { PropertyAttribute.Coordination, 580 },
                { PropertyAttribute.Quickness, 550 },
                { PropertyAttribute.Focus, 550 },
                { PropertyAttribute.Self, 510 },
            };

            foreach (var kvp in attributeTargets)
            {
                var attrType = kvp.Key;
                var targetValue = kvp.Value;
                if (!player.Attributes.TryGetValue(attrType, out var attr))
                    continue;

                // Set innate StartingValue to 100
                attr.StartingValue = 100;
                
                // Ranks = Target - 100
                uint ranks = targetValue > 100 ? targetValue - 100 : 0;
                player.SetAttributeRank(attr, ranks);
                
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, attr));
            }

            // 2. Set Secondary Vitals (Max)
            // Secondary Vitals: Max Health 685, Max Stamina 900, Max Mana 920
            var vitalTargets = new Dictionary<PropertyAttribute2nd, uint>()
            {
                { PropertyAttribute2nd.MaxHealth, 685 },
                { PropertyAttribute2nd.MaxStamina, 900 },
                { PropertyAttribute2nd.MaxMana, 920 },
            };

            foreach (var kvp in vitalTargets)
            {
                var vitalType = kvp.Key;
                var targetValue = kvp.Value;
                if (!player.Vitals.TryGetValue(vitalType, out var vital))
                    continue;

                // Clear CP ranks
                vital.Ranks = 0;
                vital.ExperienceSpent = 0;

                // Adjust starting value based on current base attribute formulas, etc.
                int baseFormula = (int)AttributeFormula.GetFormula(player, vitalType, true);
                int enlBonus = (int)vital.EnlBonus;
                int gearBonus = (int)vital.GearBonus;
                
                int startingValue = (int)targetValue - baseFormula - enlBonus - gearBonus;
                vital.StartingValue = (uint)Math.Max(1, startingValue);
                
                // Fully restore current vital value
                vital.Current = vital.MaxValue;
                
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, vital));
            }

            // 2.5 Set Level and Maximize All Skills
            player.Level = 1100;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Level, 1100));

            foreach (var skill in player.Skills.Values)
            {
                skill.AdvancementClass = SkillAdvancementClass.Specialized;
                skill.InitLevel = 10;

                var skillXPTable = Player.GetSkillXPTable(SkillAdvancementClass.Specialized);
                if (skillXPTable != null)
                {
                    skill.Ranks = (ushort)(skillXPTable.Count - 1);
                    skill.ExperienceSpent = skillXPTable[skillXPTable.Count - 1];
                }

                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(player, skill));
            }

            // 3. Set Custom Augmentations (Luminance Augmentations)
            // Crit: 5,000 | Item: 3,500 | Life: 3,500 | War: 3,500 | Void: 3,500 | Dur: 3,000 | Spec: 100 | Sum: 1,100 | Mel: 3,500 | Mis: 3,500
            player.LuminanceAugmentCreatureCount = 5000;
            player.LuminanceAugmentItemCount = 3500;
            player.LuminanceAugmentLifeCount = 3500;
            player.LuminanceAugmentWarCount = 3500;
            player.LuminanceAugmentVoidCount = 3500;
            player.LuminanceAugmentSpellDurationCount = 3000;
            player.LuminanceAugmentSpecializeCount = 100;
            player.LuminanceAugmentSummonCount = 1100;
            player.LuminanceAugmentMeleeCount = 3500;
            player.LuminanceAugmentMissileCount = 3500;

            // Sync updated custom augs to the client
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugCreatureCount, player.LuminanceAugmentCreatureCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugItemCount, player.LuminanceAugmentItemCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugLifeCount, player.LuminanceAugmentLifeCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugWarCount, player.LuminanceAugmentWarCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugVoidCount, player.LuminanceAugmentVoidCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugDurationCount, player.LuminanceAugmentSpellDurationCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugSpecializeCount, player.LuminanceAugmentSpecializeCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugSummonCount, player.LuminanceAugmentSummonCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugMeleeCount, player.LuminanceAugmentMeleeCount ?? 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugMissileCount, player.LuminanceAugmentMissileCount ?? 0));

            // 4. Set Retail Augmentations to max/acquired
            foreach (var kvp in AugmentationDevice.MaxAugs)
            {
                var type = kvp.Key;
                var maxVal = kvp.Value;
                var augProp = AugmentationDevice.AugProps[type];
                player.SetProperty(augProp, maxVal);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, augProp, maxVal));
            }

            // 5. Learn All Spells Silently
            foreach (var spellID in Player.PlayerSpellTable)
            {
                if (player.AddKnownSpell(spellID))
                {
                    player.Session.Network.EnqueueSend(new GameEventMagicUpdateSpell(player.Session, (ushort)spellID));
                }
            }

            // 6. Spawn Prismatic GSA Armor Suit
            var armorWcids = new List<uint>()
            {
                14594, // helmprismatic
                23777, // coatamulishadowbrilliant
                23785, // leggingsamulishadowbrilliant
                23793, // breastplateceldonshadowbrilliant
                23801, // girthceldonshadowbrilliant
                23809, // leggingsceldonshadowbrilliant
                23817, // sleevesceldonshadowbrilliant
                23825, // breastplatekoujiashadowbrilliant
                23833, // leggingskoujiashadowbrilliant
                23841, // sleeveskoujiashadowbrilliant
            };

            foreach (var wcid in armorWcids)
            {
                var armorItem = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (armorItem != null)
                {
                    player.TryCreateInInventoryWithNetworking(armorItem);
                }
            }

            // 7. Spawn Complete Set of Weapons (28 total)
            // styles: UA (29651), 2H (29671), Bow (29639), Wand (29661)
            // elements: Slash, Pierce, Bludgeon, Cold, Fire, Acid, Electric
            var weaponBases = new Dictionary<string, (uint wcid, string baseName)>()
            {
                { "UA", (29651, "Spiked Knuckles") },
                { "2H", (29671, "Two Handed Sword") },
                { "Bow", (29639, "Bow") },
                { "Wand", (29661, "Wand") }
            };

            var elements = new List<DamageType>()
            {
                DamageType.Slash,
                DamageType.Pierce,
                DamageType.Bludgeon,
                DamageType.Cold,
                DamageType.Fire,
                DamageType.Acid,
                DamageType.Electric
            };

            foreach (var baseKvp in weaponBases)
            {
                var weaponType = baseKvp.Key;
                var baseWcid = baseKvp.Value.wcid;
                var baseName = baseKvp.Value.baseName;

                foreach (var element in elements)
                {
                    var weapon = WorldObjectFactory.CreateNewWorldObject(baseWcid);
                    if (weapon != null)
                    {
                        weapon.SetProperty(PropertyInt.DamageType, (int)element);
                        weapon.Name = $"{element.DisplayName()} {baseName}";
                        player.TryCreateInInventoryWithNetworking(weapon);
                    }
                }
            }

            // 8. Consume the Gem (Skipped: Unlimited Uses)

            // 9. Notify and Save
            player.SendMessage("Character configured for Tier 11! Spelled up, base attributes scaled, GSA armor, and 28 elemental weapons generated.");
            player.SaveBiotaToDatabase();
        }
    }
}
