DELETE FROM `weenie` WHERE `class_Id` = 777700444;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700444','invasion_spawner_Neydisa_Olthoi','1','2026-06-22 21:50:54');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700444','11',1)
     , ('777700444','51',0)
     , ('777700444','1',1)
     , ('777700444','83',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700444','1','33554433')
     , ('777700444','3','536870913')
     , ('777700444','8','100669177');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700444','41','5')
     , ('777700444','43','50');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700444','1','777701044','15','10','10','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700444','1','777701045','15','10','10','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700444','1','777701046','15','10','10','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700444','1','0')
     , ('777700444','81','30')
     , ('777700444','82','30')
     , ('777700444','142','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700444','1','Invasion Spawner (Neydisa - Olthoi)')
     , ('777700444','34','Invasion_Neydisa_Olthoi');
