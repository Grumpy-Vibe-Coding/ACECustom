using System;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.WeaponScaling
{
    /// <summary>
    /// Swing-time resolution for the weapon aug-scaling system (T11 weapon relevance plan §6).
    ///
    /// A stamped weapon carries only QUALITY (0-1000) + TIER; everything else — k ranges, tier
    /// caps, kc — resolves LIVE from <see cref="WeaponScalingManager.Current"/> (lock-free
    /// snapshot read), and the wielder's aug counts are read fresh on every damage event, never
    /// baked or cached (the Blood-Drinker-bakes-at-cast trap, deliberately avoided).
    ///
    /// Everything is gated on the master Enabled flag: disabled = every method returns 0 after
    /// one volatile read + null check, and combat is byte-identical to pre-system behavior.
    /// </summary>
    public static class WeaponScalingCombat
    {
        /// <summary>The family key a weapon resolves against in the config's Scripts table —
        /// derived from weapon properties at call time (nothing stamped): weight subtypes merge
        /// by design, multi-strike splits via the MultiStrike attack flag, two-handed splits
        /// cleaver/spear via thrust flags. Null = not a weapon we scale.</summary>
        public static string GetFamilyKey(WorldObject weapon)
        {
            if (weapon == null)
                return null;

            if (weapon is Caster)
                return (weapon.ElementalDamageMod ?? 1.0) > 1.0 ? "caster_elemental" : "caster_non_elemental";

            if (weapon is MissileLauncher)
            {
                switch (weapon.AmmoType)
                {
                    case AmmoType.Arrow: return "bow";
                    case AmmoType.Bolt: return "crossbow";
                    case AmmoType.Atlatl: return "atlatl";
                }
                switch (weapon.W_WeaponType)
                {
                    case WeaponType.Bow: return "bow";
                    case WeaponType.Crossbow: return "crossbow";
                    case WeaponType.Thrown: return "atlatl";
                }
                return null;
            }

            if (!(weapon is MeleeWeapon))
                return null;

            var attackType = weapon.W_AttackType;
            var multiStrike = (attackType & AttackType.MultiStrike) != 0;

            switch (weapon.W_WeaponType)
            {
                case WeaponType.Sword: return multiStrike ? "sword_ms" : "sword";
                case WeaponType.Dagger: return multiStrike ? "dagger_ms" : "dagger";
                case WeaponType.Axe: return "axe";
                case WeaponType.Mace: return "mace";       // jitte folds in
                case WeaponType.Spear: return "spear";
                case WeaponType.Staff: return "staff";
                case WeaponType.Unarmed: return "unarmed";
                case WeaponType.TwoHanded:
                    // Both 2H families strike twice via stance; thrust-flagged = the spear line.
                    var thrust = AttackType.Thrust | AttackType.DoubleThrust | AttackType.TripleThrust;
                    return (attackType & thrust) != 0 ? "two_handed_spear" : "cleaver";
            }
            return null;
        }

        /// <summary>Resolve a stamped weapon's k coefficient + tier row under all the system gates
        /// (enabled, stamped, non-caster, known family, known tier). False = the weapon has no
        /// scaling identity and every term is 0.</summary>
        private static bool TryResolve(WorldObject weapon, out double k, out WeaponScalingTier tierRow)
        {
            k = 0;
            tierRow = null;

            if (weapon == null)
                return false;

            var cfg = WeaponScalingManager.Current;
            if (!cfg.Enabled)
                return false;

            var quality = weapon.GetProperty(PropertyInt.WeaponAugScaleQuality);
            if (quality == null)
                return false;
            var tier = weapon.GetProperty(PropertyInt.WeaponAugScaleTier);
            if (tier == null)
                return false;

            var family = GetFamilyKey(weapon);
            if (family == null || weapon is Caster)
                return false;

            if (!cfg.Scripts.TryGetValue(family, out var script))
                return false;

            foreach (var t in cfg.Tiers)
                if (t.Tier == tier.Value) { tierRow = t; break; }
            if (tierRow == null)
                return false;

            k = WeaponScalingManager.ResolveFromQuality(script.KMin, script.KMax, quality.Value);
            return true;
        }

        /// <summary>The per-strike flat damage term: k(quality) x min(wielder's item augs, tier cap).
        /// 0 when disabled, unstamped, casters (inert until the caster wire-in), LAUNCHERS (their
        /// quality grades the damage modifier instead — owner 2026-08-01), unknown family, or a
        /// non-player wielder. Added post-roll in DamageEvent alongside the melee/missile aug
        /// flat — NOT via BaseDamageMod.DamageBonus, which launcher DamageMod would multiply
        /// ~3.6-3.9x on atlatls (plan §6.1 trap).</summary>
        public static float GetFlatBonus(WorldObject weapon, Player wielder)
        {
            if (wielder == null || weapon is MissileLauncher || !TryResolve(weapon, out var k, out var tierRow))
                return 0f;

            var augs = wielder.LuminanceAugmentItemCount ?? 0;
            return (float)(k * Math.Min(augs, tierRow.Cap));
        }

        /// <summary>Launcher grading (owner 2026-08-01): bows have ALWAYS scaled through their
        /// damage modifier — the multiplier applies to (ammo + Blood Drinker + elemental), and BD
        /// is 0.5 x item augs, so the mod already couples the weapon to the wielder's augs. A flat
        /// term on top double-dips, so launchers get NO flat term; instead the quality roll
        /// RESOLVES the effective damage modifier directly: lerp(kMin, kMax, quality/1000) with
        /// the launcher family's rows REINTERPRETED as the modifier band (T11 seed 3.00-3.40 —
        /// grade F just above the legacy T10 authored 2.92, S ~+10% over it). REPLACE semantics:
        /// the authored DamageMod property is the fallback whenever this returns false (system
        /// disabled, unstamped legacy launcher, unknown family) — the kill switch restores
        /// pre-system behavior exactly.</summary>
        public static bool TryGetLauncherDamageMod(WorldObject weapon, out float damageMod)
        {
            damageMod = 0f;

            if (!(weapon is MissileLauncher) || !TryResolve(weapon, out var k, out _))
                return false;

            damageMod = (float)k;
            return true;
        }

        /// <summary>The GUARANTEED-at-equip term: k(quality) x the tier's wield floor (capped).
        /// Because the item-aug wield req means no possible wielder has fewer augs than the floor,
        /// this is an honest minimum for ANY hands — shown as the weapon's "natural" damage to
        /// every examiner, so drops read like real weapons without baking anything on the item
        /// (owner 2026-08-01: baking would freeze the config at drop time and break both the
        /// retroactive re-pricing and the kill switch's full revert).</summary>
        public static float GetFloorBonus(WorldObject weapon)
        {
            if (weapon is MissileLauncher || !TryResolve(weapon, out var k, out var tierRow))
                return 0f;

            return (float)(k * Math.Min(tierRow.MinWieldAugs, tierRow.Cap));
        }

        /// <summary>The crit-damage term: kc(quality) x melee_missile_aug_crit_modifier x
        /// min(matching combat augs, tier cap) — the aug-pegged crit floor. Composed via
        /// Math.Max against the CriticalMultiplier/Crippling Blow path, so zone crit cards
        /// stay the jackpot above it. 0 under the same gates as the flat term.</summary>
        public static float GetCritDamageBonus(WorldObject weapon, Creature wielder)
        {
            if (!(wielder is Player player) || weapon == null)
                return 0f;

            var cfg = WeaponScalingManager.Current;
            if (!cfg.Enabled)
                return 0f;

            var quality = weapon.GetProperty(PropertyInt.WeaponAugScaleQuality);
            if (quality == null)
                return 0f;
            var tier = weapon.GetProperty(PropertyInt.WeaponAugScaleTier);
            if (tier == null)
                return 0f;
            if (weapon is Caster)
                return 0f;

            WeaponScalingTier tierRow = null;
            foreach (var t in cfg.Tiers)
                if (t.Tier == tier.Value) { tierRow = t; break; }
            if (tierRow == null)
                return 0f;

            var kc = WeaponScalingManager.ResolveFromQuality(cfg.KcMin, cfg.KcMax, quality.Value);
            var count = weapon.IsMissileWeapon
                ? player.LuminanceAugmentMissileCount ?? 0
                : player.LuminanceAugmentMeleeCount ?? 0;
            // Same per-aug crit modifier the aug crit bonus itself uses, so the peg stays honest
            // if the server ever retunes it.
            var modifier = ServerConfig.melee_missile_aug_crit_modifier.Value;
            return (float)(kc * modifier * Math.Min(count, tierRow.Cap));
        }
    }
}
