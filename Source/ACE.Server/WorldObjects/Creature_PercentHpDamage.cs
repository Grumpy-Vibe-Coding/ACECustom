using System;

using ACE.Common;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        /// <summary>
        /// v11+ Percent-HP floor damage system.
        /// Returns the minimum damage (a fraction of the defender's max health) that a high-variation
        /// monster should deal to a player, bypassing the life-aug protection reduction that otherwise
        /// pushes normal damage toward zero. Callers apply this as a floor: finalDamage = Max(normal, floor).
        /// The floor itself is reduced by a separate, gentler life-aug curve (capped well below 100%).
        /// Returns 0 when the system is disabled, the attacker is below the variation threshold, or inputs are invalid.
        /// See T10/V11_PercentHP_Damage_Plan.md.
        /// </summary>
        public static float GetPercentHpFloorDamage(Creature attacker, Player defender, bool isCrit = false)
        {
            if (attacker == null || defender == null)
                return 0f;

            if (!ServerConfig.v11_pcthp_enabled.Value)
                return 0f;

            var variation = PrestigeManager.GetEffectiveVariation(attacker);
            var minVariation = ServerConfig.v11_pcthp_min_variation.Value;
            if (variation < minVariation)
                return 0f;

            // floor fraction P: per-weenie override wins; otherwise tier-scaled (+ boss multiplier)
            double p;
            var pOverride = attacker.GetProperty(PropertyFloat.PercentHpDamageOverride);
            if (pOverride.HasValue)
            {
                p = pOverride.Value;
            }
            else
            {
                // geometric growth per tier so the base outpaces the (accelerating) life-aug reduction -> rising endgame
                p = ServerConfig.v11_pcthp_base.Value
                    * Math.Pow(ServerConfig.v11_pcthp_tier_growth.Value, variation - minVariation);

                if (attacker.GetProperty(PropertyBool.IsEmpowerSource) == true)
                    p *= ServerConfig.v11_pcthp_boss_mult.Value;
            }

            if (p <= 0.0)
                return 0f;

            // floor's own life-aug reduction curve: cap * (1 - (1-r)^augs), capped below 100%
            var cap = Math.Clamp(
                attacker.GetProperty(PropertyFloat.PercentHpReductionCapOverride) ?? ServerConfig.v11_pcthp_reduction_cap.Value,
                0.0, 1.0);
            var r = Math.Clamp(
                attacker.GetProperty(PropertyFloat.PercentHpReductionROverride) ?? ServerConfig.v11_pcthp_reduction_r.Value,
                0.0, 1.0);

            // only life augs above the threshold reduce the floor: v11+ is a fresh defensive climb
            var threshold = ServerConfig.v11_pcthp_aug_threshold.Value;
            var effectiveAugs = Math.Max(0, (defender.LuminanceAugmentLifeCount ?? 0) - threshold);
            var reduction = Math.Min(cap, cap * (1.0 - Math.Pow(1.0 - r, effectiveAugs)));

            var maxHealth = defender.Health?.MaxValue ?? 0;
            if (maxHealth == 0)
                return 0f;

            var floor = p * (1.0 - reduction) * maxHealth;

            // Empower and crit multiply the floor too — otherwise they vanish once the floor
            // dominates the (heavily mitigated) normal damage component.
            if (attacker.GetProperty(PropertyBool.IsEmpowered) == true)
                floor *= ServerConfig.v11_pcthp_empower_mult.Value;

            if (isCrit)
                floor *= ServerConfig.v11_pcthp_crit_mult.Value;

            // per-hit random spread so damage isn't identical every swing
            var variance = ServerConfig.v11_pcthp_variance.Value;
            if (variance > 0.0)
                floor *= 1.0 + ThreadSafeRandom.Next((float)-variance, (float)variance);
            if (floor <= 0.0 || double.IsNaN(floor) || double.IsInfinity(floor))
                return 0f;

            return (float)floor;
        }

        /// <summary>
        /// v11+ attack-skill floor. When a variation>=v11_pcthp_min_variation monster attacks a player,
        /// returns the configured minimum effective attack skill (v11_min_attack_skill) so endgame mobs can
        /// land hits against very high Effective Melee/Missile Defense. Returns 0 when disabled or ineligible,
        /// in which case callers keep the monster's normal attack skill.
        /// </summary>
        public static uint GetV11AttackSkillFloor(Creature attacker, Player defender)
        {
            if (attacker == null || defender == null)
                return 0;

            var floor = ServerConfig.v11_min_attack_skill.Value;
            if (floor <= 0)
                return 0;

            var variation = PrestigeManager.GetEffectiveVariation(attacker);
            if (variation < ServerConfig.v11_pcthp_min_variation.Value)
                return 0;

            return (uint)floor;
        }
    }
}
