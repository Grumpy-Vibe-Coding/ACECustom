# Town Invasion System — DB content pack (preserved 2026-08-08)

The Town Invasion System was pruned from the live server and from the ILT carry-over pack
on 2026-08-08 (owner ruling: remove entirely, park for later development). This folder is
the complete DB-side re-seed so the system can be restored in one pass when work resumes.
The server-side code for the system lives on THIS branch (`Random-Town-Invasion-System`):
InvasionManager, InvasionManager.BossAdmin, InvasionObjective/ThreeBossBurnObjective,
per-boss WCID tuning. The client plugin is a separate repo:
`C:\GrumpyDecalPlugins\InvasionHelper` (standalone, own remote).

## Contents

- **75 spawner weenies** `777700400-474 invasion_spawner_<Town>_<Shadow|Tusker|Olthoi>.sql`
  — 25 towns x 3 invasion types (Discord-import format, cascade DELETE headers).
- **4 legacy invasion mobs** `72000001 TyrantDarkspireGolemLifeVoid.sql`, `72000010-12
  invasion720000xx.sql`.
- **1 currency** `777700500 invasion_coin.sql`.
- **`invasion-events.sql`** — the 75 `event` rows (`Invasion_<Town>_<Type>`), the state machine.
- **`invasion-instances.sql`** — the 75 spawner placements (one post per town per type),
  guids 3751145638-3751145712. The legacy mobs have no placements.

## To restore

Apply every weenie SQL, then invasion-events.sql, then invasion-instances.sql (weenies must
exist before instances — `DELETE FROM weenie` cascades to `landblock_instance`). Invasion
config rows (server properties) were never carried to the fresh server and must be
reconfigured from this branch's InvasionManager defaults.
