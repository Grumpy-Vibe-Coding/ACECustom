# Invasion System — Live Boss Tuning & Plugin Protocol

**Audience:** ACECustom server devs + AI agents working on the Invasion System and the **Invasion Helper** Decal plugin.
**Status:** Phase 1 built & deployed (2026-06-26). One boss (Tyrant Darkspire Golem, WCID `72000001`).

This documents how the plugin's **Bosses** tab tunes an invasion boss's stats live, and the general plugin↔server protocol it builds on.

---

## 1. Why it's built this way
The Invasion Helper plugin is a **Decal (client-side) plugin** — it cannot read or write server objects directly. So everything goes over two channels that already exist:

| Direction | Mechanism |
|---|---|
| Plugin → Server | `CoreManager.Current.Actions.InvokeChatParser("/dev …")` — issues a normal command (respects server `AccessLevel`). |
| Server → Plugin | A system-chat line with a sentinel prefix the plugin intercepts in `ChatBoxMessage`, sets `e.Eat = true` (never displayed), and parses. |

Sentinels in use:
- `[[IH]]` — the per-tick live invasion state feed (HP, timers, eligibility…). See `InvasionManager.Sync.cs`.
- `[[IHB]]` — **boss-admin data**, sent on demand (this doc). See `InvasionManager.BossAdmin.cs`.

The plugin only shows the Bosses tab when the sync feed flags the session as admin (`admin=1`, i.e. `AccessLevel.Developer+`). All server commands are `AccessLevel.Developer`.

---

## 2. The flow
```
Bosses tab opened ──► /dev invasion bossinfo ──────────────► SendBossInfo(player)
fields populated  ◄── [[IHB]]wcid=..|hp=..|scale=..|dr=..|act=..|livehpmax=..|livescale=..|livedr=..
edit + Apply      ──► /dev invasion bossset <prop> <value> ─► SetBossProperty(prop,value)
                                                              • persist ServerConfig override
                                                              • apply live to ActiveBoss (if any)
fields refreshed  ◄── [[IHB]] (echoed)
```

---

## 3. Commands (`/dev invasion …`, AccessLevel.Developer)
| Command | Effect |
|---|---|
| `bossinfo` | Sends a `[[IHB]]` line with the boss's current override + live values to the caller. |
| `bossset <health\|scale\|damagerating> <value>` | Sets the override (persists) and applies it live to the active boss. Echoes `bossinfo`. |

(Related reward/control commands: `reward <wcid> [amount]`, `treasure <id>`, `lockout <seconds>`, `start <town> <species> [force]`, `stop`, `status`, `minions on|off`, `enable|disable`.)

---

## 4. `[[IHB]]` payload fields
Pipe-delimited `k=v`:

| Key | Meaning |
|---|---|
| `wcid` | Boss weenie id (72000001) |
| `hp` | Max-health override (0 = use weenie default) |
| `scale` | Scale override (0 = default) |
| `dr` | Damage-rating override (0 = none) |
| `act` | 1 if a boss is currently spawned |
| `livehpmax` | Active boss's current max health (only if `act=1`) |
| `livescale` | Active boss's current scale (only if `act=1`) |
| `livedr` | Active boss's current damage rating (only if `act=1`) |

---

## 5. Overrides (ServerConfig — persist in shard DB across restarts)
| Key | Type | Default | Applied where |
|---|---|---|---|
| `invasion_boss_health` | long | 0 | `ApplyBossOverrides` at spawn (0 = skip) |
| `invasion_boss_scale` | double | 0 | `ApplyBossOverrides` at spawn (0 = skip) |
| `invasion_boss_damage_rating` | long | 0 | `ApplyBossOverrides` at spawn (0 = skip) |

`ApplyBossOverrides(Creature boss)` runs in `SpawnBoss()` **before `EnterWorld()`** so scale/health are correct on spawn.

### Per-property apply mechanics
| Property | Live apply | Notes |
|---|---|---|
| **Max Health** | `boss.Health.StartingValue = value; boss.SetMaxVitals();` | Plugin HUD boss bar reflects new max immediately. |
| **Scale** | `boss.ObjScale = value;` | Value set live; visual reliably correct on (re)spawn. |
| **Damage Rating** | `boss.DamageRating = value;` | Honored by the combat damage calc live (`Creature_Rating.GetDamageRating`). |

---

## 6. Files
**Server (`ACE.Server`):**
- `Managers/InvasionManager.BossAdmin.cs` — *new*. `BossWcid`, override properties, `ApplyBossOverrides`, `SetBossProperty`, `SendBossInfo`, `BossSyncPrefix`.
- `Managers/InvasionManager.cs` — `SpawnBoss()` calls `ApplyBossOverrides(boss)` before `EnterWorld()`.
- `Command/Handlers/DevCommands.cs` — `bossinfo` / `bossset` subcommands.
- `Managers/PropertyManager.cs` — the three `invasion_boss_*` ServerConfig entries.

**Plugin (`InvasionHelper`):**
- `InvasionState.cs` — boss-admin fields.
- `PluginCore.cs` — `BossSyncPrefix`, `ParseBossInfo`, intercept in `Core_ChatBoxMessage`.
- `InvasionHud.cs` — `RenderBossesTab` / `DrawBossField`; tab gated by `State.IsAdmin`.

---

## 7. Extending (Phase 2)
- **Add a tunable property:** one `case` in `SetBossProperty` (live-apply + override), add the field to `SendBossInfo`'s `[[IHB]]` line, and one `DrawBossField(...)` row in the plugin. (Candidates: attributes, defenses, level, XP.)
- **Multiple bosses:** make the dropdown server-driven — have the server send the boss list (a `[[IHB]]` variant or a list command), and key `bossinfo`/`bossset` by WCID. Overrides would move from single ServerConfig keys to per-WCID storage.

---

## 8. Deployment reminders (see `codebase_architecture_map.md` §9)
- No code edits without approval; **server OFF before deploying** `ACE.Server.dll`.
- Deploy with file filters only — never `robocopy /MIR` or `/PURGE` on `ACEBuild` (would delete `Config.js`).
- Keep `SslMode=Disabled`; GM commands use `AccessLevel.Developer`+.
