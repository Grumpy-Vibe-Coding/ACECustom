using System.Linq;

using ACE.Server.Managers.WeaponScaling;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

namespace ACE.Server.Tests
{
    /// <summary>
    /// Weapon aug-scaling config (T11_WeaponRelevance_Plan_2026-07-31.md): the locked launch
    /// defaults, the quality->k lerp the combat wire-in will lean on, and store round-trip safety.
    /// Pure config/math only — no DB, no combat (the wire-in ships in a later step).
    /// </summary>
    [TestClass]
    public class WeaponScalingConfigTests
    {
        // ── Locked defaults (plan §4 / §7) ──

        [TestMethod]
        public void Defaults_SystemStartsDisabled()
        {
            Assert.IsFalse(WeaponScalingManager.BuildDefaults().Enabled,
                "The master switch must default OFF — deploy-cold guarantee.");
        }

        [TestMethod]
        public void Defaults_TierLadder_CapAndWieldFloorRule()
        {
            var cfg = WeaponScalingManager.BuildDefaults();

            var t11 = cfg.Tiers.Single(t => t.Tier == 11);
            Assert.AreEqual(2500, t11.Cap);
            Assert.AreEqual(2000, t11.MinWieldAugs,
                "T11's floor = the pre-existing live gate (ZoneLootSetWieldItemAugs), not 0.");

            // +500 cap per tier; each tier's wield floor = previous tier's cap (the economy joint:
            // your T(n) weapon plateaus at exactly the count where T(n+1) becomes wieldable).
            for (var tier = 12; tier <= 25; tier++)
            {
                var row = cfg.Tiers.Single(t => t.Tier == tier);
                var prev = cfg.Tiers.Single(t => t.Tier == tier - 1);
                Assert.AreEqual(prev.Cap + 500, row.Cap, $"T{tier} cap");
                Assert.AreEqual(prev.Cap, row.MinWieldAugs, $"T{tier} minWieldAugs");
            }
        }

        [TestMethod]
        public void Defaults_LockedKRanges()
        {
            var cfg = WeaponScalingManager.BuildDefaults();

            // One row per weapon family, weight subtypes merged; k EQUAL WITHIN MECHANICS GROUPS
            // (owner 2026-08-01) — the discount rule follows what the weapon GIVES UP: multi-strike
            // gives up nothing -> discounted by strike count for per-swing parity; two-handed gives
            // up a SHIELD -> NO discount (k = singles per hit; both strikes carry it = the premium).
            Assert.AreEqual(16, cfg.Scripts.Count);
            var expected = new (string Key, double KMin, double KMax)[]
            {
                ("sword", 0.90, 1.15), ("axe", 0.90, 1.15), ("dagger", 0.90, 1.15),
                ("mace", 0.90, 1.15), ("spear", 0.90, 1.15), ("staff", 0.90, 1.15),
                ("unarmed", 0.90, 1.15),
                ("sword_ms", 0.40, 0.51), ("dagger_ms", 0.40, 0.51),
                ("cleaver", 0.90, 1.15), ("two_handed_spear", 0.90, 1.15),
                // Launchers: kMin/kMax = the EFFECTIVE DAMAGE MODIFIER band (replace semantics,
                // owner 2026-08-01), not a flat-term coefficient. Band FINAL 2026-08-02: S = 4.00
                // (owner pick), KMin = 4.00 x (0.90/1.15) = 3.13 so grades ladder at the same
                // F->S ratio as melee; floor still clears the real T10 roll ceiling (3.08).
                ("bow", 3.13, 4.00), ("crossbow", 3.13, 4.00), ("atlatl", 3.13, 4.00),
                ("caster_elemental", 0.90, 1.15), ("caster_non_elemental", 0.90, 1.15),
            };
            foreach (var e in expected)
            {
                Assert.AreEqual(e.KMin, cfg.Scripts[e.Key].KMin, 1e-9, e.Key);
                Assert.AreEqual(e.KMax, cfg.Scripts[e.Key].KMax, 1e-9, e.Key);
            }
            Assert.AreEqual(0.60, cfg.KcMin, 1e-9);
            Assert.AreEqual(0.80, cfg.KcMax, 1e-9);
        }

        // ── Grade ladder (owner 2026-08-03, WeaponGradeLadder_Plan) ──

