using System;

using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity
{
    public class BaseDamageMod
    {
        public BaseDamage BaseDamage;

        public float DamageBonus   = 0.0f;    // blood drinker
        public float DamageMod     = 1.0f;    // for missile launchers (+113% yumis = 2.13)
        public float VarianceMod   = 1.0f;

        public int ElementalBonus = 0;

        public float MaxDamage
        {
            get
            {
                var maxDamage = (BaseDamage.MaxDamage + DamageBonus + ElementalBonus) * DamageMod;

                if (BaseDamage.MaxDamage >= 0)
                    maxDamage = Math.Max(0, maxDamage);
                else
                    maxDamage = Math.Min(0, maxDamage);

                return maxDamage;   
            }
        }

        public float MinDamage => MaxDamage * (1.0f - BaseDamage.Variance * VarianceMod);

        public Range Range => new(MinDamage, MaxDamage);

        public BaseDamageMod(BaseDamage baseDamage)
        {
            BaseDamage = baseDamage;
        }

        public BaseDamageMod(BaseDamage baseDamage, Creature wielder, WorldObject weapon)
        {
            BaseDamage = baseDamage;

            if (weapon == null)
                return;

            DamageBonus += weapon.EnchantmentManager.GetDamageBonus();
            VarianceMod *= weapon.EnchantmentManager.GetVarianceMod();

            // Weapon aug-scaling: a stamped T11+ launcher's quality roll GRADES the damage
            // modifier (replace semantics — launchers scale through the mod, never a flat term);
            // authored DamageMod is the fallback whenever the system is off or the launcher is
            // unstamped legacy.
            var baseDamageMod = Managers.WeaponScaling.WeaponScalingCombat.TryGetLauncherDamageMod(weapon, out var gradedMod)
                ? gradedMod
                : (float)(weapon.GetProperty(PropertyFloat.DamageMod) ?? 1.0f);

            DamageMod = baseDamageMod + weapon.EnchantmentManager.GetDamageMod();

            if (weapon.IsEnchantable)
            {
                // factor in wielder auras for enchantable weapons
                DamageBonus += wielder.EnchantmentManager.GetDamageBonus();
                VarianceMod *= wielder.EnchantmentManager.GetVarianceMod();

                DamageMod += wielder.EnchantmentManager.GetDamageMod();
            }

            // Missile tier cap (owner 2026-08-06): a stamped launcher gives back the Blood Drinker
            // damage earned ABOVE its tier's aug cap, so bows gain the upgrade economy melee already
            // has. Runs LAST, after both DamageBonus contributions are in, so the clamp inside sees
            // the wielder's full aura. Melee is untouched — it keeps uncapped BD by design.
            DamageBonus -= Managers.WeaponScaling.WeaponScalingCombat.GetLauncherAugExcess(weapon, wielder);
        }
    }
}
