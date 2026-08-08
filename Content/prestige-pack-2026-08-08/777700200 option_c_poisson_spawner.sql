DELETE FROM `weenie` WHERE `class_Id` = 777700200;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700200','option_c_poisson_spawner','1','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700200','83',0)
     , ('777700200','11',1)
     , ('777700200','51',1)
     , ('777700200','1',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700200','1','33556439')
     , ('777700200','3','536870913')
     , ('777700200','8','100670201');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700200','41','5')
     , ('777700200','43','96');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700200','1','777700030','5','100','100','2','256','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700200','1','0')
     , ('777700200','81','100')
     , ('777700200','82','100')
     , ('777700200','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700200','1','Option C Spawner (Poisson)')
     , ('777700200','16','A test generator configured for PoissonDiskScatter (Option C).');
