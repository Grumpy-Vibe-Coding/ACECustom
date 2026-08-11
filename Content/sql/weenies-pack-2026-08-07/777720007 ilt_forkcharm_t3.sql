DELETE FROM `weenie` WHERE `class_Id` = 777720007;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777720007','ilt_forkcharm_t3','38','2026-06-19 19:04:24');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777720007','11',1)
     , ('777720007','13',1)
     , ('777720007','14',1)
     , ('777720007','63',1)
     , ('777720007','9040',1)
     , ('777720007','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777720007','1','33554556')
     , ('777720007','3','536870932')
     , ('777720007','8','100670725')
     , ('777720007','48','100676435')
     , ('777720007','50','100667552');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777720007','1','2048')
     , ('777720007','5','5')
     , ('777720007','8','5')
     , ('777720007','16','8')
     , ('777720007','19','1')
     , ('777720007','33','1')
     , ('777720007','83','2')
     , ('777720007','93','1044')
     , ('777720007','114','1')
     , ('777720007','50000','27')
     , ('777720007','50005','3')
     , ('777720007','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777720007','1','Master Fork Charm')
     , ('777720007','14','Double-click to activate. While active, your Streak, Arc, and Bolt spells will fork to nearby enemies on hit, dealing full damage.');
