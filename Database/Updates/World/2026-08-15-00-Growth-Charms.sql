/* Growth Charms — five Charms and their twenty Gems (2026-08-15)
   ============================================================================
   The content half of the growth-charm system. The code half is on this branch:
   EmoteManager (the PromptAddAugment handlers), AppraiseInfo, Creature_Properties
   (the Effective*AugCount accessors) and PlayerCommands (/aug).

   WCIDs follow the owner convention: 7777XXXXX = custom items.
     777700030      Charm of the Triune Weave        gems 777700031-034
     777700051      Charm of the Battlemage's Wrath  gems 777700052-055   (War)
     777700056      Charm of the Nether Veil         gems 777700057-060   (Void)
     777700061      Charm of Crashing Steel          gems 777700062-065   (Melee)
     777700066      Charm of the True Shot           gems 777700067-070   (Missile)

   HOW IT WORKS
   A Charm is a 1-coin Bonded + Attuned token that holds no progress itself; the
   count lives on the CHARACTER (PropertyInt64 50000-50004) so a replacement charm
   always shows the true total. Using a Gem fires custom emote PromptAddAugment
   (123) with a message key (Triune / Wrath / Nether / Steel / TrueShot, plus 10,
   50 and 100 suffixes); the handler requires the matching Charm in inventory,
   consumes the Gem, and adds its value. There is no confirmation dialog - the
   Gems cost Prestige Coins rather than luminance, so there is nothing to
   second-guess.

   The Triune Weave adds +1 Creature, +1 Item and +1 Life augmentation per point,
   applied at bonus sites only; it never touches the raw purchased counts, so it
   cannot eat into the per-school augmentation caps. The four school counters
   accumulate toward FUTURE ability unlocks and deliberately add NOTHING to the
   capped War/Void/Melee/Missile aug stats.

   TRAPS, all of which have already cost time once:
     * Charms and Gems MUST be WeenieType 38 (Gem). Type 1 (Generic) never lands
       in the pack - the audit log claims success and the item is nowhere.
     * Every item needs PropertyBool 63 UnlimitedUse, or Gem.ActOnUse consumes the
       item itself on use. Gems are consumed by the handler instead, on success
       only. The flag is stripped from the appraisal panel in AppraiseInfo.cs, and
       Structure/MaxStructure (91/92) = 1 makes a Gem read as single-use.
     * Emotes are COPIED ONTO AN OBJECT WHEN IT IS CREATED. Any Gem minted before
       an emote change keeps the old message and silently does nothing. After
       importing: /clearcache weenie, trash old copies, spawn fresh.
     * class_Name still says "mote" (triune_weave_mote_1 and friends). Dev-facing
       only, deliberately not renamed through two rounds of item renaming, because
       nothing depends on it and churning it would break nothing but diffs.

   NO VENDORS. This file creates items only - see the note at the bottom. Each
   Gem's Value is still set to its magnitude (1 / 10 / 50 / 100), which is what a
   vendor would charge in Prestige Coins if one is ever set up, but nothing here
   puts them on sale. Spawn with /ci.

   RE-RUNNABLE. The deletes below cascade through weenie_properties_* (including
   emotes and emote actions), so this file can be applied any number of times and
   always lands on the same state. That matters: AutoApplyDatabaseUpdates tracks
   what it has run in a text file next to the server binary, not in the database,
   so a fresh deployment folder re-runs everything from zero.
   ============================================================================ */

/* Cleanup: an early 2026-08-11 draft briefly used 81000155-159 before the ids
   were moved into the owner's own range. Harmless if absent. */
DELETE FROM `weenie_properties_create_list` WHERE `object_Id` IN (19860001, 19860002, 19860003, 19853035) AND `weenie_Class_Id` BETWEEN 81000155 AND 81000159;
DELETE FROM `weenie` WHERE `class_Id` BETWEEN 81000155 AND 81000159;

/* This file stocks no vendors, but it still clears any stock rows for its own
   wcids: earlier drafts put these items on four vendors that were never tested,
   and leaving that wiring half-present on some servers and not others is worse
   than not having it. Deleting rows this file never creates is deliberate - it
   owns these wcids everywhere, not just where it inserts. */
