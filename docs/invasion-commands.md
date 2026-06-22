# Invasion System — Command Reference

## Admin Commands (`/dev invasion`)

> Requires Admin access level. Alias: `/dev invasions`

---

### System Control

| Command | Description |
|---------|-------------|
| `/dev invasion enable` | Enable automatic random invasions. Auto-invasions will begin after the startup grace period + a random cooldown roll. |
| `/dev invasion disable` | Disable automatic random invasions. Any active invasion continues until it ends naturally or is stopped manually. |
| `/dev invasion start <town> <species>` | Manually start an invasion in a specific town. Bypasses the cooldown timer. |
| `/dev invasion stop` | Force stop the currently active invasion (no rewards, no boss kill credit). |
| `/dev invasion status` | Display full system status — active state, current town/species, boss HP, elapsed time, cooldown remaining, thresholds, and per-player participation. |

---

### Timing

| Command | Description |
|---------|-------------|
| `/dev invasion cooldown min <value>` | Set the minimum random cooldown between invasions. |
| `/dev invasion cooldown max <value>` | Set the maximum random cooldown between invasions. |
| `/dev invasion cooldown <value>` | Set both min and max to the same fixed value (no randomness). |
| `/dev invasion timeout <value>` | Set the proximity grace period — how long a player can be out of range before losing participation. |

**Duration shorthand:** Values accept shorthand format — `30s`, `2m`, `1h`, `1h30m`, `90s`, etc.  
**Defaults:** Cooldown min `1h`, cooldown max `4h`. Startup grace period is 5 minutes after server comes online.

---

### Participation Thresholds

| Command | Description |
|---------|-------------|
| `/dev invasion threshold damage <value>` | Set the minimum damage a player must deal to invasion mobs to be considered eligible for rewards. Default: `500000` |
| `/dev invasion threshold healing <value>` | Set the minimum healing a player must do to be considered eligible for rewards. Default: `10000` |

> A player only needs to meet **one** threshold (damage **or** healing) to qualify.

---

### Minions

| Command | Description |
|---------|-------------|
| `/dev invasion minions on` | Enable minion wave spawning during invasions. |
| `/dev invasion minions off` | Disable minion waves — boss-only mode (default). |

---

### Valid Towns
```
Al-Arqas, Al-Jalima, Arwic, Baishi, Cragstone, Eastham, Glenden Wood,
Hebian-to, Holtburg, Kara, Khayyaban, Lytelthorpe, Mayoi, Nanto, Neydisa,
Rithwic, Samsur, Sawato, Shoushi, Stonehold, Tufa, Uziz, Yanshi, Yaraq, Zaikhal
```

### Valid Species
```
Shadow, Tusker, Olthoi
```

---

## Player Commands (`/ilt invasion`)

> Available to all players.

| Command | Description |
|---------|-------------|
| `/ilt invasion` | Show your current invasion broadcast preference. |
| `/ilt invasion on` | Enable `[Invasion]` server-wide broadcast messages (default). |
| `/ilt invasion off` | Hide all `[Invasion]` server-wide broadcast messages. Preference persists across logouts. |

> **Note:** Only server-wide `[Invasion]` announcements are filtered. Private messages (e.g. threshold reached notifications) are always delivered.

---

## Notes

- All settings persist across server restarts via the shard database (`ServerConfig`).
- Damage tracking only counts damage dealt to mobs with **"Invasion"** in their name.
- Healing tracking counts spell heals and healer kits on other players — capped to missing health (no overheal).
- The kill proximity radius (150 world units) is measured from the **invasion generator** location, not the boss spawn table.
