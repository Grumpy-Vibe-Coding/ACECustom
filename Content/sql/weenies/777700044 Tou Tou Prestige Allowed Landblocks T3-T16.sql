-- Clean up existing allowed landblocks for T3 through T16
DELETE FROM `prestige_allowed_landblocks` WHERE `tier` BETWEEN 3 AND 16;

-- Copy allowed landblocks from Tier 2 (v12) to T3 through T16 (v13 to v26)
INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 3, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 4, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 5, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 6, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 7, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 8, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 9, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 10, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 11, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 12, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 13, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 14, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 15, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;

INSERT INTO `prestige_allowed_landblocks` (`tier`, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped`)
SELECT 16, `landblock`, `is_active`, `area_name`, `boundary_wcid`, `boundary_scale`, `boundary_script_id`, `is_wiped` FROM `prestige_allowed_landblocks` WHERE `tier` = 2;