DELETE FROM `weenie_properties_create_list` WHERE `object_Id` IN (19860001, 19860002, 19860003, 19853035) AND `weenie_Class_Id` BETWEEN 777700030 AND 777700034;
DELETE FROM `weenie_properties_create_list` WHERE `object_Id` IN (19860001, 19860002, 19860003, 19853035) AND `weenie_Class_Id` BETWEEN 777700051 AND 777700070;
DELETE FROM `weenie` WHERE `class_Id` BETWEEN 777700030 AND 777700034;
DELETE FROM `weenie` WHERE `class_Id` BETWEEN 777700051 AND 777700070;

/* ============================================================================
   WEENIES
   ============================================================================ */
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`) VALUES
(777700030, 'triune_weave_charm',         38),
(777700031, 'triune_weave_mote_1',        38),
(777700032, 'triune_weave_mote_10',       38),
(777700033, 'triune_weave_mote_50',       38),
(777700034, 'triune_weave_mote_100',      38),
(777700051, 'battlemages_wrath_charm',    38),
(777700052, 'battlemages_wrath_mote_1',   38),
(777700053, 'battlemages_wrath_mote_10',  38),
(777700054, 'battlemages_wrath_mote_50',  38),
(777700055, 'battlemages_wrath_mote_100', 38),
(777700056, 'nether_veil_charm',          38),
(777700057, 'nether_veil_mote_1',         38),
(777700058, 'nether_veil_mote_10',        38),
(777700059, 'nether_veil_mote_50',        38),
(777700060, 'nether_veil_mote_100',       38),
(777700061, 'crashing_steel_charm',       38),
(777700062, 'crashing_steel_mote_1',      38),
(777700063, 'crashing_steel_mote_10',     38),
(777700064, 'crashing_steel_mote_50',     38),
(777700065, 'crashing_steel_mote_100',    38),
(777700066, 'true_shot_charm',            38),
(777700067, 'true_shot_mote_1',           38),
(777700068, 'true_shot_mote_10',          38),
(777700069, 'true_shot_mote_50',          38),
(777700070, 'true_shot_mote_100',         38);

/* ============================================================================
   INTS
     1 ItemType 2048 Gem · 5 EncumbranceVal · 11 MaxStackSize · 12 StackSize
     16 ItemUseable 8 Contained · 19 Value (= price in Prestige Coins)
     33 Bonded 1 · 91/92 Structure/MaxStructure · 93 PhysicsState · 94 TargetType
     114 Attuned 1
   Charms and Gems are both Bonded AND Attuned: they cannot be traded, sold or
   given, and they do not drop on death.
   ============================================================================ */
INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(777700030, 1, 2048), (777700030, 5, 10), (777700030, 16, 8), (777700030, 19,   1), (777700030, 33, 1), (777700030, 93, 1044), (777700030, 114, 1),
(777700051, 1, 2048), (777700051, 5, 10), (777700051, 16, 8), (777700051, 19,   1), (777700051, 33, 1), (777700051, 93, 1044), (777700051, 114, 1),
(777700056, 1, 2048), (777700056, 5, 10), (777700056, 16, 8), (777700056, 19,   1), (777700056, 33, 1), (777700056, 93, 1044), (777700056, 114, 1),
(777700061, 1, 2048), (777700061, 5, 10), (777700061, 16, 8), (777700061, 19,   1), (777700061, 33, 1), (777700061, 93, 1044), (777700061, 114, 1),
(777700066, 1, 2048), (777700066, 5, 10), (777700066, 16, 8), (777700066, 19,   1), (777700066, 33, 1), (777700066, 93, 1044), (777700066, 114, 1);

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`) VALUES
(777700031, 1, 2048), (777700031, 5, 5), (777700031, 11, 1), (777700031, 12, 1), (777700031, 16, 8), (777700031, 19,   1), (777700031, 33, 1), (777700031, 91, 1), (777700031, 92, 1), (777700031, 93, 1044), (777700031, 94, 16), (777700031, 114, 1),
(777700032, 1, 2048), (777700032, 5, 5), (777700032, 11, 1), (777700032, 12, 1), (777700032, 16, 8), (777700032, 19,  10), (777700032, 33, 1), (777700032, 91, 1), (777700032, 92, 1), (777700032, 93, 1044), (777700032, 94, 16), (777700032, 114, 1),
(777700033, 1, 2048), (777700033, 5, 5), (777700033, 11, 1), (777700033, 12, 1), (777700033, 16, 8), (777700033, 19,  50), (777700033, 33, 1), (777700033, 91, 1), (777700033, 92, 1), (777700033, 93, 1044), (777700033, 94, 16), (777700033, 114, 1),
(777700034, 1, 2048), (777700034, 5, 5), (777700034, 11, 1), (777700034, 12, 1), (777700034, 16, 8), (777700034, 19, 100), (777700034, 33, 1), (777700034, 91, 1), (777700034, 92, 1), (777700034, 93, 1044), (777700034, 94, 16), (777700034, 114, 1),
(777700052, 1, 2048), (777700052, 5, 5), (777700052, 11, 1), (777700052, 12, 1), (777700052, 16, 8), (777700052, 19,   1), (777700052, 33, 1), (777700052, 91, 1), (777700052, 92, 1), (777700052, 93, 1044), (777700052, 94, 16), (777700052, 114, 1),
(777700053, 1, 2048), (777700053, 5, 5), (777700053, 11, 1), (777700053, 12, 1), (777700053, 16, 8), (777700053, 19,  10), (777700053, 33, 1), (777700053, 91, 1), (777700053, 92, 1), (777700053, 93, 1044), (777700053, 94, 16), (777700053, 114, 1),
(777700054, 1, 2048), (777700054, 5, 5), (777700054, 11, 1), (777700054, 12, 1), (777700054, 16, 8), (777700054, 19,  50), (777700054, 33, 1), (777700054, 91, 1), (777700054, 92, 1), (777700054, 93, 1044), (777700054, 94, 16), (777700054, 114, 1),
(777700055, 1, 2048), (777700055, 5, 5), (777700055, 11, 1), (777700055, 12, 1), (777700055, 16, 8), (777700055, 19, 100), (777700055, 33, 1), (777700055, 91, 1), (777700055, 92, 1), (777700055, 93, 1044), (777700055, 94, 16), (777700055, 114, 1),
(777700057, 1, 2048), (777700057, 5, 5), (777700057, 11, 1), (777700057, 12, 1), (777700057, 16, 8), (777700057, 19,   1), (777700057, 33, 1), (777700057, 91, 1), (777700057, 92, 1), (777700057, 93, 1044), (777700057, 94, 16), (777700057, 114, 1),
(777700058, 1, 2048), (777700058, 5, 5), (777700058, 11, 1), (777700058, 12, 1), (777700058, 16, 8), (777700058, 19,  10), (777700058, 33, 1), (777700058, 91, 1), (777700058, 92, 1), (777700058, 93, 1044), (777700058, 94, 16), (777700058, 114, 1),
(777700059, 1, 2048), (777700059, 5, 5), (777700059, 11, 1), (777700059, 12, 1), (777700059, 16, 8), (777700059, 19,  50), (777700059, 33, 1), (777700059, 91, 1), (777700059, 92, 1), (777700059, 93, 1044), (777700059, 94, 16), (777700059, 114, 1),
(777700060, 1, 2048), (777700060, 5, 5), (777700060, 11, 1), (777700060, 12, 1), (777700060, 16, 8), (777700060, 19, 100), (777700060, 33, 1), (777700060, 91, 1), (777700060, 92, 1), (777700060, 93, 1044), (777700060, 94, 16), (777700060, 114, 1),
(777700062, 1, 2048), (777700062, 5, 5), (777700062, 11, 1), (777700062, 12, 1), (777700062, 16, 8), (777700062, 19,   1), (777700062, 33, 1), (777700062, 91, 1), (777700062, 92, 1), (777700062, 93, 1044), (777700062, 94, 16), (777700062, 114, 1),
(777700063, 1, 2048), (777700063, 5, 5), (777700063, 11, 1), (777700063, 12, 1), (777700063, 16, 8), (777700063, 19,  10), (777700063, 33, 1), (777700063, 91, 1), (777700063, 92, 1), (777700063, 93, 1044), (777700063, 94, 16), (777700063, 114, 1),
(777700064, 1, 2048), (777700064, 5, 5), (777700064, 11, 1), (777700064, 12, 1), (777700064, 16, 8), (777700064, 19,  50), (777700064, 33, 1), (777700064, 91, 1), (777700064, 92, 1), (777700064, 93, 1044), (777700064, 94, 16), (777700064, 114, 1),
(777700065, 1, 2048), (777700065, 5, 5), (777700065, 11, 1), (777700065, 12, 1), (777700065, 16, 8), (777700065, 19, 100), (777700065, 33, 1), (777700065, 91, 1), (777700065, 92, 1), (777700065, 93, 1044), (777700065, 94, 16), (777700065, 114, 1),
(777700067, 1, 2048), (777700067, 5, 5), (777700067, 11, 1), (777700067, 12, 1), (777700067, 16, 8), (777700067, 19,   1), (777700067, 33, 1), (777700067, 91, 1), (777700067, 92, 1), (777700067, 93, 1044), (777700067, 94, 16), (777700067, 114, 1),
(777700068, 1, 2048), (777700068, 5, 5), (777700068, 11, 1), (777700068, 12, 1), (777700068, 16, 8), (777700068, 19,  10), (777700068, 33, 1), (777700068, 91, 1), (777700068, 92, 1), (777700068, 93, 1044), (777700068, 94, 16), (777700068, 114, 1),
(777700069, 1, 2048), (777700069, 5, 5), (777700069, 11, 1), (777700069, 12, 1), (777700069, 16, 8), (777700069, 19,  50), (777700069, 33, 1), (777700069, 91, 1), (777700069, 92, 1), (777700069, 93, 1044), (777700069, 94, 16), (777700069, 114, 1),
(777700070, 1, 2048), (777700070, 5, 5), (777700070, 11, 1), (777700070, 12, 1), (777700070, 16, 8), (777700070, 19, 100), (777700070, 33, 1), (777700070, 91, 1), (777700070, 92, 1), (777700070, 93, 1044), (777700070, 94, 16), (777700070, 114, 1);

