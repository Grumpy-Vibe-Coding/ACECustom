# Custom Creatures Changelog

Tracking all custom creature variants created for ACECustom server.

---

## 72000001 — Tyrant Darkspire Golem - Life Void

**Status:** Testing  
**Created:** 2026-06-20  
**File:** `72000001-TyrantDarkspireGolemLifeVoid.sql`

**Description:**
- Hybrid caster-tank variant of base Tyrant Darkspire Golem (WCID 71700354)
- LifeMagic 9000 (specialized) — for drain spells
- VoidMagic 9000 (specialized) — for curse spells
- Removed WarMagic to focus on life/void casting

**Spells:**
- HarmOther8 (4317) — high-tier destruction curse
- DrainHealth8 (4652) — high-tier life drain
- Both probability 2.11 (cast frequently)

**Stats:**
- Level 1100
- MaxHealth: 3,480,000
- MaxMana: 900,000
- MaxStamina: 180,000
- DamageRating: 975
- DamageResistRating: 800

**Testing Checklist:**
- [ ] Spawns without errors (`/create 72000001`)
- [ ] No lag or crashes in-game
- [ ] Spells cast properly (HarmOther8, DrainHealth8)
- [ ] Targeting/AI works correctly
- [ ] Stable for permanent spawn (`/createinst 72000001`)

**Notes:**
- Based on successful base golem (71700354)
- Ready for in-game testing

---

## Template for future creatures

**[WCID]** — [Name]

**Status:** [Planning/Development/Testing/Completed]  
**Created:** [Date]  
**File:** `[filename].sql`

**Description:**
- [Brief overview]

**Spells:**
- [Spell list with IDs and probabilities]

**Stats:**
- [Key stats]

**Testing Checklist:**
- [ ] [Tests needed]

**Notes:**
- [Any special considerations]
