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

        // Scheme C (2026-08-03): the family's authored per-hit variance. Non-crit melee hits
        // roll the WHOLE envelope (weenie base + aug term) down from max by the quality-
        // tightened fraction of this. 0 = inert (launchers/casters, or a store from before
        // this field existed — the old flat-hit behavior is the fallback either way).
        public double Variance { get; set; }

        // Grade ladder (owner 2026-08-03, WeaponGradeLadder plan): k resolved from the weapon's
        // SUB-GRADE, not lerped from quality. Sixteen AUTHORED values keyed by sub-grade name
        // (S, A+, A, A-, ... F-) — the ladder is drawn, not derived, because the quality bands
        // are wildly uneven in width (S->A is 50 quality points, D->F is 325) and a linear lerp
        // inherited that geometry, bunching S/A/B within +7.5 pct of each other.
        // NULL/EMPTY = this family falls back to the KMin/KMax lerp — that is the migration path
        // for old stores AND the deliberate current state of launchers (their rows are a damage
        // MOD band, retuned in the missile pass) and casters (inert).
        public Dictionary<string, double> Grades { get; set; }

        /// <summary>True when this family resolves off an authored ladder rather than the lerp.
        /// Gates BOTH k resolution and the variance sub-grade snap, so the two never disagree.</summary>
        [JsonIgnore]
        public bool HasLadder => Grades != null && Grades.Count > 0;
    }

    public class WeaponScalingConfig
    {
        public bool Enabled { get; set; }

        public List<WeaponScalingTier> Tiers { get; set; } = new();

        public Dictionary<string, WeaponScalingScript> Scripts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public double KcMin { get; set; }
        public double KcMax { get; set; }

        // Scheme C: how much of the family variance a perfect-quality weapon sheds.
        // v_eff = Variance x (1 - TightenStrength x quality/1000); 0.7 = S keeps 30 pct of
        // the family's wildness, F keeps ~83 pct. 0 (old stores) = no tightening.
        public double TightenStrength { get; set; }

        // Grade drop weights (owner 2026-08-02): the quality roll picks a GRADE from these
        // weights, then rolls uniform INSIDE that grade's quality band — frequency decoupled
        // from what a grade MEANS (the band cutoffs stay fixed; S is always the single perfect
        // roll q=1000). Relative weights, normalized at roll time. Null/empty (old stores) =
        // the legacy uniform 0-1000 roll.
        public Dictionary<string, double> GradeWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

        // ── Grade tables ──
        // DECLARED BEFORE _current ON PURPOSE: static field initializers run in declaration order,
        // and _current calls BuildDefaults(), which seeds ladders off these. Move them below and
        // the type initializer throws a NullReferenceException on first touch.

        /// <summary>Grade quality bands — MUST match the ForgeGrade cutoffs (plugin + appraisal
        /// labels). The weights table picks the grade; quality rolls uniform inside the band.
        /// S is the single perfect roll (owner 2026-08-02: "S remains 100 pct of max").</summary>
        public static readonly (string Grade, int QMin, int QMax)[] GradeBands =
        {
            ("S", 1000, 1000), ("A", 900, 999), ("B", 800, 899),
            ("C", 650, 799), ("D", 500, 649), ("F", 0, 499),
        };

        /// <summary>SUB-grade bands (owner 2026-08-03): each full grade split into thirds, S left
        /// alone as the single perfect roll. Costs NOTHING in the drop system — these are
        /// sub-ranges of the existing <see cref="GradeBands"/>, so GradeWeights and per-grade drop
        /// rates are untouched; the sub-grade is simply where inside the band the uniform roll
        /// landed. Order is the LADDER order (best to worst) and the payload wire order — the
        /// plugin indexes by position, so never reorder without bumping both sides.</summary>
        public static readonly (string Grade, int QMin, int QMax, int QMid)[] SubGradeBands =
        {
            ("S",  1000, 1000, 1000),
            ("A+",  967,  999,  983),
            ("A",   933,  966,  950),
            ("A-",  900,  932,  916),
            ("B+",  867,  899,  883),
            ("B",   833,  866,  850),
            ("B-",  800,  832,  816),
            ("C+",  750,  799,  775),
            ("C",   700,  749,  725),
            ("C-",  650,  699,  675),
            ("D+",  600,  649,  625),
            ("D",   550,  599,  575),
            ("D-",  500,  549,  525),
            ("F+",  333,  499,  416),
            ("F",   167,  332,  250),
            ("F-",    0,  166,   83),
        };

        /// <summary>Ladder step: +18 pct per FULL grade = three sub-grade steps, so one sub-grade
        /// step is the cube root. Owner 2026-08-03; S sits on the same even step (the premium
        /// variant was offered and declined).</summary>
        public static readonly double LadderStep = Math.Pow(1.18, 1.0 / 3.0);

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
            // Scheme C (2026-08-03, WeaponVariance_SchemeC_Plan): per-family authored variance
            // (config-owned — loot's per-drop DamageVariance roll is display-legacy). k stays
            // UNIFORM within mechanics groups: the EV normalization for variance is LIVE in
            // WeaponScalingCombat.EvNormalization (owner ask: editing a Variance knob must
            // auto-rebalance) — never bake it into these numbers, that would double-dip.
            // GRADE LADDER (owner 2026-08-03): melee families resolve k from a 16-value authored
            // ladder, seeded here at +18 pct per full grade (+5.67 pct per sub-grade) off an S
            // anchor. KMin/KMax are KEPT on every row as the fallback (and as what launchers and
            // casters still actually use) — a family drops back to the lerp the moment its ladder
            // is cleared. Singles anchor S = 0.90; multi-strike anchors 0.40 = the same 0.444x
            // per-swing discount the old 0.40/0.51 band encoded (they strike 2-3x per swing).
            cfg.TightenStrength = 0.7;
            void Melee(string key, double variance, double anchorS = 0.90, double kMin = 0.90, double kMax = 1.15)
                => cfg.Scripts[key] = new WeaponScalingScript
                {
                    KMin = kMin,
                    KMax = kMax,
                    Variance = variance,
                    Grades = BuildLadder(anchorS, variance, cfg.TightenStrength),
                };

            Melee("mace", 0.35);
            Melee("sword", 0.40);
            Melee("staff", 0.45);
            Melee("cleaver", 0.50);
            Melee("two_handed_spear", 0.50);
            Melee("dagger", 0.55);
            Melee("unarmed", 0.55);
            Melee("spear", 0.60);
            Melee("axe", 0.70);
            Melee("sword_ms", 0.40, anchorS: 0.40, kMin: 0.40, kMax: 0.51);
            Melee("dagger_ms", 0.55, anchorS: 0.40, kMin: 0.40, kMax: 0.51);
            // casters stay on the pre-C band, variance inert until the caster wire-in
            foreach (var caster in new[] { "caster_elemental", "caster_non_elemental" })
                cfg.Scripts[caster] = new WeaponScalingScript { KMin = 0.90, KMax = 1.15 };

            // Grade drop weights (owner 2026-08-02): S stays ~1-in-1000; A 5 / B 10 / C 15
            // owner-picked, D/F fill per the "gentle ladder" option.
            cfg.GradeWeights["S"] = 0.1;
            cfg.GradeWeights["A"] = 5;
            cfg.GradeWeights["B"] = 10;
            cfg.GradeWeights["C"] = 15;
            cfg.GradeWeights["D"] = 25;
            cfg.GradeWeights["F"] = 44.9;
            // LAUNCHERS (owner 2026-08-01): kMin/kMax are REINTERPRETED as the EFFECTIVE DAMAGE
            // MODIFIER band (replace semantics), not a flat-term coefficient — bows always scaled
            // through their mod (it multiplies ammo + Blood Drinker + elemental, and BD is
            // 0.5 x item augs, so the mod is already aug-coupled). No flat term (double-dip).
            // Band RETUNED 2026-08-02 (owner, two passes): S = 4.00 (just past the pre-system
            // authored 3.90), KMin = 4.00 x (0.90/1.15) = 3.13 — the SAME F->S ratio as the
            // melee k band (+27.8 pct), so grades ladder identically across all weapon types.
            // Floor still clears the real legacy T10 roll ceiling (T10 bows roll 2.84-3.08 in
            // the mutation scripts). Per-hit parity caveat accepted 08-01/08-02; the deferred
            // DPS session can retune — pure config.
            foreach (var launcher in new[] { "bow", "crossbow", "atlatl" })
                cfg.Scripts[launcher] = new WeaponScalingScript { KMin = 3.13, KMax = 4.00 };

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

        /// <summary>The live EV-normalization multiplier for an effective variance. THE one
        /// definition — <see cref="WeaponScalingCombat"/> calls this at swing time and
        /// <see cref="BuildLadder"/> calls it when generating rungs, so the ladder a knob writes
        /// and the damage combat deals can never drift apart. (p = 0.5 crit chance, M = 3.0 crit
        /// cap; see the plan's EV-normalization section before changing the constants.)</summary>
        public static double EvNormalization(double vEff) => 2.0 / (2.0 - 0.25 * vEff);

        /// <summary>v_eff for a family variance + tighten at a given quality.</summary>
        public static double EffectiveVariance(double familyVariance, double tighten, int quality)
        {
            var t = Math.Max(0.0, Math.Min(1.0, quality / (double)QualityMax));
            return familyVariance * (1.0 - tighten * t);
        }

        /// <summary>Generate the 16-value ladder from an S anchor, each rung one
        /// <see cref="LadderStep"/> below the last IN DEALT DAMAGE — not in raw k.
        ///
        /// The distinction is load-bearing: dealt damage is k x augs x EvNormalization(v_eff), and
        /// v_eff RISES as grade falls (lower grades shed less family variance), so EV normalization
        /// quietly inflates the low rungs. A purely geometric k ladder therefore lands +5.6 pct at
        /// the top and only +4.8 pct at the bottom — visibly uneven, which is the exact defect this
        /// whole system was built to remove. Dividing the normalization back out makes every
        /// observed step exactly +5.67 pct. Consequence: the ladder is FAMILY-SPECIFIC (an axe at
        /// variance 0.70 gets different k than a mace at 0.35 for the same damage), which is why
        /// this takes variance/tighten rather than being a shared constant table.</summary>
        public static Dictionary<string, double> BuildLadder(double anchorS, double variance, double tighten)
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var evAnchor = EvNormalization(EffectiveVariance(variance, tighten, QualityMax));
            for (var i = 0; i < SubGradeBands.Length; i++)
            {
                var ev = EvNormalization(EffectiveVariance(variance, tighten, SubGradeBands[i].QMid));
                d[SubGradeBands[i].Grade] = Math.Round(anchorS * evAnchor / (Math.Pow(LadderStep, i) * ev), 4);
            }
            return d;
        }

        /// <summary>Re-price an authored ladder for a new variance/tighten so DEALT DAMAGE is
        /// unchanged. Because a rung is stored with EV normalization divided out, editing a
        /// family's Variance would otherwise leave every rung compensating for the OLD variance —
        /// the ladder would go uneven and shift level, breaking the owner's standing invariant
        /// that "editing a Variance knob auto-rebalances" (the reason normalization was moved to
        /// swing-time in the first place). Multiplying each rung by evOld/evNew preserves any
        /// HAND-AUTHORED shape, which regenerating from the anchor would silently discard.</summary>
        public static void RebaseLadder(WeaponScalingScript s, double oldVariance, double oldTighten,
                                        double newVariance, double newTighten)
        {
            if (s == null || !s.HasLadder)
                return;

            foreach (var b in SubGradeBands)
            {
                if (!s.Grades.TryGetValue(b.Grade, out var k))
                    continue;
                var evOld = EvNormalization(EffectiveVariance(oldVariance, oldTighten, b.QMid));
                var evNew = EvNormalization(EffectiveVariance(newVariance, newTighten, b.QMid));
                if (evNew > 0)
                    s.Grades[b.Grade] = Math.Round(k * evOld / evNew, 4);
            }
        }

        /// <summary>The sub-grade band a quality roll lands in. Never null — F- catches 0.</summary>
        public static (string Grade, int QMin, int QMax, int QMid) GetSubGradeBand(int quality)
        {
            var q = Math.Clamp(quality, 0, QualityMax);
            foreach (var b in SubGradeBands)
                if (q >= b.QMin)
                    return b;
            return SubGradeBands[SubGradeBands.Length - 1];
        }

        /// <summary>Sub-grade label ("B+") for a quality roll — the appraisal/plugin-facing name.
        /// <see cref="GetQualityGrade"/> stays the FULL-grade label and still drives GradeWeights.</summary>
        public static string GetQualitySubGrade(int quality) => GetSubGradeBand(quality).Grade;

        /// <summary>Roll a drop's quality: weighted grade pick, then uniform inside the band.
        /// Falls back to the legacy uniform 0-1000 roll when no weights are authored.</summary>
        public static int RollQuality()
        {
            Initialize();
            var weights = _current.GradeWeights;

            var total = 0.0;
            if (weights != null)
                foreach (var b in GradeBands)
                    if (weights.TryGetValue(b.Grade, out var w) && w > 0)
                        total += w;
            if (total <= 0)
                return ACE.Common.ThreadSafeRandom.Next(0, QualityMax);   // legacy uniform

            var pick = ACE.Common.ThreadSafeRandom.Next(0f, (float)total);
            var acc = 0.0;
            foreach (var b in GradeBands)
            {
                if (!weights.TryGetValue(b.Grade, out var w) || w <= 0)
                    continue;
                acc += w;
                if (pick <= acc)
                    return b.QMin >= b.QMax ? b.QMin : ACE.Common.ThreadSafeRandom.Next(b.QMin, b.QMax);
            }
            return QualityMax;   // float edge: pick landed exactly on total
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
                    s.Variance = Math.Max(0, Math.Min(0.95, s.Variance));

                    // Grade ladder: rebuild with the case-insensitive comparer (JSON loses it),
                    // drop unknown sub-grade keys, clamp negatives. An EMPTY dictionary is
                    // normalized to null so HasLadder and the payload agree on "no ladder".
                    // A PARTIAL ladder is completed from the lerp at each missing sub-grade's
                    // midpoint — never left half-authored, because ResolveScriptK would then
                    // silently mix ladder rungs with lerp rungs and the ladder would be uneven
                    // in a way nobody authored.
                    if (s.Grades != null)
                    {
                        var ladder = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        foreach (var g in SubGradeBands)
                            if (s.Grades.TryGetValue(g.Grade, out var k))
                                ladder[g.Grade] = Math.Max(0, k);

                        if (ladder.Count > 0)
                        {
                            foreach (var g in SubGradeBands)
                                if (!ladder.ContainsKey(g.Grade))
                                    ladder[g.Grade] = ResolveFromQuality(s.KMin, s.KMax, g.QMid);
                            s.Grades = ladder;
                        }
                        else
                            s.Grades = null;
                    }

                    scripts[kv.Key.Trim()] = s;
                }
            }
            cfg.Scripts = scripts;

            cfg.TightenStrength = Math.Max(0, Math.Min(1.0, cfg.TightenStrength));

            var gradeWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (cfg.GradeWeights != null)
                foreach (var kv in cfg.GradeWeights)
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                        gradeWeights[kv.Key.Trim()] = Math.Max(0, kv.Value);
            cfg.GradeWeights = gradeWeights;

            // 2026-08-01 semantics migration: launcher rows became the EFFECTIVE DAMAGE MODIFIER
            // band (replace semantics — quality grades the mod, no flat term). A store written
            // before the change still carries flat-term-era coefficients (~0.9-1.15); resolving
            // those AS the modifier would ~quarter launcher damage. Any launcher row entirely
            // below 2.0 is unmistakably pre-migration (real launcher mods are T10 2.92+) and is
            // bumped to the current defaults; deliberate values >= 2.0 are always respected.
            foreach (var launcherKey in new[] { "bow", "crossbow", "atlatl" })
            {
                if (cfg.Scripts.TryGetValue(launcherKey, out var row) && row.KMax < 2.0)
                {
                    row.KMin = 3.13;
                    row.KMax = 4.00;
                }
            }

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

        /// <summary>k for a script row + quality roll. Authored LADDER when the family has one
        /// (flat per sub-grade — NO interpolation, owner 2026-08-03: 16 authored values were the
        /// whole point, and lerping between them hands control back to the band geometry that
        /// caused the bunching); otherwise the legacy KMin/KMax lerp, which is what launchers
        /// (mod band) and casters (inert) and any pre-ladder store still use.</summary>
        public static double ResolveScriptK(WeaponScalingScript s, int quality)
        {
            if (s.HasLadder && s.Grades.TryGetValue(GetQualitySubGrade(quality), out var k))
                return k;
            return ResolveFromQuality(s.KMin, s.KMax, quality);
        }

        /// <summary>The wielder-facing k for a script + quality roll, or null when the script is unknown
        /// (unknown script = weapon contributes no scaling term; loud in logs at the wire-in, silent here).</summary>
        public static double? ResolveK(string script, int quality)
        {
            if (string.IsNullOrWhiteSpace(script))
                return null;
            if (!Current.Scripts.TryGetValue(script, out var s))
                return null;
            return ResolveScriptK(s, quality);
        }

        public static double ResolveKc(int quality)
        {
            var cfg = Current;
            return ResolveFromQuality(cfg.KcMin, cfg.KcMax, quality);
        }
    }
}