/* ============================================================================
   BOOLS — 63 UnlimitedUse. See the trap note in the header: without this,
   Gem.ActOnUse consumes the item on every use, including a Charm.
   ============================================================================ */
INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`) VALUES
(777700030, 63, 1), (777700031, 63, 1), (777700032, 63, 1), (777700033, 63, 1), (777700034, 63, 1),
(777700051, 63, 1), (777700052, 63, 1), (777700053, 63, 1), (777700054, 63, 1), (777700055, 63, 1),
(777700056, 63, 1), (777700057, 63, 1), (777700058, 63, 1), (777700059, 63, 1), (777700060, 63, 1),
(777700061, 63, 1), (777700062, 63, 1), (777700063, 63, 1), (777700064, 63, 1), (777700065, 63, 1),
(777700066, 63, 1), (777700067, 63, 1), (777700068, 63, 1), (777700069, 63, 1), (777700070, 63, 1);

/* ============================================================================
   DIDs — 1 Setup · 3 SoundTable · 6 PaletteBase · 8 Icon · 22 PhysicsEffectTable
          50 IconOverlay

   Icons must be 32x32. 64x64 renders nothing and sub-32 is not icon art.

   Each Charm carries the same overlay, 100675462: the green gem stamp retail uses
   to mark the ten "An Explorer ..." weapons as one set. An overlay composites ON
   TOP of the base icon, so it shows regardless of the base art.

   Do NOT reach for IconUnderlay (52) here. An underlay draws BEHIND the icon, so
   it is only visible through transparent pixels, and several of these icons are
   fully opaque - a red underlay on the War charm looked exactly like nothing had
   happened. Measured see-through: 100673010 (True Shot) and 100677484 (Melee) are
   generous, 100672793 (Triune) is 34 pct, 100671667 (Void) 21 pct.
   ============================================================================ */
INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(777700030, 1, 33556769), (777700030, 3, 536870932), (777700030, 6, 67111919), (777700030,  8, 100672793), (777700030, 22, 872415275), (777700030, 50, 100675462),
(777700051, 1, 33556769), (777700051, 3, 536870932), (777700051, 6, 67111919), (777700051,  8, 100667388), (777700051, 22, 872415275), (777700051, 50, 100675462),
(777700056, 1, 33556769), (777700056, 3, 536870932), (777700056, 6, 67111919), (777700056,  8, 100671667), (777700056, 22, 872415275), (777700056, 50, 100675462),
(777700061, 1, 33556769), (777700061, 3, 536870932), (777700061, 6, 67111919), (777700061,  8, 100677484), (777700061, 22, 872415275), (777700061, 50, 100675462),
(777700066, 1, 33556769), (777700066, 3, 536870932), (777700066, 6, 67111919), (777700066,  8, 100673010), (777700066, 22, 872415275), (777700066, 50, 100675462);

/* Gems: one icon per school, shared by all four tiers. The tier is carried by the
   name (Chipped / Flawed / Flawless / Perfect), not by the art. */
INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`) VALUES
(777700031, 1, 33556769), (777700031, 3, 536870932), (777700031, 6, 67111919), (777700031, 8, 100668361), (777700031, 22, 872415275),
(777700032, 1, 33556769), (777700032, 3, 536870932), (777700032, 6, 67111919), (777700032, 8, 100668361), (777700032, 22, 872415275),
(777700033, 1, 33556769), (777700033, 3, 536870932), (777700033, 6, 67111919), (777700033, 8, 100668361), (777700033, 22, 872415275),
(777700034, 1, 33556769), (777700034, 3, 536870932), (777700034, 6, 67111919), (777700034, 8, 100668361), (777700034, 22, 872415275),
(777700052, 1, 33556769), (777700052, 3, 536870932), (777700052, 6, 67111919), (777700052, 8, 100667482), (777700052, 22, 872415275),
(777700053, 1, 33556769), (777700053, 3, 536870932), (777700053, 6, 67111919), (777700053, 8, 100667482), (777700053, 22, 872415275),
(777700054, 1, 33556769), (777700054, 3, 536870932), (777700054, 6, 67111919), (777700054, 8, 100667482), (777700054, 22, 872415275),
(777700055, 1, 33556769), (777700055, 3, 536870932), (777700055, 6, 67111919), (777700055, 8, 100667482), (777700055, 22, 872415275),
(777700057, 1, 33556769), (777700057, 3, 536870932), (777700057, 6, 67111919), (777700057, 8, 100668359), (777700057, 22, 872415275),
(777700058, 1, 33556769), (777700058, 3, 536870932), (777700058, 6, 67111919), (777700058, 8, 100668359), (777700058, 22, 872415275),
(777700059, 1, 33556769), (777700059, 3, 536870932), (777700059, 6, 67111919), (777700059, 8, 100668359), (777700059, 22, 872415275),
(777700060, 1, 33556769), (777700060, 3, 536870932), (777700060, 6, 67111919), (777700060, 8, 100668359), (777700060, 22, 872415275),
(777700062, 1, 33556769), (777700062, 3, 536870932), (777700062, 6, 67111919), (777700062, 8, 100668360), (777700062, 22, 872415275),
(777700063, 1, 33556769), (777700063, 3, 536870932), (777700063, 6, 67111919), (777700063, 8, 100668360), (777700063, 22, 872415275),
(777700064, 1, 33556769), (777700064, 3, 536870932), (777700064, 6, 67111919), (777700064, 8, 100668360), (777700064, 22, 872415275),
(777700065, 1, 33556769), (777700065, 3, 536870932), (777700065, 6, 67111919), (777700065, 8, 100668360), (777700065, 22, 872415275),
(777700067, 1, 33556769), (777700067, 3, 536870932), (777700067, 6, 67111919), (777700067, 8, 100668362), (777700067, 22, 872415275),
(777700068, 1, 33556769), (777700068, 3, 536870932), (777700068, 6, 67111919), (777700068, 8, 100668362), (777700068, 22, 872415275),
(777700069, 1, 33556769), (777700069, 3, 536870932), (777700069, 6, 67111919), (777700069, 8, 100668362), (777700069, 22, 872415275),
(777700070, 1, 33556769), (777700070, 3, 536870932), (777700070, 6, 67111919), (777700070, 8, 100668362), (777700070, 22, 872415275);

