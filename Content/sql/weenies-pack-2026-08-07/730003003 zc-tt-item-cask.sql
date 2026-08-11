DELETE FROM `weenie` WHERE `class_Id` = 730003003;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003003','zc-tt-item-cask','1','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003003','13',1)
     , ('730003003','15',1)
     , ('730003003','22',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003003','1','33554597')
     , ('730003003','3','536870932')
     , ('730003003','8','100675564')
     , ('730003003','22','872415275');

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES ('730003003','39','0.5');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003003','1','1024')
     , ('730003003','5','25')
     , ('730003003','8','25')
     , ('730003003','9','0')
     , ('730003003','16','1')
     , ('730003003','19','3226')
     , ('730003003','93','3092')
     , ('730003003','150','103')
     , ('730003003','151','9');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003003','1','Water-Stained Cask')
     , ('730003003','16','Mi Chi\'s last cask, rolled out of his tavern by drowned hands. Still sloshes.');
