DELETE FROM `weenie` WHERE `class_Id` = 777700028;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700028','ilt_farshotcharm_t1','38','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700028','11',1)
     , ('777700028','13',1)
     , ('777700028','14',1)
     , ('777700028','63',1)
     , ('777700028','9040',1)
     , ('777700028','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700028','1','33554556')
     , ('777700028','3','536870932')
     , ('777700028','8','100672653')
     , ('777700028','48','100676435')
     , ('777700028','50','100667550');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700028','1','2048')
     , ('777700028','5','5')
     , ('777700028','8','5')
     , ('777700028','16','8')
     , ('777700028','19','1')
     , ('777700028','33','1')
     , ('777700028','83','2')
     , ('777700028','93','1044')
     , ('777700028','114','1')
     , ('777700028','50000','28')
     , ('777700028','50005','1')
     , ('777700028','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700028','1','Far Shot Charm')
     , ('777700028','14','Double-click to activate. While active, your maximum missile attack range is increased by +15% and final missile damage is increased by +5%.');
