-- =========================================================================
-- Rollback: Random Town Invasion System
-- Removes all invasion generators, events, and landblock placements.
-- Also clears the db_script record so the setup SQL can be re-applied
-- cleanly when switching to the invasion branch.
-- =========================================================================

DELETE FROM `weenie` WHERE `class_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `weenie_properties_int` WHERE `object_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `weenie_properties_string` WHERE `object_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `weenie_properties_float` WHERE `object_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `event` WHERE `Name` LIKE 'Invasion_%';
DELETE FROM `landblock_instance` WHERE `weenie_Class_Id` BETWEEN 777700400 AND 777700474;
DELETE FROM `db_script` WHERE `name` = '2026-06-20-00-Town-Invasion-Setup.sql';
