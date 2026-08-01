using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using log4net;

using Newtonsoft.Json;

using ACE.Database;
using ACE.Database.Models.Shard;

namespace ACE.Server.Managers.WeaponScaling
{
    /// <summary>One tier band: the aug count the scaling term stops growing at, and the item-aug
    /// floor required to wield the tier's weapons (the market-segmentation gate — NOT power gating;
    /// power is self-gated by the per-wielder scaling itself).</summary>
    public class WeaponScalingTier
    {
        public int Tier { get; set; }
        public int Cap { get; set; }
        public int MinWieldAugs { get; set; }
    }

    /// <summary>Per-loot-script k roll range. A weapon's stored QUALITY (0-1000) lerps between
    /// KMin and KMax at swing time, so retuning the range re-prices every existing drop.</summary>
    public class WeaponScalingScript
    {
        public double KMin { get; set; }
        public double KMax { get; set; }
    }

    public class WeaponScalingConfig
    {
        public bool Enabled { get; set; }

        public List<WeaponScalingTier> Tiers { get; set; } = new();

        public Dictionary<string, WeaponScalingScript> Scripts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public double KcMin { get; set; }
        public double KcMax { get; set; }
    }

    /// <summary>
    /// Weapon aug-scaling config: the plugin-tunable knobs for the T11+ weapon relevance system
    /// (plan: C:\AI\ZoneControl\T11_WeaponRelevance_Plan_2026-07-31.md).
    ///
    /// Weapons store a QUALITY percentile + tier; the MEANING (k ranges, tier caps, wield floors)
    /// lives here, resolved at swing time — so every knob edit retroactively re-prices existing
    /// drops. Persisted as a single JSON blob in shard config (same pattern as zonecontrol_data).
    ///
    /// This manager is deploy-cold: nothing reads it in combat until the wire-in ships, and the
    /// master Enabled flag (default OFF) gates that wire-in when it does.
    /// </summary>
    public static class WeaponScalingManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const string StoreKey = "weaponscaling_data";

        /// <summary>Quality rolls span 0..1000 (permille, integer-friendly for the old client wire).</summary>
        public const int QualityMax = 1000;

        private static readonly object _lock = new object();
        private static volatile bool _initialized;

        // Immutable-by-convention snapshot: mutations clone + swap; readers never see a torn config.
        private static volatile WeaponScalingConfig _current = BuildDefaults();

        /// <summary>Current config snapshot. Treat as read-only — all edits go through <see cref="Mutate"/>.</summary>
        public static WeaponScalingConfig Current
        {
            get { Initialize(); return _current; }
        }

        public static void Initialize()
        {
            if (_initialized)
                return;

            lock (_lock)
            {
                if (_initialized)
                    return;

                try { Load(); }
                catch (Exception ex) { log.Error($"WeaponScalingManager: failed to load store, using defaults. {ex}"); }

                _initialized = true;
            }
        }

        /// <summary>Locked launch defaults (plan §4, owner 2026-07-31): k 0.90-1.15 baseline with
        /// per-script ratios, kc 0.60-0.80, caps T11=2500 +500/tier, minWieldAugs = previous tier's
        /// cap (T11 exempt). Enabled = FALSE — the system is inert until deliberately switched on.
        /// Script keys follow the T11 loot-script names; step 3 (loot stamping) must use the same keys.</summary>
        public static WeaponScalingConfig BuildDefaults()
        {
            var cfg = new WeaponScalingConfig
            {
                Enabled = false,
                KcMin = 0.60,
                KcMax = 0.80,
            };

            // Tiers T11..T25: cap = 2500 + 500 * (tier - 11); minWieldAugs = previous tier's cap.
            // T11's floor is the PRE-EXISTING live gate (2,000 item augs, owner 2026-07-20,
            // LootGenerationFactory.ZoneLootSetWieldItemAugs) — not 0: every T11+ drop already
            // requires it, and ApplyT11WieldRequirement now reads this table per tier.
            for (var tier = 11; tier <= 25; tier++)
            {
                cfg.Tiers.Add(new WeaponScalingTier
                {
                    Tier = tier,
                    Cap = 2500 + 500 * (tier - 11),
                    MinWieldAugs = tier == 11 ? 2000 : 2500 + 500 * (tier - 12),
                });
            }

            // One key per weapon FAMILY, weight subtypes merged (owner 2026-08-01). k ranges are
            // EQUAL WITHIN MECHANICS GROUPS (owner, same day): all weapons speed-cap at endgame
            // (item-aug Alacrity aura -1 time/aug, WeaponTime floors at 0 in
            // WorldObject_Weapon.GetWeaponSpeed), so authored per-family damage differences would just
            // crown a best-in-slot — k encodes MECHANICS, not weapon identity. The discount rule
            // follows what the weapon GIVES UP (owner, final):
            //  - multi-strike gives up nothing (longer animation only) -> k discounted by strike count
            //    for per-swing weapon-term parity with singles;
            //  - TWO-HANDED gives up a SHIELD (AL + block + a gem/cantrip slot) -> NO discount: k
            //    equals singles per hit, so both strikes carry it and every damage term doubles per
            //    swing — the +100% weapon-term premium IS the shield compensation.
            // Step-3 mapping: damage mutation file -> family key (heavy_X / light_finesse_X -> X;
            // *_ms own rows; two_handed_cleaver -> cleaver; jitte -> mace; bow/xbow/atlatl elem +
            // non-elem -> launcher key). CASTER rows are seeded for authoring but INERT until the
            // caster wire-in ships (parity audit pending — caster damage rides ElementalDamageMod).
            foreach (var single in new[] { "sword", "axe", "dagger", "mace", "spear", "staff", "unarmed",
                                           "cleaver", "two_handed_spear",
                                           "bow", "crossbow", "atlatl",
                                           "caster_elemental", "caster_non_elemental" })
                cfg.Scripts[single] = new WeaponScalingScript { KMin = 0.90, KMax = 1.15 };
            foreach (var ms in new[] { "sword_ms", "dagger_ms" })
                cfg.Scripts[ms] = new WeaponScalingScript { KMin = 0.40, KMax = 0.51 };

            return cfg;
        }

