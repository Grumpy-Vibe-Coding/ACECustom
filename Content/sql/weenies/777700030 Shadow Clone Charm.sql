-- ============================================================
-- Shadow Clone Charm — WCID 777700030
-- ILT Ability Charm — Ability ID 24 (HasShadowCloneCharm)
-- While active, two ethereal clones flank the player (left + right),
-- mirror every equipped item's appearance, and deal 100% of all
-- melee, missile, and magic damage the player deals to the same targets.
-- Clones are ethereal: untargetable, cannot be killed, pass through walls.
-- They respawn automatically after death or portal travel.
-- ============================================================

DELETE FROM `weenie` WHERE `class_Id` = 777700030;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777700030, 'ilt_shadowclonecharm', 38, NOW());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777700030,    11, 1)  /* IgnoreCollisions */
     , (777700030,    13, 1)  /* Ethereal */
     , (777700030,    14, 1)  /* GravityStatus */
     , (777700030,    63, 1)  /* UnlimitedUse */
     , (777700030,  9040, 1)  /* IsCharm */
     , (777700030, 50000, 1); /* IsAbilityCharm */

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777700030,     1, 2048) /* ItemType - Gem */
     , (777700030,     5,    5) /* EncumbranceVal */
     , (777700030,     8,    5) /* Mass */
     , (777700030,    16,    8) /* ItemUseable - Contained */
     , (777700030,    19,    1) /* UiEffects - Magical */
     , (777700030,    33,    1) /* Bonded */
     , (777700030,    83,    2) /* ActivationResponse - Use */
     , (777700030,    93, 1044) /* PhysicsState */
     , (777700030,   114,    1) /* Attuned */
     , (777700030, 50000,   24) /* CharmGrantsAbility - ID 24 = Shadow Clone */
     , (777700030, 50005,    1) /* CharmLevel - 1 */
     , (777700030, 50006,    1); /* CharmMaxLevel - 1 */

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777700030,    1, 33554556)  /* Setup - Coffer/Chest visual */
     , (777700030,    3, 536870932) /* SoundTable */
     , (777700030,    8, 100692234) /* Icon */
     , (777700030,   48, 100676435) /* IconUnderlay */
     , (777700030,   50, 100667550); /* IconOverlay - Tier 1 badge */

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777700030,  1, 'Shadow Clone Charm')
     , (777700030, 14, '
Double-click to activate. While active, two ethereal shadows of yourself flank your left and right side. They mirror your equipped appearance exactly and strike every enemy you hit — melee, missile, or magic — dealing identical damage. The clones cannot be targeted or killed, pass through obstacles, and automatically respawn when you die or travel through a portal.
');
