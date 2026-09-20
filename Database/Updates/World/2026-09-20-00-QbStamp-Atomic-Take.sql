-- ===========================================================================
-- GrantRandomQuestStamp: fold TakeItems into the grant  -  2026-09-20
--
-- WHY. These NPCs take the turn-in at emote action order 2 and grant the stamp
-- at order 4, with a 0.5 s pre-delay between them. The chain is queued on the
-- NPC's own EmoteManager, so if the NPC is destroyed in that window - a timed
-- event NPC despawning, a generator shutting down - the item is consumed and
-- nothing is ever granted. Silently: WorldObject.EnqueueAction discards the
-- continuation for creatures with no log and no exception.
--
-- WHAT. Emote type 136 now understands a "TAKE:<wcid>[:<amount>]|" prefix and
-- consumes the turn-in itself, in the same synchronous step as the grant.
-- Nothing can run between them. It also means the refund branch disappears:
-- with nothing to grant, the item is simply never taken.
--
-- This removes the ord2 TakeItems row and moves its wcid into the 136 message.
-- Safe to re-run: the UPDATE skips messages that already carry a TAKE prefix.
--
-- Weenies changed (every one with this shape):
--   696900158  QBs R Us               takes 987100  Event Coin
--   98760330   QB Dynamo (tester)     takes 300004  Enlightened Coin
--   98760369   QB Dynamo (placed)     takes 867530128 QB Stipend
--   777706010/11/12  Grumpy QB Test   takes 777706001   (test rig)
--   777706020/21/22  Grumpy QB Dyn    takes 777706003   (test rig)
-- ===========================================================================

-- --- 1. carry the TakeItems wcid (and stack size, when set) into the message
UPDATE weenie_properties_emote_action a
JOIN (
    SELECT emote_Id, weenie_Class_Id, stack_Size
      FROM weenie_properties_emote_action
     WHERE type = 74
       AND weenie_Class_Id IS NOT NULL
) t ON t.emote_Id = a.emote_Id
SET a.message = CONCAT('TAKE:', t.weenie_Class_Id,
                       CASE WHEN t.stack_Size IS NULL OR t.stack_Size <= 1 THEN '' ELSE CONCAT(':', t.stack_Size) END,
                       '|', a.message)
WHERE a.type = 136
  AND a.message NOT LIKE 'TAKE:%'
  AND a.emote_Id IN (
        SELECT id FROM weenie_properties_emote
         WHERE object_Id IN (696900158, 98760330, 98760369,
                             777706010, 777706011, 777706012,
                             777706020, 777706021, 777706022)
  );

-- --- 2. drop the now-redundant TakeItems action from those same emote sets
DELETE FROM weenie_properties_emote_action
 WHERE type = 74
   AND emote_Id IN (
        SELECT id FROM weenie_properties_emote
         WHERE object_Id IN (696900158, 98760330, 98760369,
                             777706010, 777706011, 777706012,
                             777706020, 777706021, 777706022)
           AND category = 13
  );

-- --- verify: every 136 message should start with TAKE:, and no type 74 left
-- SELECT e.object_Id, LEFT(a.message, 40) AS msg,
--        (SELECT COUNT(*) FROM weenie_properties_emote_action t
--          WHERE t.emote_Id = a.emote_Id AND t.type = 74) AS takeitems_left
--   FROM weenie_properties_emote_action a
--   JOIN weenie_properties_emote e ON e.id = a.emote_Id
--  WHERE a.type = 136;

-- ===========================================================================
-- ROLLBACK. Restores the separate TakeItems action and strips the prefix.
-- Only correct while the server runs a build WITHOUT the TAKE support.
-- ===========================================================================
-- INSERT INTO weenie_properties_emote_action (emote_Id, `order`, type, delay, extent, weenie_Class_Id)
--   SELECT a.emote_Id, 2, 74, 0, 1,
--          CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(SUBSTRING(a.message, 6), '|', 1), ':', 1) AS UNSIGNED)
--     FROM weenie_properties_emote_action a
--     JOIN weenie_properties_emote e ON e.id = a.emote_Id
--    WHERE a.type = 136 AND a.message LIKE 'TAKE:%'
--      AND e.object_Id IN (696900158, 98760330, 98760369, 777706010, 777706011, 777706012, 777706020, 777706021, 777706022);
-- UPDATE weenie_properties_emote_action a
--    JOIN weenie_properties_emote e ON e.id = a.emote_Id
--     SET a.message = SUBSTRING(a.message, LOCATE('|', a.message) + 1)
--  WHERE a.type = 136 AND a.message LIKE 'TAKE:%'
--    AND e.object_Id IN (696900158, 98760330, 98760369, 777706010, 777706011, 777706012, 777706020, 777706021, 777706022);
