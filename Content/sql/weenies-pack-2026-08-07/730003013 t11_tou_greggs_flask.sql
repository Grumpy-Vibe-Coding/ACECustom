DELETE FROM `weenie` WHERE `class_Id` = 730003013;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003013','t11_tou_greggs_flask','1','2026-07-25 01:54:41');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003013','22',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003013','1','33554689')
     , ('730003013','3','536870932')
     , ('730003013','6','67111919')
     , ('730003013','7','268435749')
     , ('730003013','8','100671846')
     , ('730003013','22','872415275')
     , ('730003013','36','234881046');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730003013','39','0.67');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003013','1','8')
     , ('730003013','3','39')
     , ('730003013','5','5')
     , ('730003013','8','5')
     , ('730003013','9','32768')
     , ('730003013','16','1')
     , ('730003013','19','15')
     , ('730003013','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003013','1','Gregg\'s Rum-Soaked Flask')
     , ('730003013','15','A small clay totem of a female Tumerok, suspended from a rawhide necklace.')
     , ('730003013','16','Scurvy Gregg\'s flask. The rum inside never seems to run dry - or stay down.');
