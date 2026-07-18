using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static class PrestigeManager
    {
        // Prestige tiers live ABOVE this offset so they never collide with other explicit
        // variation layers. Variations 1..PRESTIGE_VAR_OFFSET are ordinary explicit layers
        // (retail instanced content, Zone Control's Tide v11-25, rifts, etc.) and are NOT
        // owned by the prestige system — see IsPrestigeVariation. Tier N == PRESTIGE_VAR_OFFSET + N.
        public const int PRESTIGE_VAR_OFFSET = 1000;
        public const int PRESTIGE_BASE_VARIATION = PRESTIGE_VAR_OFFSET + 1;
        private const int DEFAULT_PRESTIGE_MAX_TIER = 10;

        /// <summary>
        /// Defines allowed landblocks for each prestige tier.
        /// Prevents players from exploring undesigned areas of the map.
        /// Empty HashSet = no restrictions for that tier (allows free exploration).
        /// </summary>
        private static readonly Dictionary<int, HashSet<ushort>> _defaultTierAllowedLandblocks = new()
        {
            // Test configuration: Landblock 0xEAEA for all tiers during development
            // Expand these lists as content is designed for each tier
            [1] = new HashSet<ushort> { 0xEAEA },
            [2] = new HashSet<ushort> { 0xEAEA },
            [3] = new HashSet<ushort> { 0xEAEA },
            [4] = new HashSet<ushort> { 0xEAEA },
            [5] = new HashSet<ushort> { 0xEAEA },
            [6] = new HashSet<ushort> { 0xEAEA },
            [7] = new HashSet<ushort> { 0xEAEA },
            [8] = new HashSet<ushort> { 0xEAEA },
            [9] = new HashSet<ushort> { 0xEAEA },
            [10] = new HashSet<ushort> { 0xEAEA },
        };
        private static Dictionary<int, HashSet<ushort>> _tierAllowedLandblocks = CloneAllowedLandblocks(_defaultTierAllowedLandblocks);
        private static readonly object _allowedLandblocksLock = new object();
        private static readonly object _migrationLock = new object();
        private static volatile bool _databaseInitialized;

        /// <summary>
        /// Checks if a landblock is allowed for the given variation.
        /// Returns true if no restrictions configured or variation is retail (0-10).
        /// </summary>
        public static bool IsLandblockAllowed(int? variation, ushort landblockId)
        {
            EnsureDatabaseInitialized();

            var tier = GetTier(variation);
            if (tier <= 0) return true; // Retail zones unrestricted

            lock (_allowedLandblocksLock)
            {
                if (!_tierAllowedLandblocks.TryGetValue(tier, out var allowed))
                    return true; // No restrictions configured for this tier

                if (allowed.Count == 0) return true; // Empty list = no restrictions

                return allowed.Contains(landblockId);
            }
        }

        /// <summary>Master kill-switch for every prestige system (config <c>prestige_systems_enabled</c>).
        /// When false, <see cref="GetTier(int)"/> reports 0 for every variation, which neutralizes all
        /// tier-driven systems (spawn scaling, boundaries/markers/wisp, live combat tier mods, kill
        /// XP/luminance and loot scaling); the variation-triggered v11 combat rules check this flag at
        /// their own gates. Variation instancing itself (visibility, effective variation) is NOT gated —
        /// other systems (Zone Control, rifts) depend on it.</summary>
        public static bool SystemsEnabled => ServerConfig.prestige_systems_enabled.Value;

        /// <summary>
        /// Converts a Variation ID to a Prestige Tier.
        /// Retail (Null/0-10) returns 0.
        /// Variation 11 returns Tier 1.
        /// Always 0 when <see cref="SystemsEnabled"/> is off (the master kill-switch choke point).
        /// </summary>
        public static int GetTier(int variation)
        {
            if (!SystemsEnabled) return 0;
            if (variation <= PRESTIGE_VAR_OFFSET) return 0;
            return variation - PRESTIGE_VAR_OFFSET;
        }

        public static int GetTier(int? variation)
        {
            if (!variation.HasValue) return 0;
            return GetTier(variation.Value);
        }

        /// <summary>
        /// Tier passed into kill XP / luminance / loot scaling: from the creature's <see cref="WorldObject.Location"/> variation only
        /// (<see cref="GetTier(int?)"/> — null and 0–10 → 0, 11+ → prestige). Retail instances are never scaled from a stale <see cref="PropertyInt.PrestigeLevel"/>.
        /// </summary>
        public static int GetKillScalingMonsterTier(WorldObject wo) => GetTier(wo?.Location?.Variation);

        public static HashSet<ushort> GetAllowedLandblocks(int? variation)
        {
            EnsureDatabaseInitialized();

            var tier = GetTier(variation);
            if (tier <= 0)
                return null;

            lock (_allowedLandblocksLock)
            {
                if (!_tierAllowedLandblocks.TryGetValue(tier, out var allowed))
                    return null;

                return new HashSet<ushort>(allowed);
            }
        }

        public static Dictionary<int, HashSet<ushort>> GetAllAllowedLandblocks()
        {
            EnsureDatabaseInitialized();

            lock (_allowedLandblocksLock)
                return CloneAllowedLandblocks(_tierAllowedLandblocks);
        }

        private static long CountRowsForTier(WorldDbContext context, int tier)
        {
            var conn = context.Database.GetDbConnection();
            var wasOpen = conn.State == ConnectionState.Open;
            if (!wasOpen)
                context.Database.OpenConnection();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM prestige_allowed_landblocks WHERE tier = @tier";
                var p = cmd.CreateParameter();
                p.ParameterName = "@tier";
                p.Value = tier;
                cmd.Parameters.Add(p);
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            finally
            {
                if (!wasOpen)
                    context.Database.CloseConnection();
            }
        }

        /// <summary>
        /// If <paramref name="tier"/> has no rows in <c>prestige_allowed_landblocks</c>, insert the current effective allow-list
        /// (in-memory defaults merged with any loaded DB state) so the next add/remove is incremental rather than replacing the tier with a single row.
        /// </summary>
        private static void EnsureTierSeededFromEffectiveSet(WorldDbContext context, int tier)
        {
            if (CountRowsForTier(context, tier) > 0)
                return;

            HashSet<ushort> snapshot;
            lock (_allowedLandblocksLock)
            {
                if (!_tierAllowedLandblocks.TryGetValue(tier, out var set) || set.Count == 0)
                    return;

                snapshot = new HashSet<ushort>(set);
            }

            foreach (var lb in snapshot)
            {
                context.Database.ExecuteSqlRaw(@"
                    INSERT INTO prestige_allowed_landblocks (tier, landblock, is_active, updated_at)
                    VALUES ({0}, {1}, 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE
                        is_active = 1,
                        updated_at = UTC_TIMESTAMP()",
                    tier, (int)lb);
            }
        }

        public static bool AddAllowedLandblock(int tier, ushort landblock)
        {
            EnsureDatabaseInitialized();

            if (tier <= 0)
                return false;

            using var context = new WorldDbContext();
            using var transaction = context.Database.BeginTransaction();
            try
            {
                EnsureTierSeededFromEffectiveSet(context, tier);

                context.Database.ExecuteSqlRaw(@"
                    INSERT INTO prestige_allowed_landblocks (tier, landblock, is_active, updated_at)
                    VALUES ({0}, {1}, 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE
                        is_active = 1,
                        updated_at = UTC_TIMESTAMP()",
                    tier, landblock);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            ReloadAllowedLandblocksFromDatabase();
            return true;
        }

        public static bool RemoveAllowedLandblock(int tier, ushort landblock)
        {
            if (tier <= 0)
                return false;

            EnsureDatabaseInitialized();

            using var context = new WorldDbContext();
            using var transaction = context.Database.BeginTransaction();
            try
            {
                EnsureTierSeededFromEffectiveSet(context, tier);

                var updated = context.Database.ExecuteSqlRaw(@"
                    UPDATE prestige_allowed_landblocks
                    SET is_active = 0, updated_at = UTC_TIMESTAMP()
                    WHERE tier = {0} AND landblock = {1} AND is_active = 1",
                    tier, landblock);

                transaction.Commit();
                ReloadAllowedLandblocksFromDatabase();
                return updated > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>Loads active rows from <c>prestige_allowed_landblocks</c> into <see cref="_tierAllowedLandblocks"/>.</summary>
        private static void ReloadAllowedLandblocksFromDatabaseInternal()
        {
            var fromDb = new Dictionary<int, HashSet<ushort>>();
            var seededTiers = new HashSet<int>();
            using var context = new WorldDbContext();
            context.Database.OpenConnection();
            try
            {
                var connection = context.Database.GetDbConnection();

                using (var tierCmd = connection.CreateCommand())
                {
                    tierCmd.CommandText = "SELECT DISTINCT tier FROM prestige_allowed_landblocks";
                    using var tierReader = tierCmd.ExecuteReader();
                    while (tierReader.Read())
                        seededTiers.Add(tierReader.GetInt32(0));
                }

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT tier, landblock
                    FROM prestige_allowed_landblocks
                    WHERE is_active = 1
                    ORDER BY tier, landblock";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var tier = reader.GetInt32(0);
                    var landblock = Convert.ToUInt16(reader.GetInt32(1));

                    if (!fromDb.TryGetValue(tier, out var set))
                    {
                        set = new HashSet<ushort>();
                        fromDb[tier] = set;
                    }
                    set.Add(landblock);
                }
            }
            finally
            {
                context.Database.CloseConnection();
            }

            var loaded = CloneAllowedLandblocks(_defaultTierAllowedLandblocks);
            foreach (var tier in seededTiers)
                loaded[tier] = new HashSet<ushort>();
            foreach (var kvp in fromDb)
            {
                if (!loaded.TryGetValue(kvp.Key, out var set))
                {
                    set = new HashSet<ushort>();
                    loaded[kvp.Key] = set;
                }
                else
                {
                    set.Clear();
                }
                foreach (var lb in kvp.Value)
                    set.Add(lb);
            }

            lock (_allowedLandblocksLock)
                _tierAllowedLandblocks = loaded;
        }

        public static void ReloadAllowedLandblocksFromDatabase()
        {
            EnsureDatabaseInitialized();
            ReloadAllowedLandblocksFromDatabaseInternal();
        }

        /// <summary>
        /// Returns the HP multiplier for a given tier.
        /// Baseline: 1.0
        /// </summary>
        public static float GetHPModifier(int tier)
        {
            if (tier <= 0) return 1.0f;

            if (ServerConfig.v11_tier_enabled.Value)
            {
                // GEOMETRIC: hp_growth^clampedTier. Steeper at high tiers than the old linear model,
                // so each tier is a consistent % jump instead of a shrinking one.
                var clamped = Math.Min(tier, (int)ServerConfig.v11_tier_max.Value);
                return (float)Math.Pow(ServerConfig.v11_tier_hp_growth.Value, clamped);
            }

            // legacy linear: +25% HP per tier
            return 1.0f + (tier * 0.25f);
        }

        /// <summary>
        /// Returns the Damage multiplier for a given tier.
        /// Baseline: 1.0
        /// </summary>
        public static float GetDamageModifier(int tier)
        {
             if (tier <= 0) return 1.0f;

             if (ServerConfig.v11_tier_enabled.Value)
             {
                 // additive DamageRating per tier -> expressed as a mod so ModToRating() yields (per_tier * tier)
                 var clamped = Math.Min(tier, (int)ServerConfig.v11_tier_max.Value);
                 return 1.0f + (float)(clamped * ServerConfig.v11_tier_damage_rating_per_tier.Value / 100.0);
             }

             // legacy linear: +15% Damage per tier
             return 1.0f + (tier * 0.15f);
        }

        /// <summary>
        /// Live per-tier scaling tier for a creature's current instance (tier = variation - 10, clamped to v11_tier_max),
        /// or 0 when the per-tier system is disabled or the creature isn't in a prestige variation. Used by the combat
        /// choke points (defense/attack skill, vuln) which read the tier fresh at hit time.
        /// </summary>
        /// <summary>
        /// Effective prestige variation for the v11+ endgame combat systems. Normally the object's real
        /// Location.Variation. TEST HOOK: a weenie with PropertyBool.ForceEndgameSystems reports an effective
        /// variation even when spawned at a non-prestige variation (0), so the endgame stack can be tested in a
        /// normal landblock (where /createinst /removeinst /reload-landblock work). PropertyInt.EndgameForcedVariation
        /// picks the simulated tier (default = base prestige, v11). No effect on real mobs (no flag set).
        /// </summary>
        public static int GetEffectiveVariation(WorldObject wo)
        {
            var real = wo?.Location?.Variation ?? 0;
            if (real >= PRESTIGE_BASE_VARIATION)
                return real;

            if (wo?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.ForceEndgameSystems) == true)
            {
                var forced = wo.GetProperty(ACE.Entity.Enum.Properties.PropertyInt.EndgameForcedVariation) ?? 0;
                return forced >= PRESTIGE_BASE_VARIATION ? forced : PRESTIGE_BASE_VARIATION;
            }

            return real;
        }

        public static int GetLiveScalingTier(Creature creature)
        {
            if (creature == null || creature is Player || !ServerConfig.v11_tier_enabled.Value)
                return 0;

            var tier = GetTier(GetEffectiveVariation(creature));
            if (tier <= 0)
                return 0;

            return Math.Min(tier, (int)ServerConfig.v11_tier_max.Value);
        }

        /// <summary>Monster melee/missile/magic DEFENSE skill multiplier for a creature (1.0 when N/A). Read live.</summary>
        public static float GetDefenseSkillModifier(Creature creature)
        {
            var tier = GetLiveScalingTier(creature);
            if (tier <= 0)
                return 1.0f;
            return 1.0f + (float)(tier * ServerConfig.v11_tier_defense_per_tier.Value);
        }

        /// <summary>Monster ATTACK skill multiplier for a creature (1.0 when N/A). Read live.</summary>
        public static float GetAttackSkillModifier(Creature creature)
        {
            var tier = GetLiveScalingTier(creature);
            if (tier <= 0)
                return 1.0f;
            return 1.0f + (float)(tier * ServerConfig.v11_tier_attack_per_tier.Value);
        }

        /// <summary>Extra vuln-effectiveness reduction (percentage points, e.g. 0.20) for a creature's tier. Read live.</summary>
        public static double GetVulnEffectivenessReduction(Creature creature)
        {
            var tier = GetLiveScalingTier(creature);
            if (tier <= 0)
                return 0.0;
            return tier * ServerConfig.v11_tier_vuln_per_tier.Value;
        }

        /// <summary>Extra damage-taken reduction (subtracted from the mitigation multiplier) for a creature's tier. 0 when flat. Read live.</summary>
        public static double GetDamageTakenTierReduction(Creature creature)
        {
            var tier = GetLiveScalingTier(creature);
            if (tier <= 0)
                return 0.0;
            return tier * ServerConfig.v11_mob_dmg_taken_per_tier.Value;
        }

        /// <summary>
        /// Returns the XP multiplier for a given tier.
        /// Baseline: 1.0
        /// </summary>
        public static float GetXPRewardModifier(int tier)
        {
            if (tier <= 0) return 1.0f;
            // +10% XP per tier
            return 1.0f + (tier * 0.10f);
        }

        /// <summary>
        /// Returns the XP multiplier for a player killing a monster.
        /// Applies a 20% penalty for each tier the player is ABOVE the monster.
        /// </summary>
        public static float GetXPPenaltyMultiplier(int playerTier, int monsterTier)
        {
            if (playerTier <= monsterTier) return 1.0f;

            var diff = playerTier - monsterTier;
            // -20% XP per tier diff
            var multiplier = 1.0f - (diff * 0.20f);

            return Math.Max(0.0f, multiplier);
        }


        /// <summary>
        /// Returns the Value (Pyreal) multiplier for generated loot.
        /// </summary>
        public static float GetLootValueModifier(int tier)
        {
            if (tier <= 0) return 1.0f;
            // +20% Value per tier
            return 1.0f + (tier * 0.20f);
        }

        /// <summary>
        /// Applies scaled bonuses to generated loot based on the monster's prestige tier.
        /// Does not modify <see cref="WorldObject.ItemWorkmanship"/> (workmanship stays as rolled/generated).
        /// </summary>
        public static void ApplyLootScaling(WorldObject wo, int tier)
        {
            if (tier <= 0) return;

            // 1. Mana bonus (10% more max mana per tier)
            if (wo.ItemMaxMana.HasValue)
            {
                wo.ItemMaxMana = (int?)Math.Round(wo.ItemMaxMana.Value * (1.0f + tier * 0.1f));
                wo.ItemCurMana = wo.ItemMaxMana; // Fill it up
            }

            // 2. Value Bonus
            var valueMod = GetLootValueModifier(tier);
            if (valueMod != 1.0f)
            {
                if (wo.Value.HasValue)
                    wo.Value = (int?)Math.Round(wo.Value.Value * valueMod);
            }
        }

        /// <summary>
        /// Reverts HP and damage changes from <see cref="ApplyPrestigeScaling"/> using <see cref="PropertyInt.PrestigeLevel"/> as the tier that was applied.
        /// Call before re-applying a different tier so stats replace rather than compound.
        /// </summary>
        public static void RemovePrestigeScaling(Creature creature)
        {
            var oldTier = creature.GetProperty(PropertyInt.PrestigeLevel) ?? 0;
            if (oldTier <= 0)
                return;

            var hpMod = GetHPModifier(oldTier);
            var maxBefore = creature.Health.MaxValue;
            var healthPct = maxBefore > 0 ? (float)creature.Health.Current / maxBefore : 1f;

            if (hpMod != 1.0f)
                creature.Health.StartingValue = (uint)Math.Round(creature.Health.StartingValue / hpMod);

            var dmgMod = GetDamageModifier(oldTier);
            if (dmgMod != 1.0f)
            {
                var rating = ModToRating(dmgMod);
                var existing = creature.GetProperty(PropertyInt.DamageRating) ?? 0;
                var next = Math.Max(0, existing - rating);
                if (next == 0)
                    creature.RemoveProperty(PropertyInt.DamageRating);
                else
                    creature.SetProperty(PropertyInt.DamageRating, next);
            }

            creature.RemoveProperty(PropertyInt.PrestigeLevel);

            if (hpMod != 1.0f)
            {
                var maxAfter = creature.Health.MaxValue;
                var newCur = (uint)Math.Clamp((uint)Math.Round(healthPct * maxAfter), 0u, maxAfter);
                creature.Health.Current = newCur;
            }
        }

        /// <summary>
        /// Clears prestige scaling from a creature without re-applying (use for admin tier 0 / retail).
        /// </summary>
        public static void ClearPrestigeScaling(Creature creature) => RemovePrestigeScaling(creature);

        /// <summary>
        /// Applies HP and Damage scaling to a spawned creature based on its location's prestige tier.
        /// Prestige-only: Zone Control's spawn snapshot is applied independently (ZoneSpawnScaler) at the
        /// same spawn call sites — the two systems don't consult each other.
        /// </summary>
        public static void ApplyPrestigeScaling(Creature creature, int? variation = null)
        {
            var prev = creature.GetProperty(PropertyInt.PrestigeLevel) ?? 0;
            // Variations 11-20 are Prestige Tiers 1-10. Use the ForceEndgameSystems-aware effective
            // variation (not the raw Location.Variation) so a test dummy can be scaled while standing
            // in a normal (variation 0) landblock, matching how the live combat systems resolve tier.
            var tier = GetTier(GetEffectiveVariation(creature));

            if (tier <= 0)
            {
                if (prev > 0)
                    RemovePrestigeScaling(creature);
                return;
            }

            if (prev == tier)
                return;

            if (prev > 0)
                RemovePrestigeScaling(creature);

            // 1. HP Scaling: geometric per-tier hpMod on the health StartingValue, preserving the current %
            // across the max change (no free heal/refill).
            var hpMod = GetHPModifier(tier);
            if (hpMod != 1.0f)
            {
                var maxBefore = creature.Health.MaxValue;
                var healthPct = maxBefore > 0 ? (float)creature.Health.Current / maxBefore : 1f;
                creature.Health.StartingValue = (uint)Math.Round(creature.Health.StartingValue * hpMod);
                var maxAfter = creature.Health.MaxValue;
                creature.Health.Current = (uint)Math.Clamp((uint)Math.Round(healthPct * maxAfter), 0u, maxAfter);
            }

            // 2. Damage Scaling (Apply as DamageRating)
            var dmgMod = GetDamageModifier(tier);
            if (dmgMod != 1.0f)
            {
                // Convert 1.15x -> 15 Damage Rating; stack on existing rating from the weenie
                var rating = ModToRating(dmgMod);
                var existing = creature.GetProperty(PropertyInt.DamageRating) ?? 0;
                creature.SetProperty(PropertyInt.DamageRating, existing + rating);
            }

            // 3. Mark the creature with its tier for XP/Loot logic later
            creature.SetProperty(PropertyInt.PrestigeLevel, tier);
            
            // Log for visibility during testing
            // creature.EnqueueBroadcast(new Network.GameMessages.Messages.GameMessageSystemChat($"{creature.Name} spawned at Tier {tier}!", ChatMessageType.System));
        }

        /// <summary>
        /// Converts a 1.xx modifier to a +x rating (e.g. 1.15 -> 15)
        /// Copied from Creature_Rating for dependency-free use here.
        /// </summary>
        public static int ModToRating(float mod)
        {
            if (mod >= 1.0f)
                return (int)Math.Round(mod * 100 - 100);
            else
                return (int)Math.Round(-100 / mod + 100);
        }

        public static bool IsPrestigeVariation(int? variation)
        {
            return variation.HasValue && variation.Value > PRESTIGE_VAR_OFFSET;
        }

        /// <summary>
        /// Generic variation identity now lives in <see cref="VariationManager"/>. These thin
        /// delegators remain only for source compatibility; the prestige system does not own
        /// generic visibility/collision variation logic.
        /// </summary>
        public static int? GetEffectiveVariationForVisibility(WorldObject wo) => VariationManager.GetEffectiveVariationForVisibility(wo);

        /// <inheritdoc cref="VariationManager.SameVariationForVisibility"/>
        public static bool SameVariationForVisibility(int? a, int? b) => VariationManager.SameVariationForVisibility(a, b);

        public static int GetBasePrestigeVariation()
        {
            return PRESTIGE_BASE_VARIATION;
        }

        public static int GetMaxConfiguredPrestigeVariation()
        {
            EnsureDatabaseInitialized();

            var maxTier = DEFAULT_PRESTIGE_MAX_TIER;
            lock (_allowedLandblocksLock)
            {
                if (_tierAllowedLandblocks.Count > 0)
                    maxTier = Math.Max(maxTier, _tierAllowedLandblocks.Keys.Max());
            }
            return PRESTIGE_VAR_OFFSET + maxTier;
        }

        public static List<int> GetDefaultMirrorTargetVariations(int? sourceVariation)
        {
            if (!sourceVariation.HasValue || sourceVariation.Value != PRESTIGE_BASE_VARIATION)
                return new List<int>();

            var targets = new List<int>();
            var maxVariation = GetMaxConfiguredPrestigeVariation();
            for (var variation = PRESTIGE_BASE_VARIATION + 1; variation <= maxVariation; variation++)
                targets.Add(variation);

            return targets;
        }

        public static List<int> NormalizeMirrorTargetVariations(IEnumerable<int> requestedTargets, int? sourceVariation)
        {
            var source = sourceVariation ?? 0;
            var maxVariation = GetMaxConfiguredPrestigeVariation();

            return requestedTargets
                .Where(v => v > PRESTIGE_VAR_OFFSET && v <= maxVariation && v != source)
                .Distinct()
                .OrderBy(v => v)
                .ToList();
        }

        public static bool IsCreateInstMirrorEligible(WeenieType weenieType, bool hasGeneratorProfiles)
        {
            if (hasGeneratorProfiles)
                return true;

            return weenieType switch
            {
                WeenieType.Creature => true,
                WeenieType.Vendor => true,
                WeenieType.Portal => true,
                WeenieType.LifeStone => true,
                WeenieType.Door => true,
                WeenieType.Chest => true,
                WeenieType.Container => true,
                WeenieType.PressurePlate => true,
                WeenieType.Switch => true,
                WeenieType.LightSource => true,
                WeenieType.Generic => true,
                _ => false,
            };
        }

        private static void EnsureDatabaseInitialized()
        {
            if (_databaseInitialized) return;

            lock (_migrationLock)
            {
                if (_databaseInitialized) return;

                using var context = new WorldDbContext();
                EnsurePrestigeAllowedLandblocksTable(context);
                ReloadAllowedLandblocksFromDatabaseInternal();
                _databaseInitialized = true;
            }
        }

        private static void EnsurePrestigeAllowedLandblocksTable(WorldDbContext context)
        {
            context.Database.OpenConnection();
            try
            {
                using var cmd = context.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM information_schema.tables
                    WHERE table_schema = DATABASE() AND table_name = 'prestige_allowed_landblocks'";
                var count = Convert.ToInt64(cmd.ExecuteScalar());
                if (count > 0)
                    return;

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `prestige_allowed_landblocks` (
                        `id` int(11) NOT NULL AUTO_INCREMENT,
                        `tier` int(11) NOT NULL,
                        `landblock` int(11) NOT NULL,
                        `is_active` tinyint(1) NOT NULL DEFAULT 1,
                        `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                        PRIMARY KEY (`id`),
                        UNIQUE KEY `ux_prestige_tier_landblock` (`tier`, `landblock`),
                        KEY `ix_prestige_tier_active` (`tier`, `is_active`),
                        KEY `ix_prestige_landblock_active` (`landblock`, `is_active`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
            }
            finally
            {
                context.Database.CloseConnection();
            }
        }

        private static Dictionary<int, HashSet<ushort>> CloneAllowedLandblocks(Dictionary<int, HashSet<ushort>> source)
        {
            return source.ToDictionary(kvp => kvp.Key, kvp => new HashSet<ushort>(kvp.Value));
        }
    }
}
