DELETE FROM `weenie` WHERE `class_Id` = 777700036;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700036','777700036TouTouPrestigePortalT18','7','2026-07-05 06:59:15');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700036','1',1)
     , ('777700036','11',0)
     , ('777700036','12',1)
     , ('777700036','13',1)
     , ('777700036','15',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700036','1','33554867')
     , ('777700036','2','150994947')
     , ('777700036','8','100667499');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700036','54','-0.1');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700036','1','65536')
     , ('777700036','16','32')
     , ('777700036','93','3084')
     , ('777700036','111','48')
     , ('777700036','133','4');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`)
VALUES ('777700036','2','4116250676','152.59','80.8','20.005','0.92388','0','0','-0.382683','18');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700036','1','Tou Tou Prestige T18')
     , ('777700036','16','A portal leading to the Tou Tou Prestige Area (v18).');
