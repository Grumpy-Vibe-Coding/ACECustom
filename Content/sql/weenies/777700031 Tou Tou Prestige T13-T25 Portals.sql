-- ==========================================================================
-- Portal T13 (WCID: 777700031)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700031;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700031, '777700031TouTouPrestigePortalT13', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700031, 1, True), (777700031, 11, False), (777700031, 12, True), (777700031, 13, True), (777700031, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700031, 1, 65536), (777700031, 16, 32), (777700031, 93, 3084), (777700031, 111, 48), (777700031, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700031, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700031, 1, 33554867), (777700031, 2, 150994947), (777700031, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700031, 1, 'Tou Tou Prestige T13'), (777700031, 16, 'A portal leading to the Tou Tou Prestige Area (v13).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700031, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 13);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176321;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176321, 777700031, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 12);

-- ==========================================================================
-- Portal T14 (WCID: 777700032)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700032;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700032, '777700032TouTouPrestigePortalT14', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700032, 1, True), (777700032, 11, False), (777700032, 12, True), (777700032, 13, True), (777700032, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700032, 1, 65536), (777700032, 16, 32), (777700032, 93, 3084), (777700032, 111, 48), (777700032, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700032, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700032, 1, 33554867), (777700032, 2, 150994947), (777700032, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700032, 1, 'Tou Tou Prestige T14'), (777700032, 16, 'A portal leading to the Tou Tou Prestige Area (v14).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700032, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 14);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176322;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176322, 777700032, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 13);

-- ==========================================================================
-- Portal T15 (WCID: 777700033)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700033;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700033, '777700033TouTouPrestigePortalT15', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700033, 1, True), (777700033, 11, False), (777700033, 12, True), (777700033, 13, True), (777700033, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700033, 1, 65536), (777700033, 16, 32), (777700033, 93, 3084), (777700033, 111, 48), (777700033, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700033, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700033, 1, 33554867), (777700033, 2, 150994947), (777700033, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700033, 1, 'Tou Tou Prestige T15'), (777700033, 16, 'A portal leading to the Tou Tou Prestige Area (v15).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700033, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 15);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176323;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176323, 777700033, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 14);

-- ==========================================================================
-- Portal T16 (WCID: 777700034)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700034;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700034, '777700034TouTouPrestigePortalT16', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700034, 1, True), (777700034, 11, False), (777700034, 12, True), (777700034, 13, True), (777700034, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700034, 1, 65536), (777700034, 16, 32), (777700034, 93, 3084), (777700034, 111, 48), (777700034, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700034, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700034, 1, 33554867), (777700034, 2, 150994947), (777700034, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700034, 1, 'Tou Tou Prestige T16'), (777700034, 16, 'A portal leading to the Tou Tou Prestige Area (v16).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700034, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 16);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176324;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176324, 777700034, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 15);

-- ==========================================================================
-- Portal T17 (WCID: 777700035)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700035;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700035, '777700035TouTouPrestigePortalT17', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700035, 1, True), (777700035, 11, False), (777700035, 12, True), (777700035, 13, True), (777700035, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700035, 1, 65536), (777700035, 16, 32), (777700035, 93, 3084), (777700035, 111, 48), (777700035, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700035, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700035, 1, 33554867), (777700035, 2, 150994947), (777700035, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700035, 1, 'Tou Tou Prestige T17'), (777700035, 16, 'A portal leading to the Tou Tou Prestige Area (v17).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700035, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 17);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176325;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176325, 777700035, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 16);

-- ==========================================================================
-- Portal T18 (WCID: 777700036)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700036;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700036, '777700036TouTouPrestigePortalT18', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700036, 1, True), (777700036, 11, False), (777700036, 12, True), (777700036, 13, True), (777700036, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700036, 1, 65536), (777700036, 16, 32), (777700036, 93, 3084), (777700036, 111, 48), (777700036, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700036, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700036, 1, 33554867), (777700036, 2, 150994947), (777700036, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700036, 1, 'Tou Tou Prestige T18'), (777700036, 16, 'A portal leading to the Tou Tou Prestige Area (v18).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700036, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 18);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176326;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176326, 777700036, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 17);

-- ==========================================================================
-- Portal T19 (WCID: 777700037)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700037;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700037, '777700037TouTouPrestigePortalT19', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700037, 1, True), (777700037, 11, False), (777700037, 12, True), (777700037, 13, True), (777700037, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700037, 1, 65536), (777700037, 16, 32), (777700037, 93, 3084), (777700037, 111, 48), (777700037, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700037, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700037, 1, 33554867), (777700037, 2, 150994947), (777700037, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700037, 1, 'Tou Tou Prestige T19'), (777700037, 16, 'A portal leading to the Tou Tou Prestige Area (v19).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700037, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 19);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176327;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176327, 777700037, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 18);

-- ==========================================================================
-- Portal T20 (WCID: 777700038)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700038;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700038, '777700038TouTouPrestigePortalT20', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700038, 1, True), (777700038, 11, False), (777700038, 12, True), (777700038, 13, True), (777700038, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700038, 1, 65536), (777700038, 16, 32), (777700038, 93, 3084), (777700038, 111, 48), (777700038, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700038, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700038, 1, 33554867), (777700038, 2, 150994947), (777700038, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700038, 1, 'Tou Tou Prestige T20'), (777700038, 16, 'A portal leading to the Tou Tou Prestige Area (v20).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700038, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 20);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176328;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176328, 777700038, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 19);

-- ==========================================================================
-- Portal T21 (WCID: 777700039)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700039;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700039, '777700039TouTouPrestigePortalT21', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700039, 1, True), (777700039, 11, False), (777700039, 12, True), (777700039, 13, True), (777700039, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700039, 1, 65536), (777700039, 16, 32), (777700039, 93, 3084), (777700039, 111, 48), (777700039, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700039, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700039, 1, 33554867), (777700039, 2, 150994947), (777700039, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700039, 1, 'Tou Tou Prestige T21'), (777700039, 16, 'A portal leading to the Tou Tou Prestige Area (v21).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700039, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 21);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176329;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176329, 777700039, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 20);

-- ==========================================================================
-- Portal T22 (WCID: 777700040)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700040;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700040, '777700040TouTouPrestigePortalT22', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700040, 1, True), (777700040, 11, False), (777700040, 12, True), (777700040, 13, True), (777700040, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700040, 1, 65536), (777700040, 16, 32), (777700040, 93, 3084), (777700040, 111, 48), (777700040, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700040, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700040, 1, 33554867), (777700040, 2, 150994947), (777700040, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700040, 1, 'Tou Tou Prestige T22'), (777700040, 16, 'A portal leading to the Tou Tou Prestige Area (v22).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700040, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 22);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176330;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176330, 777700040, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 21);

-- ==========================================================================
-- Portal T23 (WCID: 777700041)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700041;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700041, '777700041TouTouPrestigePortalT23', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700041, 1, True), (777700041, 11, False), (777700041, 12, True), (777700041, 13, True), (777700041, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700041, 1, 65536), (777700041, 16, 32), (777700041, 93, 3084), (777700041, 111, 48), (777700041, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700041, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700041, 1, 33554867), (777700041, 2, 150994947), (777700041, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700041, 1, 'Tou Tou Prestige T23'), (777700041, 16, 'A portal leading to the Tou Tou Prestige Area (v23).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700041, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 23);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176331;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176331, 777700041, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 22);

-- ==========================================================================
-- Portal T24 (WCID: 777700042)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700042;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700042, '777700042TouTouPrestigePortalT24', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700042, 1, True), (777700042, 11, False), (777700042, 12, True), (777700042, 13, True), (777700042, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700042, 1, 65536), (777700042, 16, 32), (777700042, 93, 3084), (777700042, 111, 48), (777700042, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700042, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700042, 1, 33554867), (777700042, 2, 150994947), (777700042, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700042, 1, 'Tou Tou Prestige T24'), (777700042, 16, 'A portal leading to the Tou Tou Prestige Area (v24).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700042, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 24);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176332;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176332, 777700042, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 23);

-- ==========================================================================
-- Portal T25 (WCID: 777700043)
-- ==========================================================================
DELETE FROM `weenie` WHERE `class_Id` = 777700043;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES (777700043, '777700043TouTouPrestigePortalT25', 7, UTC_TIMESTAMP());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES (777700043, 1, True), (777700043, 11, False), (777700043, 12, True), (777700043, 13, True), (777700043, 15, True);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES (777700043, 1, 65536), (777700043, 16, 32), (777700043, 93, 3084), (777700043, 111, 48), (777700043, 133, 4);

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES (777700043, 54, -0.1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES (777700043, 1, 33554867), (777700043, 2, 150994947), (777700043, 8, 100667499);

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES (777700043, 1, 'Tou Tou Prestige T25'), (777700043, 16, 'A portal leading to the Tou Tou Prestige Area (v25).');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`) VALUES (777700043, 2, 4116250676, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 25);

DELETE FROM `landblock_instance` WHERE `guid` = 2133176333;
INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`) VALUES (2133176333, 777700043, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 24);
