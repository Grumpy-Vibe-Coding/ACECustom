DELETE FROM `weenie` WHERE `class_Id` = 730003002;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003002','zc-tt-item-tallow','1','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003002','22',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003002','1','33554695')
     , ('730003002','8','100667478');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003002','1','128')
     , ('730003002','5','50')
     , ('730003002','8','25')
     , ('730003002','9','0')
     , ('730003002','16','1')
     , ('730003002','19','7')
     , ('730003002','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003002','1','Umbral Tallow')
     , ('730003002','16','A greasy black lump cut from shadow-kind. It burns - and the ward-lanterns are hungry for it.');
