-- Prestige tier-ladder portal placements: 7 rows, all in interior cell 0x010A of landblock
-- 0xF25B (Tou Tou prestige tier-1 area). Each portal is placed in the variation ONE BELOW its
-- destination tier (T13 portal stands in v12, etc). The T12/T15-T18/T23-T24 portals were never
-- placed. The 9 spawners and 39 prestige mobs had NO placements (ad-hoc admin spawns only).
-- Extracted 2026-08-08 from 2026-08-07-02-GrumpyPack-Instances.sql before the prestige prune.
DELETE FROM `landblock_instance` WHERE `guid` IN (2133176321, 2133176322, 2133176327, 2133176328, 2133176329, 2133176330, 2133176333);

INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `last_Modified`, `variation_Id`)
VALUES ('2133176321','777700031','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','12')
     , ('2133176322','777700032','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','13')
     , ('2133176327','777700037','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','18')
     , ('2133176328','777700038','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','19')
     , ('2133176329','777700039','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','20')
     , ('2133176330','777700040','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','21')
     , ('2133176333','777700043','4066050314','-0.343002','6.54412','-10.063','0.912462','0','0','-0.409162',0,'2026-07-05 02:59:15','24');
