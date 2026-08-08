# Prestige tier-ladder + population experiments — DB content pack (preserved 2026-08-08)

Pruned from the live server and the ILT carry-over pack on 2026-08-08 (owner ruling: remove
entirely, park for later). These were the terrain-map/prestige-merge lane experiments; the
plan of record moved to ZoneControl zones (per-variation Default layers, Territory boundaries),
so the ladder was abandoned half-built. The PrestigeManager server code is NOT part of this
pack — it lives on in the main branches; this is DB content only.

## Contents (62 weenies + 7 placements)

- **14 tier portals** `777700030-043` — "Tou Tou Prestige T12".."T25". Identical clones; all
  teleport to landblock 0xF559 x152.59 y80.8 (Tou Tou island), each pinned to variation
  v12..v25 respectively. Only 7 were ever placed (see `prestige-instances.sql`): inside cell
  0x010A of landblock 0xF25B, each standing in the variation one below its destination.
- **9 spawners** `777700200-214` — never placed anywhere:
  - `777700200` Option C Poisson scatter test rig (spawns portals 777700030 as markers).
  - `777700201-203` cluster spawners: shadow / inland / coastal biome scatter tests.
  - `777700210-214` biome spawners: water / golem / undead / fauna / beach, spawning the
    prestige mobs below.
- **39 prestige mobs** `777701001-053` (`prestige_*` retail clones: golems, zombie lich,
  gromnus, shadows/void lord, margul, grievers, drudges, olthoi, banderlings, monougas,
  tuskers, wisps, phyntos wasps, tzefir, shark). Referenced ONLY by the spawners above; no
  world placements.

NOT included (deliberately): `777700029 TouTouPrestigeRecallGem` (ILT-shared wcid, live gems
exist in the shard) and the `prestige_allowed_landblocks` table rows (ZoneControl boundary
lane, still live).

## To restore

Apply all weenie SQLs, then `prestige-instances.sql` (weenies before instances). Spawners and
mobs need placing (they never had placements); the poisson/cluster rigs were driven by admin
spawn commands.
