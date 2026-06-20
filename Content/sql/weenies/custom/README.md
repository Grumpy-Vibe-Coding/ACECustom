# Custom Creatures

This folder contains custom creature variants developed for ACECustom server testing and deployment.

---

## Folder Structure

```
custom/
├── README.md          (this file)
├── CHANGELOG.md       (what we've built, testing status)
└── <WCID>-<Name>.sql (individual creature definitions)
```

## WCID Range

**Reserve:** 72000000 - 72999999

All custom creatures use WCIDs in this range to avoid conflicts with retail/upstream content.

---

## Workflow

### 1. Development (you tell me what to create)

I build the creature SQL in this folder following the template in CHANGELOG.md.

### 2. Testing (you test in-game)

**Sync to database:**
```powershell
cd C:\ACE\ACECustom\Database
.\sync-custom-creatures.bat
```

**Spawn in-game:**
```
/create 72000001        # Temporary spawn for testing
/createinst 72000001    # Permanent spawn (persists after restart)
/delete                 # Remove temporary spawn
(target creature)
/removeinst             # Permanently remove landblock instance
```

### 3. Validation

Test checklist in CHANGELOG.md:
- [ ] Spawns without errors
- [ ] No lag or crashes
- [ ] AI/spells work as intended
- [ ] Stable for permanent deployment

### 4. Deployment (when ready)

- Update CHANGELOG.md with "Completed" status
- Commit to feature branch
- I push to fork
- When fully tested across multiple variants, merge to master
- Eventually PR upstream to rkroska/ACECustom (with explicit permission)

---

## Creating a New Creature

1. **I query the base creature** from live DB via HeidiSQL
2. **Generate full SQL** using DbQueryTool export
3. **Customize:** skills, spells, stats as needed
4. **Save to:** `<WCID>-<Descriptive-Name>.sql`
5. **Add entry to CHANGELOG.md** with status "Planning"
6. **Sync and test:** Run `sync-custom-creatures.bat`
7. **You test:** `/create <wcid>` in-game
8. **Iterate** until good, then mark "Testing" → "Completed"

---

## Key Files

| File | Purpose |
|------|---------|
| `CHANGELOG.md` | Track all creatures, status, and testing checklist |
| `<WCID>-<Name>.sql` | Individual creature definition (INSERT statements) |
| `../sync-custom-creatures.bat` | Robocopy sync to Database/Updates |

---

## Git Workflow

**Current branch:** `creatures/custom-golems`

- Feature branch for creature development
- Isolated from master (upstream)
- Safe testing ground
- Will merge back when batch is validated

**Commits:**
- Logged with your GitHub username (`oldmangrumpy`)
- Descriptive messages (what creature, why)
- Tracked in git log for history

---

## Quick Reference

| Action | Command |
|--------|---------|
| Sync creatures to DB | `.\Database\sync-custom-creatures.bat` |
| Spawn temp | `/create 72000001` |
| Spawn permanent | `/createinst 72000001` |
| Remove temp | `/delete` (after targeting) |
| Remove permanent | `/removeinst` (after targeting) |
| Check status | View CHANGELOG.md |
| See changes | `git log --oneline` |

---

## Questions?

- **How to test?** See "Testing" section above
- **Create new creature?** Tell me: base creature, stats, spells, name
- **Need help?** Check CHANGELOG.md for examples
- **Want to commit?** I handle it — just say "good" when tested

---

Created: 2026-06-20  
Last updated: 2026-06-20