        [TestMethod]
        public void Ladder_SeededOnMeleeFamiliesOnly()
        {
            var cfg = WeaponScalingManager.BuildDefaults();

            foreach (var melee in new[] { "sword", "axe", "dagger", "mace", "spear", "staff",
                                          "unarmed", "cleaver", "two_handed_spear", "sword_ms", "dagger_ms" })
                Assert.IsTrue(cfg.Scripts[melee].HasLadder, $"{melee} should resolve off the authored ladder.");

            // Launchers resolve their rows as a damage MOD band and casters are inert — both stay
            // on the KMin/KMax lerp until the missile/magic passes.
            foreach (var lerp in new[] { "bow", "crossbow", "atlatl", "caster_elemental", "caster_non_elemental" })
                Assert.IsFalse(cfg.Scripts[lerp].HasLadder, $"{lerp} must stay on the lerp for now.");
        }

        [TestMethod]
        public void Ladder_EveryStepIsEvenInDEALTDamage()
        {
            var cfg = WeaponScalingManager.BuildDefaults();

            // The rung is stored with EV normalization divided out, so evenness must be asserted
            // on k x EvNormalization — the quantity that actually reaches the damage envelope.
            // Asserting on raw k would pass a ladder that visibly steps +5.6 pct at the top and
            // +4.8 pct at the bottom, which is the exact defect this system removes.
            foreach (var kv in cfg.Scripts.Where(s => s.Value.HasLadder))
            {
                double? prev = null;
                foreach (var b in WeaponScalingManager.SubGradeBands)
                {
                    var k = kv.Value.Grades[b.Grade];
                    var vEff = WeaponScalingManager.EffectiveVariance(kv.Value.Variance, cfg.TightenStrength, b.QMid);
                    var dealt = k * WeaponScalingManager.EvNormalization(vEff);

                    if (prev.HasValue)
                        Assert.AreEqual(WeaponScalingManager.LadderStep, prev.Value / dealt, 1e-3,
                            $"{kv.Key} step into {b.Grade}");
                    prev = dealt;
                }
            }
        }

        [TestMethod]
        public void Ladder_FullGradeStepIs18Percent()
        {
            var cfg = WeaponScalingManager.BuildDefaults();
            var s = cfg.Scripts["unarmed"];

            double Dealt(string grade)
            {
                var b = WeaponScalingManager.SubGradeBands.Single(x => x.Grade == grade);
                return s.Grades[grade] * WeaponScalingManager.EvNormalization(
                    WeaponScalingManager.EffectiveVariance(s.Variance, cfg.TightenStrength, b.QMid));
            }

            Assert.AreEqual(1.18, Dealt("A") / Dealt("B"), 1e-3, "A -> B is one full grade");
            Assert.AreEqual(1.18, Dealt("B") / Dealt("C"), 1e-3, "B -> C is one full grade");
            // S vs B was the presenting complaint: +7.5 pct under the old lerp, +32 pct now.
            Assert.AreEqual(1.318, Dealt("S") / Dealt("B"), 5e-3, "S vs B");
        }

        [TestMethod]
        public void Ladder_ResolvesFlatPerSubGrade_NoInterpolation()
        {
            var cfg = WeaponScalingManager.BuildDefaults();
            var s = cfg.Scripts["unarmed"];

            // Every quality inside the B+ band (867-899) must give the SAME k — the owner chose
            // 16 authored values precisely so the label fully determines the weapon.
            var atLow = WeaponScalingManager.ResolveScriptK(s, 867);
            var atHigh = WeaponScalingManager.ResolveScriptK(s, 899);
            Assert.AreEqual(atLow, atHigh, 1e-12, "B+ must not interpolate across its band.");
            Assert.AreEqual(s.Grades["B+"], atLow, 1e-12);

            // ...and the neighbouring band must differ.
            Assert.AreNotEqual(atLow, WeaponScalingManager.ResolveScriptK(s, 900), 1e-6);
        }

