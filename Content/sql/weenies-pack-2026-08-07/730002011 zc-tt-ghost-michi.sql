DELETE FROM `weenie` WHERE `class_Id` = 730002011;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730002011','zc-tt-ghost-michi','10','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`)
VALUES ('730002011','1','80','0','0')
     , ('730002011','2','65','0','0')
     , ('730002011','3','70','0','0')
     , ('730002011','4','75','0','0')
     , ('730002011','5','30','0','0')
     , ('730002011','6','30','0','0');

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`)
VALUES ('730002011','1','5','0','0','38')
     , ('730002011','3','5','0','0','70')
     , ('730002011','5','0','0','0','30');

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`)
VALUES ('730002011','0','4','0','0','0','0','0','0','0','0','0','0','0','1','0.33','0','0','0.33','0','0','0.33','0','0','0.33','0','0')
     , ('730002011','1','4','0','0','0','0','0','0','0','0','0','0','0','2','0.44','0.17','0','0.44','0.17','0','0.44','0.17','0','0.44','0.17','0')
     , ('730002011','2','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0.17','0','0','0.17','0','0','0.17','0','0','0.17','0')
     , ('730002011','3','4','0','0','0','0','0','0','0','0','0','0','0','1','0.23','0.03','0','0.23','0.03','0','0.23','0.03','0','0.23','0.03','0')
     , ('730002011','4','4','0','0','0','0','0','0','0','0','0','0','0','2','0','0.3','0','0','0.3','0','0','0.3','0','0','0.3','0')
     , ('730002011','5','4','2','0.75','0','0','0','0','0','0','0','0','0','2','0','0.2','0','0','0.2','0','0','0.2','0','0','0.2','0')
     , ('730002011','6','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0.13','0.18','0','0.13','0.18','0','0.13','0.18','0','0.13','0.18')
     , ('730002011','7','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0','0.6','0','0','0.6','0','0','0.6','0','0','0.6')
     , ('730002011','8','4','2','0.75','0','0','0','0','0','0','0','0','0','3','0','0','0.22','0','0','0.22','0','0','0.22','0','0','0.22');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730002011','1',1)
     , ('730002011','12',1)
     , ('730002011','13',0)
     , ('730002011','19',0)
     , ('730002011','39',1)
     , ('730002011','41',1);

INSERT INTO `weenie_properties_create_list` (`object_Id`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`)
VALUES ('730002011','2','134','0','5','0',0)
     , ('730002011','2','117','0','5','0',0)
     , ('730002011','2','115','0','9','1',0)
     , ('730002011','2','10696','0','18','1',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730002011','1','33554510')
     , ('730002011','2','150994945')
     , ('730002011','3','536870914')
     , ('730002011','4','805306368')
     , ('730002011','8','100667446');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730002011','1','5')
     , ('730002011','2','0')
     , ('730002011','3','0.16')
     , ('730002011','4','5')
     , ('730002011','5','1')
     , ('730002011','11','300')
     , ('730002011','13','0.9')
     , ('730002011','14','1')
     , ('730002011','15','1.1')
     , ('730002011','16','0.4')
     , ('730002011','17','0.4')
     , ('730002011','18','1')
     , ('730002011','19','0.6')
     , ('730002011','37','0.9')
     , ('730002011','38','1.55')
     , ('730002011','54','3')
     , ('730002011','64','1')
     , ('730002011','65','1')
     , ('730002011','66','1')
     , ('730002011','67','1')
     , ('730002011','68','1')
     , ('730002011','69','1')
     , ('730002011','70','1')
     , ('730002011','71','1')
     , ('730002011','72','1')
     , ('730002011','73','1')
     , ('730002011','74','1')
     , ('730002011','75','1')
     , ('730002011','104','10')
     , ('730002011','125','1')
     , ('730002011','76','0.5');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730002011','1','16')
     , ('730002011','2','31')
     , ('730002011','6','-1')
     , ('730002011','7','-1')
     , ('730002011','8','120')
     , ('730002011','16','32')
     , ('730002011','25','7')
     , ('730002011','27','0')
     , ('730002011','74','262176')
     , ('730002011','75','0')
     , ('730002011','76','100000')
     , ('730002011','93','2098200')
     , ('730002011','126','125')
     , ('730002011','127','125')
     , ('730002011','133','4')
     , ('730002011','134','16')
     , ('730002011','146','59')
     , ('730002011','95','8');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730002011','1','Ghost of Mi Chi the Barkeep')
     , ('730002011','3','Female')
     , ('730002011','4','Sho')
     , ('730002011','5','Barkeeper')
     , ('730002011','24','Tou-Tou');

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','7','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','12','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '1','21','0','1',NULL,'ZC_TouTou_A2Completed@wake1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','13','1',NULL,NULL,NULL,'ZC_TouTou_A2Completed@wake1',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','8','0','1',NULL,'A pale shape wavers in the doorway of a ruined taproom, unseeing. Something binds it here still.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','12','1',NULL,NULL,NULL,'ZC_TouTou_A2Completed@wake1',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','21','0','1',NULL,'ZC_TouTou_C1Completed@cool',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','12','1',NULL,NULL,NULL,'ZC_TouTou_C1Completed@cool',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','10','0','1',NULL,'The cask sits where it belongs. Come back tomorrow - a tavern is never stocked for long.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','13','1',NULL,NULL,NULL,'ZC_TouTou_C1Completed@cool',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','76','0','1',NULL,'ZC_TouTou_C1@have',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'730003003','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','23','1',NULL,NULL,NULL,'ZC_TouTou_C1@have',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','10','0','1',NULL,'My tavern poured for the whole village, living or otherwise. The plunderers rolled my last cask out my own door. Their crews camp on every beach - try the southeast strand near 31.8S, 96.7E, or the southwest sands by 31.8S, 91.8E. Bring it back - water-stained or no, it is mine to pour.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002011','22','1',NULL,NULL,NULL,'ZC_TouTou_C1@have',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','74','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'730003003','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '1','10','0','1',NULL,'Ha! Still sloshes. Tonight the dead drink better - and the living get their share too.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '2','3','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'300000','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '3','22','0','1',NULL,'ZC_TouTou_C1Completed',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