/* ============================================================================
   STRINGS — 1 Name · 14 Use · 15 ShortDesc · 16 LongDesc
   The Charms' LongDesc is appended to at runtime by AppraiseInfo.cs with the
   examiner's own count, which is why it reads as an opening line rather than a
   complete description.
   ============================================================================ */
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700030,  1, 'Charm of the Triune Weave'),
(777700030, 15, 'A charm woven from three strands of enchantment magic.'),
(777700030, 16, 'A charm woven from three strands of enchantment magic. Its power is bound to its keeper, not the trinket itself.'),
(777700051,  1, 'Charm of the Battlemage''s Wrath'),
(777700051, 15, 'A charm crackling with the fury of war magic.'),
(777700051, 16, 'A charm crackling with the fury of war magic. Its power is bound to its keeper, and its deeper abilities remain sealed.'),
(777700056,  1, 'Charm of the Nether Veil'),
(777700056, 15, 'A charm shrouded in whispers from the nether.'),
(777700056, 16, 'A charm shrouded in whispers from the nether. Its power is bound to its keeper, and its deeper abilities remain sealed.'),
(777700061,  1, 'Charm of Crashing Steel'),
(777700061, 15, 'A charm that rings with the clash of battle.'),
(777700061, 16, 'A charm that rings with the clash of battle. Its power is bound to its keeper, and its deeper abilities remain sealed.'),
(777700066,  1, 'Charm of the True Shot'),
(777700066, 15, 'A charm carved with the patience of the hunt.'),
(777700066, 16, 'A charm carved with the patience of the hunt. Its power is bound to its keeper, and its deeper abilities remain sealed.');