        [TestMethod]
        public void Ladder_LerpFamiliesStillUseKMinKMax()
        {
            var cfg = WeaponScalingManager.BuildDefaults();
            var bow = cfg.Scripts["bow"];

            Assert.AreEqual(4.00, WeaponScalingManager.ResolveScriptK(bow, 1000), 1e-9);
            Assert.AreEqual(3.13, WeaponScalingManager.ResolveScriptK(bow, 0), 1e-9);
            // Continuous, not stepped — proof the ladder path is genuinely bypassed.
            Assert.AreNotEqual(WeaponScalingManager.ResolveScriptK(bow, 500),
                               WeaponScalingManager.ResolveScriptK(bow, 600), 1e-6);
        }

        [TestMethod]
        public void SubGrade_BandLabelsAndEdges()
        {
            Assert.AreEqual("S", WeaponScalingManager.GetQualitySubGrade(1000));
            Assert.AreEqual("A+", WeaponScalingManager.GetQualitySubGrade(999));
            Assert.AreEqual("A+", WeaponScalingManager.GetQualitySubGrade(967));
            Assert.AreEqual("A", WeaponScalingManager.GetQualitySubGrade(966));
            Assert.AreEqual("A-", WeaponScalingManager.GetQualitySubGrade(900));
            Assert.AreEqual("B+", WeaponScalingManager.GetQualitySubGrade(899));
            Assert.AreEqual("B-", WeaponScalingManager.GetQualitySubGrade(800));
            Assert.AreEqual("C+", WeaponScalingManager.GetQualitySubGrade(799));
            Assert.AreEqual("D-", WeaponScalingManager.GetQualitySubGrade(500));
            Assert.AreEqual("F+", WeaponScalingManager.GetQualitySubGrade(499));
            Assert.AreEqual("F-", WeaponScalingManager.GetQualitySubGrade(0));

            // Sub-grades must TILE their parent grade exactly — a gap or overlap would mean a
            // drop rolled inside GradeWeights lands on a rung from a different letter.
            foreach (var g in WeaponScalingManager.GradeBands)
            {
                var subs = WeaponScalingManager.SubGradeBands.Where(b => b.Grade.TrimEnd('+', '-') == g.Grade).ToList();
                Assert.AreEqual(g.QMin, subs.Min(b => b.QMin), $"{g.Grade} sub-grades must start at the grade floor");
                Assert.AreEqual(g.QMax, subs.Max(b => b.QMax), $"{g.Grade} sub-grades must end at the grade ceiling");
            }
        }

        [TestMethod]
        public void Normalize_CompletesAPartialLadderFromTheLerp()
        {
            var cfg = new WeaponScalingConfig { TightenStrength = 0.7 };
            cfg.Scripts["x"] = new WeaponScalingScript
            {
                KMin = 0.40,
                KMax = 0.90,
                Grades = new System.Collections.Generic.Dictionary<string, double> { ["S"] = 0.90 },
            };

            WeaponScalingManager.Normalize(cfg);

            // A half-authored ladder would silently mix ladder rungs with lerp rungs.
            Assert.AreEqual(WeaponScalingManager.SubGradeBands.Length, cfg.Scripts["x"].Grades.Count);
            Assert.AreEqual(0.90, cfg.Scripts["x"].Grades["S"], 1e-9, "authored rung preserved");
        }

        [TestMethod]
        public void Normalize_EmptyLadderBecomesNoLadder()
        {
            var cfg = new WeaponScalingConfig();
            cfg.Scripts["x"] = new WeaponScalingScript
            {
                KMin = 0.40,
                KMax = 0.90,
                Grades = new System.Collections.Generic.Dictionary<string, double>(),
            };

            WeaponScalingManager.Normalize(cfg);

            Assert.IsFalse(cfg.Scripts["x"].HasLadder, "An empty dictionary must read as 'no ladder'.");
        }

        [TestMethod]
        public void RebaseLadder_HoldsDealtDamageFlatAcrossAVarianceEdit()
        {
            var cfg = WeaponScalingManager.BuildDefaults();
            var s = cfg.Scripts["unarmed"];
            const double oldVar = 0.55, newVar = 0.20;

            double Dealt(double variance, string grade)
            {
                var b = WeaponScalingManager.SubGradeBands.Single(x => x.Grade == grade);
                return s.Grades[grade] * WeaponScalingManager.EvNormalization(
                    WeaponScalingManager.EffectiveVariance(variance, cfg.TightenStrength, b.QMid));
            }

            var before = WeaponScalingManager.SubGradeBands.Select(b => Dealt(oldVar, b.Grade)).ToList();
            WeaponScalingManager.RebaseLadder(s, oldVar, cfg.TightenStrength, newVar, cfg.TightenStrength);
            var after = WeaponScalingManager.SubGradeBands.Select(b => Dealt(newVar, b.Grade)).ToList();

            // The owner's standing invariant: editing a Variance knob rebalances rather than
            // silently changing how hard the family hits.
            for (var i = 0; i < before.Count; i++)
                Assert.AreEqual(before[i], after[i], before[i] * 1e-3,
                    $"{WeaponScalingManager.SubGradeBands[i].Grade} dealt damage moved on a variance edit");
        }

