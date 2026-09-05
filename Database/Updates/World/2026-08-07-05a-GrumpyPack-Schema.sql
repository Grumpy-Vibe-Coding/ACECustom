-- Carry-over: schema additions our branch made by hand on the test DB (never previously
-- scripted). Boundary-decouple columns on prestige_allowed_landblocks; required by the
-- zone-control branch's EF model and by the -06 data script.
ALTER TABLE `prestige_allowed_landblocks`
  ADD COLUMN IF NOT EXISTS `area_name` varchar(100) NOT NULL DEFAULT 'Default',
  ADD COLUMN IF NOT EXISTS `boundary_wcid` int(11) NULL DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `boundary_scale` float NULL DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `boundary_script_id` int(11) NULL DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `is_wiped` tinyint(1) NOT NULL DEFAULT 0;
