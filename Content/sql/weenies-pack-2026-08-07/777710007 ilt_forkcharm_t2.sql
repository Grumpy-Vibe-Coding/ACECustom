DELETE FROM `weenie` WHERE `class_Id` = 777710007;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777710007','ilt_forkcharm_t2','38','2026-06-19 19:04:24');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777710007','11',1)
     , ('777710007','13',1)
     , ('777710007','14',1)
     , ('777710007','63',1)
     , ('777710007','9040',1)
     , ('777710007','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777710007','1','33554556')
     , ('777710007','3','536870932')
     , ('777710007','8','100670725')
     , ('777710007','48','100676435')
     , ('777710007','50','100667551');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777710007','1','2048')
     , ('777710007','5','5')
     , ('777710007','8','5')
     , ('777710007','16','8')
     , ('777710007','19','1')
     , ('777710007','33','1')
     , ('777710007','83','2')
     , ('777710007','93','1044')
     , ('777710007','114','1')
     , ('777710007','50000','27')
     , ('777710007','50005','2')
     , ('777710007','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777710007','1','Greater Fork Charm')
     , ('777710007','14','Double-click to activate. While active, your Streak, Arc, and Bolt spells will fork to nearby enemies on hit, dealing 75% of the original spell\'s damage.');
