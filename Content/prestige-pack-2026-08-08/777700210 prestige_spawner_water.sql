DELETE FROM `weenie` WHERE `class_Id` = 777700210;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700210','prestige_spawner_water','1','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700210','11',1)
     , ('777700210','1',1)
     , ('777700210','83',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700210','1','33554433')
     , ('777700210','8','100670201');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700210','41','5')
     , ('777700210','43','140');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700210','-1','777701053','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700210','-1','777701051','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700210','-1','777701049','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700210','-1','777701050','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700210','-1','777701030','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700210','1','0')
     , ('777700210','93','1044')
     , ('777700210','81','40')
     , ('777700210','82','40');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700210','1','Prestige Spawner (Water)')
     , ('777700210','16','Water/Ocean - Sea creatures & flyers');
