DELETE FROM `weenie` WHERE `class_Id` = 777700031;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777700031, '777700031PrestigeMirrorShadowGenerator', 1, '2026-06-08 22:00:00') /* Generic */;

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777700031,   1,         0) /* ItemType - None */
     , (777700031,  81,         5) /* MaxGeneratedObjects - 5 */
     , (777700031,  82,         5) /* InitGeneratedObjects - 5 */
     , (777700031,  93,      1044) /* PhysicsState - Stuck, IgnoreCollisions, Ethereal */;

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777700031,   1, True ) /* Stuck */
     , (777700031,  11, True ) /* IgnoreCollisions */
     , (777700031,  13, True ) /* Ethereal */
     , (777700031,  14, False) /* GravityStatus */;

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777700031,  41,         5) /* RegenerationInterval - 5 seconds */
     , (777700031,  43,        10) /* GeneratorRadius - 10 meters scatter radius */;

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777700031,   1, 'Prestige Mirror Shadow Generator') /* Name */;

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777700031,   1,  33555051) /* Setup - Generator portal setup */
     , (777700031,   8, 100667494) /* Icon - Generator icon */;

DELETE FROM `weenie_properties_generator` WHERE `object_Id` = 777700031;
INSERT INTO `weenie_properties_generator` (`object_Id`, `probability`, `weenie_Class_Id`, `delay`, `init_Create`, `max_Create`, `when_Create`, `where_Create`)
VALUES (777700031, 1.0, 777700030, 5, 5, 5, 1, 2) /* 777700030 Prestige Mirror Shadow, 5s delay, 5 init, 5 max, upon Destruction, Scatter */;

-- Spawn the generator in Variation 11 (Prestige Tier 1) of Tou Tou (cell 0xF559003D)
DELETE FROM `landblock_instance` WHERE `weenie_Class_Id` = 777700031 AND `variation_Id` = 11;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `last_Modified`, `variation_Id`)
VALUES (0x70F55902, 777700031, 0xF559003D, 175.0, 115.0, 20.265999, 1.0, 0, 0, 0, False, '2026-06-08 22:00:00', 11);
