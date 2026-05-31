-- ============================================================
-- Tier 11 Test Character Gem (Armor) — WCID 777902012
-- Developer-only tool to spawn the complete Prismatic GSA
-- armor suit for Tier 11 content testing.
-- ============================================================

DELETE FROM `weenie` WHERE `class_Id` = 777902012;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777902012, 'ilt_test_char_gem_t11_armor', 38, NOW());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777902012,    11, 1)  /* IgnoreCollisions */
     , (777902012,    13, 1)  /* Ethereal */
     , (777902012,    14, 1)  /* GravityStatus */
     , (777902012,    63, 1); /* UnlimitedUse */

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777902012,     1, 2048) /* ItemType - Gem */
     , (777902012,     3,    6) /* PaletteTemplate - Cobalt Blue */
     , (777902012,     5,    5) /* EncumbranceVal */
     , (777902012,     8,    5) /* Mass */
     , (777902012,    16,    8) /* ItemUseable - Contained */
     , (777902012,    19,    1) /* UiEffects - Magical */
     , (777902012,    33,    1) /* Bonded */
     , (777902012,    83,    2) /* ActivationResponse - Use */
     , (777902012,    93, 1044) /* PhysicsState */
     , (777902012,   114,    1); /* Attuned */

DELETE FROM `weenie_properties_float` WHERE `object_Id` = 777902012;
INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777902012,    12,  0.0); /* Shade - 0.0 */

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777902012,    1, 33554460)  /* Setup - Gem Mesh */
     , (777902012,    3, 536870932) /* SoundTable */
     , (777902012,    8, 100668132) /* Icon - Prismatic/Rainbow Gem icon */
     , (777902012,   48, 100676435); /* IconUnderlay */

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777902012,  1, 'Tier 11 Test Character Gem (Armor)')
     , (777902012, 14, 'Double-click to spawn a complete suit of Prismatic GSA armor (Shadow Brilliant Amuli, Celdon, Koujia sets + Prismatic Helm) directly in your inventory. Developer use only.');
