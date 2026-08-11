DELETE FROM `weenie` WHERE `class_Id` = 777710005;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777710005','ilt_explosivearrowcharm_level2','38','2026-06-19 19:04:24');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777710005','11',1)
     , ('777710005','13',1)
     , ('777710005','14',1)
     , ('777710005','63',1)
     , ('777710005','9040',1)
     , ('777710005','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777710005','1','33554556')
     , ('777710005','3','536870932')
     , ('777710005','8','100672653')
     , ('777710005','48','100676435')
     , ('777710005','50','100667551');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777710005','1','2048')
     , ('777710005','5','5')
     , ('777710005','8','5')
     , ('777710005','16','8')
     , ('777710005','19','1')
     , ('777710005','33','1')
     , ('777710005','83','2')
     , ('777710005','93','1044')
     , ('777710005','114','1')
     , ('777710005','50000','21')
     , ('777710005','50005','2')
     , ('777710005','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777710005','1','Greater Explosive Arrow Charm')
     , ('777710005','14','\nDouble-click to activate. While active, Bow, Crossbow, and Thrown weapon projectiles explode on impact, firing a damage-type-matched ring spell at the target\'s location after a 1s delay. The explosion deals 75% of the arrow\'s damage (65% - 85% random spread).\n');
