DELETE FROM `weenie` WHERE `class_Id` = 777700500;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES ('777700500','invasion_coin','51','2026-06-24 18:48:42');

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES ('777700500','1','33557367')
     , ('777700500','8','100690337')
     , ('777700500','50','100671476')
     , ('777700500','52','100667854');

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES ('777700500','1','128')
     , ('777700500','5','0')
     , ('777700500','8','0')
     , ('777700500','11','50000')
     , ('777700500','12','1')
     , ('777700500','13','0')
     , ('777700500','14','0')
     , ('777700500','15','0')
     , ('777700500','16','0')
     , ('777700500','18','1')
     , ('777700500','19','0')
     , ('777700500','20','0')
     , ('777700500','93','1044');

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES ('777700500','1','Invasion Coin')
     , ('777700500','16','A coin minted for those who turned back an invasion of Dereth.');
