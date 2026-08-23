using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Command;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
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
            "Boost character stats/gear/weapons to Tier 11 or Tier 10, or reset to Tier 0.",
            "Usage: /testchar <T11|T10|T0>  |  /testchar stats <T11|T10>  |  /testchar gear <tier>  |  /testchar weapons <tier> [style]  |  /testchar gems <tier>  |  /testchar charms\n" +
            "Granular (Character tab): /testchar set attrs|vitals|level|enl|augs|raugs <csv>  |  /testchar apply skills,spells,aetheria,manastone  |  /testchar extra <keys>  |  /testchar save  |  /testchar report")]
        public static void HandleTestChar(Session session, params string[] parameters)
        {
            var player = session.Player;
            if (player == null) return;

            // Granular setters (Admin > Character tab, 2026-08-02 — CharacterTab_Plan): the
            // plugin applies a tier preset as a short batch of these. Dispatched BEFORE the
            // tier parsing below, which would reject them as bad tiers.
            var sub0 = parameters[0].ToLowerInvariant();
            if (sub0 == "set" || sub0 == "apply" || sub0 == "save" || sub0 == "extra" || sub0 == "report")
            {
                HandleCharacterSetter(session, player, parameters);
                return;
            }

            if (parameters.Length == 1)
            {
                // Full booster package (stats + gear + weapons)
                var tier = parameters[0].ToUpper();
                if (tier == "T0" || tier == "0")
                {
                    if (!GuardWipe(session, player)) return;
                    ResetToTier0(player);
                    player.SendMessage("Character successfully reset to Tier 0 baseline! Please log out and back in to completely refresh your client spellbook.");
                    player.SaveBiotaToDatabase();
                    return;
                }

                bool isT10 = (tier == "T10" || tier == "10");
                if (!isT10 && tier != "T11" && tier != "11")
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("Supported tiers: T11, T10, T0. Usage: /testchar T11 or /testchar T10 or /testchar T0", ChatMessageType.System));
                    return;
                }

                if (isT10)
                    ConfigureStatsAndSpellsT10(player);
                else
                    ConfigureStatsAndSpells(player);

                // Weapons go into their own pack (owner 08-15)
                var weaponsPack = GetOrCreatePack(player, "Weapons Pack");

                CreateCustomBow(player, DamageType.Cold, true, weaponsPack);
                CreateCustomBow(player, DamageType.Fire, true, weaponsPack);
                CreateCustomBow(player, DamageType.Electric, true, weaponsPack);
                CreateCustomBow(player, DamageType.Acid, true, weaponsPack);
                CreateCustomBow(player, DamageType.Slash, true, weaponsPack);
                CreateCustomBow(player, DamageType.Pierce, true, weaponsPack);
                CreateCustomBow(player, DamageType.Bludgeon, true, weaponsPack);
                CreateCustomBow(player, DamageType.Nether, true, weaponsPack);

                CreateCustomUA(player, DamageType.Cold, weaponsPack);
                CreateCustomUA(player, DamageType.Fire, weaponsPack);
                CreateCustomUA(player, DamageType.Electric, weaponsPack);
                CreateCustomUA(player, DamageType.Acid, weaponsPack);
                CreateCustomUA(player, DamageType.Slash, weaponsPack);
                CreateCustomUA(player, DamageType.Pierce, weaponsPack);
                CreateCustomUA(player, DamageType.Bludgeon, weaponsPack);
                CreateCustomUA(player, DamageType.Nether, weaponsPack);

                CreateCustomWand(player, DamageType.Cold, weaponsPack);
                CreateCustomWand(player, DamageType.Fire, weaponsPack);
                CreateCustomWand(player, DamageType.Electric, weaponsPack);
                CreateCustomWand(player, DamageType.Acid, weaponsPack);
                CreateCustomWand(player, DamageType.Slash, weaponsPack);
                CreateCustomWand(player, DamageType.Pierce, weaponsPack);
                CreateCustomWand(player, DamageType.Bludgeon, weaponsPack);
                CreateCustomWand(player, DamageType.Nether, weaponsPack);

                // Spawn empty booster packs 5 down to 2. Only 4: the Weapons / Portal
                // Gems / Ability Charms / Spell Comps packs (owner 08-15) share the 8
                // pack slots a premade has (7 base + retail PackSlot aug). "Booster
                // Pack 1" is never spawned empty — it is the legacy gem-bag name.
                for (int i = 5; i >= 2; i--)
                {
                    var packName = $"Booster Pack {i}";
                    if (HasItemNamed(player, packName)) continue;
                    var emptyBag = WorldObjectFactory.CreateNewWorldObject(310025) as Container;
                    if (emptyBag != null)
                    {
                        emptyBag.Name = packName;
                        emptyBag.SetProperty(PropertyString.Name, packName);
                        player.TryCreateInInventoryWithNetworking(emptyBag);
                    }
                }

                // Portal gems go into their own pack (owner 08-15; "Booster Pack 1"
                // is the legacy name for the same bag on pre-existing test chars)
                SpawnTeleportGems(player, GetOrCreatePack(player, "Portal Gems Pack", "Booster Pack 1"));

                var gearTier = "T10";
                SpawnOlthoiShadowArmor(player, gearTier);
                SpawnCustomUndergarmentsAndCloak(player, gearTier);
                SpawnCustomJewelry(player, gearTier);

                // Add and auto-equip Infinite Deadly Prismatic Arrow
                bool hasArrow = player.GetAllPossessionsDeep().Any(i => i.WeenieClassId == 4395100);
                if (!hasArrow)
                {
                    var arrow = WorldObjectFactory.CreateNewWorldObject(4395100);
                    if (arrow != null)
                    {
                        AddItemToInventory(player, arrow);
                    }
                }

                // Add Aetherias
                SpawnAetherias(player);

                // Add all charms (T1) + Charm Catalysts
                SpawnCharms(player);

                var tierLabel = isT10 ? "Tier 10" : "Tier 11";
                player.SendMessage($"Character fully configured with {tierLabel} stats, elemental weapons, Olthoi Infused Shadow armor, custom undergarments, cloak, jewelry, Infinite Arrow, Aetherias, and all ability charms!");
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
                        if (!GuardWipe(session, player)) return;
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
                        session.Network.EnqueueSend(new GameMessageSystemChat("Unknown subcommand. Usage: /testchar <tier> | /testchar [stats|gear|weapons|gems] <tier> [style]", ChatMessageType.System));
                    }
                    return;
                }

                bool isT10 = (tier == "T10" || tier == "10");
                if (!isT10 && tier != "T11" && tier != "11")
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Supported tiers: T11, T10, T0. Usage: /testchar {sub} T11 or /testchar {sub} T10", ChatMessageType.System));
                    return;
                }

                if (sub == "stats")
                {
                    if (isT10)
                        ConfigureStatsAndSpellsT10(player);
                    else
                        ConfigureStatsAndSpells(player);
                    var statsTierLabel = isT10 ? "Tier 10" : "Tier 11";
                    player.SendMessage($"Character stats, skills, augmentations, and spells configured for {statsTierLabel}!");
                    player.SaveBiotaToDatabase();
                }
                else if (sub == "gear")
                {
                    var gearTier = "T10";
                    SpawnArmor(player);
                    SpawnOlthoiShadowArmor(player, gearTier);
                    SpawnCustomUndergarmentsAndCloak(player, gearTier);
                    SpawnCustomJewelry(player, gearTier);

                    // Add and auto-equip Infinite Deadly Prismatic Arrow
                    bool hasArrow = player.GetAllPossessionsDeep().Any(i => i.WeenieClassId == 4395100);
                    if (!hasArrow)
                    {
                        var arrow = WorldObjectFactory.CreateNewWorldObject(4395100);
                        if (arrow != null)
                        {
                            AddItemToInventory(player, arrow);
                        }
                    }

                    // Add Aetherias
                    SpawnAetherias(player);

                    player.SendMessage("Prismatic GSA armor, Olthoi Infused Shadow armor (No Cloak), custom undergarments, custom cloak, custom jewelry, Infinite Arrow, and Aetherias generated and equipped.");
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
                    player.SendMessage($"Weapons generated in your inventory.");
                    player.SaveBiotaToDatabase();
                }
                else if (sub == "gems")
                {
                    // Gems ride Create Premade via this granular verb — pack them (owner 08-15)
                    SpawnTeleportGems(player, GetOrCreatePack(player, "Portal Gems Pack", "Booster Pack 1"));
                    player.SendMessage("Custom Teleport Gems generated in your inventory.");
                    player.SaveBiotaToDatabase();
                }
                else if (sub == "charms")
                {
                    SpawnCharms(player);
                    player.SendMessage("All ability charms (T1) and Charm Catalysts added to your inventory.");
                    player.SaveBiotaToDatabase();
                }
                else if (sub == "print")
                {
                    if (parameters.Length >= 2 && uint.TryParse(parameters[1], out var wcid))
                    {
                        var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
                        if (wo != null)
                        {
                            player.SendMessage($"--- WorldObject Properties for Weenie {wcid} ({wo.Name}) ---");
                            foreach (var prop in wo.GetAllPropertyInt())
                                player.SendMessage($"  Int {prop.Key} ({(int)prop.Key}): {prop.Value}");
                            foreach (var prop in wo.GetAllPropertyBools())
                                player.SendMessage($"  Bool {prop.Key} ({(int)prop.Key}): {prop.Value}");
                            foreach (var prop in wo.GetAllPropertyFloat())
                                player.SendMessage($"  Float {prop.Key} ({(int)prop.Key}): {prop.Value}");
                            foreach (var prop in wo.GetAllPropertyDataId())
                                player.SendMessage($"  DataId {prop.Key} ({(int)prop.Key}): {prop.Value}");
                            foreach (var prop in wo.GetAllPropertyString())
                                player.SendMessage($"  String {prop.Key} ({(int)prop.Key}): {prop.Value}");
                            wo.Destroy();
                        }
                        else
                        {
                            player.SendMessage($"Weenie {wcid} could not be created.");
                        }
                    }
                    else
                    {
                        player.SendMessage("Usage: /testchar print <wcid>");
                    }
                }
                else
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("Unknown subcommand. Usage: /testchar <tier> | /testchar [stats|gear|weapons|gems|print] <tier> [style]", ChatMessageType.System));
                }
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /testchar <tier> | /testchar [stats|gear|weapons|gems] <tier> [style]", ChatMessageType.System));
            }
        }

        /// <summary>The T0 wipe may only ever touch an admin (plussed, "+Name") character —
        /// never a real player character (owner 2026-08-02). Gate for BOTH wipe entry points
        /// (/testchar T0 and /testchar stats T0).</summary>
        private static bool GuardWipe(Session session, Player player)
        {
            if (player.IsPlussed)
                return true;
            ChatPacket.SendServerMessage(session,
                "testchar T0: REFUSED - this is not an admin (+) character. The wipe only runs on plussed test characters.",
                ChatMessageType.Broadcast);
            return false;
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
            player.TotalExperience = 0;
            player.TotalExperienceDouble = 0;
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

            // 7b. Reset growth charm counters to 0 (owner 08-15: /aug kept showing charm
            // lines after a wipe). Same PropertyInt64 set that /testchar set charms writes.
            var charmProps = new[] { PropertyInt64.TriuneWeaveCount, PropertyInt64.BattlemagesWrathCharmCount,
                                     PropertyInt64.NetherVeilCharmCount, PropertyInt64.CrashingSteelCharmCount,
                                     PropertyInt64.TrueShotCharmCount };
            foreach (var charmProp in charmProps)
            {
                player.SetProperty(charmProp, 0);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, charmProp, 0));
            }

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

            // 10. Reset Enlightenment to 0
            player.Enlightenment = 0;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Enlightenment, 0));

            // 11. Dequip and destroy all equipped objects first (before clearing inventory)
            var equippedGuids = new List<ObjectGuid>(player.EquippedObjects.Keys);
            foreach (var guid in equippedGuids)
            {
                player.TryDequipObjectWithNetworking(guid, out _, Player.DequipObjectAction.ConsumeItem);
            }

            // 12. Clear all remaining inventory items. NOT Container.ClearInventory: that
            // destroys server-side without telling the client (packs looked untouched in-game,
            // owner 2026-08-02), and one removal failure stops it destroying anything after.
            // Per-item remove + client notify + destroy; top-level packs take their contents
            // with them. Deliberately no ability-charm exemption — a wipe means everything.
            var inventoryItems = new List<WorldObject>(player.Inventory.Values);
            foreach (var invItem in inventoryItems)
            {
                if (player.TryRemoveFromInventory(invItem.Guid, out var removed))
                {
                    player.Session.Network.EnqueueSend(new GameMessageInventoryRemoveObject(removed));
                    removed.Destroy();
                }
            }
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.EncumbranceVal, player.EncumbranceVal ?? 0));
        }

        private static void ConfigureStatsAndSpells(Player player)
        {
            // 1. Set Base Attributes
            // Base Attributes: Strength 450, Endurance 450, Coordination 575, Quickness 450, Focus 450, Self 450
            var attributeTargets = new Dictionary<PropertyAttribute, uint>()
            {
                { PropertyAttribute.Strength, 450 },
                { PropertyAttribute.Endurance, 450 },
                { PropertyAttribute.Coordination, 575 },
                { PropertyAttribute.Quickness, 450 },
                { PropertyAttribute.Focus, 450 },
                { PropertyAttribute.Self, 450 },
            };

            foreach (var kvp in attributeTargets)
                SetChAttribute(player, kvp.Key, kvp.Value);

            // 2. Set Secondary Vitals (Max)
            // Secondary Vitals: Max Health 700, Max Stamina 900, Max Mana 900
            var vitalTargets = new Dictionary<PropertyAttribute2nd, uint>()
            {
                { PropertyAttribute2nd.MaxHealth, 700 },
                { PropertyAttribute2nd.MaxStamina, 900 },
                { PropertyAttribute2nd.MaxMana, 900 },
            };

            foreach (var kvp in vitalTargets)
                SetChVital(player, kvp.Key, kvp.Value);

            // 2.5 Set Level and Maximize All Skills
            player.Level = 1300;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Level, 1300));

            ApplyChMaxSkills(player);

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
            ApplyChAllSpells(player);

            // 6. Enable Aetheria Slots
            player.UpdateProperty(player, PropertyInt.AetheriaBitfield, (int)AetheriaBitfield.All);

            // 7. Set Enlightenment to 325
            player.Enlightenment = 325;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Enlightenment, 325));

            // 8. Spawn Eternal Mana Charge (Infinite Mana Stone)
            SpawnChManaStone(player);
        }

        // ─── Per-knob setter helpers, shared by /testchar stats and the granular
        // /testchar set|apply commands (Admin > Character tab, 2026-08-02). Extracted from
        // ConfigureStatsAndSpells the same way the gear spawns were split for /asforge. ───

        /// <summary>Set one base attribute to an exact target (innate 100 + ranks; a target
        /// under 100 becomes the innate value with 0 ranks).</summary>
        private static void SetChAttribute(Player player, PropertyAttribute attrType, uint targetValue)
        {
            if (!player.Attributes.TryGetValue(attrType, out var attr))
                return;

            attr.StartingValue = Math.Min(targetValue, 100);
            uint ranks = targetValue > 100 ? targetValue - 100 : 0;
            player.SetAttributeRank(attr, ranks);
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, attr));
        }

        /// <summary>Set one secondary vital's MAX to an exact target by back-solving the
        /// starting value against the attribute formula + enl/gear bonuses. Attribute and
        /// enlightenment setters must run BEFORE this for the solve to land on target.</summary>
        private static void SetChVital(Player player, PropertyAttribute2nd vitalType, uint targetValue)
        {
            if (!player.Vitals.TryGetValue(vitalType, out var vital))
                return;

            vital.Ranks = 0;
            vital.ExperienceSpent = 0;

            int baseFormula = (int)AttributeFormula.GetFormula(player, vitalType, true);
            int enlBonus = (int)vital.EnlBonus;
            int gearBonus = (int)vital.GearBonus;

            int startingValue = (int)targetValue - baseFormula - enlBonus - gearBonus;
            vital.StartingValue = (uint)Math.Max(1, startingValue);

            vital.Current = vital.MaxValue;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, vital));
        }

        /// <summary>Specialize every skill and max its ranks (does NOT touch Level).</summary>
        private static void ApplyChMaxSkills(Player player)
        {
            foreach (var skill in player.Skills.Values)
            {
                skill.AdvancementClass = SkillAdvancementClass.Specialized;
                skill.InitLevel = 10;

                var skillXPTable = Player.GetSkillXPTable(SkillAdvancementClass.Specialized);
                if (skillXPTable != null && skillXPTable.Count > 0)
                {
                    skill.Ranks = (ushort)(skillXPTable.Count - 1);
                    skill.ExperienceSpent = skillXPTable[skillXPTable.Count - 1];
                }

                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(player, skill));
            }
        }

        private static void ApplyChAllSpells(Player player)
        {
            foreach (var spellID in Player.PlayerSpellTable)
            {
                if (player.AddKnownSpell(spellID))
                    player.Session.Network.EnqueueSend(new GameEventMagicUpdateSpell(player.Session, (ushort)spellID));
            }
        }

        private static void SpawnChManaStone(Player player)
        {
            if (!player.GetAllPossessionsDeep().Any(i => i.WeenieClassId == 30254))
            {
                var manaCharge = WorldObjectFactory.CreateNewWorldObject(30254);
                if (manaCharge != null)
                    player.TryCreateInInventoryWithNetworking(manaCharge);
            }
        }

        /// <summary>Set all 10 luminance augs at once (plugin order: creature, item, life,
        /// war, void, duration, specialize, summon, melee, missile).</summary>
        private static void SetChLumAugs(Player player, uint[] v)
        {
            player.LuminanceAugmentCreatureCount = v[0];
            player.LuminanceAugmentItemCount = v[1];
            player.LuminanceAugmentLifeCount = v[2];
            player.LuminanceAugmentWarCount = v[3];
            player.LuminanceAugmentVoidCount = v[4];
            player.LuminanceAugmentSpellDurationCount = v[5];
            player.LuminanceAugmentSpecializeCount = v[6];
            player.LuminanceAugmentSummonCount = v[7];
            player.LuminanceAugmentMeleeCount = v[8];
            player.LuminanceAugmentMissileCount = v[9];

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
        }

        /// <summary>/testchar set|apply|save — the Admin > Character tab's Apply batch
        /// (CharacterTab_Plan_2026-08-02.md section 3). Setters do NOT save; the batch ends
        /// with /testchar save.</summary>
        private static void HandleCharacterSetter(Session session, Player player, string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);
            var sub = parameters[0].ToLowerInvariant();

            if (sub == "save")
            {
                player.SaveBiotaToDatabase();
                Msg("testchar: character saved.");
                return;
            }

            // /testchar report - read-only. Emits the LIVE character as a [[ZCCHAR]] wire payload
            // so the plugin's Character tab can show real values beside the preset (owner
            // 2026-08-20: "add our real aug counts and attribute count, so we can compare
            // quickly"). The plugin has no other route to this - it never touches Decal's
            // WorldFilter; every number it draws arrives as chat wire.
            //
            // Values are the exact ones the setters target, so preset and live are comparable
            // without conversion: attr.Base (SetChAttribute writes StartingValue + ranks to hit
            // it), vital.MaxValue (what SetChVital back-solves for), and the RAW lum aug counts
            // (NOT the Effective* accessors - charms scale those past the caps by design, and
            // the preset is raw).
            if (sub == "report")
            {
                var attrOrder = new[] { PropertyAttribute.Strength, PropertyAttribute.Endurance,
                                        PropertyAttribute.Coordination, PropertyAttribute.Quickness,
                                        PropertyAttribute.Focus, PropertyAttribute.Self };
                var attrs = new List<string>();
                foreach (var a in attrOrder)
                    attrs.Add(player.Attributes.TryGetValue(a, out var at) ? at.Base.ToString() : "0");

                var vitalOrder = new[] { PropertyAttribute2nd.MaxHealth, PropertyAttribute2nd.MaxStamina,
                                         PropertyAttribute2nd.MaxMana };
                var vitals = new List<string>();
                foreach (var vt in vitalOrder)
                    vitals.Add(player.Vitals.TryGetValue(vt, out var vi) ? vi.MaxValue.ToString() : "0");

                // Same order as `/testchar set augs`, so the plugin can index one against the other.
                var augs = new[]
                {
                    player.LuminanceAugmentCreatureCount ?? 0,
                    player.LuminanceAugmentItemCount ?? 0,
                    player.LuminanceAugmentLifeCount ?? 0,
                    player.LuminanceAugmentWarCount ?? 0,
                    player.LuminanceAugmentVoidCount ?? 0,
                    player.LuminanceAugmentSpellDurationCount ?? 0,
                    player.LuminanceAugmentSpecializeCount ?? 0,
                    player.LuminanceAugmentSummonCount ?? 0,
                    player.LuminanceAugmentMeleeCount ?? 0,
                    player.LuminanceAugmentMissileCount ?? 0,
                };

                Msg("[[ZCCHAR]]attr=" + string.Join(",", attrs)
                    + "|vital=" + string.Join(",", vitals)
                    + "|level=" + (player.Level ?? 0)
                    + "|enl=" + player.Enlightenment
                    + "|augs=" + string.Join(",", augs));
                return;
            }

            if (sub == "apply")
            {
                if (parameters.Length < 2)
                {
                    Msg("usage: /testchar apply skills,spells,aetheria,manastone");
                    return;
                }
                foreach (var key in parameters[1].ToLowerInvariant().Split(','))
                {
                    switch (key)
                    {
                        case "skills":
                            ApplyChMaxSkills(player);
                            Msg("applied: all skills specialized + maxed");
                            break;
                        case "spells":
                            ApplyChAllSpells(player);
                            Msg("applied: all spells learned");
                            break;
                        case "aetheria":
                            player.UpdateProperty(player, PropertyInt.AetheriaBitfield, (int)AetheriaBitfield.All);
                            Msg("applied: aetheria slots");
                            break;
                        case "manastone":
                            SpawnChManaStone(player);
                            Msg("applied: eternal mana charge");
                            break;
                        default:
                            Msg($"testchar apply: unknown '{key}' (skills|spells|aetheria|manastone)");
                            return;
                    }
                }
                return;
            }

            // extra <keys csv> — utility-item minting (Admin > Extras tab, 2026-08-03). Same
            // convention as the gems set: an item already possessed (deep check) is skipped.
            if (sub == "extra")
            {
                if (parameters.Length < 2)
                {
                    Msg("usage: /testchar extra healkit,stamkit,manakit,lockpick,ivory,arrow,dispel,enlcoins,mmds,scarabs,aetheria,bags");
                    return;
                }
                var owned = player.GetAllPossessionsDeep();
                var possessed = new HashSet<uint>(owned.Select(i => i.WeenieClassId));
                void Mint(uint wcid, string label)
                {
                    if (possessed.Contains(wcid)) { Msg($"extra: {label} already in inventory - skipped"); return; }
                    var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
                    if (wo == null) { Msg($"extra: {label} (wcid {wcid}) failed to create"); return; }
                    if (player.TryCreateInInventoryWithNetworking(wo))
                        Msg($"extra: {label} created");
                    else
                        Msg($"extra: {label} could not be added (inventory full?)");
                }
                // Stackables top up to the target: possessing 4500 of a 5000 target creates the missing 500.
                // A pack routes the new stack there (falls back to main pack when full/missing).
                void MintStack(uint wcid, string label, int target, Container pack = null)
                {
                    var have = owned.Where(i => i.WeenieClassId == wcid).Sum(i => i.StackSize ?? 1);
                    var missing = target - have;
                    if (missing <= 0) { Msg($"extra: {label} already at {have} - skipped"); return; }
                    var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
                    if (wo == null) { Msg($"extra: {label} (wcid {wcid}) failed to create"); return; }
                    if (missing > 1)
                        wo.SetStackSize(missing);
                    if (TryPlaceInPack(player, wo, pack))
                        Msg($"extra: {label} +{missing} (now {target}) [{pack.Name}]");
                    else if (player.TryCreateInInventoryWithNetworking(wo))
                        Msg($"extra: {label} +{missing} (now {target})");
                    else
                        Msg($"extra: {label} could not be added (inventory full?)");
                }
                foreach (var key in parameters[1].ToLowerInvariant().Split(','))
                {
                    switch (key)
                    {
                        case "healkit":  Mint(30247, "Eternal Health Kit"); break;
                        case "stamkit":  Mint(30249, "Eternal Stamina Kit"); break;
                        case "manakit":  Mint(30248, "Eternal Mana Kit"); break;
                        case "lockpick": Mint(30253, "Limitless Lockpick"); break;
                        case "ivory":    Mint(30092, "Infinite Ivory"); break;
                        // The never-depleting ammo the /testchar T10/T11 package already
                        // auto-grants (owner 2026-08-06) — now mintable on its own, so a bow
                        // test char can get ammo without re-running the whole tier package.
                        case "arrow":    Mint(4395100, "Infinite Deadly Prismatic Arrow"); break;
                        case "dispel":   Mint(3110181, "Rune of Dispel"); break;   // ILT variant (sold in VoD), owner 08-03
                        case "enlcoins": MintStack(300004, "Enlightened Coins", 25000); break;
                        case "mmds":     MintStack(20630, "Trade Notes (250k)", 5000); break;
                        case "scarabs":
                            // 100 of each casting scarab, topped up (owner 08-03). Dark Scarab
                            // (37117) excluded: its max stack is 1 here. Packed (owner 08-15).
                            var compsPack = GetOrCreatePack(player, "Spell Comps Pack");
                            MintStack(691, "Lead Scarabs", 100, compsPack);
                            MintStack(689, "Iron Scarabs", 100, compsPack);
                            MintStack(686, "Copper Scarabs", 100, compsPack);
                            MintStack(688, "Silver Scarabs", 100, compsPack);
                            MintStack(687, "Gold Scarabs", 100, compsPack);
                            MintStack(690, "Pyreal Scarabs", 100, compsPack);
                            MintStack(8897, "Platinum Scarabs", 100, compsPack);
                            MintStack(37155, "Mana Scarabs", 100, compsPack);
                            MintStack(7299, "Diamond Scarabs", 100, compsPack);
                            MintStack(20631, "Prismatic Tapers", 10000, compsPack);   // max stack, owner 08-03
                            break;
                        case "aetheria": SpawnAetherias(player); Msg("extra: aetheria set spawned (skips owned pieces)"); break;
                        case "bags":     MintBoosterPacks(player, Msg); break;
                        case "growthcharms":
                            // The five growth charms (aug-caps lane). Items only - the counters
                            // that actually scale damage are set via /testchar set charms.
                            Mint(777700030, "Charm of the Triune Weave");
                            Mint(777700051, "Charm of the Battlemage's Wrath");
                            Mint(777700056, "Charm of the Nether Veil");
                            Mint(777700061, "Charm of Crashing Steel");
                            Mint(777700066, "Charm of the True Shot");
                            break;
                        default:
                            Msg($"testchar extra: unknown '{key}' (healkit|stamkit|manakit|lockpick|ivory|arrow|dispel|enlcoins|mmds|scarabs|aetheria|bags|growthcharms)");
                            return;
                    }
                }
                player.SaveBiotaToDatabase();
                return;
            }

            // set <what> <values>
            if (parameters.Length < 3)
            {
                Msg("usage: /testchar set attrs|vitals|level|enl|augs|charms|raugs <values>");
                return;
            }
            var what = parameters[1].ToLowerInvariant();
            var arg = parameters[2];

            bool ParseCsv(string csv, int count, out uint[] vals)
            {
                vals = new uint[count];
                var parts = csv.Split(',');
                if (parts.Length != count) return false;
                for (int i = 0; i < count; i++)
                    if (!uint.TryParse(parts[i], out vals[i])) return false;
                return true;
            }

            switch (what)
            {
                case "attrs":
                {
                    if (!ParseCsv(arg, 6, out var v))
                    {
                        Msg("set attrs: need 6 csv values (str,end,coord,quick,focus,self)");
                        return;
                    }
                    var order = new[] { PropertyAttribute.Strength, PropertyAttribute.Endurance, PropertyAttribute.Coordination,
                                        PropertyAttribute.Quickness, PropertyAttribute.Focus, PropertyAttribute.Self };
                    for (int i = 0; i < 6; i++)
                        SetChAttribute(player, order[i], v[i]);
                    Msg("set attrs: " + arg);
                    return;
                }
                case "vitals":
                {
                    if (!ParseCsv(arg, 3, out var v))
                    {
                        Msg("set vitals: need 3 csv values (health,stamina,mana)");
                        return;
                    }
                    var order = new[] { PropertyAttribute2nd.MaxHealth, PropertyAttribute2nd.MaxStamina, PropertyAttribute2nd.MaxMana };
                    for (int i = 0; i < 3; i++)
                        SetChVital(player, order[i], v[i]);
                    Msg("set vitals: " + arg);
                    return;
                }
                case "level":
                {
                    if (!int.TryParse(arg, out var lvl) || lvl < 1)
                    {
                        Msg("set level: bad value");
                        return;
                    }
                    player.Level = lvl;
                    player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Level, lvl));
                    Msg("set level: " + lvl);
                    return;
                }
                case "enl":
                {
                    if (!int.TryParse(arg, out var enl) || enl < 0)
                    {
                        Msg("set enl: bad value");
                        return;
                    }
                    player.Enlightenment = enl;
                    player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Enlightenment, enl));
                    Msg("set enl: " + enl);
                    return;
                }
                case "augs":
                {
                    if (!ParseCsv(arg, 10, out var v))
                    {
                        Msg("set augs: need 10 csv values (creature,item,life,war,void,duration,specialize,summon,melee,missile)");
                        return;
                    }
                    SetChLumAugs(player, v);
                    Msg("set augs: " + arg);
                    return;
                }
                case "charms":
                {
                    // Growth charm counters (PropertyInt64 50000-50004). These are what the
                    // Effective*AugCount accessors add on top of the raw lum augs - the charm
                    // ITEMS (extra growthcharms) grant nothing without these.
                    if (!ParseCsv(arg, 5, out var v))
                    {
                        Msg("set charms: need 5 csv values (weave,wrath,nether,steel,trueshot)");
                        return;
                    }
                    var props = new[] { PropertyInt64.TriuneWeaveCount, PropertyInt64.BattlemagesWrathCharmCount,
                                        PropertyInt64.NetherVeilCharmCount, PropertyInt64.CrashingSteelCharmCount,
                                        PropertyInt64.TrueShotCharmCount };
                    for (int i = 0; i < 5; i++)
                    {
                        player.SetProperty(props[i], v[i]);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(player, props[i], v[i]));
                    }
                    Msg("set charms: " + arg);
                    return;
                }
                case "raugs":
                {
                    if (arg.Equals("max", StringComparison.OrdinalIgnoreCase) || arg.Equals("zero", StringComparison.OrdinalIgnoreCase))
                    {
                        var toMax = arg.Equals("max", StringComparison.OrdinalIgnoreCase);
                        foreach (var kvp in AugmentationDevice.MaxAugs)
                        {
                            var prop = AugmentationDevice.AugProps[kvp.Key];
                            var val = toMax ? kvp.Value : 0;
                            player.SetProperty(prop, val);
                            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, prop, val));
                        }
                        Msg("set raugs: " + (toMax ? "all max" : "all zero"));
                        return;
                    }

                    var applied = 0;
                    foreach (var pair in arg.Split(','))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length != 2 || !int.TryParse(kv[1], out var val)
                            || !Enum.TryParse<AugmentationType>(kv[0], true, out var at)
                            || !AugmentationDevice.MaxAugs.TryGetValue(at, out var max))
                        {
                            Msg($"set raugs: bad entry '{pair}' (key=value, key = augmentation name)");
                            return;
                        }
                        val = Math.Clamp(val, 0, max);
                        var prop = AugmentationDevice.AugProps[at];
                        player.SetProperty(prop, val);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, prop, val));
                        applied++;
                    }
                    Msg($"set raugs: {applied} set");
                    return;
                }
                default:
                    Msg("usage: /testchar set attrs|vitals|level|enl|augs|charms|raugs <values>");
                    return;
            }
        }

        private static void ConfigureStatsAndSpellsT10(Player player)
        {
            // Same as T11 but with lower luminance augs and ENL 300
            // Reuse T11 to set all attributes, vitals, skills, retail augs, and spells
            ConfigureStatsAndSpells(player);

            // Override luminance augs for T10
            player.LuminanceAugmentCreatureCount = 4000;
            player.LuminanceAugmentItemCount = 2000;
            player.LuminanceAugmentLifeCount = 2000;
            player.LuminanceAugmentWarCount = 1750;
            player.LuminanceAugmentVoidCount = 1750;
            player.LuminanceAugmentSpellDurationCount = 1750;
            player.LuminanceAugmentSpecializeCount = 100;
            player.LuminanceAugmentSummonCount = 1100;
            player.LuminanceAugmentMeleeCount = 1750;
            player.LuminanceAugmentMissileCount = 1750;

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

            // Override Enlightenment to 300
            player.Enlightenment = 300;
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Enlightenment, 300));
        }

        /// <summary>The 102-slot "Booster Pack" bags (wcid 310025) the /testchar tier package spawns,
        /// as a standalone extra. Fills the player's pack slots but NEVER past what the client is
        /// showing: ContainerCapacity is read at login, so a PackSlot aug taken this session gives a
        /// slot the client won't render until relog and a pack put there looks lost (owner
        /// 2026-08-11 - hence 7 normally, 8 only for a character that logged in already holding the
        /// aug). Existing Booster Packs are counted, so re-running tops up instead of duplicating.</summary>
        private static void MintBoosterPacks(Player player, Action<string> Msg)
        {
            const uint PackWcid = 310025;

            // Slots the client is showing, minus every container already in the pack bar.
            var slots = Math.Min(player.ClientPackSlots, player.ContainerCapacity ?? 7);
            var held = player.Inventory.Values.OfType<Container>().Count();
            var room = slots - held;

            if (room <= 0)
            {
                Msg($"extra: bags - all {slots} pack slots already filled ({held} packs) - skipped");
                if ((player.ContainerCapacity ?? 7) > slots)
                    Msg("extra: bags - the PackSlot aug added a slot this session; relog and re-run to fill it.");
                return;
            }

            var made = 0;
            for (int i = 1; i <= 8 && made < room; i++)
            {
                var packName = $"Booster Pack {i}";
                if (HasItemNamed(player, packName)) continue;
                if (!(WorldObjectFactory.CreateNewWorldObject(PackWcid) is Container bag)) continue;
                bag.Name = packName;
                bag.SetProperty(PropertyString.Name, packName);
                if (player.TryCreateInInventoryWithNetworking(bag))
                    made++;
                else
                {
                    bag.Destroy();
                    break;
                }
            }

            Msg(made > 0
                ? $"extra: bags - {made} Booster Pack(s) created ({held + made}/{slots} slots filled, 102 slots each)"
                : "extra: bags - nothing to create");
            if ((player.ContainerCapacity ?? 7) > slots)
                Msg("extra: bags - an 8th slot exists server-side but the client shows it only after a relog; re-run then.");
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
                bool alreadyHas = player.GetAllPossessionsDeep().Any(i => i.WeenieClassId == wcid);
                if (alreadyHas) continue;
                var armorItem = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (armorItem != null)
                    player.TryCreateInInventoryWithNetworking(armorItem);
            }
        }

        // The VoD 9-piece Olthoi Shadow set — the ONE armor look for test gear (owner
        // 2026-08-02). Shared by /testchar (full-set spawn below) and /asforge (any piece at
        // any tier label): piece keys match the plugin's Armor Forge workbench.
        private static readonly (string Key, uint Wcid, string Label)[] VodArmorPieces =
        {
            ("helm",      3110264, "Helm"),
            ("coat",      3110308, "Coat (No Cloak)"),
            ("pauldrons", 3110269, "Pauldrons"),
            ("bracers",   3110272, "Bracers"),
            ("gloves",    3110271, "Gloves"),
            ("girth",     3110266, "Girth"),
            ("tassets",   3110267, "Tassets"),
            ("greaves",   3110268, "Greaves"),
            ("sollerets", 3110270, "Sollerets"),
        };

        // ── Premade suits (owner 2026-08-21, PremadeSuits_Design/Math_2026-08-21.md) ──
        // /asforge premade <tier> <avg|bis> [melee|caster]: the 18-piece `all` roster with the
        // cantrip lines written EXPLICITLY instead of rolled, so a tester can wear "the average
        // T-whatever suit" or "the best possible one" without farming. Two presets:
        //   BiS     = core four at window cap, the tier's MAX line count, every line at band MAX,
        //             lines in the fixed order below.
        //   Average = core four at window midpoint, floor(expected) lines per piece dealt
        //             round-robin from the class-weight mix (trash 10 / mid 6 / chase 1), every
        //             line at band MIDPOINT.
        // Line count ladder (max / guaranteed): T11 2/0, T12-14 3/1, T15-17 4/2, T18-20 5/3,
        // T21-24 6/4, T25 7/5 (owner ruling 08-21 late: T25 = 5 guaranteed, BiS 7th = 34 Item Aug).
        // ONE BiS list as of 2026-08-22: the class-split aug keys 37-40 are retired, so there is no
        // longer a melee vs caster suit - the universal damage lines lead. The melee|caster words are
        // still accepted on the command line and ignored, so old habits don't error.
        // 2026-08-22 pool: all seven aug keys retired; 47 Pct Max Health + 49 Reinforced (armor) / 48 Life on Hit (jewelry)
        // added. ArmorOnly / JewelryOnly keys are skipped per piece below, so armor and jewelry each reach the cap.
        private static readonly int[] PremadeBisLines = { 28, 29, 43, 33, 19, 47, 49, 48 };
        // Average mix - deal order. 43 All Attributes = the one chase line per suit; 25 Aegis is
        // armor-only; 33 Crit Rating ladders by COUNT through the per-tier chance, so an average
        // suit carries none of it (Math file section 2).
        private static readonly int[] PremadeAvgTrashKeys = { 20, 21, 32 };
        private static readonly int[] PremadeAvgMidKeys = { 19, 28, 29, 31, 49 };
        private const int PremadeAvgChaseKey = 43;
        private const int PremadeArmorOnlyKey = 25;

        private static int PremadeLineMax(int tier) =>
            tier <= 11 ? 2 : tier <= 14 ? 3 : tier <= 17 ? 4 : tier <= 20 ? 5 : tier <= 24 ? 6 : 7;

        /// <summary>The effective roll band for a cantrip key at a tier: the live Default-layer band
        /// authored on variation = tier wins (what real drops there roll from); otherwise the Armor v2
        /// formula - 1250-class keys max = 1250/18 x f, min = 20 pct; key 25 max 250 x f / min 50 x f;
        /// keys 32/33 pinned 1-3. Unrounded so the midpoint rounds ONCE (Math file section 1).</summary>
        private static (double Min, double Max) PremadeBand(int key, int tier)
        {
            var vd = ACE.Server.Managers.ZoneControl.ZoneControlManager.GetVariationDefault(tier);
            if (vd?.Profile?.CustomCantripBands != null
                && vd.Profile.CustomCantripBands.TryGetValue(key, out var live)
                && live != null && live.Max >= live.Min && live.Max > 0)
                return (live.Min, live.Max);

            var f = 1.0 + (tier - 11) / 14.0;
            switch (key)
            {
                case 32:
                case 33: return (1, 3);
                case PremadeArmorOnlyKey: return (50 * f, 250 * f);
                default:
                    var max = 1250.0 / 18.0 * f;
                    return (0.2 * max, max);
            }
        }

        /// <summary>Deal the Average suit's lines: per-piece count = guaranteed (max - 2) plus one
        /// extra on pieces 1,3,5,...,15 (the 8 leftover lines of 18 x 0.44); keys pulled round-robin
        /// from the class-weight multiset (trash round(L x 10/108) each, Aegis half of that on armor,
        /// one 43, the rest mid keys ascending), distinct per piece, key 25 only where there is AL.
        /// Returns one key list per roster index.</summary>
        private static List<int>[] PremadeDealAverage(int tier, bool[] hasArmorLevel)
        {
            var pieces = hasArmorLevel.Length;
            var guaranteed = Math.Max(0, PremadeLineMax(tier) - 2);
            var counts = new int[pieces];
            var total = 0;
            for (var i = 0; i < pieces; i++)
            {
                counts[i] = guaranteed + (i % 2 == 0 && i < 16 ? 1 : 0);
                total += counts[i];
            }

            // the multiset, by class weight over the 108-point armor pool
            var trashEach = (int)Math.Round(total * 10.0 / 108.0);
            var aegis = trashEach / 2;                                   // armor is half the roster
            var chase = total >= 18 ? 1 : 0;                             // T11's 8 lines carry no chase
            var mid = Math.Max(0, total - trashEach * PremadeAvgTrashKeys.Length - aegis - chase);

            var remaining = new Dictionary<int, int>();
            if (chase > 0) remaining[PremadeAvgChaseKey] = chase;
            if (aegis > 0) remaining[PremadeArmorOnlyKey] = aegis;
            foreach (var k in PremadeAvgTrashKeys) remaining[k] = trashEach;
            for (var i = 0; i < mid; i++)
            {
                var k = PremadeAvgMidKeys[i % PremadeAvgMidKeys.Length];
                remaining[k] = remaining.TryGetValue(k, out var c) ? c + 1 : 1;
            }

            // deal order: cycle the key list, one of each per pass, so every key spreads across pieces
            var order = new List<int> { PremadeAvgChaseKey, PremadeArmorOnlyKey };
            order.AddRange(PremadeAvgTrashKeys);
            order.AddRange(PremadeAvgMidKeys);
            var queue = new List<int>();
            var left = remaining.Values.Sum();
            while (left > 0)
                foreach (var k in order)
                {
                    if (!remaining.TryGetValue(k, out var c) || c <= 0) continue;
                    queue.Add(k);
                    remaining[k] = c - 1;
                    left--;
                }

            var dealt = new List<int>[pieces];
            for (var i = 0; i < pieces; i++) dealt[i] = new List<int>();
            for (var round = 0; queue.Count > 0 && round < 8; round++)
                for (var i = 0; i < pieces && queue.Count > 0; i++)
                {
                    if (counts[i] <= round) continue;
                    var idx = queue.FindIndex(k => !dealt[i].Contains(k) && (k != PremadeArmorOnlyKey || hasArmorLevel[i]));
                    if (idx < 0) continue;                               // nothing this piece can take - it runs short
                    dealt[i].Add(queue[idx]);
                    queue.RemoveAt(idx);
                }
            return dealt;
        }

        // The eight per-element armor resistance mods (mirrors LootGenerationFactory's
        // private list) — the loadout "prot" card overrides all of them uniformly.
        private static readonly ACE.Entity.Enum.Properties.PropertyFloat[] ForgeArmorModVsProps =
        {
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsSlash,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsPierce,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsBludgeon,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsFire,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsCold,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsAcid,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsElectric,
            ACE.Entity.Enum.Properties.PropertyFloat.ArmorModVsNether,
        };

        // Loadout spell suites (owner 2026-08-02, DB-verified ids): the 7 Legendary elemental
        // Wards and the 7 life Protections, both WEARER buffs -> both apply to every minted
        // piece. Protection tier = "Incantation of X Protection Self" — the tier the /testchar
        // gear (the set this forge is based on) actually carries (necklace: 4462/4466 + wards
        // 6079/6085; shirt 4466; pants 4470). Banes/Impen rejected (owner: life protections).
        private static readonly uint[] LegendaryWardSpells = { 6079, 6080, 6081, 6082, 6083, 6084, 6085 };
        private static readonly uint[] LifeProtectionSpells = { 4460, 4462, 4464, 4466, 4468, 4470, 4472 };
        // Owner 2026-08-09, from the live top-30 gear survey: Legendary Impenetrability
        // (strongest impen; same-family levels don't stack so one id suffices) and the
        // defensive cantrip cluster worn by virtually every endgame suit.
        private static readonly uint[] ImpenSpells = { 6095 };
        private static readonly uint[] DefensiveCantripSpells = { 6055, 6077, 6102, 6103, 6104, 6105 };
        // Owner 2026-08-16: the necklace is the ONLY test-gear buff carrier — it consolidates
        // EVERYTHING the old distributed set granted (all 10 builders' spells + the premade's
        // ward/lifeprot/defcantrip cards + the old trinket default suite), so one piece equals
        // the whole historical kit. Impen family (4667/6095) deliberately EXCLUDED — armor is
        // the impen carrier. Shared by BuildTestNecklace (/testchar) and the /asforge default.
        private static readonly uint[] NecklaceBuffSpells =
        {
            // Curated by owner 2026-08-16 (trimmed from the full historical kit: protections,
            // VI-era self buffs, blessings, banes, and the trade-skill Epics all cut).
            3731,                                     // Prodigal Regeneration
            6079, 6080, 6081, 6082, 6083, 6084, 6085, // Legendary elemental Wards x7
            6055, 6077, 6102, 6103, 6104, 6105,       // Leg. Invuln/Health Gain/Armor/Coord/End/Focus
            3694, 3730,                               // Prodigal Coordination / Quickness
            2059,                                     // Honed Control
            3569, 3570, 3571,                         // Mana / Stamina / Health Boost
            5137, 5139, 5141, 5187,                   // Augmented Underst./Dmg/DmgReduction + Rare Dmg X
            5238, 5253,                               // Sigil of Destruction XV / Defense XV
            5449, 5450,                               // Surging Strength / Towering Defense
            6170,                                     // Honeyed Life Mead
        };

        // Owner 2026-08-09: the trinket slot's DEFAULT suite (no card needed) — the 7
        // Legendary Wards + Legendary Armor, Augmented Understanding III, Augmented
        // Damage II, Rare Damage Boost X, Sigil of Defense XV, Sigil of Destruction XV,
        // Honeyed Life Mead, Towering Defense, Health/Stamina/Mana Boost.
        // UNUSED since 2026-08-16 (trinket mints blank); kept as the historical suite.
        private static readonly uint[] TrinketDefaultSpells =
        {
            6079, 6080, 6081, 6082, 6083, 6084, 6085, 6102,
            5137, 5139, 5187, 5253, 5238, 6170, 5450,
            3571, 3570, 3569,
        };

        /// <summary>Adds spells to a forged piece and stamps the /testchar-norm spell-support
        /// props (spellcraft/mana) if the piece has none — without them item spells never
        /// activate. No ItemDifficulty stamp: test gear gets no arcane lore gate.</summary>
        private static void AddForgeSpells(WorldObject wo, uint[] spells)
        {
            foreach (var s in spells)
                wo.Biota.GetOrAddKnownSpell((int)s, wo.BiotaDatabaseLock, out _);
            if ((wo.GetProperty(PropertyInt.ItemSpellcraft) ?? 0) == 0)
                wo.SetProperty(PropertyInt.ItemSpellcraft, 750);
            if ((wo.GetProperty(PropertyInt.ItemMaxMana) ?? 0) == 0)
                wo.SetProperty(PropertyInt.ItemMaxMana, 3500);
            wo.SetProperty(PropertyInt.ItemCurMana, wo.GetProperty(PropertyInt.ItemMaxMana) ?? 3500);
            wo.UiEffects = UiEffects.Magical;
        }

        // The full Gear* rating set — /asforge strips these from every mint (owner 2026-08-02:
        // bare pieces until the per-tier loadout is decided).
        private static readonly PropertyInt[] ForgeStrippedRatings =
        {
            PropertyInt.GearDamage, PropertyInt.GearDamageResist, PropertyInt.GearCrit,
            PropertyInt.GearCritResist, PropertyInt.GearCritDamage, PropertyInt.GearCritDamageResist,
            PropertyInt.GearHealingBoost, PropertyInt.GearNetherResist, PropertyInt.GearLifeResist,
            PropertyInt.GearMaxHealth, PropertyInt.GearPKDamageRating, PropertyInt.GearPKDamageResistRating,
            PropertyInt.GearOverpower, PropertyInt.GearOverpowerResist,
        };

        /// <summary>
        /// The `yardstick` card (owner 2026-08-20): re-applies the exact per-piece rating spread
        /// that /testchar gear carries, after /asforge's bare strip. Premades are the reference
        /// character every damage measurement is taken on, and every PRE-forge measurement was
        /// taken on /testchar gear — without this a premade came out with 960 less max health,
        /// so the two paths were not comparable.
        ///
        /// It has to live server-side: the `cards:` clause is applied UNIFORMLY to every piece
        /// in the batch, but this spread is per-piece (shirt/pants 175, each jewel 100, cloak
        /// 10). GearNetherResist and EquipmentSetId also have no card at all.
        ///
        /// Values below are the builders' own (BuildTestShirt/Pants/Cloak/Ring/Bracelet/Necklace
        /// /Trinket). If a builder's rating changes, change it HERE too — same-file, so keep
        /// them adjacent. VoD armor is absent on purpose: /testchar armor carries no ratings.
        /// Total GearMaxHealth = 175 + 175 + 10 + (6 x 100) = 960.
        /// </summary>
        private static void ApplyYardstickRatings(WorldObject wo)
        {
            switch (wo.WeenieClassId)
            {
                case 28607:     // shirt
                    wo.SetProperty(PropertyInt.GearDamage, 13);
                    wo.SetProperty(PropertyInt.GearDamageResist, 4);
                    wo.SetProperty(PropertyInt.GearCritDamage, 13);
                    wo.SetProperty(PropertyInt.GearCritDamageResist, 4);
                    wo.SetProperty(PropertyInt.GearNetherResist, 9);
                    wo.SetProperty(PropertyInt.GearMaxHealth, 175);
                    break;
                case 2599:      // pants
                    wo.SetProperty(PropertyInt.GearDamage, 14);
                    wo.SetProperty(PropertyInt.GearDamageResist, 4);
                    wo.SetProperty(PropertyInt.GearCritDamage, 13);
                    wo.SetProperty(PropertyInt.GearCritDamageResist, 4);
                    wo.SetProperty(PropertyInt.GearNetherResist, 8);
                    wo.SetProperty(PropertyInt.GearMaxHealth, 175);
                    break;
                case 227190032: // cloak — also the only piece with an equipment set
                    wo.SetProperty(PropertyInt.GearDamageResist, 5);
                    wo.SetProperty(PropertyInt.GearCritDamageResist, 3);
                    wo.SetProperty(PropertyInt.GearNetherResist, 5);
                    wo.SetProperty(PropertyInt.GearMaxHealth, 10);
                    wo.SetProperty(PropertyInt.EquipmentSetId, 71);
                    break;
                case 21392:     // bracelet (both wrists)
                case 21394:     // ring (both fingers)
                case 27445:     // necklace
                case 41483:     // trinket
                    wo.SetProperty(PropertyInt.GearMaxHealth, 100);
                    break;
            }
        }

        private static WorldObject BuildVodArmorPiece(uint wcid, string label, string tier)
        {
            var item = WorldObjectFactory.CreateNewWorldObject(wcid);
            if (item == null) return null;
            var name = $"{tier} {label} (Test)";
            item.Name = name;
            item.SetProperty(PropertyString.Name, name);
            item.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
            // Armor is allowed EXACTLY ONE buff: Legendary Impenetrability (owner 2026-08-16;
            // matches the /asforge T11+ default). All other buffs live on the necklace only.
            // /asforge re-clears and re-adds this, so both paths agree.
            item.Biota.ClearSpells(item.BiotaDatabaseLock);
            AddForgeSpells(item, ImpenSpells);
            item.ChangesDetected = true;
            return item;
        }

        private static void SpawnOlthoiShadowArmor(Player player, string tier = "T11")
        {
            foreach (var piece in VodArmorPieces)
            {
                if (HasItemNamed(player, $"{tier} {piece.Label} (Test)")) continue;
                var item = BuildVodArmorPiece(piece.Wcid, piece.Label, tier);
                if (item != null)
                    AddItemToInventory(player, item);
            }
        }

        // Clothing + jewelry builders: each configures ONE piece exactly the way the /testchar
        // gear has always been built (name from the tier label, props, spells) and returns it
        // WITHOUT adding to inventory — /testchar's Spawn wrappers keep their has-item dedup,
        // /asforge always mints. Do not fork these blocks; both commands share them.
        private static void SpawnCustomUndergarmentsAndCloak(Player player, string tier = "T11")
        {
            if (!HasItemNamed(player, $"{tier} Shirt"))
            {
                var shirt = BuildTestShirt(tier);
                if (shirt != null)
                    AddItemToInventory(player, shirt);
            }

            if (!HasItemNamed(player, $"{tier} Pants"))
            {
                var pants = BuildTestPants(tier);
                if (pants != null)
                    AddItemToInventory(player, pants);
            }

            if (!HasItemNamed(player, $"{tier} Cloak"))
            {
                var cloak = BuildTestCloak(tier);
                if (cloak != null)
                    AddItemToInventory(player, cloak);
            }
        }

        private static WorldObject BuildTestShirt(string tier)
        {
            var shirtName = $"{tier} Shirt";
            var shirt = WorldObjectFactory.CreateNewWorldObject(28607);
            if (shirt != null)
            {
                shirt.Name = shirtName;
                shirt.SetProperty(PropertyString.Name, shirtName);
                shirt.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                shirt.SetProperty(PropertyInt.Value, 11519);
                shirt.SetProperty(PropertyInt.Mass, 75);
                shirt.SetProperty(PropertyInt.EncumbranceVal, 75);
                shirt.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                shirt.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                shirt.SetProperty(PropertyInt.WieldDifficulty, 725);
                shirt.SetProperty(PropertyInt.ItemWorkmanship, 7);
                shirt.SetProperty(PropertyInt.ItemSpellcraft, 750);
                shirt.SetProperty(PropertyInt.ItemCurMana, 3240);
                shirt.SetProperty(PropertyInt.ItemMaxMana, 3500);
                shirt.SetProperty(PropertyInt.ItemDifficulty, 750);
                shirt.SetProperty(PropertyInt.ItemMaxLevel, 20);
                shirt.SetProperty(PropertyInt.GearDamage, 13);
                shirt.SetProperty(PropertyInt.GearDamageResist, 4);
                shirt.SetProperty(PropertyInt.GearCritDamage, 13);
                shirt.SetProperty(PropertyInt.GearCritDamageResist, 4);
                shirt.SetProperty(PropertyInt.GearNetherResist, 9);
                shirt.SetProperty(PropertyInt.GearMaxHealth, 175);
                
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                // ClearSpells stays so base-weenie-authored spells are wiped too.
                shirt.Biota.ClearSpells(shirt.BiotaDatabaseLock);
                shirt.ChangesDetected = true;
            }
            return shirt;
        }

        private static WorldObject BuildTestPants(string tier)
        {
            var pantsName = $"{tier} Pants";
            var pants = WorldObjectFactory.CreateNewWorldObject(2599);
            if (pants != null)
            {
                pants.Name = pantsName;
                pants.SetProperty(PropertyString.Name, pantsName);
                pants.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                pants.SetProperty(PropertyInt.Value, 13948);
                pants.SetProperty(PropertyInt.Mass, 90);
                pants.SetProperty(PropertyInt.EncumbranceVal, 135);
                pants.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                pants.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                pants.SetProperty(PropertyInt.WieldDifficulty, 725);
                pants.SetProperty(PropertyInt.ItemWorkmanship, 8);
                pants.SetProperty(PropertyInt.ItemSpellcraft, 750);
                pants.SetProperty(PropertyInt.ItemCurMana, 3240);
                pants.SetProperty(PropertyInt.ItemMaxMana, 3500);
                pants.SetProperty(PropertyInt.ItemDifficulty, 750);
                pants.SetProperty(PropertyInt.ItemMaxLevel, 20);
                pants.SetProperty(PropertyInt.GearDamage, 14);
                pants.SetProperty(PropertyInt.GearDamageResist, 4);
                pants.SetProperty(PropertyInt.GearCritDamage, 13);
                pants.SetProperty(PropertyInt.GearCritDamageResist, 4);
                pants.SetProperty(PropertyInt.GearNetherResist, 8);
                pants.SetProperty(PropertyInt.GearMaxHealth, 175);
                
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                pants.Biota.ClearSpells(pants.BiotaDatabaseLock);
                pants.ChangesDetected = true;
            }
            return pants;
        }

        private static WorldObject BuildTestCloak(string tier)
        {
            var cloakName = $"{tier} Cloak";
            var cloak = WorldObjectFactory.CreateNewWorldObject(227190032);
            if (cloak != null)
            {
                cloak.Name = cloakName;
                cloak.SetProperty(PropertyString.Name, cloakName);
                cloak.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                cloak.SetProperty(PropertyInt.Value, 2500);
                cloak.SetProperty(PropertyInt.Mass, 0);
                cloak.SetProperty(PropertyInt.EncumbranceVal, 75);
                cloak.SetProperty(PropertyInt.ItemWorkmanship, 10);
                cloak.SetProperty(PropertyInt.ItemSpellcraft, 2000);
                cloak.SetProperty(PropertyInt.ItemCurMana, 4791);
                cloak.SetProperty(PropertyInt.ItemMaxMana, 5000);
                cloak.SetProperty(PropertyInt.EquipmentSetId, 71);
                cloak.SetProperty(PropertyInt.ItemMaxLevel, 5);
                cloak.SetProperty(PropertyInt.ItemXpStyle, (int)ItemXpStyle.ScalesWithLevel);
                cloak.SetProperty(PropertyInt.GearDamageResist, 5);
                cloak.SetProperty(PropertyInt.GearCritDamageResist, 3);
                cloak.SetProperty(PropertyInt.GearNetherResist, 5);
                cloak.SetProperty(PropertyInt.GearMaxHealth, 10);
                
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                cloak.Biota.ClearSpells(cloak.BiotaDatabaseLock);
                cloak.ChangesDetected = true;
            }
            return cloak;
        }

        private static void SpawnCustomJewelry(Player player, string tier = "T11")
        {
            if (!HasItemNamed(player, $"{tier} Bracelet 1"))
            {
                var b = BuildTestBracelet1(tier);
                if (b != null) AddItemToInventory(player, b);
            }
            if (!HasItemNamed(player, $"{tier} Bracelet 2"))
            {
                var b = BuildTestBracelet2(tier);
                if (b != null) AddItemToInventory(player, b);
            }
            if (!HasItemNamed(player, $"{tier} Ring 1"))
            {
                var r = BuildTestRing1(tier);
                if (r != null) AddItemToInventory(player, r);
            }
            if (!HasItemNamed(player, $"{tier} Ring 2"))
            {
                var r = BuildTestRing2(tier);
                if (r != null) AddItemToInventory(player, r);
            }
            if (!HasItemNamed(player, $"{tier} Necklace"))
            {
                var n = BuildTestNecklace(tier);
                if (n != null) AddItemToInventory(player, n);
            }
            if (!HasItemNamed(player, $"{tier} Trinket"))
            {
                var t = BuildTestTrinket(tier);
                if (t != null) AddItemToInventory(player, t);
            }
        }

        private static WorldObject BuildTestBracelet1(string tier)
        {
            var leftBraceletName = $"{tier} Bracelet 1";
            var leftBracelet = WorldObjectFactory.CreateNewWorldObject(21392);
            if (leftBracelet != null)
            {
                leftBracelet.Name = leftBraceletName;
                leftBracelet.SetProperty(PropertyString.Name, leftBraceletName);
                leftBracelet.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                leftBracelet.SetProperty(PropertyInt.Value, 150);
                leftBracelet.SetProperty(PropertyInt.Mass, 60);
                leftBracelet.SetProperty(PropertyInt.EncumbranceVal, 150);
                leftBracelet.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                leftBracelet.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                leftBracelet.SetProperty(PropertyInt.WieldDifficulty, 725);
                leftBracelet.SetProperty(PropertyInt.ItemWorkmanship, 6);
                leftBracelet.SetProperty(PropertyInt.ItemSpellcraft, 3870);
                leftBracelet.SetProperty(PropertyInt.ItemCurMana, 1196);
                leftBracelet.SetProperty(PropertyInt.ItemMaxMana, 1618);
                leftBracelet.SetProperty(PropertyInt.ItemDifficulty, 1698);
                leftBracelet.SetProperty(PropertyInt.GemCount, 2);
                leftBracelet.SetProperty(PropertyInt.GemType, 49);
                leftBracelet.SetProperty(PropertyInt.GearMaxHealth, 100);
                leftBracelet.ValidLocations = EquipMask.WristWearLeft;
 
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                leftBracelet.Biota.ClearSpells(leftBracelet.BiotaDatabaseLock);
                leftBracelet.ChangesDetected = true;
            }
            return leftBracelet;
        }

        private static WorldObject BuildTestBracelet2(string tier)
        {
            var rightBraceletName = $"{tier} Bracelet 2";
            var rightBracelet = WorldObjectFactory.CreateNewWorldObject(21392);
            if (rightBracelet != null)
            {
                rightBracelet.Name = rightBraceletName;
                rightBracelet.SetProperty(PropertyString.Name, rightBraceletName);
                rightBracelet.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                rightBracelet.SetProperty(PropertyInt.Value, 150);
                rightBracelet.SetProperty(PropertyInt.Mass, 60);
                rightBracelet.SetProperty(PropertyInt.EncumbranceVal, 150);
                rightBracelet.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                rightBracelet.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                rightBracelet.SetProperty(PropertyInt.WieldDifficulty, 725);
                rightBracelet.SetProperty(PropertyInt.ItemWorkmanship, 6);
                rightBracelet.SetProperty(PropertyInt.ItemSpellcraft, 3825);
                rightBracelet.SetProperty(PropertyInt.ItemCurMana, 1392);
                rightBracelet.SetProperty(PropertyInt.ItemMaxMana, 1743);
                rightBracelet.SetProperty(PropertyInt.ItemDifficulty, 1608);
                rightBracelet.SetProperty(PropertyInt.GemCount, 4);
                rightBracelet.SetProperty(PropertyInt.GemType, 33);
                rightBracelet.SetProperty(PropertyInt.GearMaxHealth, 100);
                rightBracelet.ValidLocations = EquipMask.WristWearRight;
 
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                rightBracelet.Biota.ClearSpells(rightBracelet.BiotaDatabaseLock);
                rightBracelet.ChangesDetected = true;
            }
            return rightBracelet;
        }

        private static WorldObject BuildTestRing1(string tier)
        {
            var leftRingName = $"{tier} Ring 1";
            var leftRing = WorldObjectFactory.CreateNewWorldObject(21394);
            if (leftRing != null)
            {
                leftRing.Name = leftRingName;
                leftRing.SetProperty(PropertyString.Name, leftRingName);
                leftRing.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                leftRing.SetProperty(PropertyInt.Value, 30);
                leftRing.SetProperty(PropertyInt.Mass, 20);
                leftRing.SetProperty(PropertyInt.EncumbranceVal, 30);
                leftRing.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                leftRing.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                leftRing.SetProperty(PropertyInt.WieldDifficulty, 725);
                leftRing.SetProperty(PropertyInt.ItemWorkmanship, 7);
                leftRing.SetProperty(PropertyInt.ItemSpellcraft, 4315);
                leftRing.SetProperty(PropertyInt.ItemCurMana, 1166);
                leftRing.SetProperty(PropertyInt.ItemMaxMana, 1517);
                leftRing.SetProperty(PropertyInt.ItemDifficulty, 1876);
                leftRing.SetProperty(PropertyInt.GemCount, 3);
                leftRing.SetProperty(PropertyInt.GemType, 49);
                leftRing.SetProperty(PropertyInt.GearMaxHealth, 100);
                leftRing.ValidLocations = EquipMask.FingerWearLeft;
 
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                leftRing.Biota.ClearSpells(leftRing.BiotaDatabaseLock);
                leftRing.ChangesDetected = true;
            }
            return leftRing;
        }

        private static WorldObject BuildTestRing2(string tier)
        {
            var rightRingName = $"{tier} Ring 2";
            var rightRing = WorldObjectFactory.CreateNewWorldObject(21394);
            if (rightRing != null)
            {
                rightRing.Name = rightRingName;
                rightRing.SetProperty(PropertyString.Name, rightRingName);
                rightRing.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                rightRing.SetProperty(PropertyInt.Value, 30);
                rightRing.SetProperty(PropertyInt.Mass, 20);
                rightRing.SetProperty(PropertyInt.EncumbranceVal, 30);
                rightRing.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                rightRing.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                rightRing.SetProperty(PropertyInt.WieldDifficulty, 725);
                rightRing.SetProperty(PropertyInt.ItemWorkmanship, 7);
                rightRing.SetProperty(PropertyInt.ItemSpellcraft, 4273);
                rightRing.SetProperty(PropertyInt.ItemCurMana, 1399);
                rightRing.SetProperty(PropertyInt.ItemMaxMana, 1751);
                rightRing.SetProperty(PropertyInt.ItemDifficulty, 1828);
                rightRing.SetProperty(PropertyInt.GemCount, 3);
                rightRing.SetProperty(PropertyInt.GemType, 39);
                rightRing.SetProperty(PropertyInt.GearMaxHealth, 100);
                rightRing.ValidLocations = EquipMask.FingerWearRight;
 
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                rightRing.Biota.ClearSpells(rightRing.BiotaDatabaseLock);
                rightRing.ChangesDetected = true;
            }
            return rightRing;
        }

        private static WorldObject BuildTestNecklace(string tier)
        {
            var necklaceName = $"{tier} Necklace";
            var necklace = WorldObjectFactory.CreateNewWorldObject(27445);
            if (necklace != null)
            {
                necklace.Name = necklaceName;
                necklace.SetProperty(PropertyString.Name, necklaceName);
                necklace.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                necklace.SetProperty(PropertyInt.Value, 90);
                necklace.SetProperty(PropertyInt.Mass, 60);
                necklace.SetProperty(PropertyInt.EncumbranceVal, 90);
                necklace.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                necklace.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                necklace.SetProperty(PropertyInt.WieldDifficulty, 725);
                necklace.SetProperty(PropertyInt.ItemWorkmanship, 8);
                necklace.SetProperty(PropertyInt.ItemSpellcraft, 4370);
                // The consolidated ~50-spell suite needs a pool that never runs dry (each item
                // spell activation draws item mana) — test gear must not fizzle mid-session.
                necklace.SetProperty(PropertyInt.ItemCurMana, 100000);
                necklace.SetProperty(PropertyInt.ItemMaxMana, 100000);
                necklace.SetProperty(PropertyInt.ItemDifficulty, 1935);
                necklace.SetProperty(PropertyInt.GemCount, 3);
                necklace.SetProperty(PropertyInt.GemType, 49);
                necklace.SetProperty(PropertyInt.GearMaxHealth, 100);
                necklace.ValidLocations = EquipMask.NeckWear;
 
                // Spells — THE buff carrier: the necklace is the ONLY test-gear piece that carries
                // buffs (owner 2026-08-16); armor additionally carries Legendary Impen only.
                // Suite shared with the /asforge default (NecklaceBuffSpells).
                necklace.Biota.ClearSpells(necklace.BiotaDatabaseLock);
                foreach (var spellId in NecklaceBuffSpells)
                    necklace.Biota.GetOrAddKnownSpell((int)spellId, necklace.BiotaDatabaseLock, out _);
                necklace.ChangesDetected = true;
                necklace.UiEffects = UiEffects.Magical;
            }
            return necklace;
        }

        private static WorldObject BuildTestTrinket(string tier)
        {
            var trinketName = $"{tier} Trinket";
            var trinket = WorldObjectFactory.CreateNewWorldObject(41483);
            if (trinket != null)
            {
                trinket.Name = trinketName;
                trinket.SetProperty(PropertyString.Name, trinketName);
                trinket.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                trinket.SetProperty(PropertyInt.Value, 100);
                trinket.SetProperty(PropertyInt.Mass, 60);
                trinket.SetProperty(PropertyInt.EncumbranceVal, 100);
                trinket.SetProperty(PropertyInt.WieldRequirements, (int)WieldRequirement.RawSkill);
                trinket.SetProperty(PropertyInt.WieldSkillType, (int)Skill.MeleeDefense);
                trinket.SetProperty(PropertyInt.WieldDifficulty, 725);
                trinket.SetProperty(PropertyInt.ItemWorkmanship, 8);
                trinket.SetProperty(PropertyInt.ItemSpellcraft, 339);
                trinket.SetProperty(PropertyInt.ItemCurMana, 1266);
                trinket.SetProperty(PropertyInt.ItemMaxMana, 1618);
                trinket.SetProperty(PropertyInt.ItemDifficulty, 397);
                trinket.SetProperty(PropertyInt.GemCount, 4);
                trinket.SetProperty(PropertyInt.GemType, 38);
                trinket.SetProperty(PropertyInt.GearMaxHealth, 100);
                trinket.ValidLocations = EquipMask.TrinketOne;
 
                // BLANK by design (owner 2026-08-16): only the necklace carries buffs.
                trinket.Biota.ClearSpells(trinket.BiotaDatabaseLock);
                trinket.ChangesDetected = true;
            }
            return trinket;
        }

        /// <summary>The Admin > Armor Forge subtab's backend (owner 2026-08-02,
        /// ArmorForge_Plan_2026-08-02.md): mints the /testchar VoD gear at any tier label.
        /// Same pipeline contract as /wsforge — normal item creation, never direct DB writes.
        /// T10 = the basic set (no aug gate); T11+ carry the per-tier item-aug wield gate like
        /// real drops. Everything forged is Attuned + Bonded (owner 2026-08-02).</summary>
        [CommandHandler("asforge", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 1,
            "Forges VoD test armor/clothing/jewelry (the /testchar look) at a chosen tier. All pieces Attuned + Bonded.",
            "<piece|suit|jewel|all> [tier 10-25, default 11] [cards:key=val,key,...]\n" +
            "premade <tier 11-25> <avg|bis> [melee|caster] [force] = the 18-piece suit with EXPLICIT cantrip lines: bis = core at cap + max lines at band max; avg = core at midpoint + expected lines at band midpoint. Minted into a 'T<tier> <Avg|BiS> Suit' bag.\n" +
            "Pieces: helm coat pauldrons bracers gloves girth tassets greaves sollerets shirt pants cloak neck ring bracelet trinket\n" +
            "suit = 9 armor + shirt/pants/cloak; jewel = necklace + 2 rings + 2 bracelets + trinket; all = both.\n" +
            "ring/bracelet mint the left + right pair.\n" +
            "cards: albonus=N (AL over tier baseline) prot=X (uniform 8-element mod) dresist/cdresist/maxhp/drating/cdrating=N (Gear ratings)\n" +
            "ward (adds the 7 Legendary elemental Wards) lifeprot (adds the 7 life Protections)\n" +
            "impen (Legendary Impenetrability) defcantrips (Legendary Armor/Invuln/Coord/Endurance/Focus/Health Gain) - all on every minted piece\n" +
            "yardstick (per-piece: restores the exact /testchar Gear rating spread, 960 total max health, incl. nether resist + the cloak's set 71)\n" +
            "Defaults (no card): VoD armor gets Legendary Impen at EVERY tier; the necklace gets the full buff suite. Everything else mints blank.\n" +
            "force = mint even if you already hold that piece (default is to skip it, so repeat presses cannot stack suits).")]
        public static void HandleAsForge(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            var player = session.Player;
            if (player == null)
                return;

            var key = parameters[0].ToLowerInvariant();

            if (key == "premade")
            {
                HandleAsForgePremade(session, player, parameters);
                return;
            }

            var tier = 11;
            if (parameters.Length > 1 && int.TryParse(parameters[1], out var t))
                tier = Math.Clamp(t, 10, 25);
            var tierLabel = $"T{tier}";

            // Loadout clause (the plugin's Cards section, owner 2026-08-02): cards:key=val,key,...
            // Any position after the piece arg. Unknown keys are an error, not a silent skip.
            var albonus = 0;
            double? protOverride = null;
            int? dresist = null, cdresist = null, maxhp = null, drating = null, cdrating = null;
            var ward = false;
            var lifeprot = false;
            var impen = false;
            var defcantrips = false;
            var yardstick = false;
            var loadoutDesc = "";

            // Owner 2026-08-20: pressing Create Premade twice used to hand you a second full
            // suit, because this command always minted. It now skips a piece the character
            // already holds, the same deep-possession convention /testchar extra and the
            // jewelry spawn already use. `force` is the deliberate re-forge (after a card
            // change, say) - without it a re-press is a no-op and SAYS so.
            var force = parameters.Skip(1).Any(a => a.Equals("force", StringComparison.OrdinalIgnoreCase));

            foreach (var p in parameters.Skip(1))
            {
                if (!p.StartsWith("cards:", StringComparison.OrdinalIgnoreCase))
                    continue;
                loadoutDesc = p.Substring(6);
                foreach (var token in loadoutDesc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = token.Split('=');
                    var ck = parts[0].ToLowerInvariant();
                    var cv = parts.Length > 1 ? parts[1] : null;
                    int Iv() => int.TryParse(cv, out var n) ? Math.Max(0, n) : 0;
                    switch (ck)
                    {
                        case "albonus": albonus = Iv(); break;
                        case "prot":
                            if (double.TryParse(cv, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var pv))
                                protOverride = pv;
                            break;
                        case "dresist": dresist = Iv(); break;
                        case "cdresist": cdresist = Iv(); break;
                        case "maxhp": maxhp = Iv(); break;
                        case "drating": drating = Iv(); break;
                        case "cdrating": cdrating = Iv(); break;
                        case "ward": ward = true; break;
                        case "lifeprot": lifeprot = true; break;
                        case "impen": impen = true; break;
                        case "defcantrips": defcantrips = true; break;
                        case "yardstick": yardstick = true; break;
                        default:
                            Msg($"asforge: unknown card '{ck}'. Cards: albonus prot dresist cdresist maxhp drating cdrating ward lifeprot impen defcantrips yardstick");
                            return;
                    }
                }
            }

            var items = new List<WorldObject>();
            bool AddPiece(string piece)
            {
                switch (piece)
                {
                    case "shirt": items.Add(BuildTestShirt(tierLabel)); return true;
                    case "pants": items.Add(BuildTestPants(tierLabel)); return true;
                    case "cloak": items.Add(BuildTestCloak(tierLabel)); return true;
                    case "neck": items.Add(BuildTestNecklace(tierLabel)); return true;
                    case "trinket": items.Add(BuildTestTrinket(tierLabel)); return true;
                    case "ring":
                        items.Add(BuildTestRing1(tierLabel));
                        items.Add(BuildTestRing2(tierLabel));
                        return true;
                    case "bracelet":
                        items.Add(BuildTestBracelet1(tierLabel));
                        items.Add(BuildTestBracelet2(tierLabel));
                        return true;
                    default:
                        foreach (var p in VodArmorPieces)
                        {
                            if (p.Key != piece) continue;
                            items.Add(BuildVodArmorPiece(p.Wcid, p.Label, tierLabel));
                            return true;
                        }
                        return false;
                }
            }
            void AddSuit()
            {
                foreach (var p in VodArmorPieces)
                    items.Add(BuildVodArmorPiece(p.Wcid, p.Label, tierLabel));
                AddPiece("shirt");
                AddPiece("pants");
                AddPiece("cloak");
            }
            void AddJewel()
            {
                AddPiece("neck");
                AddPiece("ring");
                AddPiece("bracelet");
                AddPiece("trinket");
            }

            switch (key)
            {
                case "suit": AddSuit(); break;
                case "jewel": AddJewel(); break;
                case "all": AddSuit(); AddJewel(); break;
                default:
                    if (!AddPiece(key))
                    {
                        Msg("asforge: unknown piece. Pieces: suit jewel all "
                            + string.Join(" ", VodArmorPieces.Select(p => p.Key))
                            + " shirt pants cloak neck ring bracelet trinket");
                        return;
                    }
                    break;
            }

            var minted = 0;
            var skipped = 0;
            foreach (var wo in items)
            {
                if (wo == null)
                {
                    Msg("asforge: a piece failed to create (missing weenie?)");
                    continue;
                }

                // Already held? Skip it. Every forged piece carries a unique tier-prefixed name
                // ("T12 Ring 1", "T12 Helm (Test)"), so presence is an exact test - and the pair
                // pieces cannot collide with each other.
                if (!force && HasItemNamed(player, wo.Name))
                {
                    skipped++;
                    wo.Destroy();
                    continue;
                }

                // T11+ carry the per-tier item-aug wield gate like real drops (replacing the
                // piece's authored skill req, same as the Creature_Death sweep does); the gate
                // helper appends armor/jewelry's "Wield requires" LongDesc line itself.
                if (tier >= 11)
                {
                    ACE.Server.Factories.LootGenerationFactory.StripWieldRequirements(wo);
                    ACE.Server.Factories.LootGenerationFactory.ApplyT11WieldRequirement(wo, tier);
                }

                wo.Attuned = AttunedStatus.Attuned;
                wo.Bonded = BondedStatus.Bonded;

                // Pieces mint BARE first (owner 2026-08-02): spells, the full Gear* rating set
                // and equipment-set membership (Dexterous etc.) all stripped — builders' loadout
                // AND anything base-weenie-authored. The cards clause then layers the requested
                // loadout back on. /testchar keeps its spells and ratings (it is the regression
                // yardstick), so none of this lives in the shared builders.
                wo.Biota.ClearSpells(wo.BiotaDatabaseLock);
                foreach (var ratingProp in ForgeStrippedRatings)
                    wo.RemoveProperty(ratingProp);
                wo.RemoveProperty(PropertyInt.EquipmentSetId);

                // T11+ deterministic per-tier budget - the SAME shared helper the loot path
                // uses, so premades match drops exactly (owner 2026-08-21). Runs BEFORE the
                // yardstick/rating cards so cards still win. Also stamps AL (doubling ladder)
                // and the gear Creature Augs gate base. Tier 10 keeps the legacy path below.
                if (tier >= 11)
                    ACE.Server.Factories.LootGenerationFactory.ApplyT11GearStats(wo, tier, forceMax: true);   // forge = core four at window cap

                // The yardstick card goes FIRST so an explicit rating card on the same line still
                // wins (e.g. cards:yardstick,maxhp=50 gives 50, not 100).
                if (yardstick) ApplyYardstickRatings(wo);

                // rating cards (any piece — jewelry carries ratings in the real game too)
                if (dresist.HasValue) wo.SetProperty(PropertyInt.GearDamageResist, dresist.Value);
                if (cdresist.HasValue) wo.SetProperty(PropertyInt.GearCritDamageResist, cdresist.Value);
                if (maxhp.HasValue) wo.SetProperty(PropertyInt.GearMaxHealth, maxhp.Value);
                if (drating.HasValue) wo.SetProperty(PropertyInt.GearDamage, drating.Value);
                if (cdrating.HasValue) wo.SetProperty(PropertyInt.GearCritDamage, cdrating.Value);

                // spell-suite cards: wards + life protections are wearer buffs — any piece
                var isVodArmor = VodArmorPieces.Any(p => p.Wcid == wo.WeenieClassId);
                if (ward)
                    AddForgeSpells(wo, LegendaryWardSpells);
                if (lifeprot)
                    AddForgeSpells(wo, LifeProtectionSpells);
                if (impen)
                    AddForgeSpells(wo, ImpenSpells);
                if (defcantrips)
                    AddForgeSpells(wo, DefensiveCantripSpells);

                // Owner defaults (2026-08-16): armor carries Legendary Impenetrability at EVERY
                // tier (was T11+); the NECKLACE carries the shared buff suite (the only buff
                // carrier — the trinket's old default suite is gone). Cards stay opt-in.
                // GetOrAddKnownSpell dedups if the matching cards are also on.
                if (isVodArmor)
                    AddForgeSpells(wo, ImpenSpells);
                if (wo.WeenieClassId == 27445)
                    AddForgeSpells(wo, NecklaceBuffSpells);
                wo.ChangesDetected = true;

                // VoD armor pieces: per-tier AL baseline (tier x 100) + albonus card on top;
                // protection = equalized to the mean (same as real T11+ drops), or the prot
                // card's uniform override.
                if (isVodArmor)
                {
                    if (tier >= 11)
                    {
                        // AL came from the shared budget (1100 doubling); albonus adds on top
                        if (albonus != 0)
                            wo.SetProperty(PropertyInt.ArmorLevel, (wo.ArmorLevel ?? 0) + albonus);
                    }
                    else
                    {
                        wo.SetProperty(PropertyInt.ArmorLevel, tier * 100 + albonus);
                        ACE.Server.Factories.LootGenerationFactory.EqualizeT11ArmorResists(wo);
                    }
                    if (protOverride.HasValue)
                        foreach (var modProp in ForgeArmorModVsProps)
                            wo.SetProperty(modProp, protOverride.Value);
                }

                var provenance = $"Created by: {player.Name}\nTier: {tier}";
                wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? provenance : wo.LongDesc + "\n" + provenance;

                if (player.TryCreateInInventoryWithNetworking(wo))
                {
                    minted++;
                }
                else
                {
                    Msg($"asforge: could not place {wo.Name} in inventory (full?)");
                    wo.Destroy();
                }
            }

            Msg($"asforged: {minted} item(s) at tier {tier} (attuned + bonded"
                + (tier >= 11 ? ", item-aug wield gate" : ", basic set - no aug gate")
                + (loadoutDesc.Length > 0 ? $") loadout: {loadoutDesc}" : ") bare"));

            // Said plainly, because a silent no-op reads as a broken command.
            if (skipped > 0)
                Msg($"asforge: skipped {skipped} piece(s) you already hold. "
                    + (minted == 0
                        ? "Nothing was made. Trash the old set first, or add 'force' to mint anyway."
                        : "Add 'force' to mint duplicates."));
        }

        /// <summary>`/asforge premade <tier 11-25> <avg|bis> [melee|caster] [force]` (owner 2026-08-21):
        /// the 18-piece roster with the cantrip lines written from the preset tables above instead of
        /// rolled. Same bare-strip pipeline as the main verb (no cards), then the shared gear helper
        /// (bis = core at cap, avg = core at the window midpoint), then explicit ZoneCantrips.Stamp
        /// per line, FinalizeT11LongDesc (asforge proper never runs it - the stamps would otherwise
        /// sit under inherited flavor text), then the forge provenance. Minted into a dedicated bag:
        /// 18 items in one frame through the main-pack path is the silent-loss window.</summary>
        private static void HandleAsForgePremade(Session session, Player player, string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            const string usage = "asforge premade <tier 11-25> <avg|bis> [melee|caster] [force]";
            if (parameters.Length < 3 || !int.TryParse(parameters[1], out var tier) || tier < 11 || tier > 25)
            {
                Msg($"Usage: {usage}");
                return;
            }
            var modeArg = parameters[2].ToLowerInvariant();
            bool bis;
            switch (modeArg)
            {
                case "bis": case "best": bis = true; break;
                case "avg": case "average": bis = false; break;
                default:
                    Msg($"asforge premade: mode must be avg or bis. {usage}");
                    return;
            }
            var force = false;
            foreach (var a in parameters.Skip(3))
            {
                switch (a.ToLowerInvariant())
                {
                    case "melee": case "caster": break;   // accepted and ignored - one BiS list since 2026-08-22
                    case "force": force = true; break;
                    default:
                        Msg($"asforge premade: unknown option '{a}'. {usage}");
                        return;
                }
            }

            var tierLabel = $"T{tier}";
            var modeTag = bis ? "BiS" : "Avg";

            // the `all` roster, in deal order (armor first so the Aegis lines land where AL lives)
            var roster = new List<(string Piece, WorldObject Wo)>();
            foreach (var p in VodArmorPieces)
                roster.Add((p.Label, BuildVodArmorPiece(p.Wcid, p.Label, tierLabel)));
            roster.Add(("Shirt", BuildTestShirt(tierLabel)));
            roster.Add(("Pants", BuildTestPants(tierLabel)));
            roster.Add(("Cloak", BuildTestCloak(tierLabel)));
            roster.Add(("Necklace", BuildTestNecklace(tierLabel)));
            roster.Add(("Ring 1", BuildTestRing1(tierLabel)));
            roster.Add(("Ring 2", BuildTestRing2(tierLabel)));
            roster.Add(("Bracelet 1", BuildTestBracelet1(tierLabel)));
            roster.Add(("Bracelet 2", BuildTestBracelet2(tierLabel)));
            roster.Add(("Trinket", BuildTestTrinket(tierLabel)));

            // names carry the mode so Avg and BiS at one tier never collide on the duplicate guard
            foreach (var (piece, wo) in roster)
            {
                if (wo == null) continue;
                var name = $"{tierLabel} {piece} ({modeTag})";
                wo.Name = name;
                wo.SetProperty(PropertyString.Name, name);
            }

            // Average deal needs to know which pieces carry AL BEFORE the gear helper runs: only
            // ItemType.Armor gets the flat ladder (clothing never, jewelry by engine rule).
            var hasAl = roster.Select(r => r.Wo != null && r.Wo.ItemType == ItemType.Armor).ToArray();
            var avgDeal = bis ? null : PremadeDealAverage(tier, hasAl);
            var bisKeys = PremadeBisLines;
            var bisCount = Math.Min(PremadeLineMax(tier), bisKeys.Length);

            var bagName = $"{tierLabel} {modeTag} Suit";
            Container bag = null;

            var minted = 0;
            var skipped = 0;
            var failed = 0;
            var lines = 0;
            for (var i = 0; i < roster.Count; i++)
            {
                var (piece, wo) = roster[i];
                if (wo == null)
                {
                    Msg($"asforge premade: {piece} failed to create (missing weenie?)");
                    failed++;
                    continue;
                }

                if (!force && HasItemNamed(player, wo.Name))
                {
                    skipped++;
                    wo.Destroy();
                    continue;
                }

                // per-tier item-aug wield gate like real drops (the helper appends its own LongDesc line)
                ACE.Server.Factories.LootGenerationFactory.StripWieldRequirements(wo);
                ACE.Server.Factories.LootGenerationFactory.ApplyT11WieldRequirement(wo, tier);

                wo.Attuned = AttunedStatus.Attuned;
                wo.Bonded = BondedStatus.Bonded;

                // bare first, exactly as the main verb: spells, every Gear* rating, set membership
                wo.Biota.ClearSpells(wo.BiotaDatabaseLock);
                foreach (var ratingProp in ForgeStrippedRatings)
                    wo.RemoveProperty(ratingProp);
                wo.RemoveProperty(PropertyInt.EquipmentSetId);

                // core four: bis = window cap (forceMax), avg = window midpoint (coreFrac 0.5);
                // also AL ladder + the gear Creature Augs base
                ACE.Server.Factories.LootGenerationFactory.ApplyT11GearStats(wo, tier,
                    forceMax: bis, p: null, coreFrac: bis ? null : 0.5);

                // the explicit lines - graded (live stat resolution 2026-08-22): bis = grade 1000, avg = 500,
                // stamped through the ZcLines record; each key at most once per piece
                var keys = bis ? bisKeys.Take(bisCount) : avgDeal[i];
                var stamped = new HashSet<int>();
                foreach (var k in keys)
                {
                    if (!stamped.Add(k)) continue;
                    if (!ACE.Server.Managers.ZoneControl.ZoneCantrips.TryGet(k, out var def) || def.SlotSpecial)
                    {
                        Msg($"asforge premade: key {k} is not a live pool line - skipped on {piece}");
                        continue;
                    }
                    // live slot rule (owner 2026-08-22): the tier's Default-layer override when authored, else the
                    // catalog's ArmorOnly / JewelryOnly - exactly what a real drop at this tier would obey
                    var slotRule = ACE.Server.Managers.ZoneControl.ZoneCantrips.EffectiveSlotMask(def,
                        ACE.Server.Managers.ZoneControl.ZoneControlManager.GetVariationDefault(tier)?.Profile?.CustomCantripSlots);
                    if (!ACE.Server.Managers.ZoneControl.ZoneCantrips.SlotAllowed(slotRule, ACE.Server.Managers.ZoneControl.ZoneCantrips.PieceMask(wo)))
                        continue;
                    var (bMin, bMax) = PremadeBand(k, tier);
                    var grade = bis ? ACE.Server.Managers.ZoneControl.ZoneStatResolver.GradeMax
                                    : ACE.Server.Managers.ZoneControl.ZoneStatResolver.GradeMax / 2;
                    ACE.Server.Managers.ZoneControl.ZoneCantrips.StampGraded(wo, def, grade,
                        ((int)Math.Round(bMin), (int)Math.Round(bMax)));
                    lines++;
                }

                // owner defaults as the main verb: armor carries Legendary Impen, the necklace the buff suite
                if (VodArmorPieces.Any(p => p.Wcid == wo.WeenieClassId))
                    AddForgeSpells(wo, ImpenSpells);
                if (wo.WeenieClassId == 27445)
                    AddForgeSpells(wo, NecklaceBuffSpells);
                wo.ChangesDetected = true;

                // drop-style description: known lines in stamp order, inherited flavor text gone.
                // Provenance goes AFTER - Finalize's whitelist would discard it.
                ACE.Server.Factories.LootGenerationFactory.FinalizeT11LongDesc(wo);
                var provenance = $"Created by: {player.Name}\nTier: {tier}\nPremade: {modeTag}";
                wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? provenance : wo.LongDesc + "\n\n" + provenance;

                // the bag is created lazily so a fully-skipped re-press leaves no empty bag behind
                bag ??= GetOrCreatePack(player, bagName);
                if (TryPlaceInPack(player, wo, bag))
                {
                    minted++;
                }
                else
                {
                    Msg($"asforge premade: could not place {wo.Name} in {bagName} (full?)");
                    wo.Destroy();
                    failed++;
                }
            }

            Msg($"Premade {tierLabel} {modeTag} suit: {minted} pieces created"
                + (lines > 0 ? $", {lines} cantrip lines" : "")
                + (failed > 0 ? $", {failed} failed" : "")
                + (skipped > 0 ? $", {skipped} skipped (already held - add 'force' to re-mint)" : "")
                + (minted > 0 ? $" - in '{bagName}'." : "."));
        }

        private static void SpawnCharms(Player player)
        {
            if (HasItemNamed(player, "Ability Charms Pack")) return;

            var rucksack = GetOrCreatePack(player, "Ability Charms Pack");

            var charmWcids = new List<uint>()
            {
                777700001,  // Mana Barrier (T1)
                777700019,  // Infinite Casting (T1)
                777700020,  // Asheron's Favor (T1)
                777700021,  // Artisan's Charm (T1)
                777700022,  // Shrapnel (T1)
                777700023,  // Agony (T1)
                777700025,  // Explosive Arrow (T1)
                777700024,  // Split Cast (T1)
                777700026,  // Omni Strike (T1)
                78780030,   // Summon Essence Refill (T1)
                78780031,   // Universal Summoning Mastery (T1)
                777700300,  // Auto-Rebuff (T1)
                777700027,  // Fork (T1)
                777700028   // Far Shot (T1)
            };

            foreach (var wcid in charmWcids)
            {
                var charm = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (charm != null)
                    PlaceInPackOrLoose(player, charm, rucksack);
            }

            // 1000x Charm Catalyst
            var catalyst = WorldObjectFactory.CreateNewWorldObject(777700010);
            if (catalyst != null)
            {
                catalyst.SetProperty(PropertyInt.StackSize, 1000);
                catalyst.SetProperty(PropertyInt.EncumbranceVal, (catalyst.StackUnitEncumbrance ?? 5) * 1000);
                PlaceInPackOrLoose(player, catalyst, rucksack);
            }

            SpawnSpellcastingConsumables(player);
        }

        private static void SpawnSpellcastingConsumables(Player player)
        {
            var possessions = player.GetAllPossessionsDeep().ToList();

            // Comps go into their own pack (owner 08-15)
            var compsPack = GetOrCreatePack(player, "Spell Comps Pack");

            void AddComp(WorldObject item) => PlaceInPackOrLoose(player, item, compsPack);

            // 1. Tapers (1000 Prismatic Tapers)
            var targetTapers = 1000;
            var currentTapers = possessions.Where(i => i.WeenieClassId == 20631).Sum(i => i.StackSize ?? 1);
            if (currentTapers < targetTapers)
            {
                var needed = targetTapers - currentTapers;
                while (needed > 0)
                {
                    var taper = WorldObjectFactory.CreateNewWorldObject(20631);
                    if (taper == null) break;
                    var maxStack = taper.MaxStackSize ?? 1000;
                    var toSpawn = Math.Min(needed, maxStack);
                    taper.SetStackSize(toSpawn);
                    AddComp(taper);
                    needed -= toSpawn;
                }
            }

            // 2. Scarabs (100 of each standard spellcasting scarab)
            var scarabWcids = new List<uint> { 691, 689, 686, 688, 687, 690, 8897, 37155 };
            var targetScarabs = 100;

            foreach (var wcid in scarabWcids)
            {
                var currentScarabs = possessions.Where(i => i.WeenieClassId == wcid).Sum(i => i.StackSize ?? 1);
                if (currentScarabs < targetScarabs)
                {
                    var needed = targetScarabs - currentScarabs;
                    while (needed > 0)
                    {
                        var scarab = WorldObjectFactory.CreateNewWorldObject(wcid);
                        if (scarab == null) break;
                        var maxStack = scarab.MaxStackSize ?? 100;
                        var toSpawn = Math.Min(needed, maxStack);
                        scarab.SetStackSize(toSpawn);
                        AddComp(scarab);
                        needed -= toSpawn;
                    }
                }
            }

        }

        private static void AddItemToInventory(Player player, WorldObject item)
        {
            player.TryCreateInInventoryWithNetworking(item);
        }

        /// <summary>Finds an existing 102-slot pack (wcid 310025) by name anywhere in the player's
        /// possessions, or creates one IN the player's inventory (wsforge GetOrCreateForgePack
        /// pattern — the pack must be live before TryPlaceInPack's client messages make sense).
        /// Returns null when no pack slot is free — callers fall back to the main pack.</summary>
        private static Container GetOrCreatePack(Player player, string name, string legacyName = null)
        {
            var pack = player.GetAllPossessionsDeep().OfType<Container>()
                .FirstOrDefault(c => c.Name == name || (legacyName != null && c.Name == legacyName));
            if (pack != null) return pack;

            if (!(WorldObjectFactory.CreateNewWorldObject(310025) is Container bag)) return null;
            bag.Name = name;
            bag.SetProperty(PropertyString.Name, name);
            bag.SetProperty(PropertyInt.MaterialType, 0);
            if (player.TryCreateInInventoryWithNetworking(bag))
                return bag;
            bag.Destroy();   // no free pack slot
            return null;
        }

        /// <summary>Place a freshly minted item into a pack the player already holds. Same as
        /// WeaponScalingCommands.TryPlaceInPack: TryCreateInInventoryWithNetworking only targets
        /// "main pack, else first side pack with room", so the client updates are sent by hand.</summary>
        private static bool TryPlaceInPack(Player player, WorldObject wo, Container pack)
        {
            if (pack == null || !pack.TryAddToInventory(wo))
                return false;

            player.Session.Network.EnqueueSend(new GameMessageCreateObject(wo));
            player.Session.Network.EnqueueSend(
                new GameEventItemServerSaysContainId(player.Session, wo, pack),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.EncumbranceVal, player.EncumbranceVal ?? 0));
            wo.SaveBiotaToDatabase();
            return true;
        }

        /// <summary>Pack an item, falling back loose to the main pack when the pack is missing/full.</summary>
        private static void PlaceInPackOrLoose(Player player, WorldObject wo, Container pack)
        {
            if (!TryPlaceInPack(player, wo, pack))
                player.TryCreateInInventoryWithNetworking(wo);
        }

        private static bool HasItemNamed(Player player, string name)
        {
            return player.GetAllPossessionsDeep().Any(i => i.Name == name);
        }

        private static void SpawnAetherias(Player player)
        {
            // Enable Aetheria slots
            player.UpdateProperty(player, PropertyInt.AetheriaBitfield, (int)AetheriaBitfield.All);

            // 1. Blue Aetheria
            if (!HasItemNamed(player, "T10 Blue Aetheria"))
            {
            var blueAetheria = WorldObjectFactory.CreateNewWorldObject(42635);
            if (blueAetheria != null)
            {
                blueAetheria.Name = "T10 Blue Aetheria";
                blueAetheria.SetProperty(PropertyString.Name, "T10 Blue Aetheria");
                blueAetheria.SetProperty(PropertyString.LongDesc, "This aetheria's sigil now shows on the surface.");
                blueAetheria.SetProperty(PropertyInt.EquipmentSetId, (int)EquipmentSet.AetheriaGrowth);
                blueAetheria.SetProperty(PropertyDataId.Icon, 100690944); // 0x06006C00 Blue Growth icon
                blueAetheria.SetProperty(PropertyDataId.ProcSpell, 5206); // Surge of Protection
                blueAetheria.SetProperty(PropertyBool.ProcSpellSelfTargeted, true);
                blueAetheria.SetProperty(PropertyInt.ValidLocations, (int)EquipMask.SigilOne);
                blueAetheria.SetProperty(PropertyInt.ItemMaxLevel, 10);
                blueAetheria.SetProperty(PropertyInt.ItemXpStyle, (int)ItemXpStyle.ScalesWithLevel);
                blueAetheria.SetProperty(PropertyInt64.ItemBaseXp, 1000000000L);
                blueAetheria.SetProperty(PropertyInt64.ItemTotalXp, 1023000000000L);
                blueAetheria.SetProperty(PropertyInt.GearCrit, 4);

                // Set Level 10 overlay & wield requirements matching Grumpy Old Man
                blueAetheria.IconOverlayId = LootGenerationFactory.IconOverlay_ItemMaxLevel[9];
                blueAetheria.WieldRequirements = WieldRequirement.RawSkill;
                blueAetheria.WieldSkillType = (int)Skill.MeleeDefense;
                blueAetheria.WieldDifficulty = 725;
                blueAetheria.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix

                AddItemToInventory(player, blueAetheria);
            }
            }

            // 2. Yellow Aetheria
            if (!HasItemNamed(player, "T10 Yellow Aetheria"))
            {
            var yellowAetheria = WorldObjectFactory.CreateNewWorldObject(42637);
            if (yellowAetheria != null)
            {
                yellowAetheria.Name = "T10 Yellow Aetheria";
                yellowAetheria.SetProperty(PropertyString.Name, "T10 Yellow Aetheria");
                yellowAetheria.SetProperty(PropertyString.LongDesc, "This aetheria's sigil now shows on the surface.");
                yellowAetheria.SetProperty(PropertyInt.EquipmentSetId, (int)EquipmentSet.AetheriaFury);
                yellowAetheria.SetProperty(PropertyDataId.Icon, 100690931); // 0x06006BF3 Yellow Fury icon
                yellowAetheria.SetProperty(PropertyDataId.ProcSpell, 5208); // Surge of Regeneration
                yellowAetheria.SetProperty(PropertyBool.ProcSpellSelfTargeted, true);
                yellowAetheria.SetProperty(PropertyInt.ValidLocations, (int)EquipMask.SigilTwo);
                yellowAetheria.SetProperty(PropertyInt.ItemMaxLevel, 8);
                yellowAetheria.SetProperty(PropertyInt.ItemXpStyle, (int)ItemXpStyle.ScalesWithLevel);
                yellowAetheria.SetProperty(PropertyInt64.ItemBaseXp, 1000000000L);
                yellowAetheria.SetProperty(PropertyInt64.ItemTotalXp, 255000000000L);
                yellowAetheria.SetProperty(PropertyInt.GearCrit, 4);

                // Set Level 8 overlay & wield requirements matching Grumpy Old Man
                yellowAetheria.IconOverlayId = LootGenerationFactory.IconOverlay_ItemMaxLevel[7];
                yellowAetheria.WieldRequirements = WieldRequirement.RawSkill;
                yellowAetheria.WieldSkillType = (int)Skill.MeleeDefense;
                yellowAetheria.WieldDifficulty = 725;
                yellowAetheria.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix

                AddItemToInventory(player, yellowAetheria);
            }
            }

            // 3. Red Aetheria
            if (!HasItemNamed(player, "T10 Red Aetheria"))
            {
            var redAetheria = WorldObjectFactory.CreateNewWorldObject(42636);
            if (redAetheria != null)
            {
                redAetheria.Name = "T10 Red Aetheria";
                redAetheria.SetProperty(PropertyString.Name, "T10 Red Aetheria");
                redAetheria.SetProperty(PropertyString.LongDesc, "This aetheria's sigil now shows on the surface.");
                redAetheria.SetProperty(PropertyInt.EquipmentSetId, (int)EquipmentSet.AetheriaFury);
                redAetheria.SetProperty(PropertyDataId.Icon, 100690948); // 0x06006C04 Red Fury icon
                redAetheria.SetProperty(PropertyDataId.ProcSpell, 5204); // Surge of Destruction
                redAetheria.SetProperty(PropertyBool.ProcSpellSelfTargeted, true);
                redAetheria.SetProperty(PropertyInt.ValidLocations, (int)EquipMask.SigilThree);
                redAetheria.SetProperty(PropertyInt.ItemMaxLevel, 8);
                redAetheria.SetProperty(PropertyInt.ItemXpStyle, (int)ItemXpStyle.ScalesWithLevel);
                redAetheria.SetProperty(PropertyInt64.ItemBaseXp, 1000000000L);
                redAetheria.SetProperty(PropertyInt64.ItemTotalXp, 255000000000L);
                redAetheria.SetProperty(PropertyInt.GearCrit, 4);

                // Set Level 8 overlay & wield requirements matching Grumpy Old Man
                redAetheria.IconOverlayId = LootGenerationFactory.IconOverlay_ItemMaxLevel[7];
                redAetheria.WieldRequirements = WieldRequirement.RawSkill;
                redAetheria.WieldSkillType = (int)Skill.MeleeDefense;
                redAetheria.WieldDifficulty = 725;
                redAetheria.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix

                AddItemToInventory(player, redAetheria);
            }
            }
        }

        private static void SpawnWeapons(Player player, string targetStyle)
        {
            var rucksackName = "Weapons Pack";
            if (HasItemNamed(player, rucksackName)) return;
            var rucksack = GetOrCreatePack(player, rucksackName);

            var weaponBases = new Dictionary<string, (uint wcid, string baseName)>()
            {
                { "UA", (29651, "Spiked Knuckles") },
                { "2H", (46105, "Atlan Two Handed Sword") },
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
                DamageType.Electric,
                DamageType.Nether
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
                    if (weaponType.Equals("Bow", StringComparison.OrdinalIgnoreCase))
                    {
                        CreateCustomBow(player, element, true, rucksack);
                    }
                    else if (weaponType.Equals("UA", StringComparison.OrdinalIgnoreCase))
                    {
                        CreateCustomUA(player, element, rucksack);
                    }
                    else if (weaponType.Equals("Wand", StringComparison.OrdinalIgnoreCase))
                    {
                        CreateCustomWand(player, element, rucksack);
                    }
                    else
                    {
                        var weapon = WorldObjectFactory.CreateNewWorldObject(baseWcid);
                        if (weapon != null)
                        {
                            weapon.SetProperty(PropertyInt.DamageType, (int)element);
                            var elementLabel = GetElementLabel(element);
                            string label = weaponType.Equals("2H", StringComparison.OrdinalIgnoreCase) ? "2H" : baseName;
                            weapon.Name = $"{elementLabel} {label} (Test)";
                            weapon.SetProperty(PropertyString.Name, $"{elementLabel} {label} (Test)");
                            weapon.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
                            PlaceInPackOrLoose(player, weapon, rucksack);
                        }
                    }
                }
            }

        }

        private static void CreateCustomBow(Player player, DamageType element, bool includeRend = true, Container destination = null)
        {
            uint baseWcid = 46139;
            string elementLabel = GetElementLabel(element);
            var expectedName = $"{elementLabel} Bow (Test)";
            if (HasItemNamed(player, expectedName)) return;

            var bow = WorldObjectFactory.CreateNewWorldObject(baseWcid);
            if (bow == null) return;
            bow.Name = $"{elementLabel} Bow (Test)";
            bow.SetProperty(PropertyString.Name, $"{elementLabel} Bow (Test)");
            bow.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix

            // Get matching rend background underlay
            ImbuedEffectType rendType = ImbuedEffectType.Undef;

            if (includeRend)
            {
                switch (element)
                {
                    case DamageType.Slash:
                        rendType = ImbuedEffectType.SlashRending;
                        break;
                    case DamageType.Pierce:
                        rendType = ImbuedEffectType.PierceRending;
                        break;
                    case DamageType.Bludgeon:
                        rendType = ImbuedEffectType.BludgeonRending;
                        break;
                    case DamageType.Cold:
                        rendType = ImbuedEffectType.ColdRending;
                        break;
                    case DamageType.Fire:
                        rendType = ImbuedEffectType.FireRending;
                        break;
                    case DamageType.Acid:
                        rendType = ImbuedEffectType.AcidRending;
                        break;
                    case DamageType.Electric:
                        rendType = ImbuedEffectType.ElectricRending;
                        break;
                    case DamageType.Nether:
                        rendType = ImbuedEffectType.NetherRending;
                        break;
                }
            }

            if (includeRend && rendType != ImbuedEffectType.Undef)
            {
                if (ACE.Server.Managers.RecipeManager.IconUnderlay.TryGetValue(rendType, out var underlayId))
                {
                    bow.IconUnderlayId = underlayId;
                }
                else if (rendType == ImbuedEffectType.NetherRending)
                {
                    bow.IconUnderlayId = 0x060067A1; // Nether Rending underlay icon
                }
            }

            if (includeRend)
            {
                bow.SetProperty(PropertyString.LongDesc, $"{elementLabel} Bow (Test) of Swift Killer, set with 1 Emerald");
            }
            else
            {
                bow.SetProperty(PropertyString.LongDesc, $"{elementLabel} Bow (Test) of Swift Killer");
            }

            // Int Properties
            bow.SetProperty(PropertyInt.DamageType, (int)element);
            bow.SetProperty(PropertyInt.WieldDifficulty, 725);
            bow.SetProperty(PropertyInt.GearCritDamage, 3);
            bow.SetProperty(PropertyInt.ElementalDamageBonus, 31);
            bow.SetProperty(PropertyInt.Cleaving, 3);
            if (includeRend && rendType != ImbuedEffectType.Undef)
            {
                bow.SetProperty(PropertyInt.ImbuedEffect, (int)rendType);
            }
            bow.SetProperty(PropertyInt.ImbuedEffect2, (int)ImbuedEffectType.MagicDefense); // 4096 is MagicDefense
            bow.SetProperty(PropertyInt.NumTimesTinkered, 10);

            // Float Properties
            bow.SetProperty(PropertyFloat.DamageMod, 3.39f);
            bow.SetProperty(PropertyFloat.WeaponDefense, 1.29f);
            bow.SetProperty(PropertyFloat.CriticalFrequency, 0.33f);
            bow.SetProperty(PropertyFloat.CriticalMultiplier, 2.25f);

            // Split Arrow custom properties
            bow.SetProperty(PropertyBool.SplitArrows, true);
            bow.SetProperty(PropertyInt.SplitArrowCount, 3);
            bow.SetProperty(PropertyFloat.SplitArrowRange, 8.0f);
            bow.SetProperty(PropertyFloat.SplitArrowDamageMultiplier, 0.95f);

            // Bow Spells
            bow.Biota.ClearSpells(bow.BiotaDatabaseLock);
            var bowSpells = new List<uint>()
            {
                6089, // Legendary Blood Thirst (CantripBloodThirst4)
            };

            foreach (var spellId in bowSpells)
            {
                bow.Biota.GetOrAddKnownSpell((int)spellId, bow.BiotaDatabaseLock, out _);
            }
            bow.ChangesDetected = true;
            bow.UiEffects = UiEffects.Magical;

            PlaceInPackOrLoose(player, bow, destination);
        }

        private static void CreateCustomUA(Player player, DamageType element, Container destination = null)
        {
            uint baseWcid = 6171;
            string elementLabel = GetElementLabel(element);
            string weaponName = $"{elementLabel} UA (Test)";
            if (HasItemNamed(player, weaponName)) return;

            var claw = WorldObjectFactory.CreateNewWorldObject(baseWcid);
            if (claw == null) return;

            claw.Name = weaponName;
            claw.SetProperty(PropertyString.Name, weaponName);
            claw.SetProperty(PropertyString.LongDesc, $"{weaponName} of Defender, set with 4 Rubies");

            // Get matching rend background underlay
            ImbuedEffectType rendType = ImbuedEffectType.Undef;
            switch (element)
            {
                case DamageType.Slash:
                    rendType = ImbuedEffectType.SlashRending;
                    break;
                case DamageType.Pierce:
                    rendType = ImbuedEffectType.PierceRending;
                    break;
                case DamageType.Bludgeon:
                    rendType = ImbuedEffectType.BludgeonRending;
                    break;
                case DamageType.Cold:
                    rendType = ImbuedEffectType.ColdRending;
                    break;
                case DamageType.Fire:
                    rendType = ImbuedEffectType.FireRending;
                    break;
                case DamageType.Acid:
                    rendType = ImbuedEffectType.AcidRending;
                    break;
                case DamageType.Electric:
                    rendType = ImbuedEffectType.ElectricRending;
                    break;
                case DamageType.Nether:
                    rendType = ImbuedEffectType.NetherRending;
                    break;
            }

            if (rendType != ImbuedEffectType.Undef)
            {
                if (ACE.Server.Managers.RecipeManager.IconUnderlay.TryGetValue(rendType, out var underlayId))
                {
                    claw.IconUnderlayId = underlayId;
                }
                else if (rendType == ImbuedEffectType.NetherRending)
                {
                    claw.IconUnderlayId = 0x060067A1; // Nether Rending underlay icon
                }
            }

            // Int Properties
            claw.SetProperty(PropertyInt.DamageType, (int)element);
            claw.SetProperty(PropertyInt.Damage, 130);
            claw.SetProperty(PropertyInt.WieldDifficulty, 800);
            claw.SetProperty(PropertyInt.WieldRequirements, 2);
            claw.SetProperty(PropertyInt.WieldSkillType, 46);
            claw.SetProperty(PropertyInt.GearCritDamage, 3);
            claw.SetProperty(PropertyInt.GemCount, 4);
            claw.SetProperty(PropertyInt.GemType, 38);
            claw.SetProperty(PropertyInt.Cleaving, 3); // Cleaving
            if (rendType != ImbuedEffectType.Undef)
            {
                claw.SetProperty(PropertyInt.ImbuedEffect, (int)rendType);
            }
            claw.SetProperty(PropertyInt.ItemCurMana, 1402);
            claw.SetProperty(PropertyInt.ItemMaxMana, 1814);
            claw.SetProperty(PropertyInt.ItemSpellcraft, 370);
            claw.SetProperty(PropertyInt.ItemWorkmanship, 9);
            claw.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
            claw.SetProperty(PropertyInt.NumTimesTinkered, 10);
            claw.SetProperty(PropertyInt.Value, 0); // Sets value to 0
            claw.SetProperty(PropertyInt.Bonded, 1);
            claw.SetProperty(PropertyInt.Attuned, 1);
            // NO AttackType override: 486 (the hilt's thrust/slash multi-strike set) has no motions in the
            // unarmed stance table, which made these un-attackable — and the protocol has no UA multi-strike
            // AttackTypes at all. The weenie's native Punch (1) applies.
            claw.SetProperty(PropertyInt.WieldRequirements2, 8);
            claw.SetProperty(PropertyInt.WieldSkillType2, 46);
            claw.SetProperty(PropertyInt.WieldDifficulty2, 3);
            claw.SetProperty(PropertyBool.Ivoryable, true);

            // Float Properties
            claw.SetProperty(PropertyFloat.DamageMod, 1.075f); // Base 1.0f + 0.075f from Recipe 527870096
            claw.SetProperty(PropertyFloat.DamageVariance, 0.53076923f);
            claw.SetProperty(PropertyFloat.ManaRate, -0.06666667f);
            claw.SetProperty(PropertyFloat.WeaponDefense, 1.29f);
            claw.SetProperty(PropertyFloat.WeaponMagicDefense, 1.04f);
            claw.SetProperty(PropertyFloat.CriticalFrequency, 0.58f); // Base 0.33f + 0.25f from Recipe 527870096
            claw.SetProperty(PropertyFloat.CriticalMultiplier, 2.425f); // Base 2.25f + 0.175f from Recipe 527870096
            claw.SetProperty(PropertyFloat.ManaStoneDestroyChance, 0.01f);

            // Claw Spells
            claw.Biota.ClearSpells(claw.BiotaDatabaseLock);
            var clawSpells = new List<uint>()
            {
                6089, // Legendary Blood Thirst (CantripBloodThirst4)
            };

            foreach (var spellId in clawSpells)
            {
                claw.Biota.GetOrAddKnownSpell((int)spellId, claw.BiotaDatabaseLock, out _);
            }
            claw.ChangesDetected = true;
            claw.UiEffects = UiEffects.Magical;

            PlaceInPackOrLoose(player, claw, destination);
        }

        private static void CreateCustomWand(Player player, DamageType element, Container destination = null)
        {
            uint baseWcid = 46122;
            string elementLabel = GetElementLabel(element);
            string weaponName = $"{elementLabel} Wand (Test)";
            if (HasItemNamed(player, weaponName)) return;

            var wand = WorldObjectFactory.CreateNewWorldObject(baseWcid);
            if (wand == null) return;

            wand.Name = weaponName;
            wand.SetProperty(PropertyString.Name, weaponName);
            wand.SetProperty(PropertyString.LongDesc, $"{weaponName} of Corrosion, set with 2 Peridots");

            // Underlays
            ImbuedEffectType rendType = element switch
            {
                DamageType.Slash => ImbuedEffectType.SlashRending,
                DamageType.Pierce => ImbuedEffectType.PierceRending,
                DamageType.Bludgeon => ImbuedEffectType.BludgeonRending,
                DamageType.Cold => ImbuedEffectType.ColdRending,
                DamageType.Fire => ImbuedEffectType.FireRending,
                DamageType.Acid => ImbuedEffectType.AcidRending,
                DamageType.Electric => ImbuedEffectType.ElectricRending,
                DamageType.Nether => ImbuedEffectType.NetherRending,
                _ => ImbuedEffectType.Undef
            };

            if (rendType != ImbuedEffectType.Undef)
            {
                if (ACE.Server.Managers.RecipeManager.IconUnderlay.TryGetValue(rendType, out var underlayId))
                {
                    wand.IconUnderlayId = underlayId;
                }
                else if (rendType == ImbuedEffectType.NetherRending)
                {
                    wand.IconUnderlayId = 0x060067A1; // Nether Rending underlay icon
                }
            }

            // Int Properties
            wand.SetProperty(PropertyInt.DamageType, (int)element);
            wand.SetProperty(PropertyInt.WieldDifficulty, 650);
            wand.SetProperty(PropertyInt.WieldRequirements, 2);
            wand.SetProperty(PropertyInt.WieldSkillType, element == DamageType.Nether ? (int)43 : (int)34); // Void Magic or Mana Conversion
            wand.SetProperty(PropertyInt.GearCritDamage, 3);
            wand.SetProperty(PropertyInt.GemCount, 2);
            wand.SetProperty(PropertyInt.GemType, 34); // Peridot
            if (rendType != ImbuedEffectType.Undef)
            {
                wand.SetProperty(PropertyInt.ImbuedEffect, (int)rendType);
            }
            wand.SetProperty(PropertyInt.ImbuedEffect2, (int)ImbuedEffectType.MagicDefense); // MagicDefense
            wand.SetProperty(PropertyInt.ItemCurMana, 4084);
            wand.SetProperty(PropertyInt.ItemMaxMana, 4084);
            wand.SetProperty(PropertyInt.ItemDifficulty, 400);
            wand.SetProperty(PropertyInt.ItemSpellcraft, 370);
            wand.SetProperty(PropertyInt.ItemWorkmanship, 7);
            wand.SetProperty(PropertyInt.MaterialType, 0); // Suppress material prefix
            wand.SetProperty(PropertyInt.NumTimesTinkered, 1);
            wand.Biota.TryRemoveProperty(PropertyInt.SlayerCreatureType, wand.BiotaDatabaseLock);
            wand.SetProperty(PropertyInt.Value, 31410);
            wand.SetProperty(PropertyInt.WeaponType, 0);
            wand.SetProperty(PropertyInt.EncumbranceVal, 50);

            // Float Properties
            wand.SetProperty(PropertyFloat.CriticalFrequency, 0.33f);
            wand.SetProperty(PropertyFloat.CriticalMultiplier, 2.25f);
            wand.SetProperty(PropertyFloat.ElementalDamageMod, 1.375f);
            wand.SetProperty(PropertyFloat.ManaConversionMod, 0.19f);
            wand.SetProperty(PropertyFloat.ManaRate, -0.06666667f);
            wand.SetProperty(PropertyFloat.WeaponDefense, 1.23f);

            // Wand Spells
            wand.Biota.ClearSpells(wand.BiotaDatabaseLock);
            var wandSpells = new List<uint>()
            {
                6098, // Legendary Spirit Thirst
            };

            foreach (var spellId in wandSpells)
            {
                wand.Biota.GetOrAddKnownSpell((int)spellId, wand.BiotaDatabaseLock, out _);
            }
            wand.ChangesDetected = true;
            wand.UiEffects = UiEffects.Magical;

            PlaceInPackOrLoose(player, wand, destination);
        }

        private static string GetElementLabel(DamageType element)
        {
            return element switch
            {
                DamageType.Electric => "Lightning",
                DamageType.Nether => "Nether",
                _ => element.ToString()
            };
        }

        private static void SpawnTeleportGems(Player player, Container destination = null)
        {
            var gemWcids = new List<uint>()
            {
                // Portal-Sending / Summoning Gems
                86753051,   // Frozen Valley Everlasting Portal Gem
                644540104,  // Gaerlan's Library Portal Sending Gem
                64454045,   // Hoshino Tent Sending Gem
                290444450,  // Mhoire Castle Portal Sending Gem
                290444449,  // Timaru Portal Sending Gem
                64454046,   // Town Network Sending Gem
                290444451,  // Tusker King Sending Gem
                86753080,   // Lifestone Sending Gem
                694200120,  // Infinite Viridian Rise Deru Portal Sending Gem
                53450,      // Viridian Rise Deru Portal Sending Gem (Single Use)
                2005053,    // Infinite Town Network Portal Gem
                290500127,  // Unlimited Dark Island Portal Gem
                290500126,  // Unlimited Vissidal Island Portal Gem
                227190017,  // Restored Portal Gem
                694200501,  // Pet Shop Quest Portal gem
                694200509,  // Burun History Quest Portal gem
                694200181,  // Enlightened Facility Hub Portal Gem
                3110166,    // Valley of Death Encampment Gem
                71271,      // Inner Burial Chamber Portal Sending Gem
                694200385,  // Defense of Zaikhal portal gem
                694200389,  // Elysas Favor portal gem

                // Self-Teleport / Recall Gems
                86753075,   // Marketplace Recall Gem
                86753079,   // Lifestone Recall Gem
                86753076,   // Portal Recall Gem
                86753077,   // Primary Portal Recall Gem
                86753078,   // Secondary Portal Recall Gem
                3110009,    // Wicked Wares Gem
                98760170,   // Zerivax Recall Gem
                300101193,  // Thaelaryn Lassel Recall Gem
                867530155,  // Penthouse Penthouse Recall Gem
                64454319,   // Igmo's Retreat Recall Gem
                98760065,   // Fraternity of QB Recall Gem
                19851000,   // Halls of Introduction Gem
                19860016,   // Prestige Palace Gem
                500008972,  // Plateau of Agility Gem
                300101097,  // Admin Bog Gem
                730003019,  // Testing Area v11 Gem

                777700029   // Tou Tou Prestige Portal Gem
            };

            var playerWcids = new HashSet<uint>(player.GetAllPossessionsDeep().Select(i => i.WeenieClassId));

            foreach (var wcid in gemWcids)
            {
                if (playerWcids.Contains(wcid)) continue;

                var gem = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (gem != null)
                    PlaceInPackOrLoose(player, gem, destination);
            }
        }
    }
}
