DELETE FROM `weenie` WHERE `class_Id` = 777700200;
DELETE FROM `weenie_properties_int` WHERE `object_Id` = 777700200;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` = 777700200;
DELETE FROM `weenie_properties_string` WHERE `object_Id` = 777700200;
DELETE FROM `weenie_properties_float` WHERE `object_Id` = 777700200;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` = 777700200;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` = 777700200;

-- 1. Core Registration
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777700200, 'option_c_poisson_spawner', 1, NOW());

-- 2. Integer Properties
INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777700200,   1,        0) /* ItemType - None */
     , (777700200,  81,      100) /* MaxGeneratedObjects - 100 active monsters max */
     , (777700200,  82,      100) /* InitGeneratedObjects - Spawns 100 monsters initially */
     , (777700200,  93,     1044); /* PhysicsState - Ethereal/IgnoreCollisions */

-- 3. Float Properties
INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777700200,  41,      5.0) /* RegenerationInterval - ticks every 5 seconds */
     , (777700200,  43,     96.0) /* GeneratorRadius - Poisson scatter radius covering half-landblock */;

-- 4. Visuals (Setup 33556439 = Glowing Crystal Lord)
INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777700200,   1,  33556439) /* Setup - Glowing Crystal Lord */
     , (777700200,   3, 0x20000001) /* SoundTable */
     , (777700200,   8, 0x06001AF9) /* Radar Icon - Magenta Dot */;

-- 5. Behavior Bools
INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777700200,  83,      False) /* IsPlayerInteractable - False */
     , (777700200,  11,       True) /* IgnoreCollisions */
     , (777700200,  51,       True) /* ShowRadarBlip */
     , (777700200,   1,       True) /* Stuck */;

-- 6. String Properties
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777700200,   1, 'Option C Spawner (Poisson)') /* Name */
     , (777700200,  16, 'A test generator configured for PoissonDiskScatter (Option C).') /* LongDesc */;

-- 7. Generator Logic
-- Spawns Prestige Mirror Shadow (777700030) using PoissonDiskScatter (where_Create = 256)
INSERT INTO `weenie_properties_generator` 
(`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`, `stack_Size`, `palette_Id`, `shade`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`)
VALUES 
(777700200, 1.0, 777700030, 5.0, 100, 100, 2, 256, -1, 0, 0.0, 0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0);
