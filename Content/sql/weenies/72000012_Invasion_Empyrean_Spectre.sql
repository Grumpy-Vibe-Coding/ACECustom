/* Invasion boss clone — source WCID 8814693 -> 72000012 (Invasion Empyrean Spectre) */

DELETE FROM `weenie` WHERE `class_Id` = 72000012;
DELETE FROM `weenie_properties_anim_part` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_attribute` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_attribute_2nd` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_body_part` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_book` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_book_page_data` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_create_list` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_emote` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_event_filter` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_float` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_int` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_int64` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_i_i_d` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_palette` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_position` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_skill` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_spell_book` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_string` WHERE `object_Id` = 72000012;
DELETE FROM `weenie_properties_texture_map` WHERE `object_Id` = 72000012;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES
(72000012, 'invasion72000012', 10, '2021-11-01 00:00:00');

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`) VALUES
(72000012, 1, 1600, 0, 0),
(72000012, 2, 2600, 0, 0),
(72000012, 3, 40, 0, 0),
(72000012, 4, 1600, 0, 0),
(72000012, 5, 3220, 0, 0),
(72000012, 6, 3220, 0, 0);

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`) VALUES
(72000012, 1, 3480000, 0, 0, 3490000),
(72000012, 3, 180000, 0, 0, 200000),
(72000012, 5, 900000, 0, 0, 100000);

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`) VALUES
(72000012, 0, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 1, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0),
(72000012, 1, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0),
(72000012, 2, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0),
(72000012, 3, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 1, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0),
(72000012, 4, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0),
(72000012, 5, 4, 400, 0.75, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0),
(72000012, 6, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18),
(72000012, 7, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6),
(72000012, 8, 4, 400, 0.75, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22);

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES
(72000012, 1, 1),
(72000012, 6, 0),
(72000012, 11, 0),
(72000012, 12, 1),
(72000012, 13, 0),
(72000012, 14, 1),
(72000012, 19, 1),
(72000012, 50, 1);

INSERT INTO `weenie_properties_create_list` (`object_Id`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`) VALUES
(72000012, 2, 8814688, 0, 0, -1.0, 0),
(72000012, 2, 15443, 500, 100, -1.0, 0),
(72000012, 9, 8814700, 0, 0, 1.0, 0),
(72000012, 9, 8814700, 0, 0, 1.0, 0),
(72000012, 9, 8814700, 0, 0, 1.0, 0),
(72000012, 9, 8814700, 0, 0, 1.0, 0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(72000012, 1, 33561104),
(72000012, 2, 150995463),
(72000012, 3, 536870914),
(72000012, 4, 805306398),
(72000012, 6, 67108990),
(72000012, 7, 268437437),
(72000012, 8, 100691500),
(72000012, 22, 872415236);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES
(72000012, 1, 5.0),
(72000012, 2, 0.0),
(72000012, 3, 2.0),
(72000012, 4, 5.0),
(72000012, 5, 1.0),
(72000012, 13, 0.2),
(72000012, 14, 0.2),
(72000012, 15, 0.2),
(72000012, 16, 0.2),
(72000012, 17, 0.2),
(72000012, 18, 0.2),
(72000012, 19, 0.2),
(72000012, 31, 20.0),
(72000012, 39, 2.0),
(72000012, 64, 0.2),
(72000012, 65, 0.2),
(72000012, 66, 0.2),
(72000012, 67, 0.2),
(72000012, 68, 0.2),
(72000012, 69, 0.2),
(72000012, 70, 0.2),
(72000012, 71, 0.2),
(72000012, 72, 0.2),
(72000012, 73, 0.2),
(72000012, 74, 1.0),
(72000012, 75, 1.0),
(72000012, 76, 0.5),
(72000012, 80, 2.0),
(72000012, 104, 20.0),
(72000012, 117, 0.5),
(72000012, 122, 2.0),
(72000012, 125, 1.0),
(72000012, 165, 0.01),
(72000012, 166, 0.001);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(72000012, 1, 16),
(72000012, 2, 51),
(72000012, 3, 82),
(72000012, 6, -1),
(72000012, 7, -1),
(72000012, 16, 32),
(72000012, 27, 0),
(72000012, 40, 2),
(72000012, 68, 5),
(72000012, 72, 84),
(72000012, 93, 1032),
(72000012, 101, 524288),
(72000012, 133, 2),
(72000012, 146, 900000000),
(72000012, 307, 825),
(72000012, 308, 300),
(72000012, 331, 900),
(72000012, 332, 0),
(72000012, 350, 900),
(72000012, 351, 900),
(72000012, 386, 250),
(72000012, 25, 1100);

INSERT INTO `weenie_properties_int64` (`object_Id`, `type`, `value`) VALUES
(72000012, 9007, 1200),
(72000012, 9009, 900),
(72000012, 9011, 130);

INSERT INTO `weenie_properties_skill` (`object_Id`, `type`, `level_From_P_P`, `s_a_c`, `p_p`, `init_Level`, `resistance_At_Last_Check`, `last_Used_Time`) VALUES
(72000012, 6, 0, 3, 0, 3200, 0, 0.0),
(72000012, 7, 0, 3, 0, 3000, 0, 0.0),
(72000012, 15, 0, 3, 0, 2500, 0, 0.0),
(72000012, 20, 0, 2, 0, 2500, 0, 0.0),
(72000012, 22, 0, 2, 0, 1000, 0, 0.0),
(72000012, 24, 0, 2, 0, 100, 0, 0.0),
(72000012, 33, 0, 3, 0, 9000, 0, 0.0),
(72000012, 43, 0, 3, 0, 9000, 0, 0.0),
(72000012, 44, 0, 3, 0, 50000, 0, 0.0),
(72000012, 45, 0, 3, 0, 50000, 0, 0.0),
(72000012, 47, 0, 3, 0, 50000, 0, 0.0);

INSERT INTO `weenie_properties_spell_book` (`object_Id`, `spell`, `probability`) VALUES
(72000012, 4483, 2.12);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(72000012, 1, 'Invasion Empyrean Spectre'),
(72000012, 5, 'Defender');
