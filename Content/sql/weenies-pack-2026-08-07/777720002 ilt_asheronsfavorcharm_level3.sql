DELETE FROM `weenie` WHERE `class_Id` = 777720002;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777720002','ilt_asheronsfavorcharm_level3','38','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777720002','11',1)
     , ('777720002','13',1)
     , ('777720002','14',1)
     , ('777720002','63',1)
     , ('777720002','9040',1)
     , ('777720002','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777720002','1','33554556')
     , ('777720002','3','536870932')
     , ('777720002','8','100683150')
     , ('777720002','48','100676435')
     , ('777720002','50','100667552');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777720002','1','2048')
     , ('777720002','5','5')
     , ('777720002','8','5')
     , ('777720002','16','8')
     , ('777720002','19','1')
     , ('777720002','33','1')
     , ('777720002','83','2')
     , ('777720002','93','1044')
     , ('777720002','114','1')
     , ('777720002','50000','17')
     , ('777720002','50005','3')
     , ('777720002','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777720002','1','Asheron\'s Blessing')
     , ('777720002','14','\nWhile held, your maximum Health is bolstered by 20% and your Natural Armor is hardened by 250 points through the combined blessings of Asheron and Antius Blackmoor.\n');
