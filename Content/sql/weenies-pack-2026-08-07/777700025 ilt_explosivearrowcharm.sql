DELETE FROM `weenie` WHERE `class_Id` = 777700025;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700025','ilt_explosivearrowcharm','38','2026-06-19 19:04:23');

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES ('777700025','11',1)
     , ('777700025','13',1)
     , ('777700025','14',1)
     , ('777700025','63',1)
     , ('777700025','9040',1)
     , ('777700025','50000',1);

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700025','1','33554556')
     , ('777700025','3','536870932')
     , ('777700025','8','100672653')
     , ('777700025','48','100676435')
     , ('777700025','50','100667550');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700025','1','2048')
     , ('777700025','5','5')
     , ('777700025','8','5')
     , ('777700025','16','8')
     , ('777700025','19','1')
     , ('777700025','33','1')
     , ('777700025','83','2')
     , ('777700025','93','1044')
     , ('777700025','114','1')
     , ('777700025','50000','21')
     , ('777700025','50005','1')
     , ('777700025','50006','3');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700025','1','Explosive Arrow Charm')
     , ('777700025','14','\nDouble-click to activate. While active, Bow, Crossbow, and Thrown weapon projectiles explode on impact, firing a damage-type-matched ring spell at the target\'s location after a 1s delay. The explosion deals 50% of the arrow\'s damage (40% - 60% random spread).\n');
