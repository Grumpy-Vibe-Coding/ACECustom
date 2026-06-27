using System.Globalization;
using System.Text;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Live boss tuning for the "Invasion Helper" Decal plugin's Bosses tab.
    ///
    /// All boss stats are backed by <c>invasion_boss_*</c> ServerConfig entries (persist in the
    /// shard DB). Two kinds of stat:
    ///   • Config-read-live — combat reads ServerConfig directly when <c>creature == ActiveBoss</c>
    ///     (the skill_* defenses/attack skills in CreatureSkill.cs, and magic_only in Monster_Combat.cs).
    ///     For these, persisting the config value IS the apply — nothing is written to the boss object.
    ///   • Object-applied — set on the boss WorldObject itself: health/stamina/mana, the six attributes,
    ///     scale, damage rating, the crit/resist ratings, crit freq/mult, the elemental resist mods,
    ///     and the infused-magic augs. Applied at spawn (ApplyBossOverrides) and live on the active boss.
    ///
    /// Protocol (see Docs/invasion-boss-admin.md):
    ///   plugin → /dev invasion bossinfo            → SendBossInfo()  → "[[IHB]]k=v|…" (override + live)
    ///   plugin → /dev invasion bossset &lt;prop&gt; &lt;v&gt;  → SetBossProperty() → persist + live-apply, echo bossinfo
    ///   plugin → /dev invasion bossinfo save_db    → SaveBossOverridesToDb() → flush ServerConfig to DB
    /// </summary>
    public static partial class InvasionManager
    {
        /// <summary>Weenie id of the invasion boss (matches SpawnBoss / PurgeOrphanedEntities).</summary>
        public const uint BossWcid = 72000001;

        /// <summary>Sentinel prefix for boss-admin lines the plugin intercepts + parses (never displayed).</summary>
        public const string BossSyncPrefix = "[[IHB]]";

        // ------------------------------------------------------------------
        // Spawn-time application
        // ------------------------------------------------------------------

        /// <summary>
        /// Apply the object-backed overrides to a freshly created boss BEFORE EnterWorld(), so
        /// scale/health/etc. are correct from the first frame. Config-read-live stats (skills,
        /// magic_only) are intentionally not touched here — combat reads them from ServerConfig.
        /// A 0 / 0.0 override means "use the weenie default" and is skipped.
        /// </summary>
        private static void ApplyBossOverrides(Creature boss)
        {
            if (boss == null) return;

            bool vitalsChanged = false;

            // Attributes (recalc vitals afterward, since vitals derive from them).
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Strength,     ServerConfig.invasion_boss_strength.Value);
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Endurance,    ServerConfig.invasion_boss_endurance.Value);
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Coordination, ServerConfig.invasion_boss_coordination.Value);
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Quickness,    ServerConfig.invasion_boss_quickness.Value);
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Focus,        ServerConfig.invasion_boss_focus.Value);
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Self,         ServerConfig.invasion_boss_self.Value);

            // Vitals (explicit overrides win over the attribute-derived recalc).
            if (ServerConfig.invasion_boss_health.Value > 0)  { boss.Health.StartingValue  = (uint)ServerConfig.invasion_boss_health.Value;  vitalsChanged = true; }
            if (ServerConfig.invasion_boss_stamina.Value > 0) { boss.Stamina.StartingValue = (uint)ServerConfig.invasion_boss_stamina.Value; vitalsChanged = true; }
            if (ServerConfig.invasion_boss_mana.Value > 0)    { boss.Mana.StartingValue    = (uint)ServerConfig.invasion_boss_mana.Value;    vitalsChanged = true; }

            if (vitalsChanged)
                boss.SetMaxVitals();

            // Scale (visual).
            if (ServerConfig.invasion_boss_scale.Value > 0)
                boss.ObjScale = (float)ServerConfig.invasion_boss_scale.Value;

            // Ratings (int).
            ApplyInt(boss, PropertyInt.DamageRating,            ServerConfig.invasion_boss_damage_rating.Value);
            ApplyInt(boss, PropertyInt.DamageResistRating,      ServerConfig.invasion_boss_damage_resist_rating.Value);
            ApplyInt(boss, PropertyInt.CritRating,              ServerConfig.invasion_boss_crit_rating.Value);
            ApplyInt(boss, PropertyInt.CritDamageRating,        ServerConfig.invasion_boss_crit_damage_rating.Value);
            ApplyInt(boss, PropertyInt.CritResistRating,        ServerConfig.invasion_boss_crit_resist_rating.Value);
            ApplyInt(boss, PropertyInt.CritDamageResistRating,  ServerConfig.invasion_boss_crit_damage_resist_rating.Value);
            ApplyInt(boss, PropertyInt.LifeResistRating,        ServerConfig.invasion_boss_life_resist_rating.Value);
            ApplyInt(boss, PropertyInt.DotResistRating,         ServerConfig.invasion_boss_dot_resist_rating.Value);
            ApplyInt(boss, PropertyInt.WeaknessRating,          ServerConfig.invasion_boss_weakness_rating.Value);

            // Infused-magic augmentations (int).
            ApplyInt(boss, PropertyInt.AugmentationInfusedCreatureMagic, ServerConfig.invasion_boss_aug_creature.Value);
            ApplyInt(boss, PropertyInt.AugmentationInfusedItemMagic,     ServerConfig.invasion_boss_aug_item.Value);
            ApplyInt(boss, PropertyInt.AugmentationInfusedLifeMagic,     ServerConfig.invasion_boss_aug_life.Value);
            ApplyInt(boss, PropertyInt.AugmentationInfusedWarMagic,      ServerConfig.invasion_boss_aug_war.Value);
            ApplyInt(boss, PropertyInt.AugmentationInfusedVoidMagic,     ServerConfig.invasion_boss_aug_void.Value);

            // Crit freq/mult (float).
            ApplyFloat(boss, PropertyFloat.CriticalFrequency,  ServerConfig.invasion_boss_crit_frequency.Value);
            ApplyFloat(boss, PropertyFloat.CriticalMultiplier, ServerConfig.invasion_boss_crit_multiplier.Value);

            // Elemental resistance mods (float).
            ApplyFloat(boss, PropertyFloat.ResistSlash,       ServerConfig.invasion_boss_res_slash.Value);
            ApplyFloat(boss, PropertyFloat.ResistPierce,      ServerConfig.invasion_boss_res_pierce.Value);
            ApplyFloat(boss, PropertyFloat.ResistBludgeon,    ServerConfig.invasion_boss_res_bludgeon.Value);
            ApplyFloat(boss, PropertyFloat.ResistFire,        ServerConfig.invasion_boss_res_fire.Value);
            ApplyFloat(boss, PropertyFloat.ResistCold,        ServerConfig.invasion_boss_res_cold.Value);
            ApplyFloat(boss, PropertyFloat.ResistAcid,        ServerConfig.invasion_boss_res_acid.Value);
            ApplyFloat(boss, PropertyFloat.ResistElectric,    ServerConfig.invasion_boss_res_electric.Value);
            ApplyFloat(boss, PropertyFloat.ResistNether,      ServerConfig.invasion_boss_res_nether.Value);
            ApplyFloat(boss, PropertyFloat.ResistHealthDrain, ServerConfig.invasion_boss_res_healthdrain.Value);
        }

        private static bool ApplyAttr(Creature boss, PropertyAttribute attr, long value)
        {
            if (value <= 0 || !boss.Attributes.TryGetValue(attr, out var ca)) return false;
            ca.StartingValue = (uint)value;
            return true;
        }

        private static void ApplyInt(Creature boss, PropertyInt prop, long value)
        {
            if (value != 0) boss.SetProperty(prop, (int)value);
        }

        private static void ApplyFloat(Creature boss, PropertyFloat prop, double value)
        {
            if (value != 0.0) boss.SetProperty(prop, value);
        }

        // ------------------------------------------------------------------
        // /dev invasion bossset <prop> <value>
        // ------------------------------------------------------------------

        /// <summary>
        /// Persist a boss-stat override to ServerConfig and, if a boss is currently spawned, apply
        /// it live. Returns true on a recognized property (message describes the result).
        /// </summary>
        public static bool SetBossProperty(string prop, double value, out string message)
        {
            if (string.IsNullOrWhiteSpace(prop))
            {
                message = "No property specified.";
                return false;
            }

            prop = prop.Trim().ToLowerInvariant();
            var boss = ActiveBoss != null && ActiveBoss.IsAlive ? ActiveBoss : null;
            long lv = (long)value;

            switch (prop)
            {
                // --- attributes (recalc vitals live) ---
                case "strength":     return SetAttr("invasion_boss_strength",     PropertyAttribute.Strength,     lv, boss, out message);
                case "endurance":    return SetAttr("invasion_boss_endurance",    PropertyAttribute.Endurance,    lv, boss, out message);
                case "coordination": return SetAttr("invasion_boss_coordination", PropertyAttribute.Coordination, lv, boss, out message);
                case "quickness":    return SetAttr("invasion_boss_quickness",    PropertyAttribute.Quickness,    lv, boss, out message);
                case "focus":        return SetAttr("invasion_boss_focus",        PropertyAttribute.Focus,        lv, boss, out message);
                case "self":         return SetAttr("invasion_boss_self",         PropertyAttribute.Self,         lv, boss, out message);

                // --- vitals ---
                case "health": return SetVital("invasion_boss_health", boss?.Health, lv, boss, out message);
                case "stamina": return SetVital("invasion_boss_stamina", boss?.Stamina, lv, boss, out message);
                case "mana": return SetVital("invasion_boss_mana", boss?.Mana, lv, boss, out message);

                // --- scale ---
                case "scale":
                    ServerConfig.SetValue("invasion_boss_scale", value);
                    if (boss != null) boss.ObjScale = (float)value;
                    message = $"Boss scale set to {value:0.##}.";
                    return true;

                // --- ratings (object-applied int) ---
                case "damagerating":              return SetIntRating("invasion_boss_damage_rating",             PropertyInt.DamageRating,            lv, boss, out message);
                case "damage_resist_rating":      return SetIntRating("invasion_boss_damage_resist_rating",      PropertyInt.DamageResistRating,      lv, boss, out message);
                case "crit_rating":               return SetIntRating("invasion_boss_crit_rating",               PropertyInt.CritRating,              lv, boss, out message);
                case "crit_damage_rating":        return SetIntRating("invasion_boss_crit_damage_rating",        PropertyInt.CritDamageRating,        lv, boss, out message);
                case "crit_resist_rating":        return SetIntRating("invasion_boss_crit_resist_rating",        PropertyInt.CritResistRating,        lv, boss, out message);
                case "crit_damage_resist_rating": return SetIntRating("invasion_boss_crit_damage_resist_rating", PropertyInt.CritDamageResistRating,  lv, boss, out message);
                case "life_resist_rating":        return SetIntRating("invasion_boss_life_resist_rating",        PropertyInt.LifeResistRating,        lv, boss, out message);
                case "dot_resist_rating":         return SetIntRating("invasion_boss_dot_resist_rating",         PropertyInt.DotResistRating,         lv, boss, out message);
                case "weakness_rating":           return SetIntRating("invasion_boss_weakness_rating",           PropertyInt.WeaknessRating,          lv, boss, out message);

                // --- infused-magic augs (object-applied int) ---
                case "aug_creature": return SetIntRating("invasion_boss_aug_creature", PropertyInt.AugmentationInfusedCreatureMagic, lv, boss, out message);
                case "aug_item":     return SetIntRating("invasion_boss_aug_item",     PropertyInt.AugmentationInfusedItemMagic,     lv, boss, out message);
                case "aug_life":     return SetIntRating("invasion_boss_aug_life",     PropertyInt.AugmentationInfusedLifeMagic,     lv, boss, out message);
                case "aug_war":      return SetIntRating("invasion_boss_aug_war",      PropertyInt.AugmentationInfusedWarMagic,      lv, boss, out message);
                case "aug_void":     return SetIntRating("invasion_boss_aug_void",     PropertyInt.AugmentationInfusedVoidMagic,     lv, boss, out message);
                // Melee/Missile-defense augs have no creature PropertyInt — persist only (reported in bossinfo).
                case "aug_melee":    ServerConfig.SetValue("invasion_boss_aug_melee", lv);   message = $"Boss melee aug set to {lv} (stored).";   return true;
                case "aug_missile":  ServerConfig.SetValue("invasion_boss_aug_missile", lv); message = $"Boss missile aug set to {lv} (stored)."; return true;

                // --- crit freq/mult (object-applied float) ---
                case "crit_frequency":  return SetFloatStat("invasion_boss_crit_frequency",  PropertyFloat.CriticalFrequency,  value, boss, out message);
                case "crit_multiplier": return SetFloatStat("invasion_boss_crit_multiplier", PropertyFloat.CriticalMultiplier, value, boss, out message);

                // --- elemental resist mods (object-applied float) ---
                case "res_slash":      return SetFloatStat("invasion_boss_res_slash",      PropertyFloat.ResistSlash,       value, boss, out message);
                case "res_pierce":     return SetFloatStat("invasion_boss_res_pierce",     PropertyFloat.ResistPierce,      value, boss, out message);
                case "res_bludgeon":   return SetFloatStat("invasion_boss_res_bludgeon",   PropertyFloat.ResistBludgeon,    value, boss, out message);
                case "res_fire":       return SetFloatStat("invasion_boss_res_fire",       PropertyFloat.ResistFire,        value, boss, out message);
                case "res_cold":       return SetFloatStat("invasion_boss_res_cold",       PropertyFloat.ResistCold,        value, boss, out message);
                case "res_acid":       return SetFloatStat("invasion_boss_res_acid",       PropertyFloat.ResistAcid,        value, boss, out message);
                case "res_electric":   return SetFloatStat("invasion_boss_res_electric",   PropertyFloat.ResistElectric,    value, boss, out message);
                case "res_nether":     return SetFloatStat("invasion_boss_res_nether",     PropertyFloat.ResistNether,      value, boss, out message);
                case "res_healthdrain":return SetFloatStat("invasion_boss_res_healthdrain",PropertyFloat.ResistHealthDrain, value, boss, out message);

                // --- magic behavior (config-read-live in combat; no object apply) ---
                case "magic_only":   ServerConfig.SetValue("invasion_boss_magic_only", lv != 0);     message = $"Boss magic-only {(lv != 0 ? "ON" : "OFF")}.";     return true;
                case "infinite_mana":ServerConfig.SetValue("invasion_boss_infinite_mana", lv != 0);  message = $"Boss infinite-mana {(lv != 0 ? "ON" : "OFF")}.";  return true;
                case "magic_delay":  ServerConfig.SetValue("invasion_boss_magic_delay", value);       message = $"Boss magic delay set to {value:0.##}s.";          return true;

                // --- skills (config-read-live in CreatureSkill.cs; no object apply) ---
                case "skill_melee_def":      ServerConfig.SetValue("invasion_boss_skill_melee_def", lv);      message = $"Boss Melee Defense set to {lv}.";      return true;
                case "skill_missile_def":    ServerConfig.SetValue("invasion_boss_skill_missile_def", lv);    message = $"Boss Missile Defense set to {lv}.";    return true;
                case "skill_magic_def":      ServerConfig.SetValue("invasion_boss_skill_magic_def", lv);      message = $"Boss Magic Defense set to {lv}.";      return true;
                case "skill_war":            ServerConfig.SetValue("invasion_boss_skill_war", lv);            message = $"Boss War Magic set to {lv}.";          return true;
                case "skill_void":           ServerConfig.SetValue("invasion_boss_skill_void", lv);           message = $"Boss Void Magic set to {lv}.";         return true;
                case "skill_life":           ServerConfig.SetValue("invasion_boss_skill_life", lv);           message = $"Boss Life Magic set to {lv}.";         return true;
                case "skill_creature":       ServerConfig.SetValue("invasion_boss_skill_creature", lv);       message = $"Boss Creature Magic set to {lv}.";     return true;
                case "skill_item":           ServerConfig.SetValue("invasion_boss_skill_item", lv);           message = $"Boss Item Magic set to {lv}.";         return true;
                case "skill_heavy_weapons":  ServerConfig.SetValue("invasion_boss_skill_heavy_weapons", lv);  message = $"Boss Heavy Weapons set to {lv}.";      return true;
                case "skill_light_weapons":  ServerConfig.SetValue("invasion_boss_skill_light_weapons", lv);  message = $"Boss Light Weapons set to {lv}.";      return true;
                case "skill_finesse_weapons":ServerConfig.SetValue("invasion_boss_skill_finesse_weapons", lv);message = $"Boss Finesse Weapons set to {lv}.";    return true;
                case "skill_two_handed":     ServerConfig.SetValue("invasion_boss_skill_two_handed", lv);     message = $"Boss Two Handed set to {lv}.";         return true;
                case "skill_missile_weapons":ServerConfig.SetValue("invasion_boss_skill_missile_weapons", lv);message = $"Boss Missile Weapons set to {lv}.";    return true;
                case "skill_dual_wield":     ServerConfig.SetValue("invasion_boss_skill_dual_wield", lv);     message = $"Boss Dual Wield set to {lv}.";         return true;
                case "skill_shield":         ServerConfig.SetValue("invasion_boss_skill_shield", lv);         message = $"Boss Shield set to {lv}.";             return true;

                default:
                    message = $"Unknown boss property '{prop}'.";
                    return false;
            }
        }

        private static bool SetAttr(string key, PropertyAttribute attr, long value, Creature boss, out string message)
        {
            ServerConfig.SetValue(key, value);
            if (boss != null && value > 0 && boss.Attributes.TryGetValue(attr, out var ca))
            {
                ca.StartingValue = (uint)value;
                boss.SetMaxVitals();
            }
            message = $"Boss {attr} set to {value}.";
            return true;
        }

        private static bool SetVital(string key, CreatureVital vital, long value, Creature boss, out string message)
        {
            ServerConfig.SetValue(key, value);
            if (boss != null && vital != null && value > 0)
            {
                vital.StartingValue = (uint)value;
                boss.SetMaxVitals();
            }
            message = $"Boss {key.Replace("invasion_boss_", "")} set to {value:N0}.";
            return true;
        }

        private static bool SetIntRating(string key, PropertyInt prop, long value, Creature boss, out string message)
        {
            ServerConfig.SetValue(key, value);
            if (boss != null)
            {
                if (value != 0) boss.SetProperty(prop, (int)value);
                else boss.RemoveProperty(prop);
            }
            message = $"Boss {key.Replace("invasion_boss_", "")} set to {value}.";
            return true;
        }

        private static bool SetFloatStat(string key, PropertyFloat prop, double value, Creature boss, out string message)
        {
            ServerConfig.SetValue(key, value);
            if (boss != null)
            {
                if (value != 0.0) boss.SetProperty(prop, value);
                else boss.RemoveProperty(prop);
            }
            message = $"Boss {key.Replace("invasion_boss_", "")} set to {value:0.####}.";
            return true;
        }

        // ------------------------------------------------------------------
        // /dev invasion bossinfo  → [[IHB]] one-shot to the caller
        // ------------------------------------------------------------------

        /// <summary>
        /// Send a one-shot "[[IHB]]" line to the caller with every override (from ServerConfig) and,
        /// when a boss is spawned, the corresponding live value read off the boss object. Config-driven
        /// stats (skills, magic flags, augs) report their live value as the active config override.
        /// </summary>
        public static void SendBossInfo(Player player)
        {
            if (player?.Session == null) return;

            var boss = ActiveBoss != null && ActiveBoss.IsAlive ? ActiveBoss : null;
            bool act = boss != null;

            var sb = new StringBuilder(900);
            sb.Append(BossSyncPrefix);
            sb.Append("wcid=").Append(BossWcid);
            sb.Append("|act=").Append(act ? 1 : 0);

            // --- overrides (ServerConfig) ---
            OL(sb, "hp",     ServerConfig.invasion_boss_health.Value);
            OD(sb, "scale",  ServerConfig.invasion_boss_scale.Value);
            OL(sb, "dr",     ServerConfig.invasion_boss_damage_rating.Value);
            OL(sb, "str",    ServerConfig.invasion_boss_strength.Value);
            OL(sb, "end",    ServerConfig.invasion_boss_endurance.Value);
            OL(sb, "coord",  ServerConfig.invasion_boss_coordination.Value);
            OL(sb, "quick",  ServerConfig.invasion_boss_quickness.Value);
            OL(sb, "focus",  ServerConfig.invasion_boss_focus.Value);
            OL(sb, "self",   ServerConfig.invasion_boss_self.Value);
            OL(sb, "stam",   ServerConfig.invasion_boss_stamina.Value);
            OL(sb, "mana",   ServerConfig.invasion_boss_mana.Value);
            OD(sb, "cfreq",  ServerConfig.invasion_boss_crit_frequency.Value);
            OD(sb, "cmult",  ServerConfig.invasion_boss_crit_multiplier.Value);
            OD(sb, "rslash", ServerConfig.invasion_boss_res_slash.Value);
            OD(sb, "rpierce",ServerConfig.invasion_boss_res_pierce.Value);
            OD(sb, "rblud",  ServerConfig.invasion_boss_res_bludgeon.Value);
            OD(sb, "rfire",  ServerConfig.invasion_boss_res_fire.Value);
            OD(sb, "rcold",  ServerConfig.invasion_boss_res_cold.Value);
            OD(sb, "racid",  ServerConfig.invasion_boss_res_acid.Value);
            OD(sb, "relec",  ServerConfig.invasion_boss_res_electric.Value);
            OD(sb, "rneth",  ServerConfig.invasion_boss_res_nether.Value);
            OD(sb, "rhdrain",ServerConfig.invasion_boss_res_healthdrain.Value);
            OL(sb, "drr",    ServerConfig.invasion_boss_damage_resist_rating.Value);
            OL(sb, "cr",     ServerConfig.invasion_boss_crit_rating.Value);
            OL(sb, "cdr",    ServerConfig.invasion_boss_crit_damage_rating.Value);
            OL(sb, "crr",    ServerConfig.invasion_boss_crit_resist_rating.Value);
            OL(sb, "cdrr",   ServerConfig.invasion_boss_crit_damage_resist_rating.Value);
            OL(sb, "lrr",    ServerConfig.invasion_boss_life_resist_rating.Value);
            OL(sb, "dotrr",  ServerConfig.invasion_boss_dot_resist_rating.Value);
            OL(sb, "wr",     ServerConfig.invasion_boss_weakness_rating.Value);
            OL(sb, "acreature", ServerConfig.invasion_boss_aug_creature.Value);
            OL(sb, "aitem",     ServerConfig.invasion_boss_aug_item.Value);
            OL(sb, "alife",     ServerConfig.invasion_boss_aug_life.Value);
            OL(sb, "awar",      ServerConfig.invasion_boss_aug_war.Value);
            OL(sb, "avoid",     ServerConfig.invasion_boss_aug_void.Value);
            OL(sb, "amelee",    ServerConfig.invasion_boss_aug_melee.Value);
            OL(sb, "amissile",  ServerConfig.invasion_boss_aug_missile.Value);
            OB(sb, "mo",     ServerConfig.invasion_boss_magic_only.Value);
            OB(sb, "im",     ServerConfig.invasion_boss_infinite_mana.Value);
            OD(sb, "md",     ServerConfig.invasion_boss_magic_delay.Value);
            OL(sb, "smdef",    ServerConfig.invasion_boss_skill_melee_def.Value);
            OL(sb, "smisdef",  ServerConfig.invasion_boss_skill_missile_def.Value);
            OL(sb, "smagdef",  ServerConfig.invasion_boss_skill_magic_def.Value);
            OL(sb, "swar",     ServerConfig.invasion_boss_skill_war.Value);
            OL(sb, "svoid",    ServerConfig.invasion_boss_skill_void.Value);
            OL(sb, "slife",    ServerConfig.invasion_boss_skill_life.Value);
            OL(sb, "screa",    ServerConfig.invasion_boss_skill_creature.Value);
            OL(sb, "sitem",    ServerConfig.invasion_boss_skill_item.Value);
            OL(sb, "sheavy",   ServerConfig.invasion_boss_skill_heavy_weapons.Value);
            OL(sb, "slight",   ServerConfig.invasion_boss_skill_light_weapons.Value);
            OL(sb, "sfinesse", ServerConfig.invasion_boss_skill_finesse_weapons.Value);
            OL(sb, "stwohand", ServerConfig.invasion_boss_skill_two_handed.Value);
            OL(sb, "smisweap", ServerConfig.invasion_boss_skill_missile_weapons.Value);
            OL(sb, "sdualw",   ServerConfig.invasion_boss_skill_dual_wield.Value);
            OL(sb, "sshield",  ServerConfig.invasion_boss_skill_shield.Value);

            // --- live values (only meaningful while a boss is spawned) ---
            if (act)
            {
                OL(sb, "livehpmax", boss.Health?.MaxValue ?? 0);
                OD(sb, "livescale", boss.ObjScale ?? 0);
                OL(sb, "livedr",    boss.GetProperty(PropertyInt.DamageRating) ?? 0);
                OL(sb, "livestr",   AttrBase(boss, PropertyAttribute.Strength));
                OL(sb, "liveend",   AttrBase(boss, PropertyAttribute.Endurance));
                OL(sb, "livecoord", AttrBase(boss, PropertyAttribute.Coordination));
                OL(sb, "livequick", AttrBase(boss, PropertyAttribute.Quickness));
                OL(sb, "livefocus", AttrBase(boss, PropertyAttribute.Focus));
                OL(sb, "liveself",  AttrBase(boss, PropertyAttribute.Self));
                OL(sb, "livestam",  boss.Stamina?.MaxValue ?? 0);
                OL(sb, "livemana",  boss.Mana?.MaxValue ?? 0);
                OD(sb, "livecfreq", boss.GetProperty(PropertyFloat.CriticalFrequency) ?? 0);
                OD(sb, "livecmult", boss.GetProperty(PropertyFloat.CriticalMultiplier) ?? 0);
                OD(sb, "liver_slash",  boss.GetProperty(PropertyFloat.ResistSlash) ?? 0);
                OD(sb, "liver_pierce", boss.GetProperty(PropertyFloat.ResistPierce) ?? 0);
                OD(sb, "liver_blud",   boss.GetProperty(PropertyFloat.ResistBludgeon) ?? 0);
                OD(sb, "liver_fire",   boss.GetProperty(PropertyFloat.ResistFire) ?? 0);
                OD(sb, "liver_cold",   boss.GetProperty(PropertyFloat.ResistCold) ?? 0);
                OD(sb, "liver_acid",   boss.GetProperty(PropertyFloat.ResistAcid) ?? 0);
                OD(sb, "liver_elec",   boss.GetProperty(PropertyFloat.ResistElectric) ?? 0);
                OD(sb, "liver_neth",   boss.GetProperty(PropertyFloat.ResistNether) ?? 0);
                OD(sb, "liver_hdrain", boss.GetProperty(PropertyFloat.ResistHealthDrain) ?? 0);
                OL(sb, "livedrr",  boss.GetProperty(PropertyInt.DamageResistRating) ?? 0);
                OL(sb, "livecr",   boss.GetProperty(PropertyInt.CritRating) ?? 0);
                OL(sb, "livecdr",  boss.GetProperty(PropertyInt.CritDamageRating) ?? 0);
                OL(sb, "livecrr",  boss.GetProperty(PropertyInt.CritResistRating) ?? 0);
                OL(sb, "livecdrr", boss.GetProperty(PropertyInt.CritDamageResistRating) ?? 0);
                OL(sb, "livelrr",  boss.GetProperty(PropertyInt.LifeResistRating) ?? 0);
                OL(sb, "livedotrr",boss.GetProperty(PropertyInt.DotResistRating) ?? 0);
                OL(sb, "livewr",   boss.GetProperty(PropertyInt.WeaknessRating) ?? 0);
                OL(sb, "liveacreature", boss.GetProperty(PropertyInt.AugmentationInfusedCreatureMagic) ?? 0);
                OL(sb, "liveaitem",     boss.GetProperty(PropertyInt.AugmentationInfusedItemMagic) ?? 0);
                OL(sb, "livealife",     boss.GetProperty(PropertyInt.AugmentationInfusedLifeMagic) ?? 0);
                OL(sb, "liveawar",      boss.GetProperty(PropertyInt.AugmentationInfusedWarMagic) ?? 0);
                OL(sb, "liveavoid",     boss.GetProperty(PropertyInt.AugmentationInfusedVoidMagic) ?? 0);
                OL(sb, "liveamelee",    ServerConfig.invasion_boss_aug_melee.Value);
                OL(sb, "liveamissile",  ServerConfig.invasion_boss_aug_missile.Value);
                OB(sb, "livemo", ServerConfig.invasion_boss_magic_only.Value);
                OB(sb, "liveim", ServerConfig.invasion_boss_infinite_mana.Value);
                OD(sb, "livemd", ServerConfig.invasion_boss_magic_delay.Value);
                // Skills are config-read-live for the boss, so the live value IS the override.
                OL(sb, "livesmdef",    ServerConfig.invasion_boss_skill_melee_def.Value);
                OL(sb, "livesmisdef",  ServerConfig.invasion_boss_skill_missile_def.Value);
                OL(sb, "livesmagdef",  ServerConfig.invasion_boss_skill_magic_def.Value);
                OL(sb, "liveswar",     ServerConfig.invasion_boss_skill_war.Value);
                OL(sb, "livesvoid",    ServerConfig.invasion_boss_skill_void.Value);
                OL(sb, "liveslife",    ServerConfig.invasion_boss_skill_life.Value);
                OL(sb, "livescrea",    ServerConfig.invasion_boss_skill_creature.Value);
                OL(sb, "livesitem",    ServerConfig.invasion_boss_skill_item.Value);
                OL(sb, "livesheavy",   ServerConfig.invasion_boss_skill_heavy_weapons.Value);
                OL(sb, "liveslight",   ServerConfig.invasion_boss_skill_light_weapons.Value);
                OL(sb, "livesfinesse", ServerConfig.invasion_boss_skill_finesse_weapons.Value);
                OL(sb, "livestwohand", ServerConfig.invasion_boss_skill_two_handed.Value);
                OL(sb, "livesmisweap", ServerConfig.invasion_boss_skill_missile_weapons.Value);
                OL(sb, "livesdualw",   ServerConfig.invasion_boss_skill_dual_wield.Value);
                OL(sb, "livesshield",  ServerConfig.invasion_boss_skill_shield.Value);
            }

            player.Session.Network.EnqueueSend(new GameMessageSystemChat(sb.ToString(), ChatMessageType.System));
        }

        private static long AttrBase(Creature boss, PropertyAttribute attr)
            => boss.Attributes.TryGetValue(attr, out var ca) ? ca.StartingValue : 0;

        private static void OL(StringBuilder sb, string key, long v) => sb.Append('|').Append(key).Append('=').Append(v);
        private static void OD(StringBuilder sb, string key, double v) => sb.Append('|').Append(key).Append('=').Append(v.ToString("0.####", CultureInfo.InvariantCulture));
        private static void OB(StringBuilder sb, string key, bool v) => sb.Append('|').Append(key).Append('=').Append(v ? 1 : 0);

        // ------------------------------------------------------------------
        // /dev invasion bossinfo save_db
        // ------------------------------------------------------------------

        /// <summary>Flush all pending ServerConfig changes (incl. boss overrides) to the shard DB.</summary>
        public static void SaveBossOverridesToDb(Player player)
        {
            ServerConfig.WriteUpdatesToDb();
            player?.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                "[Invasion] Boss overrides saved to database.", ChatMessageType.System));
        }
    }
}
