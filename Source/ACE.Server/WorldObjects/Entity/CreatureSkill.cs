using System;

using ACE.Common.Extensions;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects.Entity
{
    public class CreatureSkill
    {
        private readonly Creature creature;

        public readonly Skill Skill;

        // The underlying database record
        public readonly PropertiesSkill PropertiesSkill;

        public CreatureSkill(Creature creature, Skill skill, PropertiesSkill propertiesSkill)
        {
            this.creature = creature;
            Skill = skill;
            this.PropertiesSkill = propertiesSkill;
        }

        /// <summary>
        /// A bonus from character creation: +5 for trained, +10 for specialized
        /// </summary>
        public uint InitLevel
        {
            get => PropertiesSkill.InitLevel;
            set => PropertiesSkill.InitLevel = value;
        }

        public SkillAdvancementClass AdvancementClass
        {
            get => PropertiesSkill.SAC;
            set
            {
                if (PropertiesSkill.SAC != value)
                    creature.ChangesDetected = true;

                PropertiesSkill.SAC = value;
            }
        }

        public bool IsUsable
        {
            get
            {
                if (AdvancementClass == SkillAdvancementClass.Trained || AdvancementClass == SkillAdvancementClass.Specialized)
                    return true;

                if (AdvancementClass == SkillAdvancementClass.Untrained)
                {
                    DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)Skill, out var skillTableRecord);

                    if (skillTableRecord?.MinLevel == 1)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The amount of experience put into this skill,
        /// from raising directly and earned through use
        /// </summary>
        public uint ExperienceSpent
        {
            get => PropertiesSkill.PP;
            set
            {
                if (PropertiesSkill.PP != value)
                    creature.ChangesDetected = true;

                PropertiesSkill.PP = value;
            }
        }

        /// <summary>
        /// Returns the amount of skill experience remaining
        /// until max rank is reached
        /// </summary>
        public uint ExperienceLeft
        {
            get
            {
                var skillXPTable = Player.GetSkillXPTable(AdvancementClass);
                if (skillXPTable == null)
                    return 0;

                // a player can actually have negative experience remaining,
                // if they had a Trained skill maxed, and then specialized it in skill temple afterwards.

                // (confirmed this is how it was in retail)

                var remainingXP = (long)skillXPTable[skillXPTable.Count - 1] - ExperienceSpent;

                return (uint)Math.Max(0, remainingXP);
            }
        }

        /// <summary>
        /// The number of levels a skill has been raised,
        /// derived from ExperienceSpent
        /// </summary>
        public ushort Ranks
        {
            get => PropertiesSkill.LevelFromPP;
            set
            {
                if (PropertiesSkill.LevelFromPP != value)
                    creature.ChangesDetected = true;

                PropertiesSkill.LevelFromPP = value;
            }
        }

        /// <summary>
        /// Returns TRUE if this skill has been raised the maximum # of times
        /// </summary>
        public bool IsMaxRank
        {
            get
            {
                var skillXPTable = Player.GetSkillXPTable(AdvancementClass);
                if (skillXPTable == null)
                    return false;

                return Ranks >= (skillXPTable.Count - 1);
            }
        }

        public uint Base
        {
            get
            {
                var over = GetCreatureSkillOverride();
                if (over > 0) return over;

                Player player = creature as Player;
                uint total = InitLevel + Ranks;
                if (IsUsable) total += AttributeFormula.GetFormula(creature, Skill, /*current=*/false);
                if (player != null) total += GetAugBonus_Base(player);
                else total += GetCreatureAugBonus_Base();
                return total;
            }
        }

        public uint Current
        {
            get
            {
                var over = GetCreatureSkillOverride();
                if (over > 0) return over;

                Player player = creature as Player;
                uint total = InitLevel + Ranks;
                if (IsUsable) total += AttributeFormula.GetFormula(creature, Skill, /*current=*/true);
                if (player != null) total += GetAugBonus_Base(player);
                else total += GetCreatureAugBonus_Base();

                // apply multiplicative enchantments
                var multiplier = creature.EnchantmentManager.GetSkillMod_Multiplier(Skill);

                var fTotal = total * multiplier;

                if (player != null)
                {
                    var vitae = player.Vitae;

                    if (vitae != 1.0f)
                        fTotal *= vitae;

                    // everything beyond this point does not get scaled by vitae
                    fTotal += GetAugBonus_Current(player);
                }

                var additives = creature.EnchantmentManager.GetSkillMod_Additives(Skill);

                var iTotal = (fTotal + additives).Round();

                iTotal = Math.Max(iTotal, 0);   // skill level cannot be debuffed below 0

                return (uint)iTotal;
            }
        }

        public uint GetAugBonus_Base(Player player)
        {
            // TODO: verify which of these are base, and which are current
            uint total = 0;

            if (player.LumAugAllSkills != 0)
                total += (uint)player.LumAugAllSkills;

            if (player.AugmentationSkilledMelee > 0 && Player.MeleeSkills.Contains(Skill))
                total += (uint)(player.AugmentationSkilledMelee * 10);
            else if (player.AugmentationSkilledMissile > 0 && Player.MissileSkills.Contains(Skill))
                total += (uint)(player.AugmentationSkilledMissile * 10);
            else if (player.AugmentationSkilledMagic > 0 && Player.MagicSkills.Contains(Skill))
                total += (uint)(player.AugmentationSkilledMagic * 10);

            //switch (Skill)
            //{
            //    case Skill.ArmorTinkering:
            //    case Skill.ItemTinkering:
            //    case Skill.MagicItemTinkering:
            //    case Skill.WeaponTinkering:
            //    case Skill.Salvaging:

            //        if (player.LumAugSkilledCraft != 0)
            //            total += (uint)player.LumAugSkilledCraft;
            //        break;
            //}

            if (AdvancementClass >= SkillAdvancementClass.Trained && player.Enlightenment != 0)
                total += (uint)player.Enlightenment;

            return total;
        }

        public uint GetAugBonus_Current(Player player)
        {
            // TODO: verify which of these are base, and which are current
            uint total = 0;

            if (player.AugmentationJackOfAllTrades != 0)
                total += (uint)(player.AugmentationJackOfAllTrades * 5);

            if (AdvancementClass == SkillAdvancementClass.Specialized && player.LumAugSkilledSpec != 0)
                total += (uint)player.LumAugSkilledSpec * 2;

            return total;
        }

        private uint GetCreatureAugBonus_Base()
        {
            uint total = 0;

            // Combat defenses
            if (Skill == Skill.MeleeDefense)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationSkilledMelee) ?? 0) * 10);
            else if (Skill == Skill.MissileDefense)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationSkilledMissile) ?? 0) * 10);

            // Magic schools
            else if (Skill == Skill.CreatureEnchantment)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationInfusedCreatureMagic) ?? 0) * 10);
            else if (Skill == Skill.ItemEnchantment)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationInfusedItemMagic) ?? 0) * 10);
            else if (Skill == Skill.LifeMagic)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationInfusedLifeMagic) ?? 0) * 10);
            else if (Skill == Skill.WarMagic)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationInfusedWarMagic) ?? 0) * 10);
            else if (Skill == Skill.VoidMagic)
                total += (uint)((creature.GetProperty(PropertyInt.AugmentationInfusedVoidMagic) ?? 0) * 10);

            return total;
        }

        private uint GetCreatureSkillOverride()
        {
            if (!InvasionManager.IsActiveBoss(creature))
                return 0;

            var wcid = creature.WeenieClassId;
            switch (Skill)
            {
                case Skill.MeleeDefense:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_melee_def");
                case Skill.MissileDefense:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_missile_def");
                case Skill.MagicDefense:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_magic_def");
                case Skill.WarMagic:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_war");
                case Skill.VoidMagic:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_void");
                case Skill.LifeMagic:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_life");
                case Skill.CreatureEnchantment:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_creature");
                case Skill.ItemEnchantment:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_item");
                case Skill.HeavyWeapons:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_heavy_weapons");
                case Skill.LightWeapons:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_light_weapons");
                case Skill.FinesseWeapons:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_finesse_weapons");
                case Skill.TwoHandedCombat:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_two_handed");
                case Skill.MissileWeapons:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_missile_weapons");
                case Skill.DualWield:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_dual_wield");
                case Skill.Shield:
                    return (uint)InvasionManager.GetBossOverride(wcid, "skill_shield");
                default:
                    return 0;
            }
        }
    }
}
