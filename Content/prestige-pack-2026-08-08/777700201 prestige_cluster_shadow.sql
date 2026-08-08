DELETE FROM `weenie` WHERE `class_Id` = 777700201;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700201','prestige_cluster_shadow','1','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700201','1','33554433');

INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES ('777700201','-1','777701020','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700201','-1','777701021','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700201','-1','777701022','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700201','-1','777701023','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700201','-1','777701024','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700201','-1','777701025','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0')
     , ('777700201','-1','777701030','300','1','1','1','512','-1','0','0','0','0','0','0','1','0','0','0');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700201','1','0');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700201','1','Prestige Cluster Shadow');
