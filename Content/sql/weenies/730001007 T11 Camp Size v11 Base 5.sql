-- 730001007 T11 Camp Size v11 Base 5.sql
-- Camp size now scales as: total = 5 + (variation - 11).
--   v11 = 5 (1 boss + 4 minions), v12 = 6, v13 = 7, ... (+1 minion per tier).
-- Previously base was 6 (v11 = 1 boss + 5 minions), set by 730001005.
-- Authoritative value is VariationScaledSpawnBase (int 50103), read by
-- ApplyVariationScaledSpawnCount() at generator start; 81/82 kept in sync for DB consistency.
-- Runs AFTER 730001005 in filename order to override it. Idempotent.
SET FOREIGN_KEY_CHECKS = 0;

UPDATE `weenie_properties_int`
   SET `value` = 5
 WHERE `type` IN (50103, 81, 82)
   AND `object_Id` BETWEEN 730000072 AND 730000091;

SET FOREIGN_KEY_CHECKS = 1;
