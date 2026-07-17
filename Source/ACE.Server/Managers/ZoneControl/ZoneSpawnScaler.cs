using System;

using ACE.Entity.Enum.Properties;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Zone Control's spawn-time stat application — standalone (consults only <see cref="ZoneControlManager"/>).
    /// Called wherever a creature enters the world, right after any other spawn scaling, so an authored zone
    /// stat is authoritative: attributes and Stamina/Mana/Health maxes are set ABSOLUTELY on the live spawn
    /// (per-instance only — weenies are never touched; despawn/respawn naturally reverts), then the profile's
    /// generic prop overrides are stamped. A creature with no governing zone is untouched.
    /// </summary>
    public static class ZoneSpawnScaler
    {
        /// <summary>
        /// Applies the governing zone's spawn snapshot to a freshly-spawned creature: attributes first
        /// (Endurance/Self feed the vital formulas), then Stamina/Mana, then Health — each preserving the
        /// current % across the max change (no free heal/refill). No-op when no enabled zone governs the
        /// creature at its effective variation.
        /// </summary>
        public static void ApplyToSpawn(Creature creature)
        {
            if (creature == null)
                return;

            var profile = ZoneControlManager.ResolveForCreature(creature);
            if (profile == null)
                return;

            ApplyAttribute(creature, profile, ZoneStat.Strength, PropertyAttribute.Strength);
            ApplyAttribute(creature, profile, ZoneStat.Endurance, PropertyAttribute.Endurance);
            ApplyAttribute(creature, profile, ZoneStat.Coordination, PropertyAttribute.Coordination);
            ApplyAttribute(creature, profile, ZoneStat.Quickness, PropertyAttribute.Quickness);
            ApplyAttribute(creature, profile, ZoneStat.Focus, PropertyAttribute.Focus);
            ApplyAttribute(creature, profile, ZoneStat.Self, PropertyAttribute.Self);

            ApplyVital(creature, profile, ZoneStat.MaxStamina, creature.Stamina);
            ApplyVital(creature, profile, ZoneStat.MaxMana, creature.Mana);

            if (profile.Has(ZoneStat.MaxHealth))
            {
                var maxBefore = creature.Health.MaxValue;
                var healthPct = maxBefore > 0 ? (float)creature.Health.Current / maxBefore : 1f;
                var target = (long)Math.Round(profile.Get(ZoneStat.MaxHealth));
                // subtract the non-starting contributions (endurance formula, ranks) so MaxValue lands on target
                var nonStarting = (long)maxBefore - creature.Health.StartingValue;
                creature.Health.StartingValue = (uint)Math.Clamp(target - nonStarting, 1L, uint.MaxValue);
                var maxAfter = creature.Health.MaxValue;
                creature.Health.Current = (uint)Math.Clamp((uint)Math.Round(healthPct * maxAfter), 0u, maxAfter);
            }

            // Generic prop overrides applied to this instance at (re)spawn. Int/Float/Bool/Int64 biota
            // collections are per-instance clones (WeenieConverter), so this never touches the shared
            // weenie; despawn/respawn naturally reverts. Guarded by ZonePropGuard.
            ApplyProps(creature, profile);
        }

        /// <summary>Set an attribute's StartingValue absolutely (mobs carry 0 XP/ranks, so this IS the Base).</summary>
        private static void ApplyAttribute(Creature creature, EvaluatedProfile profile, string statKey, PropertyAttribute attr)
        {
            if (!profile.Has(statKey))
                return;

            var target = (long)Math.Round(profile.Get(statKey));
            creature.Attributes[attr].StartingValue = (uint)Math.Clamp(target, 1L, uint.MaxValue);
        }

        /// <summary>Set a vital's (Stamina/Mana) max absolutely, as a spawn snapshot preserving current %
        /// (same pattern as the MaxHealth block above; Health is handled separately in ApplyToSpawn).</summary>
        private static void ApplyVital(Creature creature, EvaluatedProfile profile, string statKey, WorldObjects.Entity.CreatureVital vital)
        {
            if (profile == null || !profile.Has(statKey))
                return;

            var maxBefore = vital.MaxValue;
            var pct = maxBefore > 0 ? (float)vital.Current / maxBefore : 1f;
            var target = (long)Math.Round(profile.Get(statKey));
            var nonStarting = (long)maxBefore - vital.StartingValue;
            vital.StartingValue = (uint)Math.Clamp(target - nonStarting, 1L, uint.MaxValue);
            var maxAfter = vital.MaxValue;
            vital.Current = (uint)Math.Clamp((uint)Math.Round(pct * maxAfter), 0u, maxAfter);
        }

        /// <summary>Stamp a zone profile's generic prop overrides onto a freshly-spawned governed monster.</summary>
        private static void ApplyProps(Creature creature, EvaluatedProfile profile)
        {
            if (profile.PropInts != null)
                foreach (var kv in profile.PropInts)
                    if (!ZonePropGuard.IsBlockedInt(kv.Key))
                        creature.SetProperty((PropertyInt)kv.Key, (int)kv.Value);

            if (profile.PropInt64s != null)
                foreach (var kv in profile.PropInt64s)
                    if (!ZonePropGuard.IsBlockedInt64(kv.Key))
                        creature.SetProperty((PropertyInt64)kv.Key, kv.Value);

            if (profile.PropFloats != null)
                foreach (var kv in profile.PropFloats)
                    if (!ZonePropGuard.IsBlockedFloat(kv.Key))
                        creature.SetProperty((PropertyFloat)kv.Key, kv.Value);

            if (profile.PropBools != null)
                foreach (var kv in profile.PropBools)
                    if (!ZonePropGuard.IsBlockedBool(kv.Key))
                        creature.SetProperty((PropertyBool)kv.Key, kv.Value);
        }
    }
}
