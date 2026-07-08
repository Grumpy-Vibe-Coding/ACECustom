using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;
using Newtonsoft.Json;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneScaling
{
    /// <summary>
    /// Central registry + resolver for the Zone Scaler (T11+ auto-scaling content). Mirrors PrestigeManager:
    /// static, lazily initialized, backed by a single JSON blob in the shard config store (no EF migration),
    /// mutated live by /zonescale + the plugin. Resolution is most-specific-wins
    /// (LandblockVariation &gt; Landblock &gt; Zone &gt; Global).
    ///
    /// PHASE 1: registry, persistence, scope resolution, GetProfile. NOT yet wired into any combat/loot choke
    /// point, so this changes NO behavior on its own — consumers are added in later phases.
    /// </summary>
    public static class ZoneScalingManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Shard config string key holding the serialized profile store.</summary>
        private const string StoreKey = "zonescale_data";

        // scope key -> profile
        private static readonly Dictionary<string, ZoneScalingProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        // zone name -> member landblocks
        private static readonly Dictionary<string, HashSet<ushort>> _zoneMap = new(StringComparer.OrdinalIgnoreCase);
        // reverse: landblock -> zone names (for fast resolution)
        private static readonly Dictionary<ushort, List<string>> _landblockZones = new();
        // memo cache of evaluated bundles, keyed "scopeKey|tier|variant"
        private static readonly Dictionary<string, EvaluatedProfile> _evalCache = new();

        private static readonly object _lock = new object();
        private static volatile bool _initialized;

        /// <summary>JSON container persisted to the shard config store.</summary>
        private class Store
        {
            public List<ZoneScalingProfile> Profiles { get; set; } = new();
            public Dictionary<string, List<int>> ZoneMap { get; set; } = new();
        }

        #region init / persistence

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_lock)
            {
                if (_initialized)
                    return;

                try
                {
                    Load();
                }
                catch (Exception ex)
                {
                    log.Error($"ZoneScalingManager: failed to load store, starting empty. {ex}");
                }

                _initialized = true;
            }
        }

        private static void Load()
        {
            _profiles.Clear();
            _zoneMap.Clear();
            _landblockZones.Clear();
            _evalCache.Clear();

            string json = null;
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                json = DatabaseManager.ShardConfig.GetString(StoreKey)?.Value;

            Store store;
            if (string.IsNullOrWhiteSpace(json))
            {
                store = BuildDefaultStore();
                PersistStore(store);
                log.Info("ZoneScalingManager: seeded default store (global profile + tou_tou zone map).");
            }
            else
            {
                store = JsonConvert.DeserializeObject<Store>(json) ?? new Store();
            }

            foreach (var p in store.Profiles)
            {
                if (p == null) continue;
                _profiles[p.ScopeKey()] = p;
            }

            foreach (var kvp in store.ZoneMap)
            {
                var set = new HashSet<ushort>(kvp.Value.Select(v => (ushort)v));
                _zoneMap[kvp.Key] = set;
            }

            RebuildLandblockZoneIndex();
        }

        private static void Save()
        {
            var store = new Store
            {
                Profiles = _profiles.Values.ToList(),
                ZoneMap = _zoneMap.ToDictionary(k => k.Key, v => v.Value.Select(lb => (int)lb).ToList()),
            };
            PersistStore(store);
            _evalCache.Clear();
        }

        private static void PersistStore(Store store)
        {
            var json = JsonConvert.SerializeObject(store);
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                DatabaseManager.ShardConfig.SaveString(new ConfigPropertiesString { Key = StoreKey, Value = json, Description = "Zone Scaler profile store (JSON)" });
            else
                DatabaseManager.ShardConfig.AddString(StoreKey, json, "Zone Scaler profile store (JSON)");
        }

        private static void RebuildLandblockZoneIndex()
        {
            _landblockZones.Clear();
            foreach (var kvp in _zoneMap)
            {
                foreach (var lb in kvp.Value)
                {
                    if (!_landblockZones.TryGetValue(lb, out var list))
                        _landblockZones[lb] = list = new List<string>();
                    list.Add(kvp.Key);
                }
            }
        }

        #endregion

        #region default seed

        /// <summary>
        /// Seeds a behavior-neutral default store: a single Global profile whose numbers mirror the currently
        /// deployed v11_* knobs + per-weenie chassis, plus the tou_tou named zone (landblocks 0xF156-0xF95F).
        /// Because no consumer reads the profile yet (Phase 1), these values only take effect once the combat/loot
        /// choke points are wired to it in later phases.
        /// </summary>
        private static Store BuildDefaultStore()
        {
            var store = new Store();

            var global = new ZoneScalingProfile
            {
                ScopeType = ZoneScopeType.Global,
                // Seeded DISABLED so wiring the consumers is behavior-neutral: with nothing authored, GetProfile
                // returns null and mobs keep their weenie stats + the existing v11_* knob behavior. Enable this
                // (or author a more specific scope) during the migration phase to start driving mobs from profiles.
                Enabled = false,
                Notes = "Auto-seeded from v11_* defaults (DISABLED). Baseline curve for endgame mobs not covered by a more specific scope; enable to activate.",
            };

            double hpGrowth = SafeCfg(() => ServerConfig.v11_tier_hp_growth.Value, 1.25);
            double pctBase  = SafeCfg(() => ServerConfig.v11_pcthp_base.Value, 0.05);
            double pctGrow  = SafeCfg(() => ServerConfig.v11_pcthp_tier_growth.Value, 1.22);
            double dtMult   = SafeCfg(() => ServerConfig.v11_mob_dmg_taken_mult.Value, 0.25);
            double dtBoss   = SafeCfg(() => ServerConfig.v11_mob_dmg_taken_boss_mult.Value, 0.60);
            double vulnCap  = SafeCfg(() => ServerConfig.v11_vuln_cap.Value, 1.5);

            // Minion variant (mirrors 730000203 chassis + 730001010 HP + knobs)
            SetCurve(global.Minion, ZoneStat.MaxHealth,          50_000_000, hpGrowth, false);
            SetCurve(global.Minion, ZoneStat.AttackSkill,        75_000,     1.0, false);
            SetCurve(global.Minion, ZoneStat.MeleeDefense,       100,        1.0, false);
            SetCurve(global.Minion, ZoneStat.MissileDefense,     100,        1.0, false);
            SetCurve(global.Minion, ZoneStat.MagicDefense,       100,        1.0, false);
            SetCurve(global.Minion, ZoneStat.DamageRating,       10_000,     1.0, false);
            SetCurve(global.Minion, ZoneStat.DamageResistRating, 10_000,     1.0, false);
            SetCurve(global.Minion, ZoneStat.DamageTakenMult,    dtMult,     1.0, false);
            SetCurve(global.Minion, ZoneStat.VulnCap,            vulnCap,    1.0, false);
            SetCurve(global.Minion, ZoneStat.PercentHpBase,      pctBase,    pctGrow, false);
            // loot defaults = neutral (no scaling until authored)
            SetCurve(global.Minion, ZoneStat.LootTierBonus,      0,   1.0, true);
            SetCurve(global.Minion, ZoneStat.LootQuantityMult,   1.0, 1.0, false);
            SetCurve(global.Minion, ZoneStat.RareChanceMult,     1.0, 1.0, false);
            SetCurve(global.Minion, ZoneStat.BonusCurrency,      0,   1.0, true);

            // Boss variant = minion baseline with the boss deltas the current systems apply
            SetCurve(global.Boss, ZoneStat.MaxHealth,          120_000_000,      hpGrowth, false);
            SetCurve(global.Boss, ZoneStat.AttackSkill,        75_000,           1.0, false);
            SetCurve(global.Boss, ZoneStat.MeleeDefense,       100,              1.0, false);
            SetCurve(global.Boss, ZoneStat.MissileDefense,     100,              1.0, false);
            SetCurve(global.Boss, ZoneStat.MagicDefense,       100,              1.0, false);
            SetCurve(global.Boss, ZoneStat.DamageRating,       10_000,           1.0, false);
            SetCurve(global.Boss, ZoneStat.DamageResistRating, 10_000,           1.0, false);
            SetCurve(global.Boss, ZoneStat.DamageTakenMult,    dtMult * dtBoss,  1.0, false);
            SetCurve(global.Boss, ZoneStat.VulnCap,            vulnCap,          1.0, false);
            SetCurve(global.Boss, ZoneStat.PercentHpBase,      pctBase,          pctGrow, false);
            SetCurve(global.Boss, ZoneStat.LootTierBonus,      0,   1.0, true);
            SetCurve(global.Boss, ZoneStat.LootQuantityMult,   1.0, 1.0, false);
            SetCurve(global.Boss, ZoneStat.RareChanceMult,     1.0, 1.0, false);
            SetCurve(global.Boss, ZoneStat.BonusCurrency,      0,   1.0, true);

            store.Profiles.Add(global);

            // tou_tou named zone = grid 0xF156-0xF95F (F1..F9 x 56..5F)
            var touTou = new List<int>();
            for (int hi = 0xF1; hi <= 0xF9; hi++)
                for (int lo = 0x56; lo <= 0x5F; lo++)
                    touTou.Add((hi << 8) | lo);
            store.ZoneMap["tou_tou"] = touTou;

            return store;
        }

        private static double SafeCfg(Func<double> getter, double fallback)
        {
            try { return getter(); } catch { return fallback; }
        }

        private static void SetCurve(ZoneVariantProfile variant, string stat, double baseVal, double growth, bool additive)
        {
            variant.Stats[stat] = new StatCurve { Base = baseVal, Growth = growth, Additive = additive };
        }

        #endregion

        #region resolution / public API

        /// <summary>
        /// Resolves the winning zone profile for a creature and evaluates it at the creature's tier + variant.
        /// Returns null when the creature should NOT be zone-scaled: it's a player, it's exempt
        /// (PropertyBool.ExemptFromZoneScaling), it's not in an endgame tier, or no enabled profile matches.
        /// </summary>
        public static EvaluatedProfile GetProfile(Creature creature)
        {
            if (creature == null || creature is Player)
                return null;

            if (creature.GetProperty(PropertyBool.ExemptFromZoneScaling) == true)
                return null;

            var tier = PrestigeManager.GetTier(PrestigeManager.GetEffectiveVariation(creature));
            if (tier <= 0)
                return null;

            EnsureInitialized();

            var landblock = creature.Location?.LandblockId.Landblock ?? 0;
            var variation = PrestigeManager.GetEffectiveVariation(creature);
            var variant = creature.GetProperty(PropertyBool.IsEmpowerSource) == true ? ZoneVariant.Boss : ZoneVariant.Minion;

            var profile = ResolveProfile(landblock, variation);
            if (profile == null || !profile.Enabled)
                return null;

            return Evaluate(profile, tier, variant);
        }

        /// <summary>Highest-precedence enabled profile matching (landblock, variation): lbvar &gt; lb &gt; zone &gt; global.</summary>
        private static ZoneScalingProfile ResolveProfile(ushort landblock, int variation)
        {
            lock (_lock)
            {
                if (TryGetEnabled(ZoneScalingProfile.MakeScopeKey(ZoneScopeType.LandblockVariation, landblock, variation, null), out var p))
                    return p;

                if (TryGetEnabled(ZoneScalingProfile.MakeScopeKey(ZoneScopeType.Landblock, landblock, null, null), out p))
                    return p;

                if (_landblockZones.TryGetValue(landblock, out var zones))
                {
                    foreach (var zoneName in zones)
                    {
                        if (TryGetEnabled(ZoneScalingProfile.MakeScopeKey(ZoneScopeType.Zone, null, null, zoneName), out p))
                            return p;
                    }
                }

                if (TryGetEnabled("global", out p))
                    return p;

                return null;
            }
        }

        private static bool TryGetEnabled(string scopeKey, out ZoneScalingProfile profile)
        {
            if (_profiles.TryGetValue(scopeKey, out profile) && profile.Enabled)
                return true;
            profile = null;
            return false;
        }

        private static EvaluatedProfile Evaluate(ZoneScalingProfile profile, int tier, ZoneVariant variant)
        {
            var cacheKey = profile.ScopeKey() + "|" + tier + "|" + (int)variant;

            lock (_lock)
            {
                if (_evalCache.TryGetValue(cacheKey, out var cached))
                    return cached;

                var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in profile.Variant(variant).Stats)
                    values[kvp.Key] = kvp.Value.Evaluate(tier);

                var eval = new EvaluatedProfile(profile.ScopeKey(), tier, variant, values);
                _evalCache[cacheKey] = eval;
                return eval;
            }
        }

        #endregion

        #region mutation (used by /zonescale + plugin in later phases)

        /// <summary>Add or replace a profile (by scope key) and persist.</summary>
        public static void UpsertProfile(ZoneScalingProfile profile)
        {
            EnsureInitialized();
            lock (_lock)
            {
                _profiles[profile.ScopeKey()] = profile;
                Save();
            }
        }

        public static bool RemoveProfile(string scopeKey)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var removed = _profiles.Remove(scopeKey);
                if (removed) Save();
                return removed;
            }
        }

        public static ZoneScalingProfile GetProfileByScope(string scopeKey)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _profiles.TryGetValue(scopeKey, out var p) ? p : null;
            }
        }

        public static IReadOnlyList<ZoneScalingProfile> ListProfiles()
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _profiles.Values.ToList();
            }
        }

        public static void AddZoneLandblock(string zoneName, ushort landblock)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (!_zoneMap.TryGetValue(zoneName, out var set))
                    _zoneMap[zoneName] = set = new HashSet<ushort>();
                set.Add(landblock);
                RebuildLandblockZoneIndex();
                Save();
            }
        }

        public static void RemoveZoneLandblock(string zoneName, ushort landblock)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (_zoneMap.TryGetValue(zoneName, out var set))
                {
                    set.Remove(landblock);
                    RebuildLandblockZoneIndex();
                    Save();
                }
            }
        }

        /// <summary>Force a reload from the shard store (e.g. after out-of-band edits).</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _initialized = false;
                EnsureInitialized();
            }
        }

        #endregion
    }
}