/* Triune Weave gems */
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700031,  1, 'Chipped Gem of the Triune Weave (+1)'),
(777700031, 14, 'Double click to empower your Charm of the Triune Weave by 1, granting +1 Creature, Item, and Life Augmentation. The Charm must be in your pack.'),
(777700031, 15, 'A gem of threefold enchantment, eager to join a Triune Weave.'),
(777700032,  1, 'Flawed Gem of the Triune Weave (+10)'),
(777700032, 14, 'Double click to empower your Charm of the Triune Weave by 10, granting +10 Creature, Item, and Life Augmentations. The Charm must be in your pack.'),
(777700032, 15, 'A gem of threefold enchantment, eager to join a Triune Weave.'),
(777700033,  1, 'Flawless Gem of the Triune Weave (+50)'),
(777700033, 14, 'Double click to empower your Charm of the Triune Weave by 50, granting +50 Creature, Item, and Life Augmentations. The Charm must be in your pack.'),
(777700033, 15, 'A gem of threefold enchantment, eager to join a Triune Weave.'),
(777700034,  1, 'Perfect Gem of the Triune Weave (+100)'),
(777700034, 14, 'Double click to empower your Charm of the Triune Weave by 100, granting +100 Creature, Item, and Life Augmentations. The Charm must be in your pack.'),
(777700034, 15, 'A gem of threefold enchantment, eager to join a Triune Weave.');

