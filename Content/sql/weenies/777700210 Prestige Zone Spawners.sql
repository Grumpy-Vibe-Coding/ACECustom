-- =============================================================
-- Prestige Zone Spawners (Ecology-based, 5 zone types)
-- WCIDs:
--   777700210 Water | 777700211 Golem | 777700212 Undead | 777700213 Fauna | 777700214 Beach
-- Each spawner uses PoissonScatter (where_Create=256), radius=140
-- Each profile = one mob type per cluster slot (groups of 5-6 mobs per cluster)
-- =============================================================

DELETE FROM `weenie` WHERE `class_Id` BETWEEN 777700210 AND 777700214;
DELETE FROM `weenie_properties_int`       WHERE `object_Id` BETWEEN 777700210 AND 777700214;
DELETE FROM `weenie_properties_bool`      WHERE `object_Id` BETWEEN 777700210 AND 777700214;
DELETE FROM `weenie_properties_string`    WHERE `object_Id` BETWEEN 777700210 AND 777700214;
DELETE FROM `weenie_properties_float`     WHERE `object_Id` BETWEEN 777700210 AND 777700214;
DELETE FROM `weenie_properties_d_i_d`     WHERE `object_Id` BETWEEN 777700210 AND 777700214;
DELETE FROM `weenie_properties_generator` WHERE `object_Id` BETWEEN 777700210 AND 777700214;

-- Core registrations
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES
  (777700210, 'prestige_spawner_water', 1, NOW()),
  (777700211, 'prestige_spawner_golem',  1, NOW()),
  (777700212, 'prestige_spawner_undead', 1, NOW()),
  (777700213, 'prestige_spawner_fauna',  1, NOW()),
  (777700214, 'prestige_spawner_beach',  1, NOW());

-- Shared int props (type=1 ItemType=None, type=93 GeneratorTimeType)
-- MaxGeneratedObjects (81) and InitGeneratedObjects (82) = profiles * init_Create per profile
--   Water: 5 profiles * 8 = 40
--   Golem: 6 profiles * 8 = 48
--   Undead: 10 profiles * 5 = 50
--   Fauna: 7 profiles * 5 = 35
--   Beach: 6 profiles * 8 = 48
INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
  (777700210, 1, 0), (777700210, 93, 1044), (777700210, 81, 40), (777700210, 82, 40),
  (777700211, 1, 0), (777700211, 93, 1044), (777700211, 81, 48), (777700211, 82, 48),
  (777700212, 1, 0), (777700212, 93, 1044), (777700212, 81, 50), (777700212, 82, 50),
  (777700213, 1, 0), (777700213, 93, 1044), (777700213, 81, 35), (777700213, 82, 35),
  (777700214, 1, 0), (777700214, 93, 1044), (777700214, 81, 48), (777700214, 82, 48);

-- Float props: RegenerationInterval=5s, GeneratorRadius=140
INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES
  (777700210, 41,   5.0), (777700210, 43, 140.0),
  (777700211, 41,   5.0), (777700211, 43, 140.0),
  (777700212, 41,   5.0), (777700212, 43, 140.0),
  (777700213, 41,   5.0), (777700213, 43, 140.0),
  (777700214, 41,   5.0), (777700214, 43, 140.0);

-- DID props: Setup (33554433 = 0x02000001 standard hotspot/generator) and icon
INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
  (777700210, 1, 33554433), (777700210, 8, 0x06001AF9),
  (777700211, 1, 33554433), (777700211, 8, 0x06001AF9),
  (777700212, 1, 33554433), (777700212, 8, 0x06001AF9),
  (777700213, 1, 33554433), (777700213, 8, 0x06001AF9),
  (777700214, 1, 33554433), (777700214, 8, 0x06001AF9);

-- Bool props
INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES
  (777700210, 11, True), (777700210, 1, True), (777700210, 83, False),
  (777700211, 11, True), (777700211, 1, True), (777700211, 83, False),
  (777700212, 11, True), (777700212, 1, True), (777700212, 83, False),
  (777700213, 11, True), (777700213, 1, True), (777700213, 83, False),
  (777700214, 11, True), (777700214, 1, True), (777700214, 83, False);

-- String props (name / description)
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
  (777700210, 1, 'Prestige Spawner (Water)'),  (777700210, 16, 'Water/Ocean - Sea creatures & flyers'),
  (777700211, 1, 'Prestige Spawner (Golem)'),   (777700211, 16, 'Rocky hills - Golem & Gromnie creatures'),
  (777700212, 1, 'Prestige Spawner (Undead)'),  (777700212, 16, 'Dark areas - Undead & Shadow creatures'),
  (777700213, 1, 'Prestige Spawner (Fauna)'),   (777700213, 16, 'Grasslands - Banderlings, wisps, and zefirs'),
  (777700214, 1, 'Prestige Spawner (Beach)'),   (777700214, 16, 'Sandy coasts - Tuskers and Monougas');

