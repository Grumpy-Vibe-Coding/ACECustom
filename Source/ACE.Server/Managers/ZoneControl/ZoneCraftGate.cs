using System.Collections.Generic;

using ACE.Database.Models.World;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Decides whether a crafting recipe may be applied to a Tier 11+ item.
    ///
    /// WHY THIS EXISTS. T11+ gear drops FINISHED - ZoneLootMutator authors its specials from a
    /// per-tier ladder. Player tinkering fights that in three different ways:
    ///
    ///  1. COMPETITION. The vanilla imbues do not stack with our cards, they compete via Math.Max
    ///     (WorldObject_Weapon.cs:362 / :440 / :470). skill.Base is clamped to 400 melee / 360
    ///     missile+magic (GetBaseSkillImbued), so at endgame Critical Strike is a FLAT 0.50 crit
    ///     chance and Crippling Blow a FLAT 6.0 crit multiplier for every capped player - constants,
    ///     not a curve. Any Biting Strike below 0.50 or Crushing Blow below 7.0x is simply invisible.
    ///  2. OVERWRITE. Several custom recipes SetValue rather than add - the crit gems pin
    ///     CriticalMultiplier to 2.25 and CriticalFrequency to 0.33, slayer stones pin
    ///     SlayerDamageBonus to 1.75, bowstring 527870117 pins IgnoreShield to 0.5. Those replace a
    ///     rolled value in BOTH directions, so a jackpot drop can be tinkered DOWN to a constant.
    ///  3. REPEATS. Some custom recipes have no target guard at all and can be applied over and over.
    ///
    /// HOW IT DECIDES. Not by recipe id - the id lists are incomplete (there are ~10 slayer stone
    /// variants alone) and any new row added to ace_world would slip past. Instead the gate asks the
    /// data what a recipe would WRITE: if the recipe modifies a property this system authors, it is
    /// blocked on T11+ gear. That covers recipes nobody has enumerated, including future ones.
    ///
    /// WHAT IS NOT BLOCKED. Plain tinkers (salvage steel/iron/granite/leather and friends) write
    /// none of these properties, so they keep working - they are salvage sinks and blocking them
    /// would cost players a system while buying the ladder nothing.
    /// </summary>
    public static class ZoneCraftGate
    {
        /// <summary>Properties the T11+ loot pipeline authors. A recipe that writes any of these is
        /// overwriting an authored roll, so it is refused on T11+ gear. Kept as raw ints because the
        /// split-arrow / override props live outside the shipped enums.</summary>
        private static readonly HashSet<int> OwnedInts = new()
        {
            (int)PropertyInt.ImbuedEffect,                  // 179 - every vanilla imbue writes this
            (int)PropertyInt.Cleaving,                      // 292 - Cleaving card
            ZoneLootMutator.SplitArrowCountIntId,           // 9031
        };

        private static readonly HashSet<int> OwnedFloats = new()
        {
            (int)PropertyFloat.CriticalFrequency,           // 147 - Biting Strike
            (int)PropertyFloat.CriticalMultiplier,          // 136 - Crushing Blow
            (int)PropertyFloat.SlayerDamageBonus,           // 138 - Slayer
            (int)PropertyFloat.IgnoreShield,                // 151 - Shield Cleaving
            ZoneLootMutator.RendingModOverridePropId,       // 9056 - Rend Power
            ZoneLootMutator.ArmorRendOverridePropId,        // 9057 - Armor Rend
            ZoneLootMutator.SplitArrowRangeFloatId,         // 9032
            ZoneLootMutator.SplitArrowDmgFloatId,           // 9033
        };

        private static readonly HashSet<int> OwnedBools = new()
        {
            ZoneLootMutator.SplitArrowsBoolId,              // 9030
        };

        /// <summary>The item's tier for gating purposes. ARMOUR and jewelry carry ZcTier, but WEAPONS
        /// DO NOT: ApplyT11GearStats returns for weapons/casters before StampIdentity ever runs
        /// (LootGenerationFactory_ZoneSet.cs:492), so they carry WeaponAugScaleTier instead. Checking
        /// only one of the two would silently gate armour and leave every weapon open.</summary>
        public static int TierOf(WorldObject wo)
        {
            if (wo == null)
                return 0;
            var zc = wo.GetProperty(PropertyInt.ZcTier) ?? 0;
            var wep = wo.GetProperty(PropertyInt.WeaponAugScaleTier) ?? 0;
            return zc > wep ? zc : wep;
        }

        /// <summary>True when this recipe must not be applied to this target.</summary>
        public static bool IsBlocked(Recipe recipe, WorldObject target, out string reason)
        {
            reason = null;
            if (recipe == null || target == null)
                return false;

            var tier = TierOf(target);
            if (tier < LootGenerationFactory.ZoneLootSetMinTier)
                return false;

            // TWO tests, because they catch different things and neither alone is enough:
            //
            //  1. IsImbuing() - salvage_Type 2. The VANILLA imbues (Critical Strike, Crippling Blow,
            //     Armor Rending, the eight elemental rends, the three defense imbues) apply their
            //     effect in CODE, not through recipe mod rows: recipe 3863 has two mod rows and NOT
            //     ONE int/float/bool stat between them. A purely data-driven check misses every one
            //     of them - i.e. exactly the Critical Strike / Crippling Blow cases this gate exists
            //     for. Verified against ace_world 2026-08-24.
            //  2. WritesOwnedProperty - the custom 527870xxx recipes DO carry stat mods, and they
            //     SetValue over our authored rolls. There are ~10 slayer stone variants alone and the
            //     id lists in circulation are incomplete, so this asks the data rather than a list,
            //     and it keeps working for recipes added to ace_world later.
            if (!recipe.IsImbuing() && !WritesOwnedProperty(recipe))
                return false;

            reason = $"This is Tier {tier} equipment - it drops with its properties already set, and "
                   + "this would overwrite them. Salvage tinkering still works.";
            return true;
        }

        /// <summary>Does this recipe write any property the T11+ pipeline authors? Reads the recipe's
        /// own mod rows, so it needs no id list and catches recipes added later.</summary>
        private static bool WritesOwnedProperty(Recipe recipe)
        {
            if (recipe.RecipeMod == null)
                return false;

            foreach (var mod in recipe.RecipeMod)
            {
                if (mod == null)
                    continue;

                if (mod.RecipeModsInt != null)
                    foreach (var m in mod.RecipeModsInt)
                        if (m != null && OwnedInts.Contains(m.Stat))
                            return true;

                if (mod.RecipeModsFloat != null)
                    foreach (var m in mod.RecipeModsFloat)
                        if (m != null && OwnedFloats.Contains(m.Stat))
                            return true;

                if (mod.RecipeModsBool != null)
                    foreach (var m in mod.RecipeModsBool)
                        if (m != null && OwnedBools.Contains(m.Stat))
                            return true;
            }

            return false;
        }
    }
}
