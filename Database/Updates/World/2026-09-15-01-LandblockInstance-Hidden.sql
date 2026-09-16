-- Per-placement visibility for ONE placed object (owner 2026-09-15). Both flags, a column each beside `scale`:
--   hidden      = PropertyBool.NoDraw  - the client is told not to DRAW it; it is still there and still solid
--                 (a hidden plate still triggers, a hidden door still blocks). Admins see it with admin vision.
--   server_only = PropertyBool.Visibility - the server never SENDS it to a player at all (how the maze/trap plates
--                 are invisible today, baked into their weenies).
-- NULL on both = exactly as today. Must run BEFORE any server build that has LandblockInstance.Hidden/ServerOnly.
ALTER TABLE `landblock_instance`
ADD COLUMN `hidden` BIT(1) NULL DEFAULT NULL AFTER `scale`,
ADD COLUMN `server_only` BIT(1) NULL DEFAULT NULL AFTER `hidden`;
