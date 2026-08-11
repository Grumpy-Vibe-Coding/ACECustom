DELETE FROM `weenie` WHERE `class_Id` = 730002015;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730002015','zc-tt-ghost-lodaiou','10','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`)
VALUES ('730002015','1','60','0','0')
     , ('730002015','2','50','0','0')
     , ('730002015','3','60','0','0')
     , ('730002015','4','70','0','0')
     , ('730002015','5','30','0','0')
     , ('730002015','6','30','0','0');

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`)
VALUES ('730002015','1','5','0','0','30')
     , ('730002015','3','5','0','0','55')
     , ('730002015','5','5','0','0','35');

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`)
VALUES ('730002015','0','4','0','0','0','0','0','0','0','0','0','0','0','1','0.33','0','0','0.33','0','0','0.33','0','0','0.33','0','0')
     , ('730002015','1','4','0','0','0','0','0','0','0','0','0','0','0','2','0.44','0.17','0','0.44','0.17','0','0.44','0.17','0','0.44','0.17','0')
     , ('730002015','2','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0.17','0','0','0.17','0','0','0.17','0','0','0.17','0')
     , ('730002015','3','4','0','0','0','0','0','0','0','0','0','0','0','1','0.23','0.03','0','0.23','0.03','0','0.23','0.03','0','0.23','0.03','0')
     , ('730002015','4','4','0','0','0','0','0','0','0','0','0','0','0','2','0','0.3','0','0','0.3','0','0','0.3','0','0','0.3','0')
     , ('730002015','5','4','2','0.75','0','0','0','0','0','0','0','0','0','2','0','0.2','0','0','0.2','0','0','0.2','0','0','0.2','0')
     , ('730002015','6','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0.13','0.18','0','0.13','0.18','0','0.13','0.18','0','0.13','0.18')
     , ('730002015','7','4','0','0','0','0','0','0','0','0','0','0','0','3','0','0','0.6','0','0','0.6','0','0','0.6','0','0','0.6')
     , ('730002015','8','4','2','0.75','0','0','0','0','0','0','0','0','0','3','0','0','0.22','0','0','0.22','0','0','0.22','0','0','0.22');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730002015','1',1)
     , ('730002015','12',1)
     , ('730002015','13',0)
     , ('730002015','19',0)
     , ('730002015','39',1)
     , ('730002015','41',1);

INSERT INTO `weenie_properties_create_list` (`object_Id`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`)
VALUES ('730002015','2','2587','0','9','0.5',0)
     , ('730002015','2','2598','0','9','0',0)
     , ('730002015','2','132','0','16','1',0)
     , ('730002015','2','118','0','16','1',0)
     , ('730002015','2','10696','0','9','0',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730002015','1','33554433')
     , ('730002015','2','150994945')
     , ('730002015','3','536870913')
     , ('730002015','4','805306368')
     , ('730002015','8','100667446');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730002015','1','5')
     , ('730002015','2','0')
     , ('730002015','3','0.16')
     , ('730002015','4','5')
     , ('730002015','5','1')
     , ('730002015','11','300')
     , ('730002015','13','0.9')
     , ('730002015','14','1')
     , ('730002015','15','1.1')
     , ('730002015','16','0.4')
     , ('730002015','17','0.4')
     , ('730002015','18','1')
     , ('730002015','19','0.6')
     , ('730002015','37','0.9')
     , ('730002015','38','1.55')
     , ('730002015','54','3')
     , ('730002015','64','1')
     , ('730002015','65','1')
     , ('730002015','66','1')
     , ('730002015','67','1')
     , ('730002015','68','1')
     , ('730002015','69','1')
     , ('730002015','70','1')
     , ('730002015','71','1')
     , ('730002015','72','1')
     , ('730002015','73','1')
     , ('730002015','74','1')
     , ('730002015','75','1')
     , ('730002015','104','10')
     , ('730002015','125','1')
     , ('730002015','76','0.5');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730002015','1','16')
     , ('730002015','2','31')
     , ('730002015','6','-1')
     , ('730002015','7','-1')
     , ('730002015','8','120')
     , ('730002015','16','32')
     , ('730002015','25','6')
     , ('730002015','27','0')
     , ('730002015','74','264200')
     , ('730002015','75','0')
     , ('730002015','76','100000')
     , ('730002015','93','2098200')
     , ('730002015','126','400')
     , ('730002015','127','250')
     , ('730002015','133','4')
     , ('730002015','134','16')
     , ('730002015','146','35')
     , ('730002015','95','8');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730002015','1','Ghost of Jeweler Lo Dai-Ou')
     , ('730002015','3','Male')
     , ('730002015','4','Sho')
     , ('730002015','5','Jeweler')
     , ('730002015','24','Tou-Tou');

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','7','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','12','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '1','21','0','1',NULL,'ZC_TouTou_A2Completed@wake5',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','13','1',NULL,NULL,NULL,'ZC_TouTou_A2Completed@wake5',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','8','0','1',NULL,'A pale shape bends over an empty workbench, setting stones no one living can see.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','12','1',NULL,NULL,NULL,'ZC_TouTou_A2Completed@wake5',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','21','0','1',NULL,'ZC_TouTou_C5Completed@cool',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','12','1',NULL,NULL,NULL,'ZC_TouTou_C5Completed@cool',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','10','0','1',NULL,'Three pearls, set and singing. The reef will grow more by tomorrow - it always does.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','13','1',NULL,NULL,NULL,'ZC_TouTou_C5Completed@cool',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','76','0','1',NULL,'ZC_TouTou_C5@have',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'730003008','3',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','23','1',NULL,NULL,NULL,'ZC_TouTou_C5@have',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','10','0','1',NULL,'I set stones for headmen and pirates alike, and never asked which was which. The reef grows black pearls now - beautiful, and wrong - and the sharks swallow them like minnows. Hunt the deep water: the southeast reef near 32.0S, 96.9E, or the northwest shallows by 26.4S, 92.1E. Bring me three.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730002015','22','1',NULL,NULL,NULL,'ZC_TouTou_C5@have',NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','74','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'730003008','3',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '1','10','0','1',NULL,'Black as the Tide and twice as honest. In a good setting, even a wrong thing can be made to shine.',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '2','3','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'300000','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)
     , (@parent, '3','22','0','1',NULL,'ZC_TouTou_C5Completed',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
