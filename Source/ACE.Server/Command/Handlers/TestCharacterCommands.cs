using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Command;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;
using ACE.Entity.Models;

namespace ACE.Server.Command.Handlers
{
    public static class TestCharacterCommands
    {
        [CommandHandler("testchar", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 1,
            "Boost character stats/gear/weapons to Tier 11, or reset character back to a fresh level 1 Tier 0.",
            "Usage: /testchar <tier>  |  /testchar stats <tier>  |  /testchar gear <tier>  |  /testchar weapons <tier> [style]")]
        public static void HandleTestChar(Session session, params string[] parameters)
        {
            var player = session.Player;
            if (player == null) return;

            if (parameters.Length == 1)
            {
                // Full booster package (stats + gear + weapons)
                var tier = parameters[0].ToUpper();
                if (tier == "T0" || tier == "0")
                {
                    ResetToTier0(player);
                    player.SendMessage("Character successfully reset to Tier 0 baseline! Please log out and back in to completely refresh your client spellbook.");
                    player.SaveBiotaToDatabase();
                    return;
                }

                if (tier != "T11" && tier != "11")
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("Currently only T11 and T0 are supported. Usage: /testchar T11 or /testchar T0", ChatMessageType.System));
                    return;
                }

                ConfigureStatsAndSpells(player);
                SpawnArmor(player);
                SpawnWeapons(player, null);

                player.SendMessage("Character fully configured, geared, and equipped for Tier 11!");
                player.SaveBiotaToDatabase();
            }
            else if (parameters.Length >= 2)
            {
                var sub = parameters[0].ToLower();
                var tier = parameters[1].ToUpper();

                if (tier == "T0" || tier == "0")
                {
                    if (sub == "stats")
                    {
                        ResetToTier0(player);
                        player.SendMessage("Character stats, skills, augmentations, and spellbook reset to Tier 0 baseline! Please log out and back in to completely refresh your client spellbook.");
                        player.SaveBiotaToDatabase();
                    }
                    else if (sub == "gear")
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("No gear is defined for Tier 0.", ChatMessageType.System));
                    }
                    else if (sub == "weapons")
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("No weapons are defined for Tier 0.", ChatMessageType.System));
                    }
                    else
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Unknown subcommand. Usage: /testchar <tier> | /testchar [stats|gear|weapons] <tier> [style]", ChatMessageType.System));
                    }
                    return;
                }

                if (tier != "T11" && tier != "11")
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Currently only T11 and T0 are supported. Usage: /testchar {sub} T11", ChatMessageType.System));
                    return;
                }

                if (sub == "stats")
                {
                    ConfigureStatsAndSpells(player);
                    player.SendMessage("Character stats, skills, augmentations, and spells configured for Tier 11!");
                    player.SaveBiotaToDatabase();
                }
                else if (sub == "gear")
                {
                    SpawnArmor(player);
                    player.SendMessage("Prismatic GSA armor generated in your inventory.");
                    player.SaveBiotaToDatabase();
                }
                else if (sub == "weapons")
                {
                    string style = null;
                    if (parameters.Length >= 3)
                    {
                        var argStyle = parameters[2].ToUpper();
                        if (argStyle == "UA" || argStyle == "2H" || argStyle == "BOW" || argStyle == "WAND")
                        {
                            style = argStyle;
                        }
                        else
                        {
                            session.Network.EnqueueSend(new GameMessageSystemChat("Invalid weapon style. Supported styles: UA, 2H, Bow, Wand.", ChatMessageType.System));
                            return;
                        }
                    }

                    SpawnWeapons(player, style);
                    var styleLabel = style != null ? $"{style} " : "";
                    player.SendMessage($"Tier 11 {styleLabel}weapons generated in your inventory.");
                    player.SaveBiotaToDatabase();
                }
                else
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("Unknown subcommand. Usage: /testchar <tier> | /testchar [stats|gear|weapons] <tier> [style]", ChatMessageType.System));
                }
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /testchar <tier> | /testchar [stats|gear|weapons] <tier> [style]", ChatMessageType.System));
            }
        }

        private static void ResetToTier0(Player player)
        {
            // 1. Reset Base Attributes to 10 starting value and 0 raised ranks
            foreach (var attrType in player.Attributes.Keys)
            {
                if (!player.Attributes.TryGetValue(attrType, out var attr))
                    continue;

                attr.StartingValue = 10;
                player.SetAttributeRank(attr, 0);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, attr));
            }

            // 2. Reset Secondary Vitals to 10 starting value and 0 raised ranks
            foreach (var vitalType in player.Vitals.Keys)
            {
                if (!player.Vitals.TryGetValue(vitalType, out var vital))
                    continue;

                vital.StartingValue = 10;
                vital.Ranks = 0;
                vital.ExperienceSpent = 0;
                vital.Current = vital.MaxValue;
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, vital));
            }

            // 3. Reset Level to 1
            player.Level = 1;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Level, 1));

            // 4. Reset Experience and Luminance to 0
            player.AvailableExperience = 0;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.AvailableExperience, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.TotalExperience, 0));

            player.AvailableLuminance = 0;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.AvailableLuminance, 0));

            // 5. Reset Skill Credits to 46 (starting amount)
            player.AvailableSkillCredits = 46;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, 46));

            // 6. Reset all Skills to Untrained with 0 ranks and 0 spent XP
            foreach (var skill in player.Skills.Values)
            {
                skill.AdvancementClass = SkillAdvancementClass.Untrained;
                skill.InitLevel = 0;
                skill.Ranks = 0;
                skill.ExperienceSpent = 0;
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(player, skill));
            }

            // 7. Reset Custom Augmentations (Luminance Augmentations) to 0
            player.LuminanceAugmentCreatureCount = 0;
            player.LuminanceAugmentItemCount = 0;
            player.LuminanceAugmentLifeCount = 0;
            player.LuminanceAugmentWarCount = 0;
            player.LuminanceAugmentVoidCount = 0;
            player.LuminanceAugmentSpellDurationCount = 0;
            player.LuminanceAugmentSpecializeCount = 0;
            player.LuminanceAugmentSummonCount = 0;
            player.LuminanceAugmentMeleeCount = 0;
            player.LuminanceAugmentMissileCount = 0;

            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugCreatureCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugItemCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugLifeCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugWarCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugVoidCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugDurationCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugSpecializeCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugSummonCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugMeleeCount, 0));
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.LumAugMissileCount, 0));

            // 8. Reset Retail Augmentations to 0
            foreach (var kvp in AugmentationDevice.MaxAugs)
            {
                var augProp = AugmentationDevice.AugProps[kvp.Key];
                player.SetProperty(augProp, 0);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, augProp, 0));
            }

            // 9. Purge all known Spells from the spellbook
            var spellsToClear = player.Biota.GetKnownSpellsIds(player.BiotaDatabaseLock);
            foreach (var spellId in spellsToClear)
            {
                player.RemoveKnownSpell((uint)spellId);
            }
        }

        private static void ConfigureStatsAndSpells(Player player)
        {
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
        }

        private static void SpawnArmor(Player player)
        {
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
        }

        private static void SpawnWeapons(Player player, string targetStyle)
        {
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

                // If style filter is provided, skip non-matching styles
                if (targetStyle != null && !weaponType.Equals(targetStyle, StringComparison.OrdinalIgnoreCase))
                    continue;

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
        }
    }
}
