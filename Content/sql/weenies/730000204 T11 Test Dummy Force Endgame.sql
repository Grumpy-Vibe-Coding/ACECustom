-- 730000204 T11 Test Dummy Force Endgame.sql
-- TEST-ONLY: flags the Patient Zero test dummy (boss 730000200, minion 730000201) so the four v11+ endgame
-- combat systems (percent-HP floor, vuln compression, damage-taken mitigation, per-tier scaling + attack-skill
-- floor) fire even when the mob is spawned at a NON-prestige variation (variation 0, a normal landblock).
-- This sidesteps the prestige variation-layer fragility in /createinst /removeinst /reload-landblock so the
-- dummy can be spawned/removed/reloaded reliably anywhere for tuning.
--   PropertyBool.ForceEndgameSystems (50046) = true.
--   Optional: PropertyInt.EndgameForcedVariation (50105) simulates a specific tier (unset => v11 = tier 1).
-- DO NOT apply this flag to real production mobs — they must use their real Location.Variation.
-- Idempotent. Generated 2026-07-07.
SET FOREIGN_KEY_CHECKS = 0;

INSERT INTO `weenie_properties_bool` (`object_Id`,`type`,`value`) VALUES
  (730000200,50046,1),
  (730000201,50046,1)
ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);

-- To simulate a deeper tier at variation 0, uncomment and set (e.g. 20 = v20):
-- INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES (730000200,50105,20),(730000201,50105,20)
--   ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);

SET FOREIGN_KEY_CHECKS = 1;
