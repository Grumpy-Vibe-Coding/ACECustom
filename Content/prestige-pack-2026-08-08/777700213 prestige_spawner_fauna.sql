DELETE FROM `weenie` WHERE `class_Id` = 777700213;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700213','prestige_spawner_fauna','1','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700213','11',1)
     , ('777700213','1',1)
     , ('777700213','83',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700213','1','33554433')
     , ('777700213','8','100670201');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700213','41','5')
     , ('777700213','43','140');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700213','-1','777701041','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700213','-1','777701037','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700213','-1','777701039','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700213','-1','777701038','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700213','-1','777701040','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700213','-1','777701029','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700213','-1','777701052','300','5','5','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700213','1','0')
     , ('777700213','93','1044')
     , ('777700213','81','35')
     , ('777700213','82','35');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700213','1','Prestige Spawner (Fauna)')
     , ('777700213','16','Grasslands - Banderlings, wisps, and zefirs');