/* Battlemage's Wrath gems (War) */
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700052,  1, 'Chipped Gem of the Battlemage''s Wrath (+1)'),
(777700052, 14, 'Double click to empower your Charm of the Battlemage''s Wrath by 1. The Charm must be in your pack.'),
(777700052, 15, 'A war-charged gem, drawn to a Battlemage''s Wrath.'),
(777700053,  1, 'Flawed Gem of the Battlemage''s Wrath (+10)'),
(777700053, 14, 'Double click to empower your Charm of the Battlemage''s Wrath by 10. The Charm must be in your pack.'),
(777700053, 15, 'A war-charged gem, drawn to a Battlemage''s Wrath.'),
(777700054,  1, 'Flawless Gem of the Battlemage''s Wrath (+50)'),
(777700054, 14, 'Double click to empower your Charm of the Battlemage''s Wrath by 50. The Charm must be in your pack.'),
(777700054, 15, 'A war-charged gem, drawn to a Battlemage''s Wrath.'),
(777700055,  1, 'Perfect Gem of the Battlemage''s Wrath (+100)'),
(777700055, 14, 'Double click to empower your Charm of the Battlemage''s Wrath by 100. The Charm must be in your pack.'),
(777700055, 15, 'A war-charged gem, drawn to a Battlemage''s Wrath.');

/* Nether Veil gems (Void) */
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700057,  1, 'Chipped Gem of the Nether Veil (+1)'),
(777700057, 14, 'Double click to empower your Charm of the Nether Veil by 1. The Charm must be in your pack.'),
(777700057, 15, 'A shadowed gem, seeping toward a Nether Veil.'),
(777700058,  1, 'Flawed Gem of the Nether Veil (+10)'),
(777700058, 14, 'Double click to empower your Charm of the Nether Veil by 10. The Charm must be in your pack.'),
(777700058, 15, 'A shadowed gem, seeping toward a Nether Veil.'),
(777700059,  1, 'Flawless Gem of the Nether Veil (+50)'),
(777700059, 14, 'Double click to empower your Charm of the Nether Veil by 50. The Charm must be in your pack.'),
(777700059, 15, 'A shadowed gem, seeping toward a Nether Veil.'),
(777700060,  1, 'Perfect Gem of the Nether Veil (+100)'),
(777700060, 14, 'Double click to empower your Charm of the Nether Veil by 100. The Charm must be in your pack.'),
(777700060, 15, 'A shadowed gem, seeping toward a Nether Veil.');

