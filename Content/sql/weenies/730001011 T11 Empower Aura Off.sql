-- 730001011 T11 Empower Aura Off.sql
-- Turns the Empower aura OFF for all T11 content (owner ruling 2026-07-30).
--
-- WHY:
--   The v11 camp model went back to a plain 1 boss + 4 minions spawned directly by the camp
--   generator, replacing the "Kingpin" model (1 boss + emote-summoned, aura-buffed minions).
--   Bosses are now tuned like any other mob -- roughly 2x their minions -- via authored
--   per-WCID Zone Control stats, so a boss no longer needs to be an IsEmpowerSource.
--
-- WHAT THIS CLEARS (64 rows, all inside the T11 wcid range; nothing else in ace_world uses
-- these props -- verified 2026-07-30):
--   PropertyBool.CanBeEmpowered  (50043) x44 minions -- could receive the "Empowered" prefix
--   PropertyBool.IsEmpowerSource (50044) x20 bosses  -- granted it to nearby minions within 20m
--
-- WHY BOTH, not just the sources:
--   Clearing only IsEmpowerSource would disable the aura functionally, but Creature_Aura's
--   early-out is on CanBeEmpowered -- so all 44 minions would keep running a per-landblock
--   scan every second (via Monster_Tick -> UpdateAuraBuffs) for no benefit. Clearing both
--   removes the aura AND the wasted per-tick work.
--
-- NOT AFFECTED:
--   * The aura CODE stays (Creature_Aura.cs). This is opt-in per weenie, so re-enabling it
--     later is just setting both props again on one boss + its ring -- see the rollback below.
--   * PropertyBool.IsEmpowered (50045) is runtime-only and has 0 persisted rows; nothing to do.
--   * Boss damage multipliers keyed on IsEmpowerSource (v11_pcthp_boss_mult,
--     v11_mob_dmg_taken_boss_mult) only apply on the NON-zone fallback branches, which an
--     authored percent_hp_base / damage_taken_mult already bypasses. Losing the flag therefore
--     costs a governed boss nothing -- its numbers come from its per-WCID stats.
--   * PropertyBool.CanEnrage (9014) is a SEPARATE low-health rage mechanic and was never set on
--     any T11 weenie (0 rows). Untouched here.
--
-- Idempotent: re-running deletes nothing further. Generated 2026-07-30.

SET FOREIGN_KEY_CHECKS = 0;

DELETE FROM `weenie_properties_bool`
 WHERE `type` IN (50043, 50044)
   AND `object_Id` BETWEEN 730000000 AND 730999999;

SET FOREIGN_KEY_CHECKS = 1;

-- ---------------------------------------------------------------------------------------
-- ROLLBACK (exact pre-change state captured 2026-07-30). Uncomment to restore.
--
-- INSERT INTO `weenie_properties_bool` (`object_Id`,`type`,`value`) VALUES
--   (730000009,50043,1),(730000016,50043,1),(730000019,50043,1),(730000020,50043,1),
--   (730000023,50043,1),(730000024,50043,1),(730000025,50043,1),(730000026,50043,1),
--   (730000027,50043,1),(730000028,50043,1),(730000030,50043,1),(730000031,50043,1),
--   (730000032,50043,1),(730000033,50043,1),(730000034,50043,1),(730000038,50043,1),
--   (730000039,50043,1),(730000040,50043,1),(730000041,50043,1),(730000042,50043,1),
--   (730000043,50043,1),(730000044,50043,1),(730000050,50043,1),(730000052,50043,1),
--   (730000053,50043,1),(730000054,50043,1),(730000055,50043,1),(730000056,50043,1),
--   (730000057,50043,1),(730000058,50043,1),(730000059,50043,1),(730000061,50043,1),
--   (730000062,50043,1),(730000063,50043,1),(730000064,50043,1),(730000066,50043,1),
--   (730000067,50043,1),(730000068,50043,1),(730000069,50043,1),(730000070,50043,1),
--   (730000071,50043,1),(730000122,50043,1),(730000123,50043,1),(730000126,50043,1),
--   (730000035,50044,1),(730000036,50044,1),(730000037,50044,1),(730000045,50044,1),
--   (730000101,50044,1),(730000102,50044,1),(730000103,50044,1),(730000104,50044,1),
--   (730000105,50044,1),(730000106,50044,1),(730000107,50044,1),(730000108,50044,1),
--   (730000109,50044,1),(730000110,50044,1),(730000111,50044,1),(730000112,50044,1),
--   (730000113,50044,1),(730000114,50044,1),(730000115,50044,1),(730000116,50044,1)
-- ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);
-- ---------------------------------------------------------------------------------------
