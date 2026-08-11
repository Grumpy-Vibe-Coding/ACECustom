DELETE FROM `weenie` WHERE `class_Id` = 777710008;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777710008','ilt_farshotcharm_t2','38','2026-06-19 19:04:24');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777710008','11',1)
     , ('777710008','13',1)
     , ('777710008','14',1)
     , ('777710008','63',1)
     , ('777710008','9040',1)
     , ('777710008','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777710008','1','33554556')
     , ('777710008','3','536870932')
     , ('777710008','8','100672653')
     , ('777710008','48','100676435')
     , ('777710008','50','100667551');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777710008','1','2048')
     , ('777710008','5','5')
     , ('777710008','8','5')
     , ('777710008','16','8')
     , ('777710008','19','1')
     , ('777710008','33','1')
     , ('777710008','83','2')
     , ('777710008','93','1044')
     , ('777710008','114','1')
     , ('777710008','50000','28')
     , ('777710008','50005','2')
     , ('777710008','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777710008','1','Greater Far Shot Charm')
     , ('777710008','14','Double-click to activate. While active, your maximum missile attack range is increased by +30% and final missile damage is increased by +10%.');
