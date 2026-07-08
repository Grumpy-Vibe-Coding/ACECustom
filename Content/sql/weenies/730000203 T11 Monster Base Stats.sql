-- 730000203 T11 Monster Base Stats.sql
-- Deliberate combat BASELINE for the Test Dummy template (Patient Zero 730000200 boss / 730000201 minion).
-- This is the "chassis" every other T11 mob copies. Design decisions (2026-07-07, rev2):
--   * MITIGATION: weenie resists + ArmorModVs = 1.0 (neutral, no element gaming) BUT heavy flat mitigation via
--     DamageResistRating(308)=10000 (~99% cut) + body_part base_Armor=1000 (~93.75% physical, pre-rending).
--     NOTE: these STACK MULTIPLICATIVELY with the global v11_mob_dmg_taken knob (boss 0.15) -> ~99.85% magic /
--     ~99.99% physical. Set MobDmgTakenOverride(9054)=1.0 on the weenie if DRR should be the sole source.
--   * OFFENSE: DamageRating(307)=10000 (~101x) so normal hits are lethal; attack skills 75k so they LAND; magic minimal.
--   * DEFENSE skills 100. CRITICAL: /testchar T10 actual skills are ~7k (Missile 6566 / Melee 7800 / War 6881),
--     NOT the ~75k the old balance doc assumed. Def 100 << 7k -> ~0% evade -> 100% land on melee/missile/magic.
-- HP (attr_2nd type 1) is owned by 730001010 and is NOT touched here.
-- Idempotent. Generated 2026-07-07.
SET FOREIGN_KEY_CHECKS = 0;

-- ============================================================ BOSS 730000200 ============================================================
-- Attributes (1Str 2End 3Coord 4Quick 5Focus 6Self) -> 500
UPDATE `weenie_properties_attribute` SET `init_Level`=500 WHERE `object_Id`=730000200 AND `type` IN (1,2,3,4,5,6);
-- Vitals: MaxStamina(3)/MaxMana(5) -> 100000 (HP untouched)
UPDATE `weenie_properties_attribute_2nd` SET `init_Level`=100000, `current_Level`=100000 WHERE `object_Id`=730000200 AND `type` IN (3,5);

-- Skills (s_a_c=3 specialized). Defense 6/7/15=100, attack 41/44/45/46/47=75000, magic 16/33/34/43=100
INSERT INTO `weenie_properties_skill` (`object_Id`,`type`,`level_From_P_P`,`s_a_c`,`p_p`,`init_Level`,`resistance_At_Last_Check`,`last_Used_Time`) VALUES
  (730000200, 6,0,3,0,100,0,0),(730000200, 7,0,3,0,100,0,0),(730000200,15,0,3,0,100,0,0),
  (730000200,41,0,3,0,75000,0,0),(730000200,44,0,3,0,75000,0,0),(730000200,45,0,3,0,75000,0,0),(730000200,46,0,3,0,75000,0,0),(730000200,47,0,3,0,75000,0,0),
  (730000200,16,0,3,0,100,0,0),(730000200,33,0,3,0,100,0,0),(730000200,34,0,3,0,100,0,0),(730000200,43,0,3,0,100,0,0)
ON DUPLICATE KEY UPDATE `init_Level`=VALUES(`init_Level`), `s_a_c`=VALUES(`s_a_c`);

-- DamageRating(307)=10000, DamageResistRating(308)=10000; remove crit/overpower ratings
INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES (730000200,307,10000),(730000200,308,10000)
ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);
DELETE FROM `weenie_properties_int` WHERE `object_Id`=730000200 AND `type` IN (315,316,317,386,387);

-- Neutral resists (64-70,166) and ArmorModVs (13-19,165) = 1.0
INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`) VALUES
  (730000200,64,1.0),(730000200,65,1.0),(730000200,66,1.0),(730000200,67,1.0),(730000200,68,1.0),(730000200,69,1.0),(730000200,70,1.0),(730000200,166,1.0),
  (730000200,13,1.0),(730000200,14,1.0),(730000200,15,1.0),(730000200,16,1.0),(730000200,17,1.0),(730000200,18,1.0),(730000200,19,1.0),(730000200,165,1.0)
ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);

-- Body armor: uniform 1000 AL (all body parts)
UPDATE `weenie_properties_body_part` SET `base_Armor`=1000 WHERE `object_Id`=730000200;

-- ============================================================ MINION 730000201 ============================================================
UPDATE `weenie_properties_attribute` SET `init_Level`=400 WHERE `object_Id`=730000201 AND `type` IN (1,2,3,4,5,6);
UPDATE `weenie_properties_attribute_2nd` SET `init_Level`=100000, `current_Level`=100000 WHERE `object_Id`=730000201 AND `type` IN (3,5);

INSERT INTO `weenie_properties_skill` (`object_Id`,`type`,`level_From_P_P`,`s_a_c`,`p_p`,`init_Level`,`resistance_At_Last_Check`,`last_Used_Time`) VALUES
  (730000201, 6,0,3,0,100,0,0),(730000201, 7,0,3,0,100,0,0),(730000201,15,0,3,0,100,0,0),
  (730000201,41,0,3,0,75000,0,0),(730000201,44,0,3,0,75000,0,0),(730000201,45,0,3,0,75000,0,0),(730000201,46,0,3,0,75000,0,0),(730000201,47,0,3,0,75000,0,0),
  (730000201,16,0,3,0,100,0,0),(730000201,33,0,3,0,100,0,0),(730000201,34,0,3,0,100,0,0),(730000201,43,0,3,0,100,0,0)
ON DUPLICATE KEY UPDATE `init_Level`=VALUES(`init_Level`), `s_a_c`=VALUES(`s_a_c`);

INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES (730000201,307,10000),(730000201,308,10000)
ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);
DELETE FROM `weenie_properties_int` WHERE `object_Id`=730000201 AND `type` IN (315,316,317,386,387);

INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`) VALUES
  (730000201,64,1.0),(730000201,65,1.0),(730000201,66,1.0),(730000201,67,1.0),(730000201,68,1.0),(730000201,69,1.0),(730000201,70,1.0),(730000201,166,1.0),
  (730000201,13,1.0),(730000201,14,1.0),(730000201,15,1.0),(730000201,16,1.0),(730000201,17,1.0),(730000201,18,1.0),(730000201,19,1.0),(730000201,165,1.0)
ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);

UPDATE `weenie_properties_body_part` SET `base_Armor`=1000 WHERE `object_Id`=730000201;

SET FOREIGN_KEY_CHECKS = 1;
