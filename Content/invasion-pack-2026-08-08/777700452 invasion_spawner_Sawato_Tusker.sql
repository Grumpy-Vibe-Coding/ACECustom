DELETE FROM `weenie` WHERE `class_Id` = 777700452;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700452','invasion_spawner_Sawato_Tusker','1','2026-06-22 21:50:54');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700452','11',1)
     , ('777700452','51',0)
     , ('777700452','1',1)
     , ('777700452','83',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700452','1','33554433')
     , ('777700452','3','536870913')
     , ('777700452','8','100669177');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700452','41','5')
     , ('777700452','43','50');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700452','1','777701031','15','15','15','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700452','1','777701032','15','15','15','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700452','1','0')
     , ('777700452','81','30')
     , ('777700452','82','30')
     , ('777700452','142','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700452','1','Invasion Spawner (Sawato - Tusker)')
     , ('777700452','34','Invasion_Sawato_Tusker');
