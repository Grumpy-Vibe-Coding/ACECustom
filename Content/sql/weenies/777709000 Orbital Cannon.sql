-- ============================================================
-- Orbital Cannon — WCID 777709000
-- ILT Ability Charm — Ability ID 9001 (Unregistered / Prank)
-- Double-click to toggle. Deals over 9000 damage to all enemies
-- on the radar and teleports their corpses to the player.
-- (Warning: Does not actually do anything. It is a prank charm!)
-- ============================================================

DELETE FROM `weenie` WHERE `class_Id` = 777709000;
DELETE FROM `weenie_properties_bool` WHERE `object_Id` = 777709000;
DELETE FROM `weenie_properties_int` WHERE `object_Id` = 777709000;
DELETE FROM `weenie_properties_float` WHERE `object_Id` = 777709000;
DELETE FROM `weenie_properties_string` WHERE `object_Id` = 777709000;
DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` = 777709000;
DELETE FROM `weenie_properties_i_i_d` WHERE `object_Id` = 777709000;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777709000, 'ilt_orbital_cannon_meme', 38, NOW());

-- Bools
INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES
(777709000,    11, 1),  -- IgnoreCollisions
(777709000,    13, 1),  -- Ethereal
(777709000,    14, 1),  -- GravityStatus
(777709000,    63, 1),  -- UnlimitedUse
(777709000,  9040, 1),  -- IsCharm — enables tier-aware appraise header
(777709000, 50000, 1);  -- IsAbilityCharm

-- Ints
INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(777709000,     1,  2048),  -- ItemType: Gem
(777709000,     5,     5),  -- EncumbranceVal
(777709000,     8,     5),  -- Mass
(777709000,    16,     8),  -- ItemUseable: Contained
(777709000,    83,     2),  -- ActivationResponse: Use
(777709000,    19,     1),  -- UiEffects: Magical
(777709000,    33,     1),  -- Bonded
(777709000,    93,  1044),  -- PhysicsState
(777709000,   114,     1),  -- Attuned
(777709000, 50000,  9001),  -- CharmGrantsAbility: Fake ID 9001 = Orbital Cannon
(777709000, 50005,  9001),  -- CharmLevel: 9001
(777709000, 50006,  9000);  -- CharmMaxLevel: 9000

-- Strings
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777709000,  1, 'Orbital Cannon'),
(777709000, 14, '
Use: Establishes a real-time, ultra-high bandwidth neural uplink with the Orbital Laser Defense Grid (Antigravity-Corp, Model v9.1).

When toggled ON, deals exactly 9001 units of unmitigated thermonuclear, orbital damage to all hostile life forms within your radar sweep. Immediately teleports all resulting piles of smoking corpses directly to your player''s feet, neatly arranged in a chest-height loot pile for convenient, low-effort pillaging.

Warning: Antigravity-Corp is not responsible for localized atmosphere ionization, accidental hair vaporization, or severe administrative reprimands from irritated game masters. Indoor activation may result in self-vaporization. Satellite latency may vary during rainstorms.
');

-- DataIds (visuals — matching custom charms coffer/chest Setup)
INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(777709000,  1, 33554556),   -- Setup (Coffer/Chest)
(777709000,  3, 536870932),  -- SoundTable
(777709000,  6, 67111919),   -- PaletteBase
(777709000,  8, 100670704),  -- Icon (Ring of Unspeakable Agony spell icon - 0x06001CF0)
(777709000, 48, 100676435),  -- IconUnderlay
(777709000, 50, 100667550);  -- IconOverlay (Tier 1 badge)
