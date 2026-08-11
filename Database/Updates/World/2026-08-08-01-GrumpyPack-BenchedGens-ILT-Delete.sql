-- Owner ruling 2026-08-08: the Tou Tou mob layer was removed (mobs to be rebuilt from
-- scratch). Seven of the benched camp gens are wcids ILT ALSO has from an earlier sync,
-- orphaned on both sides (zero instance refs verified at migration) — delete them on ILT
-- at merge so no stale copies survive. Idempotent; on the local DB this is a no-op after
-- the 2026-08-08 prune. Archive: Migration pruned-archive\toutou-mobs-2026-08-08.
DELETE FROM `weenie` WHERE `class_Id` IN (730000078, 730000079, 730000084, 730000085, 730000086, 730000087, 730000091);
