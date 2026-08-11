DELETE FROM `weenie` WHERE `class_Id` = 730003001;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003001','zc-tt-item-keepsake','1','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003001','22',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003001','1','33554689')
     , ('730003001','3','536870932')
     , ('730003001','6','67111919')
     , ('730003001','7','268435749')
     , ('730003001','8','100671846')
     , ('730003001','22','872415275')
     , ('730003001','36','234881046');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730003001','39','0.67');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003001','1','8')
     , ('730003001','3','39')
     , ('730003001','5','5')
     , ('730003001','8','5')
     , ('730003001','9','32768')
     , ('730003001','16','1')
     , ('730003001','19','15')
     , ('730003001','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003001','1','Stolen Keepsake')
     , ('730003001','15','A small clay totem of a female Tumerok, suspended from a rawhide necklace.')
     , ('730003001','16','A small thing someone once carried home every night - a figurine, worn smooth by a dead hand. The drowned plunderers hoard these. Elder Tou Shan wants them back.');
