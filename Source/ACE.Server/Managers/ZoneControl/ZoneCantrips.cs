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
        public const int SkillBonusBase = 50300;      // + (int)Skill => 50300+.. (flat skill, post-vitae)

        public class Def
        {
            public int Key;
            public string Name;
            public string Effect;                          // shown in appraisal + cantrip list
            public (int PropId, int Value)[] Ints;         // int props stamped on the item
            public int ArmorBonus;                         // added straight to the item's own ArmorLevel
        }

        private static (int, int)[] Attr(PropertyAttribute a, int v) => new[] { (AttrBonusBase + (int)a, v) };
        private static (int, int)[] Sk(Skill s, int v) => new[] { (SkillBonusBase + (int)s, v) };
        private static (int, int)[] P(int propId, int v) => new[] { (propId, v) };

        /// <summary>The unique zone cantrip catalog. Keys are stable — they live in saved zone pools.</summary>
        public static readonly SortedDictionary<int, Def> Catalog = new()
        {
            // Empyrean attribute line
            { 1, new Def { Key = 1, Name = "Empyrean Might", Effect = "+400 Strength", Ints = Attr(PropertyAttribute.Strength, 400) } },
            { 2, new Def { Key = 2, Name = "Empyrean Fortitude", Effect = "+400 Endurance", Ints = Attr(PropertyAttribute.Endurance, 400) } },
            { 3, new Def { Key = 3, Name = "Empyrean Grace", Effect = "+400 Coordination", Ints = Attr(PropertyAttribute.Coordination, 400) } },
            { 4, new Def { Key = 4, Name = "Empyrean Celerity", Effect = "+400 Quickness", Ints = Attr(PropertyAttribute.Quickness, 400) } },
            { 5, new Def { Key = 5, Name = "Empyrean Clarity", Effect = "+400 Focus", Ints = Attr(PropertyAttribute.Focus, 400) } },
            { 6, new Def { Key = 6, Name = "Empyrean Dominion", Effect = "+400 Willpower", Ints = Attr(PropertyAttribute.Self, 400) } },
            // masteries (weapon + magic schools + summoning)
            { 7, new Def { Key = 7, Name = "Warlord's Edge", Effect = "+400 Heavy Weapons", Ints = Sk(Skill.HeavyWeapons, 400) } },
            { 8, new Def { Key = 8, Name = "Assassin's Edge", Effect = "+400 Finesse Weapons", Ints = Sk(Skill.FinesseWeapons, 400) } },
            { 9, new Def { Key = 9, Name = "Duelist's Tempo", Effect = "+400 Light Weapons", Ints = Sk(Skill.LightWeapons, 400) } },
            { 10, new Def { Key = 10, Name = "Juggernaut's Rhythm", Effect = "+400 Two Handed Combat", Ints = Sk(Skill.TwoHandedCombat, 400) } },
            { 11, new Def { Key = 11, Name = "Twinblade's Dance", Effect = "+400 Dual Wield", Ints = Sk(Skill.DualWield, 400) } },
            { 12, new Def { Key = 12, Name = "Deadeye's Mark", Effect = "+400 Missile Weapons", Ints = Sk(Skill.MissileWeapons, 400) } },
            { 13, new Def { Key = 13, Name = "Archmage's Authority", Effect = "+400 War Magic", Ints = Sk(Skill.WarMagic, 400) } },
            { 14, new Def { Key = 14, Name = "Voidcaller's Pact", Effect = "+400 Void Magic", Ints = Sk(Skill.VoidMagic, 400) } },
            { 15, new Def { Key = 15, Name = "Lifeweaver's Bond", Effect = "+400 Life Magic", Ints = Sk(Skill.LifeMagic, 400) } },
            { 16, new Def { Key = 16, Name = "Beastcaller's Command", Effect = "+400 Creature Enchantment", Ints = Sk(Skill.CreatureEnchantment, 400) } },
            { 17, new Def { Key = 17, Name = "Artificer's Insight", Effect = "+400 Item Enchantment", Ints = Sk(Skill.ItemEnchantment, 400) } },
            { 18, new Def { Key = 18, Name = "Archon's Covenant", Effect = "+400 Summoning", Ints = Sk(Skill.Summoning, 400) } },
            // vitals
            { 19, new Def { Key = 19, Name = "Heart of the Colossus", Effect = "+300 Max Health", Ints = P((int)PropertyInt.GearMaxHealth, 300) } },
            { 20, new Def { Key = 20, Name = "Boundless Vigor", Effect = "+500 Max Stamina", Ints = P(MaxStaminaBonus, 500) } },
            { 21, new Def { Key = 21, Name = "Sea of Mana", Effect = "+500 Max Mana", Ints = P(MaxManaBonus, 500) } },
            // regen (multiplies with enchantment regen mods - stacks ON TOP of Prodigal Regeneration)
            { 22, new Def { Key = 22, Name = "Wellspring of Souls", Effect = "x25 health regen", Ints = P(HealthRegenAdd, 24) } },
            { 23, new Def { Key = 23, Name = "Marathon's Breath", Effect = "x25 stamina regen", Ints = P(StaminaRegenAdd, 24) } },
            { 24, new Def { Key = 24, Name = "Tides of Ether", Effect = "x25 mana regen", Ints = P(ManaRegenAdd, 24) } },
            // bulwark
            { 25, new Def { Key = 25, Name = "Aegis of the Ancients", Effect = "+300 Armor Level on this piece", ArmorBonus = 300 } },
            { 26, new Def { Key = 26, Name = "Bulwark of Ages", Effect = "+60 Damage Reduction Rating", Ints = P((int)PropertyInt.GearDamageResist, 60) } },
            { 27, new Def { Key = 27, Name = "Deathward", Effect = "+40 Critical Damage Reduction Rating", Ints = P((int)PropertyInt.GearCritDamageResist, 40) } },
            // slaughter
            { 28, new Def { Key = 28, Name = "Wrath of the Fallen", Effect = "+25 Damage Rating", Ints = P((int)PropertyInt.GearDamage, 25) } },
            { 29, new Def { Key = 29, Name = "Executioner's Eye", Effect = "+40 Critical Damage Rating", Ints = P((int)PropertyInt.GearCritDamage, 40) } },
            // whimsy
            { 30, new Def { Key = 30, Name = "Fleet of the Zephyr", Effect = "+400 Run", Ints = Sk(Skill.Run, 400) } },
            { 31, new Def { Key = 31, Name = "Undying Blessing", Effect = "+40 Healing Boost Rating", Ints = P((int)PropertyInt.GearHealingBoost, 40) } },
            { 32, new Def { Key = 32, Name = "Chronomancer's Seal", Effect = "+100 pct spell duration", Ints = P(SpellDurationLevels, 5) } },
        };

        public static bool TryGet(int key, out Def def) => Catalog.TryGetValue(key, out def);

        /// <summary>Stamps a cantrip's props onto a dropped item and marks it in the appraisal long description.</summary>
        public static void Stamp(WorldObject wo, Def def)
        {
            if (wo == null || def == null)
                return;

            if (def.Ints != null)
            {
                foreach (var (propId, value) in def.Ints)
                {
                    var cur = wo.GetProperty((PropertyInt)propId) ?? 0;
                    wo.SetProperty((PropertyInt)propId, cur + value);
                }
            }

            if (def.ArmorBonus != 0 && wo.ArmorLevel.HasValue)
                wo.ArmorLevel = wo.ArmorLevel.Value + def.ArmorBonus;

            var line = $"Zone Cantrip: {def.Name} ({def.Effect})";
            wo.LongDesc = string.IsNullOrEmpty(wo.LongDesc) ? line : wo.LongDesc + "\n\n" + line;
        }
    }
}
