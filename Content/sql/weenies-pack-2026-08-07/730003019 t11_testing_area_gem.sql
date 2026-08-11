DELETE FROM `weenie` WHERE `class_Id` = 730003019;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003019','t11_testing_area_gem','38','2026-07-25 16:45:12');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003019','15',1)
     , ('730003019','63',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003019','1','33556769')
     , ('730003019','3','536870932')
     , ('730003019','6','67111919')
     , ('730003019','7','268435723')
     , ('730003019','8','100668361')
     , ('730003019','22','872415275');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730003019','76','0.5')
     , ('730003019','167','30');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003019','1','2048')
     , ('730003019','3','77')
     , ('730003019','5','5')
     , ('730003019','8','5')
     , ('730003019','9','0')
     , ('730003019','11','1')
     , ('730003019','12','1')
     , ('730003019','13','5')
     , ('730003019','14','5')
     , ('730003019','15','75')
     , ('730003019','16','8')
     , ('730003019','18','1')
     , ('730003019','19','75')
     , ('730003019','33','1')
     , ('730003019','93','3092')
     , ('730003019','94','16')
     , ('730003019','106','210')
     , ('730003019','107','70')
     , ('730003019','108','70')
     , ('730003019','109','40')
     , ('730003019','110','0')
     , ('730003019','114','1')
     , ('730003019','150','103')
     , ('730003019','151','2');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003019','1','Testing Area v11')
     , ('730003019','14','Double Click on this portal gem to transport yourself to the Hoshino tent.')
     , ('730003019','15','A glowing gem for teleporting.')
     , ('730003019','16','A humming gem. Use it to step into the v11 testing arena.');

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`, `weenie_Class_Id`, `style`, `substyle`, `quest`, `vendor_Type`, `min_Health`, `max_Health`, `damage_type`)
VALUES ('730003019','7','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);

SET @parent = LAST_INSERT_ID();

INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `motion`, `message`, `test_String`, `min`, `max`, `min_64`, `max_64`, `min_Dbl`, `max_Dbl`, `stat`, `display`, `amount`, `amount_64`, `hero_X_P_64`, `percent`, `spell_Id`, `wealth_Rating`, `treasure_Class`, `treasure_Type`, `p_Script`, `sound`, `destination_Type`, `weenie_Class_Id`, `stack_Size`, `palette`, `shade`, `try_To_Bond`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES (@parent, '0','99','0','1',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'11',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'6750486','30','-43.42','0.005','1','0','0','0');
