-- ============================================================
-- Player Clone Placeholder — WCID 777700031
-- A minimal WorldObject placeholder used as the base weenie for
-- PlayerClone instances spawned by the Shadow Clone Charm.
-- All meaningful properties (SetupTableId, MotionTableId, position,
-- Name, appearance) are overridden at runtime by PlayerClone.Initialize().
-- ============================================================

DELETE FROM `weenie` WHERE `class_Id` = 777700031;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777700031, 'ilt_playerclone_placeholder', 2, NOW()); /* type 2 = Creature so client renders animations */

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777700031,  11, 1)  /* IgnoreCollisions = true  */
     , (777700031,  13, 1)  /* Ethereal = true           */
     , (777700031,  14, 1)  /* GravityStatus = true (will be overridden to false at runtime) */
     , (777700031,  22, 0); /* Attackable = false        */

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777700031,  93, 1044); /* PhysicsState baseline */

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777700031,  1, 'Shadow Clone'); /* Placeholder name; overridden at runtime */
