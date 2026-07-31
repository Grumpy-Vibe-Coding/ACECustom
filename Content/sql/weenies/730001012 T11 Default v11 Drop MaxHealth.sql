-- 730001012 T11 Default v11 Drop MaxHealth.sql
-- Removes max_health from the v11 variation Default (owner call 2026-07-31).
--
-- WHY: `/zonecontrol default 11 setall 1100` wrote 1100 to max_health, and a Default REPLACES the
-- monster's own weenie value at spawn. The 105 T11 mob weenies carry deliberately AUTHORED health
-- spanning 60 .. 990,000,000 (avg ~93M), tuned against the measured player DPS of ~1.1-1.2M/s to hit
-- the TTK targets in TouTou_PluginTuning_Handoff_2026-07-26.md (trash ~45s, boss ~99s). Frosttusk's
-- 50,000,032 is exactly that trash figure, not an accident.
--
-- A single Default value cannot be "sane" here: 1100 flattens a 50,000x range to one number, and even
-- the correct trash value (50M) would still cut a 990M boss by 20x. The per-mob spread IS the design,
-- so health simply should not be governed by the zone-wide Default layer.
--
-- Everything else `setall` wrote is left in place; only max_health is dropped.
--
-- NOTE: this edits the Zone Control JSON store in ace_shard.config_properties_string, NOT a weenie
-- table -- it lives here so the change ships with the branch like every other content artifact.
-- Run with the SERVER STOPPED (the manager holds the store in memory and rewrites it on any zone
-- edit, which would clobber this).
--
-- Idempotent: JSON_REMOVE on an absent path is a no-op.
-- Backup of the pre-change store: C:\AI\ZoneControl\zonecontrol_data_backup_2026-07-31.json

UPDATE `config_properties_string`
   SET `value` = JSON_REMOVE(`value`, '$.VariationDefaults."11".Profile.Stats.max_health')
 WHERE `key` = 'zonecontrol_data'
   AND JSON_EXTRACT(`value`, '$.VariationDefaults."11".Profile.Stats.max_health') IS NOT NULL;

-- ---------------------------------------------------------------------------------------
-- ROLLBACK (restores the 1100 that setall wrote):
--
-- UPDATE `config_properties_string`
--    SET `value` = JSON_SET(`value`, '$.VariationDefaults."11".Profile.Stats.max_health',
--                           CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON))
--  WHERE `key` = 'zonecontrol_data';
-- ---------------------------------------------------------------------------------------
