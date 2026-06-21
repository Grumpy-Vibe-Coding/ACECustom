-- Prestige Cluster Spawner Setup
-- Architecture: Multiple generators placed per landblock at grid positions
-- Each generator spawns 5-7 mobs AT its position (where_Create=512 = Spawn_Default = cluster at generator point)
-- 42 generators per landblock (7x6 grid) = 42 clusters of 5-7 mobs each

DELETE FROM `weenie` WHERE `class_Id` BETWEEN 777700200 AND 777700203;
DELETE FROM `weenie_properties_int` WHERE `object_Id` BETWEEN 777700200 AND 777700203;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` BETWEEN 777700200 AND 777700203;
DELETE FROM `weenie_properties_string` WHERE `object_Id` BETWEEN 777700200 AND 777700203;
DELETE FROM `weenie_properties_float` WHERE `object_Id` BETWEEN 777700200 AND 777700203;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` BETWEEN 777700200 AND 777700203;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` BETWEEN 777700200 AND 777700203;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES
(777700200, 'prestige_cluster_golem',   1, NOW()),
(777700201, 'prestige_cluster_shadow',  1, NOW()),
(777700202, 'prestige_cluster_inland',  1, NOW()),
(777700203, 'prestige_cluster_coastal', 1, NOW());

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(777700200, 1, 0), (777700201, 1, 0), (777700202, 1, 0), (777700203, 1, 0);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700200, 1, 'Prestige Cluster Golem'),
(777700201, 1, 'Prestige Cluster Shadow'),
(777700202, 1, 'Prestige Cluster Inland'),
(777700203, 1, 'Prestige Cluster Coastal');

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(777700200, 1, 33554433), (777700201, 1, 33554433), (777700202, 1, 33554433), (777700203, 1, 33554433);

INSERT INTO `weenie_properties_generator`
(`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES
(777700200, -1, 777701001, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700200, -1, 777701002, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700200, -1, 777701027, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700200, -1, 777701028, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700200, -1, 777701029, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701020, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701021, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701022, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701023, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701024, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701025, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700201, -1, 777701030, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701015, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701016, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701018, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701031, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701033, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701037, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700202, -1, 777701042, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701044, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701045, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701046, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701047, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701049, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701051, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0),
(777700203, -1, 777701053, 300.0, 1, 1, 1, 512, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0)
;