/* Crashing Steel gems (Melee) */
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700062,  1, 'Chipped Gem of Crashing Steel (+1)'),
(777700062, 14, 'Double click to empower your Charm of Crashing Steel by 1. The Charm must be in your pack.'),
(777700062, 15, 'A hardened gem, ringing for Crashing Steel.'),
(777700063,  1, 'Flawed Gem of Crashing Steel (+10)'),
(777700063, 14, 'Double click to empower your Charm of Crashing Steel by 10. The Charm must be in your pack.'),
(777700063, 15, 'A hardened gem, ringing for Crashing Steel.'),
(777700064,  1, 'Flawless Gem of Crashing Steel (+50)'),
(777700064, 14, 'Double click to empower your Charm of Crashing Steel by 50. The Charm must be in your pack.'),
(777700064, 15, 'A hardened gem, ringing for Crashing Steel.'),
(777700065,  1, 'Perfect Gem of Crashing Steel (+100)'),
(777700065, 14, 'Double click to empower your Charm of Crashing Steel by 100. The Charm must be in your pack.'),
(777700065, 15, 'A hardened gem, ringing for Crashing Steel.');

/* True Shot gems (Missile) */
INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES
(777700067,  1, 'Chipped Gem of the True Shot (+1)'),
(777700067, 14, 'Double click to empower your Charm of the True Shot by 1. The Charm must be in your pack.'),
(777700067, 15, 'A keen gem, flying true toward its Charm.'),
(777700068,  1, 'Flawed Gem of the True Shot (+10)'),
(777700068, 14, 'Double click to empower your Charm of the True Shot by 10. The Charm must be in your pack.'),
(777700068, 15, 'A keen gem, flying true toward its Charm.'),
(777700069,  1, 'Flawless Gem of the True Shot (+50)'),
(777700069, 14, 'Double click to empower your Charm of the True Shot by 50. The Charm must be in your pack.'),
(777700069, 15, 'A keen gem, flying true toward its Charm.'),
(777700070,  1, 'Perfect Gem of the True Shot (+100)'),
(777700070, 14, 'Double click to empower your Charm of the True Shot by 100. The Charm must be in your pack.'),
(777700070, 15, 'A keen gem, flying true toward its Charm.');

/* ============================================================================
   USE EMOTES — category 7 (Use), action type 123 (PromptAddAugment).
   The message is the key EmoteManager dispatches on. Changing one of these
   strings breaks every Gem already sitting in a player's pack, because the emote
   is snapshot onto the item at creation. They have deliberately survived two
   rounds of item renaming untouched.
   ============================================================================ */
INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700031, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Triune', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700032, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Triune10', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700033, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Triune50', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700034, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Triune100', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700052, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Wrath', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700053, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Wrath10', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700054, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Wrath50', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700055, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Wrath100', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700057, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Nether', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700058, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Nether10', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700059, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Nether50', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700060, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Nether100', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700062, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Steel', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700063, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Steel10', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700064, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Steel50', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700065, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'Steel100', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700067, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'TrueShot', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700068, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'TrueShot10', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700069, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'TrueShot50', 0);

INSERT INTO `weenie_properties_emote` (`object_Id`, `category`, `probability`) VALUES (777700070, 7, 1);
SET @e := LAST_INSERT_ID();
INSERT INTO `weenie_properties_emote_action` (`emote_Id`, `order`, `type`, `delay`, `extent`, `message`, `amount`) VALUES (@e, 0, 123, 0, 1, 'TrueShot100', 0);

/* ============================================================================
   NO VENDOR STOCK — deliberate.

   The four vendors this system was drafted against (Enephior 19860002 and
   19853035, Khervef 19860001, Quillan Stormleaf 19860003) were never created or
   tested as part of this work, so nothing here stocks them. This file owns the 25
   items and nothing else; spawn them with /ci.

   If vendor sales are wanted later they are their own change, and their own test:
   each vendor needs AlternateCurrency 19860000 (Prestige Coin) and SellPrice 1.0
   for a Gem's Value to read as its price in coins, plus create_list rows. Note
   that weenie_properties_create_list.object_Id is a FOREIGN KEY to
   weenie.class_Id, so stocking a vendor that does not exist on the target server
   throws - and AutoApplyDatabaseUpdates marks a file as applied EVEN IF IT THREW,
   which would abort the rest of the file and never retry. Guard any such inserts
   with WHERE EXISTS, and put them last.
   ============================================================================ */