        [TestMethod]
        public void Defaults_ScriptLookupIsCaseInsensitive()
        {
            var cfg = WeaponScalingManager.BuildDefaults();
            Assert.IsTrue(cfg.Scripts.ContainsKey("Sword"));
        }

        // ── Quality lerp (the swing-time resolve) ──

        [TestMethod]
        public void ResolveFromQuality_EndpointsAndMidpoint()
        {
            Assert.AreEqual(0.90, WeaponScalingManager.ResolveFromQuality(0.90, 1.15, 0), 1e-9);
            Assert.AreEqual(1.15, WeaponScalingManager.ResolveFromQuality(0.90, 1.15, 1000), 1e-9);
            Assert.AreEqual(1.025, WeaponScalingManager.ResolveFromQuality(0.90, 1.15, 500), 1e-9);
        }

        [TestMethod]
        public void ResolveFromQuality_OutOfRangeQualityClamps()
        {
            Assert.AreEqual(0.90, WeaponScalingManager.ResolveFromQuality(0.90, 1.15, -50), 1e-9);
            Assert.AreEqual(1.15, WeaponScalingManager.ResolveFromQuality(0.90, 1.15, 99999), 1e-9);
        }

        [TestMethod]
        public void QualityGrade_SchoolStyleBandsAndEdges()
        {
            // S = a literally perfect roll only (1 in 1,001); the rest are school-style.
            Assert.AreEqual("S", WeaponScalingManager.GetQualityGrade(1000));
            Assert.AreEqual("A", WeaponScalingManager.GetQualityGrade(999));
            Assert.AreEqual("A", WeaponScalingManager.GetQualityGrade(900));
            Assert.AreEqual("B", WeaponScalingManager.GetQualityGrade(899));
            Assert.AreEqual("B", WeaponScalingManager.GetQualityGrade(800));
            Assert.AreEqual("C", WeaponScalingManager.GetQualityGrade(799));
            Assert.AreEqual("C", WeaponScalingManager.GetQualityGrade(650));
            Assert.AreEqual("D", WeaponScalingManager.GetQualityGrade(649));
            Assert.AreEqual("D", WeaponScalingManager.GetQualityGrade(500));
            Assert.AreEqual("F", WeaponScalingManager.GetQualityGrade(499));
            Assert.AreEqual("F", WeaponScalingManager.GetQualityGrade(0));
        }

        // ── Normalize (hand-edited / legacy store repair) ──

        [TestMethod]
        public void Normalize_RepairsInvertedRangesAndNegatives()
        {
            var cfg = new WeaponScalingConfig
            {
                KcMin = 0.9,
                KcMax = 0.5,
            };
            cfg.Scripts["x"] = new WeaponScalingScript { KMin = 1.2, KMax = 0.8 };
            cfg.Tiers.Add(new WeaponScalingTier { Tier = 11, Cap = -5, MinWieldAugs = -1 });

            WeaponScalingManager.Normalize(cfg);

            Assert.AreEqual(0.5, cfg.KcMin, 1e-9);
            Assert.AreEqual(0.9, cfg.KcMax, 1e-9);
            Assert.AreEqual(0.8, cfg.Scripts["x"].KMin, 1e-9);
            Assert.AreEqual(1.2, cfg.Scripts["x"].KMax, 1e-9);
            Assert.AreEqual(0, cfg.Tiers[0].Cap);
            Assert.AreEqual(0, cfg.Tiers[0].MinWieldAugs);
        }

