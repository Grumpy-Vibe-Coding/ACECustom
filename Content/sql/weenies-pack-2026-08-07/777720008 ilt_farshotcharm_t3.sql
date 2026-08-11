DELETE FROM `weenie` WHERE `class_Id` = 777720008;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777720008','ilt_farshotcharm_t3','38','2026-06-19 19:04:24');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777720008','11',1)
     , ('777720008','13',1)
     , ('777720008','14',1)
     , ('777720008','63',1)
     , ('777720008','9040',1)
     , ('777720008','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777720008','1','33554556')
     , ('777720008','3','536870932')
     , ('777720008','8','100672653')
     , ('777720008','48','100676435')
     , ('777720008','50','100667552');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777720008','1','2048')
     , ('777720008','5','5')
     , ('777720008','8','5')
     , ('777720008','16','8')
     , ('777720008','19','1')
     , ('777720008','33','1')
     , ('777720008','83','2')
     , ('777720008','93','1044')
     , ('777720008','114','1')
     , ('777720008','50000','28')
     , ('777720008','50005','3')
     , ('777720008','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777720008','1','Master Far Shot Charm')
     , ('777720008','14','Double-click to activate. While active, your maximum missile attack range is increased by +41% (up to 120 yards) and final missile damage is increased by +20%.');
