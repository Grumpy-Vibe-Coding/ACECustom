/* Invasion boss clone — source WCID 8814692 -> 72000011 (Invasion Empyrean Shade) */

DELETE FROM `weenie` WHERE `class_Id` = 72000011;
DELETE FROM `weenie_properties_anim_part` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_attribute` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_attribute_2nd` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_body_part` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_book` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_book_page_data` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_create_list` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_emote` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_event_filter` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_float` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_int` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_int64` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_i_i_d` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_palette` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_position` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_skill` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_spell_book` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_string` WHERE `object_Id` = 72000011;
DELETE FROM `weenie_properties_texture_map` WHERE `object_Id` = 72000011;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES
(72000011, 'invasion72000011', 10, '2021-11-01 00:00:00');

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`) VALUES
(72000011, 1, 1600, 0, 0),
(72000011, 2, 2600, 0, 0),
(72000011, 3, 40, 0, 0),
(72000011, 4, 1600, 0, 0),
(72000011, 5, 3220, 0, 0),
(72000011, 6, 3220, 0, 0);

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`) VALUES
(72000011, 1, 3480000, 0, 0, 3490000),
(72000011, 3, 180000, 0, 0, 200000),
(72000011, 5, 900000, 0, 0, 100000);

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`) VALUES
(72000011, 0, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 1, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0),
(72000011, 1, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0),
(72000011, 2, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0),
(72000011, 3, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 1, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0),
(72000011, 4, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0),
(72000011, 5, 4, 400, 0.75, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0),
(72000011, 6, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18),
(72000011, 7, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6),
(72000011, 8, 4, 400, 0.75, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22);

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES
(72000011, 1, 1),
(72000011, 6, 0),
(72000011, 11, 0),
(72000011, 12, 1),
(72000011, 13, 0),
(72000011, 14, 1),
(72000011, 19, 1),
(72000011, 50, 1),
(72000011, 65, 1);

INSERT INTO `weenie_properties_create_list` (`object_Id`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`) VALUES
(72000011, 9, 8814700, 0, 0, 1.0, 0),
(72000011, 9, 8814700, 0, 0, 1.0, 0),
(72000011, 9, 8814700, 0, 0, 1.0, 0),
(72000011, 9, 8814700, 0, 0, 1.0, 0),
(72000011, 9, 8814700, 0, 0, 1.0, 0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(72000011, 1, 33561104),
(72000011, 2, 150995463),
(72000011, 3, 536870914),
(72000011, 4, 805306398),
(72000011, 6, 67108990),
(72000011, 7, 268437437),
(72000011, 8, 100691500),
(72000011, 22, 872415236);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES
(72000011, 1, 5.0),
(72000011, 2, 0.0),
(72000011, 3, 2.0),
(72000011, 4, 5.0),
(72000011, 5, 1.0),
(72000011, 13, 0.3),
(72000011, 14, 0.3),
(72000011, 15, 0.3),
(72000011, 16, 0.3),
(72000011, 17, 0.3),
(72000011, 18, 0.3),
(72000011, 19, 0.3),
(72000011, 31, 20.0),
(72000011, 39, 2.0),
(72000011, 64, 0.3),
(72000011, 65, 0.3),
(72000011, 66, 0.3),
(72000011, 67, 0.3),
(72000011, 68, 0.3),
(72000011, 69, 0.3),
(72000011, 70, 0.3),
(72000011, 71, 0.3),
(72000011, 72, 0.2),
(72000011, 73, 0.2),
(72000011, 74, 1.0),
(72000011, 75, 1.0),
(72000011, 76, 0.5),
(72000011, 80, 3.0),
(72000011, 104, 20.0),
(72000011, 117, 0.5),
(72000011, 122, 2.0),
(72000011, 125, 1.0),
(72000011, 165, 0.2),
(72000011, 166, 0.01);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(72000011, 1, 16),
(72000011, 2, 51),
(72000011, 3, 22),
(72000011, 6, -1),
(72000011, 7, -1),
(72000011, 16, 32),
(72000011, 27, 0),
(72000011, 40, 2),
(72000011, 68, 5),
(72000011, 72, 84),
(72000011, 93, 1032),
(72000011, 133, 2),
(72000011, 146, 900000000),
(72000011, 307, 675),
(72000011, 308, 300),
(72000011, 331, 700),
(72000011, 332, 0),
(72000011, 350, 900),
(72000011, 351, 900),
(72000011, 386, 250),
(72000011, 25, 1100);

INSERT INTO `weenie_properties_int64` (`object_Id`, `type`, `value`) VALUES
(72000011, 9007, 1200),
(72000011, 9009, 1300),
(72000011, 9011, 600);

INSERT INTO `weenie_properties_skill` (`object_Id`, `type`, `level_From_P_P`, `s_a_c`, `p_p`, `init_Level`, `resistance_At_Last_Check`, `last_Used_Time`) VALUES
(72000011, 6, 0, 3, 0, 3200, 0, 0.0),
(72000011, 7, 0, 3, 0, 3000, 0, 0.0),
(72000011, 15, 0, 3, 0, 2500, 0, 0.0),
(72000011, 20, 0, 2, 0, 2500, 0, 0.0),
(72000011, 22, 0, 2, 0, 1000, 0, 0.0),
(72000011, 24, 0, 2, 0, 100, 0, 0.0),
(72000011, 33, 0, 3, 0, 9000, 0, 0.0),
(72000011, 43, 0, 3, 0, 9000, 0, 0.0),
(72000011, 44, 0, 3, 0, 50000, 0, 0.0),
(72000011, 45, 0, 3, 0, 50000, 0, 0.0),
(72000011, 47, 0, 3, 0, 50000, 0, 0.0);

INSERT INTO `weenie_properties_spell_book` (`object_Id`, `spell`, `probability`) VALUES
(72000011, 1838, 2.16),
(72000011, 2708, 2.16),
(72000011, 3051, 2.16),
(72000011, 3210, 2.16);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(72000011, 1, 'Invasion Empyrean Shade'),
(72000011, 5, 'Sorcerer');
