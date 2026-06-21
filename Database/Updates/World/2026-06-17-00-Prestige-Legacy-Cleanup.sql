-- Legacy Spawner Cleanups for Tou Tou Prestige Zone (variation 11 & 12)
-- Delete old Prestige Mirror Shadow Generator and Creature instances (777700030, 777700031) from variations 11 and 12.

DELETE FROM `landblock_instance_link` WHERE `parent_GUID` IN (SELECT `guid` FROM `landblock_instance` WHERE `weenie_Class_Id` IN (777700030, 777700031));
DELETE FROM `landblock_instance` WHERE `weenie_Class_Id` IN (777700030, 777700031);

