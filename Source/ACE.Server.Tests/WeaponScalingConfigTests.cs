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
                // owner 2026-08-01), not a flat-term coefficient. F 3.00 sits just above the
                // legacy T10 authored 2.92 so every T11 drop upgrades; no flat term for launchers.
                ("bow", 3.00, 3.40), ("crossbow", 3.00, 3.40), ("atlatl", 3.00, 3.40),
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

            Assert.AreEqual(3.00, cfg.Scripts["bow"].KMin, 1e-9);
            Assert.AreEqual(3.40, cfg.Scripts["bow"].KMax, 1e-9);
            Assert.AreEqual(3.00, cfg.Scripts["crossbow"].KMin, 1e-9);
            Assert.AreEqual(3.40, cfg.Scripts["crossbow"].KMax, 1e-9);
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
