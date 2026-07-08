-- 730000200 T11 Test Dummy Camp ("Patient Zero").sql
-- Self-contained test-dummy boss camp, cloned from the shadow camp, to use as the TEMPLATE
-- for all other T11 mobs and as a stable tuning target.
--   730000200 = boss  "Patient Zero"        (clone of 730000101 Umbral Tyrant, keeps IsEmpowerSource)
--   730000201 = minion "Patient Zero Minion"  (clone of 730000023 Shadow Vortex, CanBeEmpowered)
--   730000202 = camp generator               (clone of 730000087 shadow camp gen)
-- Camp gen spawns 1 boss (prob -1, 1/1) + variation-scaled minions (prob 1.0, init 1 / max -1).
-- Fresh empower group 18 (won't cross-empower real shadow mobs). The four v11 combat systems
-- (percent-HP floor, vuln compression, per-tier scaling, damage-taken mitigation) auto-apply at variation>=11.
-- SPAWN LIVE: stand anywhere in T11 (variation>=11) and  /createinst 730000202
-- Idempotent: re-run safe (DELETE + INSERT...SELECT from the live templates). Generated 2026-07-07.
SET FOREIGN_KEY_CHECKS = 0;

-- ===== 0. Clean any prior copies =====
DELETE FROM `weenie`                          WHERE `class_Id`  IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_attribute`     WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_attribute_2nd` WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_body_part`     WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_bool`          WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_create_list`   WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_d_i_d`         WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_float`         WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_int`           WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_int64`         WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_skill`         WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_spell_book`    WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_string`        WHERE `object_Id` IN (730000200,730000201,730000202);
DELETE FROM `weenie_properties_generator`     WHERE `object_Id` IN (730000200,730000201,730000202);

-- ===== 1. BOSS 730000200  <-  730000101 (Umbral Tyrant) =====
INSERT INTO `weenie` (`class_Id`,`class_Name`,`type`,`last_Modified`)
  SELECT 730000200,'t11_test_dummy_boss',`type`,NOW() FROM `weenie` WHERE `class_Id`=730000101;
INSERT INTO `weenie_properties_attribute` (`object_Id`,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent`)
  SELECT 730000200,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent` FROM `weenie_properties_attribute` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent`,`current_Level`)
  SELECT 730000200,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent`,`current_Level` FROM `weenie_properties_attribute_2nd` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_body_part` (`object_Id`,`key`,`d_Type`,`d_Val`,`d_Var`,`base_Armor`,`armor_Vs_Slash`,`armor_Vs_Pierce`,`armor_Vs_Bludgeon`,`armor_Vs_Cold`,`armor_Vs_Fire`,`armor_Vs_Acid`,`armor_Vs_Electric`,`armor_Vs_Nether`,`b_h`,`h_l_f`,`m_l_f`,`l_l_f`,`h_r_f`,`m_r_f`,`l_r_f`,`h_l_b`,`m_l_b`,`l_l_b`,`h_r_b`,`m_r_b`,`l_r_b`)
  SELECT 730000200,`key`,`d_Type`,`d_Val`,`d_Var`,`base_Armor`,`armor_Vs_Slash`,`armor_Vs_Pierce`,`armor_Vs_Bludgeon`,`armor_Vs_Cold`,`armor_Vs_Fire`,`armor_Vs_Acid`,`armor_Vs_Electric`,`armor_Vs_Nether`,`b_h`,`h_l_f`,`m_l_f`,`l_l_f`,`h_r_f`,`m_r_f`,`l_r_f`,`h_l_b`,`m_l_b`,`l_l_b`,`h_r_b`,`m_r_b`,`l_r_b` FROM `weenie_properties_body_part` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_bool` (`object_Id`,`type`,`value`)
  SELECT 730000200,`type`,`value` FROM `weenie_properties_bool` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_create_list` (`object_Id`,`destination_Type`,`weenie_Class_Id`,`stack_Size`,`palette`,`shade`,`try_To_Bond`)
  SELECT 730000200,`destination_Type`,`weenie_Class_Id`,`stack_Size`,`palette`,`shade`,`try_To_Bond` FROM `weenie_properties_create_list` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_d_i_d` (`object_Id`,`type`,`value`)
  SELECT 730000200,`type`,`value` FROM `weenie_properties_d_i_d` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`)
  SELECT 730000200,`type`,`value` FROM `weenie_properties_float` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`)
  SELECT 730000200,`type`,`value` FROM `weenie_properties_int` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_int64` (`object_Id`,`type`,`value`)
  SELECT 730000200,`type`,`value` FROM `weenie_properties_int64` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_skill` (`object_Id`,`type`,`level_From_P_P`,`s_a_c`,`p_p`,`init_Level`,`resistance_At_Last_Check`,`last_Used_Time`)
  SELECT 730000200,`type`,`level_From_P_P`,`s_a_c`,`p_p`,`init_Level`,`resistance_At_Last_Check`,`last_Used_Time` FROM `weenie_properties_skill` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_spell_book` (`object_Id`,`spell`,`probability`)
  SELECT 730000200,`spell`,`probability` FROM `weenie_properties_spell_book` WHERE `object_Id`=730000101;
INSERT INTO `weenie_properties_string` (`object_Id`,`type`,`value`)
  SELECT 730000200,`type`,`value` FROM `weenie_properties_string` WHERE `object_Id`=730000101;

-- ===== 2. MINION 730000201  <-  730000023 (Shadow Vortex) =====
INSERT INTO `weenie` (`class_Id`,`class_Name`,`type`,`last_Modified`)
  SELECT 730000201,'t11_test_dummy_minion',`type`,NOW() FROM `weenie` WHERE `class_Id`=730000023;
INSERT INTO `weenie_properties_attribute` (`object_Id`,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent`)
  SELECT 730000201,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent` FROM `weenie_properties_attribute` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent`,`current_Level`)
  SELECT 730000201,`type`,`init_Level`,`level_From_C_P`,`c_P_Spent`,`current_Level` FROM `weenie_properties_attribute_2nd` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_body_part` (`object_Id`,`key`,`d_Type`,`d_Val`,`d_Var`,`base_Armor`,`armor_Vs_Slash`,`armor_Vs_Pierce`,`armor_Vs_Bludgeon`,`armor_Vs_Cold`,`armor_Vs_Fire`,`armor_Vs_Acid`,`armor_Vs_Electric`,`armor_Vs_Nether`,`b_h`,`h_l_f`,`m_l_f`,`l_l_f`,`h_r_f`,`m_r_f`,`l_r_f`,`h_l_b`,`m_l_b`,`l_l_b`,`h_r_b`,`m_r_b`,`l_r_b`)
  SELECT 730000201,`key`,`d_Type`,`d_Val`,`d_Var`,`base_Armor`,`armor_Vs_Slash`,`armor_Vs_Pierce`,`armor_Vs_Bludgeon`,`armor_Vs_Cold`,`armor_Vs_Fire`,`armor_Vs_Acid`,`armor_Vs_Electric`,`armor_Vs_Nether`,`b_h`,`h_l_f`,`m_l_f`,`l_l_f`,`h_r_f`,`m_r_f`,`l_r_f`,`h_l_b`,`m_l_b`,`l_l_b`,`h_r_b`,`m_r_b`,`l_r_b` FROM `weenie_properties_body_part` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_bool` (`object_Id`,`type`,`value`)
  SELECT 730000201,`type`,`value` FROM `weenie_properties_bool` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_create_list` (`object_Id`,`destination_Type`,`weenie_Class_Id`,`stack_Size`,`palette`,`shade`,`try_To_Bond`)
  SELECT 730000201,`destination_Type`,`weenie_Class_Id`,`stack_Size`,`palette`,`shade`,`try_To_Bond` FROM `weenie_properties_create_list` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_d_i_d` (`object_Id`,`type`,`value`)
  SELECT 730000201,`type`,`value` FROM `weenie_properties_d_i_d` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`)
  SELECT 730000201,`type`,`value` FROM `weenie_properties_float` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`)
  SELECT 730000201,`type`,`value` FROM `weenie_properties_int` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_int64` (`object_Id`,`type`,`value`)
  SELECT 730000201,`type`,`value` FROM `weenie_properties_int64` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_skill` (`object_Id`,`type`,`level_From_P_P`,`s_a_c`,`p_p`,`init_Level`,`resistance_At_Last_Check`,`last_Used_Time`)
  SELECT 730000201,`type`,`level_From_P_P`,`s_a_c`,`p_p`,`init_Level`,`resistance_At_Last_Check`,`last_Used_Time` FROM `weenie_properties_skill` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_spell_book` (`object_Id`,`spell`,`probability`)
  SELECT 730000201,`spell`,`probability` FROM `weenie_properties_spell_book` WHERE `object_Id`=730000023;
INSERT INTO `weenie_properties_string` (`object_Id`,`type`,`value`)
  SELECT 730000201,`type`,`value` FROM `weenie_properties_string` WHERE `object_Id`=730000023;

-- ===== 3. CAMP GEN 730000202  <-  730000087 (shadow camp gen) =====
INSERT INTO `weenie` (`class_Id`,`class_Name`,`type`,`last_Modified`)
  SELECT 730000202,'t11_test_dummy_camp_gen',`type`,NOW() FROM `weenie` WHERE `class_Id`=730000087;
INSERT INTO `weenie_properties_bool` (`object_Id`,`type`,`value`)
  SELECT 730000202,`type`,`value` FROM `weenie_properties_bool` WHERE `object_Id`=730000087;
INSERT INTO `weenie_properties_d_i_d` (`object_Id`,`type`,`value`)
  SELECT 730000202,`type`,`value` FROM `weenie_properties_d_i_d` WHERE `object_Id`=730000087;
INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`)
  SELECT 730000202,`type`,`value` FROM `weenie_properties_float` WHERE `object_Id`=730000087;
INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`)
  SELECT 730000202,`type`,`value` FROM `weenie_properties_int` WHERE `object_Id`=730000087;
INSERT INTO `weenie_properties_string` (`object_Id`,`type`,`value`)
  SELECT 730000202,`type`,`value` FROM `weenie_properties_string` WHERE `object_Id`=730000087;

-- Camp-gen spawn profiles: boss (always) + one minion type (variation-scaled count via C# 50103/50104).
INSERT INTO `weenie_properties_generator`
  (`object_Id`,`probability`,`weenie_Class_Id`,`delay`,`init_Create`,`max_Create`,`when_Create`,`where_Create`,`stack_Size`,`palette_Id`,`shade`,`obj_Cell_Id`,`origin_X`,`origin_Y`,`origin_Z`,`angles_W`,`angles_X`,`angles_Y`,`angles_Z`) VALUES
  (730000202,-1,730000200,300,1, 1,1,2,-1,0,0,0,0,0,0,0,0,0,0),
  (730000202, 1,730000201,300,1,-1,1,2,-1,0,0,0,0,0,0,0,0,0,0);

-- ===== 4. Overrides: names, fresh empower group 18, strip shadow kill-quest tag =====
UPDATE `weenie_properties_string` SET `value`='Patient Zero'          WHERE `object_Id`=730000200 AND `type`=1;
UPDATE `weenie_properties_string` SET `value`='Patient Zero Minion'  WHERE `object_Id`=730000201 AND `type`=1;
UPDATE `weenie_properties_string` SET `value`='Test Dummy Camp Gen'   WHERE `object_Id`=730000202 AND `type`=1;
DELETE FROM `weenie_properties_string` WHERE `object_Id`=730000201 AND `type`=45; -- drop KillTaskShadowVortex tag
UPDATE `weenie_properties_int` SET `value`=18 WHERE `object_Id` IN (730000200,730000201) AND `type`=50102; -- empower group 18

SET FOREIGN_KEY_CHECKS = 1;
