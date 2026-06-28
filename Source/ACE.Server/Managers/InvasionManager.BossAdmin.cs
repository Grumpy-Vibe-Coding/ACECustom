using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;
using ACE.Server.Network.GameMessages.Messages;
using Newtonsoft.Json;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Per-boss tuning for the "Invasion Helper" Decal plugin's Bosses tab.
    ///
    /// Overrides are stored PER WCID in <see cref="_bossOverrides"/> (wcid → property-key → value)
    /// and persisted as JSON in the <c>invasion_boss_overrides_json</c> ServerConfig string, so each
    /// boss is tuned independently and the existing "Save to DB" flow still works. The legacy global
    /// <c>invasion_boss_*</c> values are migrated to the golem (<see cref="BossWcid"/>) on first load.
    ///
    /// Two kinds of stat:
    ///   • Config-read-live — combat reads the per-WCID override directly when the creature is an
    ///     active boss (the skill_* defenses/attack skills in CreatureSkill.cs, magic_only in
    ///     Monster_Combat.cs). Persisting the override IS the apply — nothing is written to the object.
    ///   • Object-applied — set on the boss WorldObject itself: health/stamina/mana, the six attributes,
    ///     scale, ratings, crit freq/mult, elemental resist mods, infused-magic augs. Applied at spawn
    ///     (ApplyBossOverrides) and live on any spawned boss of that WCID.
    ///
    /// Protocol (see Docs/invasion-boss-admin.md):
    ///   plugin → /dev invasion bossinfo &lt;wcid&gt;          → SendBossInfo(wcid) → "[[IHB]]k=v|…"
    ///   plugin → /dev invasion bossset &lt;wcid&gt; &lt;prop&gt; &lt;v&gt; → SetBossProperty() → persist + live-apply, echo
    ///   plugin → /dev invasion bossinfo save_db        → SaveBossOverridesToDb() → flush to DB
    /// </summary>
    public static partial class InvasionManager
    {
        /// <summary>Weenie id of the original invasion boss (golem) — default for commands with no wcid,
        /// and the migration target for legacy global overrides.</summary>
        public const uint BossWcid = 72000001;

        /// <summary>Sentinel prefix for boss-admin lines the plugin intercepts + parses (never displayed).</summary>
        public const string BossSyncPrefix = "[[IHB]]";

        // ------------------------------------------------------------------
        // Per-WCID override store + persistence
        // ------------------------------------------------------------------

        // wcid → (canonical property key → value). Keys match the /dev invasion bossset names.
        private static readonly Dictionary<uint, Dictionary<string, double>> _bossOverrides = new();
        private static readonly object _ovLock = new object();
        private static bool _bossOverridesLoaded = false;

        public const string BossOverridesConfigKey = "invasion_boss_overrides_json";

        /// <summary>Current override value for (wcid, key), or 0 if none. Safe to call from combat.</summary>
        public static double GetBossOverride(uint wcid, string key)
        {
            EnsureBossOverridesLoaded();
            lock (_ovLock)
            {
                if (_bossOverrides.TryGetValue(wcid, out var m) && m.TryGetValue(key, out var v))
                    return v;
            }
            return 0.0;
        }

        /// <summary>Set/clear an override for (wcid, key) and persist the whole map to ServerConfig.
        /// A value of 0 removes the override (= "use weenie default").</summary>
        private static void SetBossOverrideValue(uint wcid, string key, double value)
        {
            EnsureBossOverridesLoaded();
            lock (_ovLock)
            {
                if (!_bossOverrides.TryGetValue(wcid, out var m))
                {
                    if (value == 0.0) return;
                    m = new Dictionary<string, double>();
                    _bossOverrides[wcid] = m;
                }
                if (value == 0.0) m.Remove(key);
                else m[key] = value;
                if (m.Count == 0) _bossOverrides.Remove(wcid);
            }
            PersistBossOverrides();
        }

        private static void PersistBossOverrides()
        {
            string json;
            lock (_ovLock) json = JsonConvert.SerializeObject(_bossOverrides);
            ServerConfig.SetValue(BossOverridesConfigKey, json);
        }

        public static void EnsureBossOverridesLoaded()
        {
            if (_bossOverridesLoaded) return;
            LoadBossOverrides();
        }

        /// <summary>Deserialize the per-WCID overrides from ServerConfig at startup. If none are stored
        /// yet, migrate the legacy global invasion_boss_* values onto the golem so existing tuning is
        /// preserved.</summary>
        public static void LoadBossOverrides()
        {
            lock (_ovLock)
            {
                if (_bossOverridesLoaded) return;
                _bossOverridesLoaded = true;
                _bossOverrides.Clear();

                var json = ServerConfig.invasion_boss_overrides_json.Value;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var parsed = JsonConvert.DeserializeObject<Dictionary<uint, Dictionary<string, double>>>(json);
                        if (parsed != null)
                            foreach (var kv in parsed)
                                if (kv.Value != null && kv.Value.Count > 0)
                                    _bossOverrides[kv.Key] = kv.Value;
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"[Invasion] Failed to parse {BossOverridesConfigKey}: {ex.Message}");
                    }
                }
                else
                {
                    MigrateLegacyGlobalOverrides();
                }
            }
        }

        /// <summary>One-time migration: copy any non-default legacy global invasion_boss_* values onto
        /// the golem's per-WCID entry, then persist. Called only when no JSON store exists yet.</summary>
        private static void MigrateLegacyGlobalOverrides()
        {
            var m = new Dictionary<string, double>();
            void add(string key, double v) { if (v != 0.0) m[key] = v; }

            add("strength",     ServerConfig.invasion_boss_strength.Value);
            add("endurance",    ServerConfig.invasion_boss_endurance.Value);
            add("coordination", ServerConfig.invasion_boss_coordination.Value);
            add("quickness",    ServerConfig.invasion_boss_quickness.Value);
            add("focus",        ServerConfig.invasion_boss_focus.Value);
            add("self",         ServerConfig.invasion_boss_self.Value);
            add("health",       ServerConfig.invasion_boss_health.Value);
            add("stamina",      ServerConfig.invasion_boss_stamina.Value);
            add("mana",         ServerConfig.invasion_boss_mana.Value);
            add("scale",        ServerConfig.invasion_boss_scale.Value);
            add("damagerating",                ServerConfig.invasion_boss_damage_rating.Value);
            add("damage_resist_rating",        ServerConfig.invasion_boss_damage_resist_rating.Value);
            add("crit_rating",                 ServerConfig.invasion_boss_crit_rating.Value);
            add("crit_damage_rating",          ServerConfig.invasion_boss_crit_damage_rating.Value);
            add("crit_resist_rating",          ServerConfig.invasion_boss_crit_resist_rating.Value);
            add("crit_damage_resist_rating",   ServerConfig.invasion_boss_crit_damage_resist_rating.Value);
            add("life_resist_rating",          ServerConfig.invasion_boss_life_resist_rating.Value);
            add("dot_resist_rating",           ServerConfig.invasion_boss_dot_resist_rating.Value);
            add("weakness_rating",             ServerConfig.invasion_boss_weakness_rating.Value);
            add("aug_creature", ServerConfig.invasion_boss_aug_creature.Value);
            add("aug_item",     ServerConfig.invasion_boss_aug_item.Value);
            add("aug_life",     ServerConfig.invasion_boss_aug_life.Value);
            add("aug_war",      ServerConfig.invasion_boss_aug_war.Value);
            add("aug_void",     ServerConfig.invasion_boss_aug_void.Value);
            add("aug_melee",    ServerConfig.invasion_boss_aug_melee.Value);
            add("aug_missile",  ServerConfig.invasion_boss_aug_missile.Value);
            add("crit_frequency",  ServerConfig.invasion_boss_crit_frequency.Value);
            add("crit_multiplier", ServerConfig.invasion_boss_crit_multiplier.Value);
            add("res_slash",      ServerConfig.invasion_boss_res_slash.Value);
            add("res_pierce",     ServerConfig.invasion_boss_res_pierce.Value);
            add("res_bludgeon",   ServerConfig.invasion_boss_res_bludgeon.Value);
            add("res_fire",       ServerConfig.invasion_boss_res_fire.Value);
            add("res_cold",       ServerConfig.invasion_boss_res_cold.Value);
            add("res_acid",       ServerConfig.invasion_boss_res_acid.Value);
            add("res_electric",   ServerConfig.invasion_boss_res_electric.Value);
            add("res_nether",     ServerConfig.invasion_boss_res_nether.Value);
            add("res_healthdrain", ServerConfig.invasion_boss_res_healthdrain.Value);
            if (ServerConfig.invasion_boss_magic_only.Value)    m["magic_only"] = 1;
            if (ServerConfig.invasion_boss_infinite_mana.Value) m["infinite_mana"] = 1;
            // magic_delay default is 3.0 — only migrate a non-default value.
            if (ServerConfig.invasion_boss_magic_delay.Value != 3.0)
                add("magic_delay", ServerConfig.invasion_boss_magic_delay.Value);
            add("skill_melee_def",       ServerConfig.invasion_boss_skill_melee_def.Value);
            add("skill_missile_def",     ServerConfig.invasion_boss_skill_missile_def.Value);
            add("skill_magic_def",       ServerConfig.invasion_boss_skill_magic_def.Value);
            add("skill_war",             ServerConfig.invasion_boss_skill_war.Value);
            add("skill_void",            ServerConfig.invasion_boss_skill_void.Value);
            add("skill_life",            ServerConfig.invasion_boss_skill_life.Value);
            add("skill_creature",        ServerConfig.invasion_boss_skill_creature.Value);
            add("skill_item",            ServerConfig.invasion_boss_skill_item.Value);
            add("skill_heavy_weapons",   ServerConfig.invasion_boss_skill_heavy_weapons.Value);
            add("skill_light_weapons",   ServerConfig.invasion_boss_skill_light_weapons.Value);
            add("skill_finesse_weapons", ServerConfig.invasion_boss_skill_finesse_weapons.Value);
            add("skill_two_handed",      ServerConfig.invasion_boss_skill_two_handed.Value);
            add("skill_missile_weapons", ServerConfig.invasion_boss_skill_missile_weapons.Value);
            add("skill_dual_wield",      ServerConfig.invasion_boss_skill_dual_wield.Value);
            add("skill_shield",          ServerConfig.invasion_boss_skill_shield.Value);

            if (m.Count > 0)
            {
                _bossOverrides[BossWcid] = m;
                log.Info($"[Invasion] Migrated {m.Count} legacy global boss override(s) to WCID {BossWcid}.");
                ServerConfig.SetValue(BossOverridesConfigKey, JsonConvert.SerializeObject(_bossOverrides));
            }
        }

        // ------------------------------------------------------------------
        // Spawn-time application
        // ------------------------------------------------------------------

        /// <summary>
        /// Apply the object-backed overrides for this boss's WCID BEFORE EnterWorld(), so
        /// scale/health/etc. are correct from the first frame. Config-read-live stats (skills,
        /// magic_only) are intentionally not touched here — combat reads them per-WCID.
        /// A 0 override means "use the weenie default" and is skipped.
        /// </summary>
        private static void ApplyBossOverrides(Creature boss)
        {
            if (boss == null) return;
            EnsureBossOverridesLoaded();

            var wcid = boss.WeenieClassId;
            double Ov(string key) => GetBossOverride(wcid, key);

            bool vitalsChanged = false;

            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Strength,     (long)Ov("strength"));
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Endurance,    (long)Ov("endurance"));
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Coordination, (long)Ov("coordination"));
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Quickness,    (long)Ov("quickness"));
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Focus,        (long)Ov("focus"));
            vitalsChanged |= ApplyAttr(boss, PropertyAttribute.Self,         (long)Ov("self"));

            var hp = (long)Ov("health"); var stam = (long)Ov("stamina"); var mana = (long)Ov("mana");
            if (hp > 0)   { boss.Health.StartingValue  = (uint)hp;   vitalsChanged = true; }
            if (stam > 0) { boss.Stamina.StartingValue = (uint)stam; vitalsChanged = true; }
            if (mana > 0) { boss.Mana.StartingValue    = (uint)mana; vitalsChanged = true; }

            if (vitalsChanged)
                boss.SetMaxVitals();

            if (Ov("scale") > 0)
                boss.ObjScale = (float)Ov("scale");

            ApplyInt(boss, PropertyInt.DamageRating,            (long)Ov("damagerating"));
            ApplyInt(boss, PropertyInt.DamageResistRating,      (long)Ov("damage_resist_rating"));
            ApplyInt(boss, PropertyInt.CritRating,              (long)Ov("crit_rating"));
            ApplyInt(boss, PropertyInt.CritDamageRating,        (long)Ov("crit_damage_rating"));
            ApplyInt(boss, PropertyInt.CritResistRating,        (long)Ov("crit_resist_rating"));
            ApplyInt(boss, PropertyInt.CritDamageResistRating,  (long)Ov("crit_damage_resist_rating"));
            ApplyInt(boss, PropertyInt.LifeResistRating,        (long)Ov("life_resist_rating"));
            ApplyInt(boss, PropertyInt.DotResistRating,         (long)Ov("dot_resist_rating"));
            ApplyInt(boss, PropertyInt.WeaknessRating,          (long)Ov("weakness_rating"));

            ApplyInt(boss, PropertyInt.AugmentationInfusedCreatureMagic, (long)Ov("aug_creature"));
            ApplyInt(boss, PropertyInt.AugmentationInfusedItemMagic,     (long)Ov("aug_item"));
            ApplyInt(boss, PropertyInt.AugmentationInfusedLifeMagic,     (long)Ov("aug_life"));
            ApplyInt(boss, PropertyInt.AugmentationInfusedWarMagic,      (long)Ov("aug_war"));
            ApplyInt(boss, PropertyInt.AugmentationInfusedVoidMagic,     (long)Ov("aug_void"));

            ApplyFloat(boss, PropertyFloat.CriticalFrequency,  Ov("crit_frequency"));
            ApplyFloat(boss, PropertyFloat.CriticalMultiplier, Ov("crit_multiplier"));

            ApplyFloat(boss, PropertyFloat.ResistSlash,       Ov("res_slash"));
            ApplyFloat(boss, PropertyFloat.ResistPierce,      Ov("res_pierce"));
            ApplyFloat(boss, PropertyFloat.ResistBludgeon,    Ov("res_bludgeon"));
            ApplyFloat(boss, PropertyFloat.ResistFire,        Ov("res_fire"));
            ApplyFloat(boss, PropertyFloat.ResistCold,        Ov("res_cold"));
            ApplyFloat(boss, PropertyFloat.ResistAcid,        Ov("res_acid"));
            ApplyFloat(boss, PropertyFloat.ResistElectric,    Ov("res_electric"));
            ApplyFloat(boss, PropertyFloat.ResistNether,      Ov("res_nether"));
            ApplyFloat(boss, PropertyFloat.ResistHealthDrain, Ov("res_healthdrain"));
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
        // /dev invasion bossset <wcid> <prop> <value>
        // ------------------------------------------------------------------

        /// <summary>
        /// Persist a boss-stat override for <paramref name="wcid"/> and, for any spawned boss of that
        /// WCID, apply it live. Returns true on a recognized property (message describes the result).
        /// </summary>
        public static bool SetBossProperty(uint wcid, string prop, double value, out string message)
        {
            if (string.IsNullOrWhiteSpace(prop))
            {
                message = "No property specified.";
                return false;
            }

            prop = prop.Trim().ToLowerInvariant();
            long lv = (long)value;

            switch (prop)
            {
                // --- attributes (recalc vitals live) ---
                case "strength":     return SetAttr(wcid, "strength",     PropertyAttribute.Strength,     lv, out message);
                case "endurance":    return SetAttr(wcid, "endurance",    PropertyAttribute.Endurance,    lv, out message);
                case "coordination": return SetAttr(wcid, "coordination", PropertyAttribute.Coordination, lv, out message);
                case "quickness":    return SetAttr(wcid, "quickness",    PropertyAttribute.Quickness,    lv, out message);
                case "focus":        return SetAttr(wcid, "focus",        PropertyAttribute.Focus,        lv, out message);
                case "self":         return SetAttr(wcid, "self",         PropertyAttribute.Self,         lv, out message);

                // --- vitals ---
                case "health":  return SetVital(wcid, "health",  b => b.Health,  lv, out message);
                case "stamina": return SetVital(wcid, "stamina", b => b.Stamina, lv, out message);
                case "mana":    return SetVital(wcid, "mana",    b => b.Mana,    lv, out message);

                // --- scale ---
                case "scale":
                    SetBossOverrideValue(wcid, "scale", value);
                    foreach (var b in LiveBossesOfWcid(wcid)) b.ObjScale = (float)value;
                    message = $"Boss scale set to {value:0.##}.";
                    return true;

                // --- ratings (object-applied int) ---
                case "damagerating":              return SetIntRating(wcid, "damagerating",              PropertyInt.DamageRating,            lv, out message);
                case "damage_resist_rating":      return SetIntRating(wcid, "damage_resist_rating",      PropertyInt.DamageResistRating,      lv, out message);
                case "crit_rating":               return SetIntRating(wcid, "crit_rating",               PropertyInt.CritRating,              lv, out message);
                case "crit_damage_rating":        return SetIntRating(wcid, "crit_damage_rating",        PropertyInt.CritDamageRating,        lv, out message);
                case "crit_resist_rating":        return SetIntRating(wcid, "crit_resist_rating",        PropertyInt.CritResistRating,        lv, out message);
                case "crit_damage_resist_rating": return SetIntRating(wcid, "crit_damage_resist_rating", PropertyInt.CritDamageResistRating,  lv, out message);
                case "life_resist_rating":        return SetIntRating(wcid, "life_resist_rating",        PropertyInt.LifeResistRating,        lv, out message);
                case "dot_resist_rating":         return SetIntRating(wcid, "dot_resist_rating",         PropertyInt.DotResistRating,         lv, out message);
                case "weakness_rating":           return SetIntRating(wcid, "weakness_rating",           PropertyInt.WeaknessRating,          lv, out message);

                // --- infused-magic augs (object-applied int) ---
                case "aug_creature": return SetIntRating(wcid, "aug_creature", PropertyInt.AugmentationInfusedCreatureMagic, lv, out message);
                case "aug_item":     return SetIntRating(wcid, "aug_item",     PropertyInt.AugmentationInfusedItemMagic,     lv, out message);
                case "aug_life":     return SetIntRating(wcid, "aug_life",     PropertyInt.AugmentationInfusedLifeMagic,     lv, out message);
                case "aug_war":      return SetIntRating(wcid, "aug_war",      PropertyInt.AugmentationInfusedWarMagic,      lv, out message);
                case "aug_void":     return SetIntRating(wcid, "aug_void",     PropertyInt.AugmentationInfusedVoidMagic,     lv, out message);
                // Melee/Missile-defense augs have no creature PropertyInt — persist only (reported in bossinfo).
                case "aug_melee":    SetBossOverrideValue(wcid, "aug_melee", lv);   message = $"Boss melee aug set to {lv} (stored).";   return true;
                case "aug_missile":  SetBossOverrideValue(wcid, "aug_missile", lv); message = $"Boss missile aug set to {lv} (stored)."; return true;

                // --- crit freq/mult (object-applied float) ---
                case "crit_frequency":  return SetFloatStat(wcid, "crit_frequency",  PropertyFloat.CriticalFrequency,  value, out message);
                case "crit_multiplier": return SetFloatStat(wcid, "crit_multiplier", PropertyFloat.CriticalMultiplier, value, out message);

                // --- elemental resist mods (object-applied float) ---
                case "res_slash":      return SetFloatStat(wcid, "res_slash",      PropertyFloat.ResistSlash,       value, out message);
                case "res_pierce":     return SetFloatStat(wcid, "res_pierce",     PropertyFloat.ResistPierce,      value, out message);
                case "res_bludgeon":   return SetFloatStat(wcid, "res_bludgeon",   PropertyFloat.ResistBludgeon,    value, out message);
                case "res_fire":       return SetFloatStat(wcid, "res_fire",       PropertyFloat.ResistFire,        value, out message);
                case "res_cold":       return SetFloatStat(wcid, "res_cold",       PropertyFloat.ResistCold,        value, out message);
                case "res_acid":       return SetFloatStat(wcid, "res_acid",       PropertyFloat.ResistAcid,        value, out message);
                case "res_electric":   return SetFloatStat(wcid, "res_electric",   PropertyFloat.ResistElectric,    value, out message);
                case "res_nether":     return SetFloatStat(wcid, "res_nether",     PropertyFloat.ResistNether,      value, out message);
                case "res_healthdrain":return SetFloatStat(wcid, "res_healthdrain",PropertyFloat.ResistHealthDrain, value, out message);

                // --- magic behavior (config-read-live in combat; no object apply) ---
                case "magic_only":   SetBossOverrideValue(wcid, "magic_only", lv != 0 ? 1 : 0);     message = $"Boss magic-only {(lv != 0 ? "ON" : "OFF")}.";    return true;
                case "infinite_mana":SetBossOverrideValue(wcid, "infinite_mana", lv != 0 ? 1 : 0);  message = $"Boss infinite-mana {(lv != 0 ? "ON" : "OFF")}."; return true;
                case "magic_delay":  SetBossOverrideValue(wcid, "magic_delay", value);              message = $"Boss magic delay set to {value:0.##}s.";         return true;

                // --- skills (config-read-live in CreatureSkill.cs; no object apply) ---
                case "skill_melee_def":      SetBossOverrideValue(wcid, "skill_melee_def", lv);      message = $"Boss Melee Defense set to {lv}.";   return true;
                case "skill_missile_def":    SetBossOverrideValue(wcid, "skill_missile_def", lv);    message = $"Boss Missile Defense set to {lv}."; return true;
                case "skill_magic_def":      SetBossOverrideValue(wcid, "skill_magic_def", lv);      message = $"Boss Magic Defense set to {lv}.";   return true;
                case "skill_war":            SetBossOverrideValue(wcid, "skill_war", lv);            message = $"Boss War Magic set to {lv}.";       return true;
                case "skill_void":           SetBossOverrideValue(wcid, "skill_void", lv);           message = $"Boss Void Magic set to {lv}.";      return true;
                case "skill_life":           SetBossOverrideValue(wcid, "skill_life", lv);           message = $"Boss Life Magic set to {lv}.";      return true;
                case "skill_creature":       SetBossOverrideValue(wcid, "skill_creature", lv);       message = $"Boss Creature Magic set to {lv}.";  return true;
                case "skill_item":           SetBossOverrideValue(wcid, "skill_item", lv);           message = $"Boss Item Magic set to {lv}.";      return true;
                case "skill_heavy_weapons":  SetBossOverrideValue(wcid, "skill_heavy_weapons", lv);  message = $"Boss Heavy Weapons set to {lv}.";   return true;
                case "skill_light_weapons":  SetBossOverrideValue(wcid, "skill_light_weapons", lv);  message = $"Boss Light Weapons set to {lv}.";   return true;
                case "skill_finesse_weapons":SetBossOverrideValue(wcid, "skill_finesse_weapons", lv);message = $"Boss Finesse Weapons set to {lv}."; return true;
                case "skill_two_handed":     SetBossOverrideValue(wcid, "skill_two_handed", lv);     message = $"Boss Two Handed set to {lv}.";      return true;
                case "skill_missile_weapons":SetBossOverrideValue(wcid, "skill_missile_weapons", lv);message = $"Boss Missile Weapons set to {lv}."; return true;
                case "skill_dual_wield":     SetBossOverrideValue(wcid, "skill_dual_wield", lv);     message = $"Boss Dual Wield set to {lv}.";      return true;
                case "skill_shield":         SetBossOverrideValue(wcid, "skill_shield", lv);         message = $"Boss Shield set to {lv}.";          return true;

                default:
                    message = $"Unknown boss property '{prop}'.";
                    return false;
            }
        }

        /// <summary>The currently-spawned, living invasion bosses (1 for single-boss, N for multi-boss).</summary>
        private static IReadOnlyList<Creature> GetLiveBosses()
            => ActiveObjective?.Bosses ?? Array.Empty<Creature>();

        /// <summary>The spawned bosses matching a specific WCID — the live targets for that WCID's tuning.</summary>
        private static List<Creature> LiveBossesOfWcid(uint wcid)
        {
            var result = new List<Creature>();
            foreach (var b in GetLiveBosses())
                if (b.WeenieClassId == wcid) result.Add(b);
            return result;
        }

        private static bool SetAttr(uint wcid, string key, PropertyAttribute attr, long value, out string message)
        {
            SetBossOverrideValue(wcid, key, value);
            if (value > 0)
                foreach (var boss in LiveBossesOfWcid(wcid))
                    if (boss.Attributes.TryGetValue(attr, out var ca))
                    {
                        ca.StartingValue = (uint)value;
                        boss.SetMaxVitals();
                    }
            message = $"Boss {attr} set to {value}.";
            return true;
        }

        private static bool SetVital(uint wcid, string key, Func<Creature, CreatureVital> vitalOf, long value, out string message)
        {
            SetBossOverrideValue(wcid, key, value);
            if (value > 0)
                foreach (var boss in LiveBossesOfWcid(wcid))
                {
                    var vital = vitalOf(boss);
                    if (vital != null)
                    {
                        vital.StartingValue = (uint)value;
                        boss.SetMaxVitals();
                    }
                }
            message = $"Boss {key} set to {value:N0}.";
            return true;
        }

        private static bool SetIntRating(uint wcid, string key, PropertyInt prop, long value, out string message)
        {
            SetBossOverrideValue(wcid, key, value);
            foreach (var boss in LiveBossesOfWcid(wcid))
            {
                if (value != 0) boss.SetProperty(prop, (int)value);
                else boss.RemoveProperty(prop);
            }
            message = $"Boss {key} set to {value}.";
            return true;
        }

        private static bool SetFloatStat(uint wcid, string key, PropertyFloat prop, double value, out string message)
        {
            SetBossOverrideValue(wcid, key, value);
            foreach (var boss in LiveBossesOfWcid(wcid))
            {
                if (value != 0.0) boss.SetProperty(prop, value);
                else boss.RemoveProperty(prop);
            }
            message = $"Boss {key} set to {value:0.####}.";
            return true;
        }

        // ------------------------------------------------------------------
        // /dev invasion bossinfo <wcid>  → [[IHB]] one-shot to the caller
        // ------------------------------------------------------------------

        /// <summary>
        /// Send a one-shot "[[IHB]]" line for <paramref name="wcid"/>: every override (from the per-WCID
        /// store) plus that boss's real base stats. Base stats come from a spawned boss of this WCID if
        /// one exists, otherwise from a transient creature built from the weenie — so the plugin can fill
        /// every field whether or not a boss is live. Config-driven stats (skills, magic flags, augs)
        /// report their value as the active override.
        /// </summary>
        public static void SendBossInfo(Player player, uint wcid)
        {
            if (player?.Session == null) return;
            EnsureBossOverridesLoaded();

            // Base/live probe: a spawned boss of this WCID if present, else a transient creature from
            // the weenie (not entered into the world; GC-collected) so base stats are still reported.
            Creature live = null;
            foreach (var b in GetLiveBosses())
                if (b.WeenieClassId == wcid) { live = b; break; }

            bool act = live != null;
            Creature probe = live;
            if (probe == null)
            {
                try { probe = WorldObjectFactory.CreateNewWorldObject(wcid) as Creature; }
                catch { probe = null; }
            }

            double Ov(string key) => GetBossOverride(wcid, key);

            var sb = new StringBuilder(1100);
            sb.Append(BossSyncPrefix);
            sb.Append("wcid=").Append(wcid);
            sb.Append("|act=").Append(act ? 1 : 0);

            // --- overrides (per-WCID store) ---
            OL(sb, "hp",     (long)Ov("health"));
            OD(sb, "scale",  Ov("scale"));
            OL(sb, "dr",     (long)Ov("damagerating"));
            OL(sb, "str",    (long)Ov("strength"));
            OL(sb, "end",    (long)Ov("endurance"));
            OL(sb, "coord",  (long)Ov("coordination"));
            OL(sb, "quick",  (long)Ov("quickness"));
            OL(sb, "focus",  (long)Ov("focus"));
            OL(sb, "self",   (long)Ov("self"));
            OL(sb, "stam",   (long)Ov("stamina"));
            OL(sb, "mana",   (long)Ov("mana"));
            OD(sb, "cfreq",  Ov("crit_frequency"));
            OD(sb, "cmult",  Ov("crit_multiplier"));
            OD(sb, "rslash", Ov("res_slash"));
            OD(sb, "rpierce",Ov("res_pierce"));
            OD(sb, "rblud",  Ov("res_bludgeon"));
            OD(sb, "rfire",  Ov("res_fire"));
            OD(sb, "rcold",  Ov("res_cold"));
            OD(sb, "racid",  Ov("res_acid"));
            OD(sb, "relec",  Ov("res_electric"));
            OD(sb, "rneth",  Ov("res_nether"));
            OD(sb, "rhdrain",Ov("res_healthdrain"));
            OL(sb, "drr",    (long)Ov("damage_resist_rating"));
            OL(sb, "cr",     (long)Ov("crit_rating"));
            OL(sb, "cdr",    (long)Ov("crit_damage_rating"));
            OL(sb, "crr",    (long)Ov("crit_resist_rating"));
            OL(sb, "cdrr",   (long)Ov("crit_damage_resist_rating"));
            OL(sb, "lrr",    (long)Ov("life_resist_rating"));
            OL(sb, "dotrr",  (long)Ov("dot_resist_rating"));
            OL(sb, "wr",     (long)Ov("weakness_rating"));
            OL(sb, "acreature", (long)Ov("aug_creature"));
            OL(sb, "aitem",     (long)Ov("aug_item"));
            OL(sb, "alife",     (long)Ov("aug_life"));
            OL(sb, "awar",      (long)Ov("aug_war"));
            OL(sb, "avoid",     (long)Ov("aug_void"));
            OL(sb, "amelee",    (long)Ov("aug_melee"));
            OL(sb, "amissile",  (long)Ov("aug_missile"));
            OB(sb, "mo",     Ov("magic_only") != 0);
            OB(sb, "im",     Ov("infinite_mana") != 0);
            OD(sb, "md",     Ov("magic_delay"));
            OL(sb, "smdef",    (long)Ov("skill_melee_def"));
            OL(sb, "smisdef",  (long)Ov("skill_missile_def"));
            OL(sb, "smagdef",  (long)Ov("skill_magic_def"));
            OL(sb, "swar",     (long)Ov("skill_war"));
            OL(sb, "svoid",    (long)Ov("skill_void"));
            OL(sb, "slife",    (long)Ov("skill_life"));
            OL(sb, "screa",    (long)Ov("skill_creature"));
            OL(sb, "sitem",    (long)Ov("skill_item"));
            OL(sb, "sheavy",   (long)Ov("skill_heavy_weapons"));
            OL(sb, "slight",   (long)Ov("skill_light_weapons"));
            OL(sb, "sfinesse", (long)Ov("skill_finesse_weapons"));
            OL(sb, "stwohand", (long)Ov("skill_two_handed"));
            OL(sb, "smisweap", (long)Ov("skill_missile_weapons"));
            OL(sb, "sdualw",   (long)Ov("skill_dual_wield"));
            OL(sb, "sshield",  (long)Ov("skill_shield"));

            // --- base/live values (from the spawned boss if any, else the weenie probe) ---
            if (probe != null)
            {
                OL(sb, "livehpmax", probe.Health?.MaxValue ?? 0);
                OD(sb, "livescale", probe.ObjScale ?? 0);
                OL(sb, "livedr",    probe.GetProperty(PropertyInt.DamageRating) ?? 0);
                OL(sb, "livestr",   AttrBase(probe, PropertyAttribute.Strength));
                OL(sb, "liveend",   AttrBase(probe, PropertyAttribute.Endurance));
                OL(sb, "livecoord", AttrBase(probe, PropertyAttribute.Coordination));
                OL(sb, "livequick", AttrBase(probe, PropertyAttribute.Quickness));
                OL(sb, "livefocus", AttrBase(probe, PropertyAttribute.Focus));
                OL(sb, "liveself",  AttrBase(probe, PropertyAttribute.Self));
                OL(sb, "livestam",  probe.Stamina?.MaxValue ?? 0);
                OL(sb, "livemana",  probe.Mana?.MaxValue ?? 0);
                OD(sb, "livecfreq", probe.GetProperty(PropertyFloat.CriticalFrequency) ?? 0);
                OD(sb, "livecmult", probe.GetProperty(PropertyFloat.CriticalMultiplier) ?? 0);
                OD(sb, "liver_slash",  probe.GetProperty(PropertyFloat.ResistSlash) ?? 0);
                OD(sb, "liver_pierce", probe.GetProperty(PropertyFloat.ResistPierce) ?? 0);
                OD(sb, "liver_blud",   probe.GetProperty(PropertyFloat.ResistBludgeon) ?? 0);
                OD(sb, "liver_fire",   probe.GetProperty(PropertyFloat.ResistFire) ?? 0);
                OD(sb, "liver_cold",   probe.GetProperty(PropertyFloat.ResistCold) ?? 0);
                OD(sb, "liver_acid",   probe.GetProperty(PropertyFloat.ResistAcid) ?? 0);
                OD(sb, "liver_elec",   probe.GetProperty(PropertyFloat.ResistElectric) ?? 0);
                OD(sb, "liver_neth",   probe.GetProperty(PropertyFloat.ResistNether) ?? 0);
                OD(sb, "liver_hdrain", probe.GetProperty(PropertyFloat.ResistHealthDrain) ?? 0);
                OL(sb, "livedrr",  probe.GetProperty(PropertyInt.DamageResistRating) ?? 0);
                OL(sb, "livecr",   probe.GetProperty(PropertyInt.CritRating) ?? 0);
                OL(sb, "livecdr",  probe.GetProperty(PropertyInt.CritDamageRating) ?? 0);
                OL(sb, "livecrr",  probe.GetProperty(PropertyInt.CritResistRating) ?? 0);
                OL(sb, "livecdrr", probe.GetProperty(PropertyInt.CritDamageResistRating) ?? 0);
                OL(sb, "livelrr",  probe.GetProperty(PropertyInt.LifeResistRating) ?? 0);
                OL(sb, "livedotrr",probe.GetProperty(PropertyInt.DotResistRating) ?? 0);
                OL(sb, "livewr",   probe.GetProperty(PropertyInt.WeaknessRating) ?? 0);
                OL(sb, "liveacreature", probe.GetProperty(PropertyInt.AugmentationInfusedCreatureMagic) ?? 0);
                OL(sb, "liveaitem",     probe.GetProperty(PropertyInt.AugmentationInfusedItemMagic) ?? 0);
                OL(sb, "livealife",     probe.GetProperty(PropertyInt.AugmentationInfusedLifeMagic) ?? 0);
                OL(sb, "liveawar",      probe.GetProperty(PropertyInt.AugmentationInfusedWarMagic) ?? 0);
                OL(sb, "liveavoid",     probe.GetProperty(PropertyInt.AugmentationInfusedVoidMagic) ?? 0);
            }

            // Config-read-live stats: the live value IS the override (regardless of a spawned probe).
            OL(sb, "liveamelee",    (long)Ov("aug_melee"));
            OL(sb, "liveamissile",  (long)Ov("aug_missile"));
            OB(sb, "livemo", Ov("magic_only") != 0);
            OB(sb, "liveim", Ov("infinite_mana") != 0);
            OD(sb, "livemd", Ov("magic_delay"));
            OL(sb, "livesmdef",    (long)Ov("skill_melee_def"));
            OL(sb, "livesmisdef",  (long)Ov("skill_missile_def"));
            OL(sb, "livesmagdef",  (long)Ov("skill_magic_def"));
            OL(sb, "liveswar",     (long)Ov("skill_war"));
            OL(sb, "livesvoid",    (long)Ov("skill_void"));
            OL(sb, "liveslife",    (long)Ov("skill_life"));
            OL(sb, "livescrea",    (long)Ov("skill_creature"));
            OL(sb, "livesitem",    (long)Ov("skill_item"));
            OL(sb, "livesheavy",   (long)Ov("skill_heavy_weapons"));
            OL(sb, "liveslight",   (long)Ov("skill_light_weapons"));
            OL(sb, "livesfinesse", (long)Ov("skill_finesse_weapons"));
            OL(sb, "livestwohand", (long)Ov("skill_two_handed"));
            OL(sb, "livesmisweap", (long)Ov("skill_missile_weapons"));
            OL(sb, "livesdualw",   (long)Ov("skill_dual_wield"));
            OL(sb, "livesshield",  (long)Ov("skill_shield"));

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

        /// <summary>Flush all pending ServerConfig changes (incl. the boss override JSON) to the shard DB.</summary>
        public static void SaveBossOverridesToDb(Player player)
        {
            ServerConfig.WriteUpdatesToDb();
            player?.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                "[Invasion] Boss overrides saved to database.", ChatMessageType.System));
        }
    }
}
