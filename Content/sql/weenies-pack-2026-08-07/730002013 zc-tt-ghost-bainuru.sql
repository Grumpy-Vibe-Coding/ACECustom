DELETE FROM `weenie` WHERE `class_Id` = 730002013;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730002013','zc-tt-ghost-bainuru','10','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`)
VALUES ('730002013','1','90','0','0')
     , ('730002013','2','80','0','0')
     , ('730002013','3','55','0','0')
     , ('730002013','4','60','0','0')
     , ('730002013','5','30','0','0')
     , ('730002013','6','25','0','0');

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`)
VALUES ('730002013','1','15','0','0','55')
     , ('730002013','3','10','0','0','90')
     , ('730002013','5','10','0','0','35');

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`)
VALUES ('730002013','0','4','0','0','0','0','0','0','0','0','0','0','0','1','0.33','0','0','0.33','0','0','0.33','0','0','0.33','0','0')
     , ('730002013','1','4','0','0','0','0','0','0','0','0','0','0','0','2','0.44','0.17','0','0.44','0.17','0','0.44','0.17','0','0.44','0.17','0')
     , ('730002013','2','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0.17','0','0','0.17','0','0','0.17','0','0','0.17','0')
     , ('730002013','3','4','0','0','0','0','0','0','0','0','0','0','0','1','0.23','0.03','0','0.23','0.03','0','0.23','0.03','0','0.23','0.03','0')
     , ('730002013','4','4','0','0','0','0','0','0','0','0','0','0','0','2','0','0.3','0','0','0.3','0','0','0.3','0','0','0.3','0')
     , ('730002013','5','4','2','0.75','0','0','0','0','0','0','0','0','0','2','0','0.2','0','0','0.2','0','0','0.2','0','0','0.2','0')
     , ('730002013','6','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0.13','0.18','0','0.13','0.18','0','0.13','0.18','0','0.13','0.18')
     , ('730002013','7','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0','0.6','0','0','0.6','0','0','0.6','0','0','0.6')
     , ('730002013','8','4','2','0.75','0','0','0','0','0','0','0','0','0','3','0','0','0.22','0','0','0.22','0','0','0.22','0','0','0.22');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730002013','1',1)
     , ('730002013','12',1)
     , ('730002013','13',0)
     , ('730002013','19',0)
     , ('730002013','39',1)
     , ('730002013','41',1);

INSERT INTO `weenie_properties_create_list` (`object_Id`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`)
VALUES ('730002013','2','356','0','0','0',0)
     , ('730002013','2','124','0','5','0',0)
     , ('730002013','2','117','0','2','0.67',0)
     , ('730002013','2','132','0','2','0.67',0)
     , ('730002013','2','10696','0','4','0.8',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730002013','1','33554510')
     , ('730002013','2','150994945')
     , ('730002013','3','536870914')
     , ('730002013','4','805306368')
     , ('730002013','8','100667446');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730002013','1','5')
     , ('730002013','2','0')
     , ('730002013','3','0.16')
     , ('730002013','4','5')
     , ('730002013','5','1')
     , ('730002013','11','300')
     , ('730002013','13','0.9')
     , ('730002013','14','1')
     , ('730002013','15','1.1')
     , ('730002013','16','0.4')
     , ('730002013','17','0.4')
     , ('730002013','18','1')
     , ('730002013','19','0.6')
     , ('730002013','37','0.9')
     , ('730002013','38','1.55')
     , ('730002013','54','3')
     , ('730002013','64','1')
     , ('730002013','65','1')
     , ('730002013','66','1')
     , ('730002013','67','1')
     , ('730002013','68','1')
     , ('730002013','69','1')
     , ('730002013','70','1')
     , ('730002013','71','1')
     , ('730002013','72','1')
     , ('730002013','73','1')
     , ('730002013','74','1')
     , ('730002013','75','1')
     , ('730002013','104','10')
     , ('730002013','125','1')
     , ('730002013','76','0.5');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730002013','1','16')
     , ('730002013','2','31')
     , ('730002013','6','-1')
     , ('730002013','7','-1')
     , ('730002013','8','120')
     , ('730002013','16','32')
     , ('730002013','25','7')
     , ('730002013','27','0')
     , ('730002013','74','1074022279')
     , ('730002013','75','0')
     , ('730002013','76','100000')
     , ('730002013','93','2098200')
     , ('730002013','126','2000')
     , ('730002013','127','1000')
     , ('730002013','133','4')
     , ('730002013','134','16')
     , ('730002013','146','80')
     , ('730002013','95','8');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730002013','1','Ghost of Bai-Nu Ru the Weaponsmith')
     , ('730002013','3','Female')
     , ('730002013','4','Sho')
     , ('730002013','5','Weaponsmith')
     , ('730002013','24','Tou-Tou');

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','7','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','12','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '1','21','0','1',NULL,'ZC_TouTou_A2Completed@wake3',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','13','1',NULL,NULL,NULL,'ZC_TouTou_A2Completed@wake3',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','8','0','1',NULL,'A pale shape stands at a cold anvil, hammering nothing. The blows make no sound.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','12','1',NULL,NULL,NULL,'ZC_TouTou_A2Completed@wake3',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','21','0','1',NULL,'ZC_TouTou_C3Completed@cool',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','12','1',NULL,NULL,NULL,'ZC_TouTou_C3Completed@cool',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','10','0','1',NULL,'My ghost-forge glows yet with your last delivery. Bring more fragments tomorrow.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','13','1',NULL,NULL,NULL,'ZC_TouTou_C3Completed@cool',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','76','0','1',NULL,'ZC_TouTou_C3@have',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'730003006','4',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','23','1',NULL,NULL,NULL,'ZC_TouTou_C3@have',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','10','0','1',NULL,'I forged for this village until the Shadows came for my steel. Their camps litter the black rock - this ruined village, 28.2S, 96.1E, and the obsidian rise west of it, 28.0S, 95.3E. The fragments they leave are metal that remembers being worked wrong. Bring me four.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002013','22','1',NULL,NULL,NULL,'ZC_TouTou_C3@have',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','74','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'730003006','4',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '1','10','0','1',NULL,'Wrong-worked steel can be worked right again. A hilt takes shape already - my forge remembers, even if my hands are smoke.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '2','3','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'300000','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '3','22','0','1',NULL,'ZC_TouTou_C3Completed',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
