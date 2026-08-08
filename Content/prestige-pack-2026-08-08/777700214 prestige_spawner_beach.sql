DELETE FROM `weenie` WHERE `class_Id` = 777700214;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700214','prestige_spawner_beach','1','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700214','11',1)
     , ('777700214','1',1)
     , ('777700214','83',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700214','1','33554433')
     , ('777700214','8','100670201');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700214','41','5')
     , ('777700214','43','140');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700214','-1','777701031','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700214','-1','777701032','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700214','-1','777701036','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700214','-1','777701034','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700214','-1','777701033','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700214','-1','777701035','300','8','8','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700214','1','0')
     , ('777700214','93','1044')
     , ('777700214','81','48')
     , ('777700214','82','48');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700214','1','Prestige Spawner (Beach)')
     , ('777700214','16','Sandy coasts - Tuskers and Monougas');
