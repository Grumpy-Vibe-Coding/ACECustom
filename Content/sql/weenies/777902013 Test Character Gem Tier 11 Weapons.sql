-- ============================================================
-- Tier 11 Test Character Gem (Weapons) — WCID 777902013
-- Developer-only tool to spawn the complete set of 28 elemental
-- weapons for Tier 11 content testing.
-- ============================================================

DELETE FROM `weenie` WHERE `class_Id` = 777902013;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777902013, 'ilt_test_char_gem_t11_weapons', 38, NOW());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777902013,    11, 1)  /* IgnoreCollisions */
     , (777902013,    13, 1)  /* Ethereal */
     , (777902013,    14, 1)  /* GravityStatus */
     , (777902013,    63, 1); /* UnlimitedUse */

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777902013,     1, 2048) /* ItemType - Gem */
     , (777902013,     3,    1) /* PaletteTemplate - Fire Red */
     , (777902013,     5,    5) /* EncumbranceVal */
     , (777902013,     8,    5) /* Mass */
     , (777902013,    16,    8) /* ItemUseable - Contained */
     , (777902013,    19,    1) /* UiEffects - Magical */
     , (777902013,    33,    1) /* Bonded */
     , (777902013,    83,    2) /* ActivationResponse - Use */
     , (777902013,    93, 1044) /* PhysicsState */
     , (777902013,   114,    1); /* Attuned */

DELETE FROM `weenie_properties_float` WHERE `object_Id` = 777902013;
INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777902013,    12,  0.0); /* Shade - 0.0 */

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777902013,    1, 33554460)  /* Setup - Gem Mesh */
     , (777902013,    3, 536870932) /* SoundTable */
     , (777902013,    8, 100668132) /* Icon - Prismatic/Rainbow Gem icon */
     , (777902013,   48, 100676435); /* IconUnderlay */

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777902013,  1, 'Tier 11 Test Character Gem (Weapons)')
     , (777902013, 14, 'Double-click to spawn a complete set of 28 elemental weapons (4 styles: UA, 2H, Bow, Wand x 7 elements) directly in your inventory. Developer use only.');
