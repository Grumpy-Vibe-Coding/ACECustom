/* Invasion boss clone — source WCID 8814691 -> 72000010 (Invasion Empyrean Apparition) */

DELETE FROM `weenie` WHERE `class_Id` = 72000010;
DELETE FROM `weenie_properties_anim_part` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_attribute` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_attribute_2nd` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_body_part` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_book` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_book_page_data` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_create_list` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_emote` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_event_filter` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_float` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_int` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_int64` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_i_i_d` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_palette` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_position` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_skill` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_spell_book` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_string` WHERE `object_Id` = 72000010;
DELETE FROM `weenie_properties_texture_map` WHERE `object_Id` = 72000010;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES
(72000010, 'invasion72000010', 10, '2021-11-01 00:00:00');

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`) VALUES
(72000010, 1, 1600, 0, 0),
(72000010, 2, 2600, 0, 0),
(72000010, 3, 40, 0, 0),
(72000010, 4, 1600, 0, 0),
(72000010, 5, 3220, 0, 0),
(72000010, 6, 3220, 0, 0);

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`) VALUES
(72000010, 1, 3480000, 0, 0, 3490000),
(72000010, 3, 180000, 0, 0, 200000),
(72000010, 5, 900000, 0, 0, 100000);

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`) VALUES
(72000010, 0, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 1, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0, 0.33, 0.0, 0.0),
(72000010, 1, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0, 0.44, 0.17, 0.0),
(72000010, 2, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0, 0.0, 0.17, 0.0),
(72000010, 3, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 1, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0, 0.23, 0.03, 0.0),
(72000010, 4, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0, 0.0, 0.3, 0.0),
(72000010, 5, 4, 400, 0.75, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 2, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0, 0.0, 0.2, 0.0),
(72000010, 6, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18, 0.0, 0.13, 0.18),
(72000010, 7, 4, 0, 0.0, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6, 0.0, 0.0, 0.6),
(72000010, 8, 4, 400, 0.75, 1550, 775, 775, 775, 775, 775, 775, 775, 0, 3, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22, 0.0, 0.0, 0.22);

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES
(72000010, 1, 1),
(72000010, 6, 0),
(72000010, 11, 0),
(72000010, 12, 1),
(72000010, 13, 0),
(72000010, 14, 1),
(72000010, 19, 1),
(72000010, 50, 1),
(72000010, 65, 1);

INSERT INTO `weenie_properties_create_list` (`object_Id`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`) VALUES
(72000010, 9, 8814700, 0, 0, 1.0, 0),
(72000010, 9, 8814700, 0, 0, 1.0, 0),
(72000010, 9, 8814700, 0, 0, 1.0, 0),
(72000010, 9, 8814700, 0, 0, 1.0, 0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(72000010, 1, 33561104),
(72000010, 2, 150995463),
(72000010, 3, 536870914),
(72000010, 4, 805306398),
(72000010, 6, 67108990),
(72000010, 7, 268437437),
(72000010, 8, 100689361),
(72000010, 22, 872415236);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES
(72000010, 1, 5.0),
(72000010, 2, 0.0),
(72000010, 3, 2.0),
(72000010, 4, 5.0),
(72000010, 5, 1.0),
(72000010, 13, 0.2),
(72000010, 14, 0.2),
(72000010, 15, 0.2),
(72000010, 16, 0.2),
(72000010, 17, 0.2),
(72000010, 18, 0.2),
(72000010, 19, 0.2),
(72000010, 31, 20.0),
(72000010, 39, 2.0),
(72000010, 64, 0.2),
(72000010, 65, 0.2),
(72000010, 66, 0.2),
(72000010, 67, 0.2),
(72000010, 68, 0.2),
(72000010, 69, 0.2),
(72000010, 70, 0.2),
(72000010, 71, 0.2),
(72000010, 72, 0.2),
(72000010, 73, 0.2),
(72000010, 74, 1.0),
(72000010, 75, 1.0),
(72000010, 76, 0.5),
(72000010, 80, 3.0),
(72000010, 104, 20.0),
(72000010, 117, 0.5),
(72000010, 122, 2.0),
(72000010, 125, 1.0),
(72000010, 165, 0.2),
(72000010, 166, 0.01);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(72000010, 1, 16),
(72000010, 2, 51),
(72000010, 3, 61),
(72000010, 6, -1),
(72000010, 7, -1),
(72000010, 16, 32),
(72000010, 27, 0),
(72000010, 40, 2),
(72000010, 68, 5),
(72000010, 72, 84),
(72000010, 93, 1032),
(72000010, 133, 2),
(72000010, 146, 900000000),
(72000010, 307, 975),
(72000010, 308, 300),
(72000010, 331, 700),
(72000010, 332, 0),
(72000010, 350, 900),
(72000010, 351, 900),
(72000010, 386, 250),
(72000010, 25, 1100);

INSERT INTO `weenie_properties_skill` (`object_Id`, `type`, `level_From_P_P`, `s_a_c`, `p_p`, `init_Level`, `resistance_At_Last_Check`, `last_Used_Time`) VALUES
(72000010, 6, 0, 3, 0, 3200, 0, 0.0),
(72000010, 7, 0, 3, 0, 3000, 0, 0.0),
(72000010, 15, 0, 3, 0, 2500, 0, 0.0),
(72000010, 20, 0, 2, 0, 2500, 0, 0.0),
(72000010, 22, 0, 2, 0, 1000, 0, 0.0),
(72000010, 24, 0, 2, 0, 100, 0, 0.0),
(72000010, 33, 0, 3, 0, 9000, 0, 0.0),
(72000010, 43, 0, 3, 0, 9000, 0, 0.0),
(72000010, 44, 0, 3, 0, 50000, 0, 0.0),
(72000010, 45, 0, 3, 0, 50000, 0, 0.0),
(72000010, 47, 0, 3, 0, 50000, 0, 0.0);

INSERT INTO `weenie_properties_spell_book` (`object_Id`, `spell`, `probability`) VALUES
(72000010, 5348, 2.15),
(72000010, 5368, 2.15),
(72000010, 5402, 2.15);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(72000010, 1, 'Invasion Empyrean Apparition'),
(72000010, 5, 'Manipulator');