        private static void Load()
        {
            string json = null;
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                json = DatabaseManager.ShardConfig.GetString(StoreKey)?.Value;

            if (string.IsNullOrWhiteSpace(json))
            {
                _current = BuildDefaults();
                return;
            }

            var cfg = JsonConvert.DeserializeObject<WeaponScalingConfig>(json);
            _current = cfg != null ? Normalize(cfg) : BuildDefaults();
        }

        /// <summary>Apply an edit to a CLONE of the current config, persist it, and publish the clone.
        /// Combat effect (once wired in) is instant — same synchronous Save->swap shape as ZoneControlManager.</summary>
        public static void Mutate(Action<WeaponScalingConfig> edit)
        {
            Initialize();
            lock (_lock)
            {
                var clone = Clone(_current);
                edit(clone);
                Normalize(clone);
                Save(clone);
                _current = clone;
            }
        }

        /// <summary>Discard in-memory state and re-read the store (or defaults when absent).</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                Load();
                _initialized = true;
            }
        }

        private static WeaponScalingConfig Clone(WeaponScalingConfig cfg)
        {
            return JsonConvert.DeserializeObject<WeaponScalingConfig>(JsonConvert.SerializeObject(cfg));
        }

        /// <summary>Deserialized dictionaries lose the case-insensitive comparer, and hand-edited
        /// values can arrive inverted or negative — repair rather than reject.</summary>
        public static WeaponScalingConfig Normalize(WeaponScalingConfig cfg)
        {
            cfg.Tiers ??= new List<WeaponScalingTier>();
            cfg.Tiers.RemoveAll(t => t == null);
            cfg.Tiers = cfg.Tiers.GroupBy(t => t.Tier).Select(g => g.First()).OrderBy(t => t.Tier).ToList();
            foreach (var t in cfg.Tiers)
            {
                t.Cap = Math.Max(0, t.Cap);
                t.MinWieldAugs = Math.Max(0, t.MinWieldAugs);
            }

            var scripts = new Dictionary<string, WeaponScalingScript>(StringComparer.OrdinalIgnoreCase);
            if (cfg.Scripts != null)
            {
                foreach (var kv in cfg.Scripts)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                    var s = kv.Value;
                    s.KMin = Math.Max(0, s.KMin);
                    s.KMax = Math.Max(0, s.KMax);
                    if (s.KMax < s.KMin)
                        (s.KMin, s.KMax) = (s.KMax, s.KMin);
                    scripts[kv.Key.Trim()] = s;
                }
            }
            cfg.Scripts = scripts;

            cfg.KcMin = Math.Max(0, cfg.KcMin);
            cfg.KcMax = Math.Max(0, cfg.KcMax);
            if (cfg.KcMax < cfg.KcMin)
                (cfg.KcMin, cfg.KcMax) = (cfg.KcMax, cfg.KcMin);

            return cfg;
        }

        private static void Save(WeaponScalingConfig cfg)
        {
            var jsonOut = JsonConvert.SerializeObject(cfg);
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                DatabaseManager.ShardConfig.SaveString(new ConfigPropertiesString { Key = StoreKey, Value = jsonOut, Description = "Weapon aug-scaling config (JSON)" });
            else
                DatabaseManager.ShardConfig.AddString(StoreKey, jsonOut, "Weapon aug-scaling config (JSON)");
        }

        // ── Resolve helpers (consumed by the step-3 combat wire-in; pure math is static for tests) ──

        public static WeaponScalingTier GetTier(int tier)
        {
            return Current.Tiers.FirstOrDefault(t => t.Tier == tier);
        }

        /// <summary>Lerp a 0..1000 quality roll across [min, max]. Out-of-range quality clamps.</summary>
        public static double ResolveFromQuality(double min, double max, int quality)
        {
            var q = Math.Clamp(quality, 0, QualityMax);
            return min + (max - min) * (q / (double)QualityMax);
        }

        /// <summary>Letter grade for a quality roll — the at-a-glance "how good was this roll vs the
        /// best possible" read. Grades the ROLL, not the damage, so it means the same thing on every
        /// family. SCHOOL-STYLE bands (owner 2026-08-01, second revision — the letters now match the
        /// percent intuition): S = a literally PERFECT 100 pct roll (1 in 1,001 — the chase item),
        /// A 90+, B 80+, C 65+, D 50+, F below 50.</summary>
        public static string GetQualityGrade(int quality)
        {
            var q = Math.Clamp(quality, 0, QualityMax);
            if (q >= 1000) return "S";
            if (q >= 900) return "A";
            if (q >= 800) return "B";
            if (q >= 650) return "C";
            if (q >= 500) return "D";
            return "F";
        }

        /// <summary>The wielder-facing k for a script + quality roll, or null when the script is unknown
        /// (unknown script = weapon contributes no scaling term; loud in logs at the wire-in, silent here).</summary>
        public static double? ResolveK(string script, int quality)
        {
            if (string.IsNullOrWhiteSpace(script))
                return null;
            if (!Current.Scripts.TryGetValue(script, out var s))
                return null;
            return ResolveFromQuality(s.KMin, s.KMax, quality);
        }

        public static double ResolveKc(int quality)
        {
            var cfg = Current;
            return ResolveFromQuality(cfg.KcMin, cfg.KcMax, quality);
        }
    }
}