-- ---------------------------------------------------------------
-- Generator profiles
-- where_Create=256 (PoissonScatter), delay=300 (5 min respawn)
-- init_Create=max_Create (10-15 based on group limits)
-- probability=-1 (all profiles always active)
-- ---------------------------------------------------------------

-- 💧 WATER SPAWNER (5 profiles * 8 = 40 max objects)
INSERT INTO `weenie_properties_generator`
(`object_Id`,`probability`,`weenie_Class_Id`,`delay`,`init_Create`,`max_Create`,`when_Create`,`where_Create`,`stack_Size`,`palette_Id`,`shade`,`obj_Cell_Id`,`origin_X`,`origin_Y`,`origin_Z`,`angles_W`,`angles_X`,`angles_Y`,`angles_Z`)
VALUES
  (777700210, -1, 777701053, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- shallowsshark
  (777700210, -1, 777701051, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- bluephyntoswasp
  (777700210, -1, 777701049, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- goldphyntoswasp
  (777700210, -1, 777701050, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- greenphyntoswasp
  (777700210, -1, 777701030, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0); -- shadowwisp

-- 🪨 GOLEM/GROMNUS SPAWNER (6 profiles * 8 = 48 max objects)
INSERT INTO `weenie_properties_generator`
(`object_Id`,`probability`,`weenie_Class_Id`,`delay`,`init_Create`,`max_Create`,`when_Create`,`where_Create`,`stack_Size`,`palette_Id`,`shade`,`obj_Cell_Id`,`origin_X`,`origin_Y`,`origin_Z`,`angles_W`,`angles_X`,`angles_Y`,`angles_Z`)
VALUES
  (777700211, -1, 777701001, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- golemgranite
  (777700211, -1, 777701027, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- elaniwoodgolem
  (777700211, -1, 777701015, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- emergentashgromnus
  (777700211, -1, 777701016, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- emergentazuregromnus
  (777700211, -1, 777701017, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- emergentivorygromnus
  (777700211, -1, 777701018, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0); -- emergentrustgromnus

-- 💀 UNDEAD/SHADOW SPAWNER (10 profiles * 5 = 50 max objects)
INSERT INTO `weenie_properties_generator`
(`object_Id`,`probability`,`weenie_Class_Id`,`delay`,`init_Create`,`max_Create`,`when_Create`,`where_Create`,`stack_Size`,`palette_Id`,`shade`,`obj_Cell_Id`,`origin_X`,`origin_Y`,`origin_Z`,`angles_W`,`angles_X`,`angles_Y`,`angles_Z`)
VALUES
  (777700212, -1, 777701002, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- zombielich
  (777700212, -1, 777701042, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- drudgelurker
  (777700212, -1, 777701043, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- drudgestalker
  (777700212, -1, 777701045, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- olthoiservant
  (777700212, -1, 777701046, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- olthoitage
  (777700212, -1, 777701044, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- olthoiworker
  (777700212, -1, 777701024, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- panumbrisshadow
  (777700212, -1, 777701020, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- shadowflyer
  (777700212, -1, 777701021, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- shadowsbreath
  (777700212, -1, 777701025, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0); -- voidlord

-- 🌿 FAUNA SPAWNER (7 profiles * 5 = 35 max objects)
INSERT INTO `weenie_properties_generator`
(`object_Id`,`probability`,`weenie_Class_Id`,`delay`,`init_Create`,`max_Create`,`when_Create`,`where_Create`,`stack_Size`,`palette_Id`,`shade`,`obj_Cell_Id`,`origin_X`,`origin_Y`,`origin_Z`,`angles_W`,`angles_X`,`angles_Y`,`angles_Z`)
VALUES
  (777700213, -1, 777701041, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- banderlingravager
  (777700213, -1, 777701037, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- banderlingbandit
  (777700213, -1, 777701039, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- banderlingscout
  (777700213, -1, 777701038, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- banderlingstriker
  (777700213, -1, 777701040, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- youngbanderling
  (777700213, -1, 777701029, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- firewisp
  (777700213, -1, 777701052, 300.0, 5, 5, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0); -- sufutzefir

-- 🏖️ BEACH SPAWNER (6 profiles * 8 = 48 max objects)
INSERT INTO `weenie_properties_generator`
(`object_Id`,`probability`,`weenie_Class_Id`,`delay`,`init_Create`,`max_Create`,`when_Create`,`where_Create`,`stack_Size`,`palette_Id`,`shade`,`obj_Cell_Id`,`origin_X`,`origin_Y`,`origin_Z`,`angles_W`,`angles_X`,`angles_Y`,`angles_Z`)
VALUES
  (777700214, -1, 777701031, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- crimsonbacktusker
  (777700214, -1, 777701032, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- goldenbacktusker
  (777700214, -1, 777701036, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- brutishmonouga
  (777700214, -1, 777701034, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- crudemonouga
  (777700214, -1, 777701033, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0), -- wildmonouga
  (777700214, -1, 777701035, 300.0, 8, 8, 2, 256, -1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0); -- wilymonouga
