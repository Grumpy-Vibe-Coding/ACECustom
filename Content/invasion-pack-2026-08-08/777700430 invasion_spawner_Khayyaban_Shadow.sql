DELETE FROM `weenie` WHERE `class_Id` = 777700430;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700430','invasion_spawner_Khayyaban_Shadow','1','2026-06-22 21:50:54');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700430','11',1)
     , ('777700430','51',0)
     , ('777700430','1',1)
     , ('777700430','83',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700430','1','33554433')
     , ('777700430','3','536870913')
     , ('777700430','8','100669177');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700430','41','5')
     , ('777700430','43','50');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700430','1','777701024','15','10','10','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700430','1','777701020','15','10','10','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700430','1','777701021','15','10','10','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700430','1','0')
     , ('777700430','81','30')
     , ('777700430','82','30')
     , ('777700430','142','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700430','1','Invasion Spawner (Khayyaban - Shadow)')
     , ('777700430','34','Invasion_Khayyaban_Shadow');
