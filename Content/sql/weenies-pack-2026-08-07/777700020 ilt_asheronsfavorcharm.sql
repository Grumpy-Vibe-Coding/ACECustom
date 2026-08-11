DELETE FROM `weenie` WHERE `class_Id` = 777700020;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700020','ilt_asheronsfavorcharm','38','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700020','11',1)
     , ('777700020','13',1)
     , ('777700020','14',1)
     , ('777700020','63',1)
     , ('777700020','9040',1)
     , ('777700020','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700020','1','33554556')
     , ('777700020','3','536870932')
     , ('777700020','8','100683150')
     , ('777700020','48','100676435')
     , ('777700020','50','100667550');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700020','1','2048')
     , ('777700020','5','5')
     , ('777700020','8','5')
     , ('777700020','16','8')
     , ('777700020','19','1')
     , ('777700020','33','1')
     , ('777700020','83','2')
     , ('777700020','93','1044')
     , ('777700020','114','1')
     , ('777700020','50000','17')
     , ('777700020','50005','1')
     , ('777700020','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700020','1','Asheron\'s Favor')
     , ('777700020','14','\nWhile held, your maximum Health is bolstered by 10% and your Natural Armor is hardened by 50 points through the combined blessings of Asheron and Antius Blackmoor.\n');
