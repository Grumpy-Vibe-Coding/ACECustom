-- 730001013 T11 Default v11 Drop Attributes And Vitals.sql
-- Removes the six attributes plus max_stamina / max_mana from the v11 variation Default
-- (owner call 2026-07-31, follow-up to 730001012 which dropped max_health).
--
-- WHY: a variation Default REPLACES the monster's own weenie value at spawn, and every one of these
-- is deliberately AUTHORED per mob across the 105 T11 weenies:
--
--   attribute      distinct values   range
--   Strength             14          45 .. 1380
--   Endurance            13          60 .. 1400
--   Coordination         16           1 ..  165
--   Quickness            14          50 .. 1360
--   Focus                14          60 ..  950
--   Self                 15          55 ..  900
--   MaxStamina           16          70 .. 150,000,000
--   MaxMana              14           0 .. 150,000,000
--
-- A flat 1100 erases that per-mob differentiation. For stamina/mana it is the same order-of-magnitude
-- error as max_health was: up to a 136,000x cut. For Coordination it is the opposite - 1..165 jumping
-- to 1100 would rewrite evade/attack math for every mob at once.
--
-- WHAT REMAINS on the v11 Default after this (10 stats): attack_skill, min_attack_skill,
-- melee_defense, missile_defense, magic_defense, damage_rating, damage_resist_rating, armor_level,
-- attack_damage, spell_damage. Those are also authored on the weenies, but at the SAME order of
-- magnitude - so a uniform 1100 there is a plausible tuning choice rather than a broken one, and is
-- left for the owner to keep or clear deliberately.
--
-- NOTE: edits the Zone Control JSON store in ace_shard.config_properties_string, not a weenie table.
-- Run with the SERVER STOPPED - the manager holds the store in memory and rewrites it on any zone edit.
--
-- Idempotent: JSON_REMOVE on absent paths is a no-op.
-- Backup of the pre-change store: C:\AI\ZoneControl\zonecontrol_data_backup_2026-07-31.json

UPDATE `config_properties_string`
   SET `value` = JSON_REMOVE(`value`,
        '$.VariationDefaults."11".Profile.Stats.strength',
        '$.VariationDefaults."11".Profile.Stats.endurance',
        '$.VariationDefaults."11".Profile.Stats.coordination',
        '$.VariationDefaults."11".Profile.Stats.quickness',
        '$.VariationDefaults."11".Profile.Stats.focus',
        '$.VariationDefaults."11".Profile.Stats.self',
        '$.VariationDefaults."11".Profile.Stats.max_stamina',
        '$.VariationDefaults."11".Profile.Stats.max_mana')
 WHERE `key` = 'zonecontrol_data';

-- ---------------------------------------------------------------------------------------
-- ROLLBACK (restores the 1100 values setall wrote):
--
-- UPDATE `config_properties_string`
--    SET `value` = JSON_SET(`value`,
--         '$.VariationDefaults."11".Profile.Stats.strength',     CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.endurance',    CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.coordination', CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.quickness',    CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.focus',        CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.self',         CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.max_stamina',  CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON),
--         '$.VariationDefaults."11".Profile.Stats.max_mana',     CAST('{"Base":1100.0,"Growth":1.0,"Additive":false,"Overrides":null}' AS JSON))
--  WHERE `key` = 'zonecontrol_data';
-- ---------------------------------------------------------------------------------------
