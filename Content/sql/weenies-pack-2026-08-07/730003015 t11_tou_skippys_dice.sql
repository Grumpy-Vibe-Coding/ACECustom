DELETE FROM `weenie` WHERE `class_Id` = 730003015;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003015','t11_tou_skippys_dice','1','2026-07-25 01:54:41');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003015','22',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003015','1','33554689')
     , ('730003015','3','536870932')
     , ('730003015','6','67111919')
     , ('730003015','7','268435749')
     , ('730003015','8','100671846')
     , ('730003015','22','872415275')
     , ('730003015','36','234881046');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730003015','39','0.67');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003015','1','8')
     , ('730003015','3','39')
     , ('730003015','5','5')
     , ('730003015','8','5')
     , ('730003015','9','32768')
     , ('730003015','16','1')
     , ('730003015','19','15')
     , ('730003015','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003015','1','Skippy\'s Loaded Dice')
     , ('730003015','15','A small clay totem of a female Tumerok, suspended from a rawhide necklace.')
     , ('730003015','16','Damned Skippy\'s dice. They always come up drowned.');
