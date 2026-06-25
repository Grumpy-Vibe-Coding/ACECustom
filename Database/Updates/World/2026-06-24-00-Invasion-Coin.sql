-- =========================================================================
-- Invasion Coin — reward currency (WCID 777700500)
-- Cloned from Event Coin (987100), with these deliberate differences:
--   * Stackable: MaxStackSize 50000 (Event Coin was 1)
--   * No value: Value/CoinValue/StackUnitValue = 0 (reward token, not sellable)
--   * Not usable (plain token)
--   * NO Lifespan — Event Coin decays after 1200s; a reward must never decay
-- =========================================================================

DELETE FROM `weenie`                     WHERE `class_Id`  = 777700500;
DELETE FROM `weenie_properties_int`      WHERE `object_Id` = 777700500;
DELETE FROM `weenie_properties_string`   WHERE `object_Id` = 777700500;
DELETE FROM `weenie_properties_d_i_d`    WHERE `object_Id` = 777700500;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`) VALUES
  (777700500, 'invasion_coin', 51, NOW());

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
  (777700500,  1, 128),     -- ItemType = Money
  (777700500,  5, 0),       -- EncumbranceVal
  (777700500,  8, 0),       -- Mass
  (777700500, 11, 50000),   -- MaxStackSize
  (777700500, 12, 1),       -- StackSize
  (777700500, 13, 0),       -- StackUnitEncumbrance
  (777700500, 14, 0),       -- StackUnitMass
  (777700500, 15, 0),       -- StackUnitValue
  (777700500, 16, 0),       -- ItemUseable (not usable)
  (777700500, 18, 1),       -- UiEffects = Magical (subtle glow)
  (777700500, 19, 0),       -- Value
  (777700500, 20, 0),       -- CoinValue
  (777700500, 93, 1044);    -- PhysicsState (standard item)

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
  (777700500,  1, 'Invasion Coin'),
  (777700500, 16, 'A coin minted for those who turned back an invasion of Dereth.');

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
  (777700500,  1, 33557367),    -- Setup (3D model, from Event Coin)
  (777700500,  8, 100690337),   -- Icon
  (777700500, 50, 100671476),   -- IconOverlaySecondary
  (777700500, 52, 100667854);   -- IconUnderlay
