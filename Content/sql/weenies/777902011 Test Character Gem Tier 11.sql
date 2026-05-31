-- ============================================================
-- Tier 11 Test Character Gem — WCID 777902011
-- Developer-only tool to instantly configure character stats
-- for Tier 11 content testing.
-- ============================================================

DELETE FROM `weenie` WHERE `class_Id` = 777902011;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777902011, 'ilt_test_char_gem_t11', 38, NOW());

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777902011,    11, 1)  /* IgnoreCollisions */
     , (777902011,    13, 1)  /* Ethereal */
     , (777902011,    14, 1)  /* GravityStatus */
     , (777902011,    63, 1); /* UnlimitedUse */

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777902011,     1, 2048) /* ItemType - Gem */
     , (777902011,     3,    0) /* PaletteTemplate - None/Rainbow */
     , (777902011,     5,    5) /* EncumbranceVal */
     , (777902011,     8,    5) /* Mass */
     , (777902011,    16,    8) /* ItemUseable - Contained */
     , (777902011,    19,    1) /* UiEffects - Magical */
     , (777902011,    33,    1) /* Bonded */
     , (777902011,    83,    2) /* ActivationResponse - Use */
     , (777902011,    93, 1044) /* PhysicsState */
     , (777902011,   114,    1); /* Attuned */

DELETE FROM `weenie_properties_float` WHERE `object_Id` = 777902011;
INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777902011,   12,  0.0); /* Shade - 0.0 */

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777902011,    1, 33554460)  /* Setup - Gem Mesh */
     , (777902011,    3, 536870932) /* SoundTable */
     , (777902011,    8, 100668132) /* Icon - Prismatic/Rainbow Gem icon */
     , (777902011,   48, 100676435); /* IconUnderlay */

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777902011,  1, 'Tier 11 Test Character Gem (Stats & Spells)')
     , (777902011, 14, 'Double-click to transform your character into an endgame Tier 11 test character. Sets base attributes to 460/580/550/510, vitals to 685/900/920, all custom and retail augs to max, and learns all spells. Developer use only.');