        [TestMethod]
        public void Normalize_DedupesTiersAndRestoresCaseInsensitivity()
        {
            var json = @"{""Enabled"":true,
                ""Tiers"":[{""Tier"":12,""Cap"":3000,""MinWieldAugs"":2500},{""Tier"":12,""Cap"":9999,""MinWieldAugs"":0},{""Tier"":11,""Cap"":2500,""MinWieldAugs"":0}],
                ""Scripts"":{""heavy_sword"":{""KMin"":0.9,""KMax"":1.15}},
                ""KcMin"":0.6,""KcMax"":0.8}";
            var cfg = WeaponScalingManager.Normalize(JsonConvert.DeserializeObject<WeaponScalingConfig>(json));

            Assert.AreEqual(2, cfg.Tiers.Count);
            Assert.AreEqual(11, cfg.Tiers[0].Tier, "tiers sort ascending");
            Assert.AreEqual(3000, cfg.Tiers.Single(t => t.Tier == 12).Cap, "first entry wins on dupes");
            Assert.IsTrue(cfg.Scripts.ContainsKey("HEAVY_SWORD"), "deserialized dictionary must regain case-insensitive lookup");
        }

        [TestMethod]
        public void Normalize_MigratesFlatEraLauncherRowsToModifierBand()
        {
            // Pre-2026-08-01 stores carry launcher rows as flat-term coefficients (~0.9-1.15);
            // under replace semantics those would resolve AS the damage modifier and ~quarter
            // launcher damage. Normalize must bump any launcher row entirely below 2.0 to the
            // modifier-band defaults, while respecting deliberate values >= 2.0.
            var cfg = new WeaponScalingConfig();
            cfg.Scripts["bow"] = new WeaponScalingScript { KMin = 0.90, KMax = 1.15 };
            cfg.Scripts["crossbow"] = new WeaponScalingScript { KMin = 0.40, KMax = 0.51 };
            cfg.Scripts["atlatl"] = new WeaponScalingScript { KMin = 2.80, KMax = 3.10 };
            cfg.Scripts["sword"] = new WeaponScalingScript { KMin = 0.90, KMax = 1.15 };

            WeaponScalingManager.Normalize(cfg);

            // Migration target follows the CURRENT band (retuned to 3.13/4.00 on 2026-08-02).
            Assert.AreEqual(3.13, cfg.Scripts["bow"].KMin, 1e-9);
            Assert.AreEqual(4.00, cfg.Scripts["bow"].KMax, 1e-9);
            Assert.AreEqual(3.13, cfg.Scripts["crossbow"].KMin, 1e-9);
            Assert.AreEqual(4.00, cfg.Scripts["crossbow"].KMax, 1e-9);
            Assert.AreEqual(2.80, cfg.Scripts["atlatl"].KMin, 1e-9, "deliberate >= 2.0 band must survive");
            Assert.AreEqual(3.10, cfg.Scripts["atlatl"].KMax, 1e-9);
            Assert.AreEqual(0.90, cfg.Scripts["sword"].KMin, 1e-9, "melee rows untouched");
            Assert.AreEqual(1.15, cfg.Scripts["sword"].KMax, 1e-9);
        }

        // ── Store round-trip ──

        [TestMethod]
        public void Store_JsonRoundTripPreservesEverything()
        {
            var cfg = WeaponScalingManager.BuildDefaults();
            cfg.Enabled = true;

            var back = WeaponScalingManager.Normalize(
                JsonConvert.DeserializeObject<WeaponScalingConfig>(JsonConvert.SerializeObject(cfg)));

            Assert.IsTrue(back.Enabled);
            Assert.AreEqual(cfg.Tiers.Count, back.Tiers.Count);
            Assert.AreEqual(cfg.Scripts.Count, back.Scripts.Count);
            foreach (var t in cfg.Tiers)
            {
                var bt = back.Tiers.Single(x => x.Tier == t.Tier);
                Assert.AreEqual(t.Cap, bt.Cap);
                Assert.AreEqual(t.MinWieldAugs, bt.MinWieldAugs);
            }
            foreach (var s in cfg.Scripts)
            {
                Assert.AreEqual(s.Value.KMin, back.Scripts[s.Key].KMin, 1e-9);
                Assert.AreEqual(s.Value.KMax, back.Scripts[s.Key].KMax, 1e-9);
            }
            Assert.AreEqual(cfg.KcMin, back.KcMin, 1e-9);
            Assert.AreEqual(cfg.KcMax, back.KcMax, 1e-9);
        }
    }
}
