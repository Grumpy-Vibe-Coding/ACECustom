DELETE FROM `weenie` WHERE `class_Id` = 777700035;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700035','777700035TouTouPrestigePortalT17','7','2026-07-05 06:59:15');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700035','1',1)
     , ('777700035','11',0)
     , ('777700035','12',1)
     , ('777700035','13',1)
     , ('777700035','15',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700035','1','33554867')
     , ('777700035','2','150994947')
     , ('777700035','8','100667499');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('777700035','54','-0.1');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700035','1','65536')
     , ('777700035','16','32')
     , ('777700035','93','3084')
     , ('777700035','111','48')
     , ('777700035','133','4');

INSERT INTO `weenie_properties_position` (`object_Id`, `position_Type`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `variation_Id`)
VALUES ('777700035','2','4116250676','152.59','80.8','20.005','0.92388','0','0','-0.382683','17');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700035','1','Tou Tou Prestige T17')
     , ('777700035','16','A portal leading to the Tou Tou Prestige Area (v17).');
