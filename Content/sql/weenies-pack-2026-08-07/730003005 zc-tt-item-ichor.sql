DELETE FROM `weenie` WHERE `class_Id` = 730003005;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('730003005','zc-tt-item-ichor','1','2026-07-17 11:22:17');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('730003005','22',1)
     , ('730003005','69',0);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('730003005','1','33554817')
     , ('730003005','8','100689076');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('730003005','1','128')
     , ('730003005','5','10')
     , ('730003005','16','1')
     , ('730003005','19','0')
     , ('730003005','33','1')
     , ('730003005','114','1');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('730003005','1','Moarsman Ichor')
     , ('730003005','33','progenitorsichorpickuptimer')
     , ('730003005','37','SplitGraelHighIchorTurnin0806')
     , ('730003005','16','Thick ichor drawn from a moarsman. Binds a remedy, according to a dead healer.');
