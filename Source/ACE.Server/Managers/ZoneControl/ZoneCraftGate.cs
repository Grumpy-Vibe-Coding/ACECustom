using System;
using System.Collections.Generic;

using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Decides whether a crafting recipe may be applied to a Tier 11+ item.
    ///
    /// THE RULE (owner 2026-08-24): *"a weaker imbue cant go on, but not imbued could"*. So this is
    /// NOT a blanket refusal. A recipe is refused only when it would REPLACE a value the item already
    /// carries with something equal or worse. An item that rolled nothing for that property can still
    /// receive the imbue - that is a real upgrade the player earned, and blocking it would be a nerf
    /// with nothing behind it.
    ///
    /// WHY IT IS NEEDED. Player crafting fights the T11+ ladder three ways:
    ///  1. COMPETITION. Vanilla imbues do not stack with our cards, they compete via Math.Max
    ///     (WorldObject_Weapon.cs:362 / :470). skill.Base is clamped to 400 melee / 360 missile+magic
    ///     (GetBaseSkillImbued), so at cap Critical Strike is a FLAT 0.50 crit chance and Crippling
    ///     Blow a FLAT 6.0 crit mod for every player - constants, not a curve.
    ///  2. OVERWRITE. Custom 527870xxx recipes SetValue rather than add, so they replace a rolled
    ///     value in BOTH directions - a jackpot drop can be tinkered DOWN to a constant.
    ///  3. REPEATS. Several custom recipes have no target guard and can be applied repeatedly.
    ///
    /// This is LAYER 2 of the design in Craft_Gate_Plan_2026-08-24.md. Layer 1 (an authorable
    /// item-type x material matrix) and its plugin surface are not built yet; until they are, every
    /// decision falls through to the downgrade rule below.
    /// </summary>
    public static class ZoneCraftGate
    {
        /// <summary>What a competing imbue is worth, and which property it fights over.
        ///
        /// CAREFUL - the two comparisons do NOT share semantics:
        ///  * CRIT CHANCE is compared raw. CriticalFrequency 0.70 vs Critical Strike 0.50.
        ///  * CRIT DAMAGE is compared as a MOD, and the engine then adds one:
        ///    CriticalDamageMod = 1.0f + GetWeaponCritDamageMod(...)  (DamageEvent.cs:418).
        ///    Our Crushing Blow card stores (N - 1) for an N-times multiplier, so an 8x card stores
        ///    7.0 and Crippling Blow's 6.0 is really 7.0x. Comparing a DISPLAYED multiplier against
        ///    the raw imbue mod would be wrong by exactly one, and would wave through a 6x card being
        ///    "upgraded" to a 7x imbue while reporting the card as stronger. Compare stored-to-stored.
        ///
        /// Pairing verified 2026-08-24 - the names are confusable and they are easy to swap:
        ///   Critical Strike (imbue)  &lt;-&gt; Biting Strike (card)   both CRIT CHANCE
        ///   Crippling Blow  (imbue)  &lt;-&gt; Crushing Blow (card)   both CRIT DAMAGE</summary>
        private readonly struct Competing
        {
            public readonly int PropertyId;
            public readonly double CappedValue;
            public readonly string Label;

            public Competing(int propertyId, double cappedValue, string label)
            {
                PropertyId = propertyId;
                CappedValue = cappedValue;
                Label = label;
            }
        }

        /// <summary>Imbue effect -&gt; what it competes with. Effects absent here fight over nothing this
        /// system authors (the three defense imbues, Spellbook), so they are always allowed.</summary>
        private static readonly Dictionary<ImbuedEffectType, Competing> ImbueCompetes = new()
        {
            [ImbuedEffectType.CriticalStrike] = new((int)PropertyFloat.CriticalFrequency, 0.50, "Critical Frequency"),
            [ImbuedEffectType.CripplingBlow] = new((int)PropertyFloat.CriticalMultiplier, 6.00, "Critical Multiplier"),
            [ImbuedEffectType.ArmorRending] = new(ZoneLootMutator.ArmorRendOverridePropId, 0.60, "Armor Rending"),
            [ImbuedEffectType.SlashRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.PierceRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.BludgeonRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.AcidRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.ColdRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.ElectricRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.FireRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
            [ImbuedEffectType.NetherRending] = new(ZoneLootMutator.RendingModOverridePropId, 2.50, "Rend Power"),
        };

        /// <summary>Which imbue a vanilla recipe applies. The recipe data does NOT say: recipe 3863
        /// has two mod rows and zero int/float/bool stat mods, because RecipeManager applies the
        /// effect in CODE from the mod's DataId (RecipeManager.cs:490-500, :576-586, :648-666).
        /// This mirrors the DataIds that switch cares about - only the ones that compete with
        /// something we author; the rest need no entry.</summary>
        private static readonly Dictionary<uint, ImbuedEffectType> DataIdToImbue = new()
        {
            [0x38000023] = ImbuedEffectType.CriticalStrike,     // Black Opal
            [0x38000024] = ImbuedEffectType.CripplingBlow,      // Fire Opal
            [0x38000025] = ImbuedEffectType.ArmorRending,       // Sunstone
            [0x3800003A] = ImbuedEffectType.AcidRending,        // Emerald
            [0x3800003B] = ImbuedEffectType.BludgeonRending,    // White Sapphire
            [0x3800003C] = ImbuedEffectType.ColdRending,        // Aquamarine
            [0x3800003D] = ImbuedEffectType.ElectricRending,    // Jet
            [0x3800003E] = ImbuedEffectType.FireRending,        // Red Garnet
            [0x3800003F] = ImbuedEffectType.PierceRending,      // Black Garnet
            [0x38000040] = ImbuedEffectType.SlashRending,       // Imperial Topaz
        };

        /// <summary>Properties the T11+ loot pipeline authors, for the custom-recipe path. A recipe
        /// writing one of these is compared value-for-value against what the item already carries.</summary>
        private static readonly HashSet<int> OwnedInts = new()
        {
            (int)PropertyInt.Cleaving,
            ZoneLootMutator.SplitArrowCountIntId,
        };

        private static readonly HashSet<int> OwnedFloats = new()
        {
            (int)PropertyFloat.CriticalFrequency,
            (int)PropertyFloat.CriticalMultiplier,
            (int)PropertyFloat.SlayerDamageBonus,
            (int)PropertyFloat.IgnoreShield,
            ZoneLootMutator.RendingModOverridePropId,
            ZoneLootMutator.ArmorRendOverridePropId,
            ZoneLootMutator.SplitArrowRangeFloatId,
            ZoneLootMutator.SplitArrowDmgFloatId,
        };

        /// <summary>The item's tier. ARMOUR and jewelry carry ZcTier, but WEAPONS DO NOT:
        /// ApplyT11GearStats returns for weapons/casters before StampIdentity ever runs
        /// (LootGenerationFactory_ZoneSet.cs:492), so they carry WeaponAugScaleTier instead. Checking
        /// only one would silently gate armour and leave every weapon open.</summary>
        public static int TierOf(WorldObject wo)
        {
            if (wo == null)
                return 0;
            var zc = wo.GetProperty(PropertyInt.ZcTier) ?? 0;
            var wep = wo.GetProperty(PropertyInt.WeaponAugScaleTier) ?? 0;
            return zc > wep ? zc : wep;
        }

        /// <summary>True when this recipe must not be applied to this target. <paramref name="reason"/>
        /// is player-facing and always states WHAT it would have overwritten - an unexplained refusal
        /// is indistinguishable from a bug.</summary>
        public static bool IsBlocked(Recipe recipe, WorldObject target, out string reason)
        {
            reason = null;
            if (recipe == null || target == null)
                return false;

            var tier = TierOf(target);
            if (tier < LootGenerationFactory.ZoneLootSetMinTier)
                return false;

            // ── the vanilla imbues, whose effect comes from the mod DataId ──
            if (recipe.IsImbuing() && TryGetImbue(recipe, out var effect)
                && ImbueCompetes.TryGetValue(effect, out var competing))
            {
                var current = target.GetProperty((PropertyFloat)competing.PropertyId);
                if (current.HasValue && current.Value >= competing.CappedValue)
                {
                    reason = $"This Tier {tier} item already has a stronger {competing.Label} "
                           + $"({Show(competing.PropertyId, current.Value)}) than this would give it "
                           + $"({Show(competing.PropertyId, competing.CappedValue)}).";
                    return true;
                }
                return false;   // nothing there, or ours is weaker - let the player have it
            }

            // ── custom recipes, which SetValue real properties ──
            if (WouldDowngrade(recipe, target, out var what, out var have, out var incoming, out var propId))
            {
                reason = $"This Tier {tier} item already has a stronger {what} "
                       + $"({Show(propId, have)}) than this would give it ({Show(propId, incoming)}).";
                return true;
            }

            return false;
        }

        /// <summary>Crit damage is stored as (multiplier - 1) because the engine computes 1 + mod, so
        /// show it the way the player reads it on the item.</summary>
        private static string Show(int propId, double stored)
        {
            if (propId == (int)PropertyFloat.CriticalMultiplier)
                return $"{stored + 1.0:0.##}x";
            if (propId == (int)PropertyFloat.CriticalFrequency)
                return $"{stored * 100.0:0.#} pct";
            return $"{stored:0.##}";
        }

        /// <summary>Which imbue this recipe applies, read from its mod DataIds.</summary>
        private static bool TryGetImbue(Recipe recipe, out ImbuedEffectType effect)
        {
            effect = ImbuedEffectType.Undef;
            if (recipe.RecipeMod == null)
                return false;

            foreach (var mod in recipe.RecipeMod)
            {
                if (mod == null || mod.DataId == 0)
                    continue;
                if (DataIdToImbue.TryGetValue((uint)mod.DataId, out effect))
                    return true;
            }
            return false;
        }

        /// <summary>Would this recipe write an owned property with a value no better than the item's?
        /// Only SetValue-style mods can downgrade; an additive mod cannot make an item worse.</summary>
        private static bool WouldDowngrade(Recipe recipe, WorldObject target,
            out string what, out double have, out double incoming, out int propId)
        {
            what = null; have = 0; incoming = 0; propId = 0;
            if (recipe.RecipeMod == null)
                return false;

            foreach (var mod in recipe.RecipeMod)
            {
                if (mod == null)
                    continue;

                if (mod.RecipeModsFloat != null)
                {
                    foreach (var m in mod.RecipeModsFloat)
                    {
                        if (m == null || !OwnedFloats.Contains(m.Stat) || !IsSetValue(m.Enum))
                            continue;
                        var cur = target.GetProperty((PropertyFloat)m.Stat);
                        if (cur.HasValue && cur.Value >= m.Value)
                        {
                            what = PropName(m.Stat); have = cur.Value; incoming = m.Value; propId = m.Stat;
                            return true;
                        }
                    }
                }

                if (mod.RecipeModsInt != null)
                {
                    foreach (var m in mod.RecipeModsInt)
                    {
                        if (m == null || !OwnedInts.Contains(m.Stat) || !IsSetValue(m.Enum))
                            continue;
                        var cur = target.GetProperty((PropertyInt)m.Stat);
                        if (cur.HasValue && cur.Value >= m.Value)
                        {
                            what = PropName(m.Stat); have = cur.Value; incoming = m.Value; propId = m.Stat;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>RecipeMod Enum 1 = SetValue - the only operation that can REPLACE, and so the only
        /// one that can downgrade. Additive operations are left alone.</summary>
        private static bool IsSetValue(int modEnum) => modEnum == 1;

        private static string PropName(int propId)
        {
            if (propId == (int)PropertyFloat.CriticalFrequency) return "Critical Frequency";
            if (propId == (int)PropertyFloat.CriticalMultiplier) return "Critical Multiplier";
            if (propId == (int)PropertyFloat.SlayerDamageBonus) return "Slayer bonus";
            if (propId == (int)PropertyFloat.IgnoreShield) return "Shield Cleaving";
            if (propId == ZoneLootMutator.RendingModOverridePropId) return "Rend Power";
            if (propId == ZoneLootMutator.ArmorRendOverridePropId) return "Armor Rending";
            if (propId == (int)PropertyInt.Cleaving) return "Cleaving";
            if (propId == ZoneLootMutator.SplitArrowCountIntId) return "Split Arrow count";
            if (propId == ZoneLootMutator.SplitArrowRangeFloatId) return "Split Arrow range";
            if (propId == ZoneLootMutator.SplitArrowDmgFloatId) return "Split Arrow damage";
            return "property " + propId;
        }
    }
}
