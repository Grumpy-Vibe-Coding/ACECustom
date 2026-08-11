DELETE FROM `weenie` WHERE `class_Id` = 730003007;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003007','zc-tt-item-heartwood','1','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003007','22',1)
     , ('730003007','23',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003007','1','33554817')
     , ('730003007','3','536870932')
     , ('730003007','6','67111919')
     , ('730003007','7','268435832')
     , ('730003007','8','100671839')
     , ('730003007','22','872415275');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730003007','39','0.4');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003007','1','128')
     , ('730003007','3','39')
     , ('730003007','5','100')
     , ('730003007','8','100')
     , ('730003007','9','0')
     , ('730003007','16','1')
     , ('730003007','19','200')
     , ('730003007','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003007','1','Elaniwood Heartwood')
     , ('730003007','16','A dense length of living wood cut from an elaniwood golem. Rou Beh calls it the only honest bow-stave left on the island.');
