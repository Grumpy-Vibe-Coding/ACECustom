DELETE FROM `weenie` WHERE `class_Id` = 777700030;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777700030, '777700030TouTouPrestigePortal', 7, UTC_TIMESTAMP()) /* Portal */;

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777700030,   1, True ) /* Stuck */
     , (777700030,  11, False) /* Openable */
     , (777700030,  12, True ) /* IsContainer */
     , (777700030,  13, True ) /* IsEthereal */
     , (777700030,  15, True ) /* LightsStatus */;


INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777700030,   1,       65536) /* ItemType - Portal */
     , (777700030,  16,          32) /* ItemUseable - World */
     , (777700030,  93,        3084) /* PhysicsState */
     , (777700030, 111,          48) /* UseLimit */
     , (777700030, 133,           4) /* RadarBehavior - ShowPortal */;

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777700030,  54,        -0.1) /* UseRadius */;

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777700030,   1,    33554867) /* Setup */
     , (777700030,   2,   150994947) /* SoundTable */
     , (777700030,   8,   100667499) /* Icon */;

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777700030,   1, 'Tou Tou Prestige T12') /* Name */
     , (777700030,  16, 'A portal leading to the Tou Tou Prestige Area (v12).') /* LongDesc */;

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`)
VALUES (777700030,   2,  0xF5590034, 152.589996, 80.800003, 20.004999, 0.923880, 0.000000, 0.000000, -0.382683, 12);

-- ==========================================================================
-- Placement (landblock_instance) for the portal in Variation 11
-- ==========================================================================
DELETE FROM `landblock_instance` WHERE `guid` = 2133176320;

INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `variation_Id`)
VALUES (2133176320, 777700030, 4066050314, -0.343002, 6.54412, -10.063, 0.912462, 0, 0, -0.409162, 0, 11);

