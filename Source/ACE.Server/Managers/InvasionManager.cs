using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using log4net;
using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Factories;
using Position = ACE.Entity.Position;

namespace ACE.Server.Managers
{
    public static partial class InvasionManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object _lock = new object();

        public static readonly List<string> Towns = new()
        {
            "Al-Arqas", "Al-Jalima", "Arwic", "Baishi", "Cragstone",
            "Eastham", "Glenden Wood", "Hebian-to", "Holtburg", "Kara",
            "Khayyaban", "Lytelthorpe", "Mayoi", "Nanto", "Neydisa",
            "Rithwic", "Samsur", "Sawato", "Shoushi", "Stonehold",
            "Subway", "Town Network", "Tufa", "Uziz", "Yanshi",
            "Yaraq", "Zaikhal"
        };

        public static readonly List<string> SpeciesList = new()
        {
            "Shadow", "Tusker", "Olthoi"
        };

        /// <summary>
        /// Hardcoded boss spawn positions per town (cell, x, y, z).
        /// Derived from landblock_instance placements in the SQL migration.
        /// </summary>
        private static readonly Dictionary<string, Position> TownBossPositions = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Al-Arqas",    new Position(2404909115, 183.851f,  60.183f,  11.326f, 0f, 0f, 0f, 1f) },
            { "Al-Jalima",   new Position(2240282668, 120.359f,  95.47f,   92.049f, 0f, 0f, 0f, 1f) },
            { "Arwic",       new Position(3332964361,  46.805f,   4.219f,  44.005f, 0f, 0f, 0f, 1f) },
            { "Baishi",      new Position(3460366343,  12.6f,   152.8f,   57.1f,   0f, 0f, 0f, 1f) },
            { "Cragstone",   new Position(3147759680, 169.358f, 168.251f,  56.005f, 0f, 0f, 0f, 1f) },
            { "Eastham",     new Position(3465805877, 151.053f, 112.61f,   19.417f, 0f, 0f, 0f, 1f) },
            { "Glenden Wood",new Position(2695102501,  96.302f, 119.847f,  61.955f, 0f, 0f, 0f, 1f) },
            { "Hebian-to",   new Position(3863871535, 138.304f, 161.905f,  22.04f,  0f, 0f, 0f, 1f) },
            { "Holtburg",    new Position(2847146009,  84f,       7.1f,    96f,     0f, 0f, 0f, 1f) },
            { "Kara",        new Position(3122069531, 85.39f,   59.64f,  132.00f,  0f, 0f, -0.58239f, -0.81291f) },
            { "Khayyaban",   new Position(2672033810,  90f,      24.553f,  33.885f, 0f, 0f, 0f, 1f) },
            { "Lytelthorpe", new Position(3229614087,  11.723f, 155.56f,   35.028f, 0f, 0f, 0f, 1f) },
            { "Mayoi",       new Position(3862036513, 107.417f,  10.763f,  31.908f, 0f, 0f, 0f, 1f) },
            { "Nanto",       new Position(3862822946,  96.96f,   37.722f,  76.542f, 0f, 0f, 0f, 1f) },
            { "Neydisa",     new Position(2513829939, 146.9f,    71.3f,   101.8f,   0f, 0f, 0f, 1f) },
            { "Rithwic",     new Position(3381395496, 113.666f, 190.259f,  24.005f, 0f, 0f, 0f, 1f) },
            { "Samsur",      new Position(2541420556,  25.811f,  73.853f,   2.005f, 0f, 0f, 0f, 1f) },
            { "Sawato",      new Position(3378184193,  14.8f,     0.3f,    14f,     0f, 0f, 0f, 1f) },
            { "Shoushi",     new Position(3663003677,  84.8f,    99f,      22f,     0f, 0f, 0f, 1f) },
            { "Stonehold",   new Position(1691680779,  30f,      50f,      80f,     0f, 0f, 0f, 1f) },
            { "Tufa",        new Position(2272002056,   2f,     186.9f,    20f,     0f, 0f, 0f, 1f) },
            { "Uziz",        new Position(2724200508, 182.919f,  87.934f,  22.005f, 0f, 0f, 0f, 1f) },
            { "Yanshi",      new Position(3027173406,  75.2f,   124.1f,    36.69f,  0f, 0f, 0f, 1f) },
            { "Yaraq",       new Position(2103705613,  31.9f,   104.6f,    13.9f,   0f, 0f, 0f, 1f) },
            { "Zaikhal",     new Position(2156920851,  64.863f,  55.687f, 126.005f, 0f, 0f, 0f, 1f) },
            // Dungeon locations (indoor) — Z left as the floor value from /loc; SpawnBoss skips
            // the terrain-Z snap for indoor cells.
            { "Town Network", new Position(459075,    69.898331f, -59.826942f, 0.005f, 0f, 0f, -0.999249f, -0.038746f) },
            { "Subway",       new Position(29950517,  79.682556f, -40.203197f, 0.005f, 0f, 0f, -0.356618f, -0.934250f) },
        };

        // State variables
        public static bool IsActive { get; private set; } = false;
        public static string ActiveTown { get; private set; }
        public static string ActiveSpecies { get; private set; }
        public static WorldObject ActiveGenerator { get; private set; }
        public static Creature ActiveBoss { get; set; } // Set or read when boss is active

        /// <summary>The active invasion's win/fail rules. Non-null only while an invasion runs:
        /// set in StartInvasion, cleared in StopInvasion/FailInvasion.</summary>
        public static InvasionObjective ActiveObjective { get; private set; }

        public static double InvasionStartTime { get; private set; }
        public static double BossSpawnTime { get; private set; }
        public static double NextInvasionTime { get; private set; } = 0;
        public static int KillCount { get; private set; } = 0;

        // Configurable thresholds/timeouts — all backed by ServerConfig (shard DB) for persistence across restarts.
        // Read: ServerConfig.invasion_*.Value  |  Write: ServerConfig.SetValue(...) marks dirty → DB on next flush.

        /// <summary>Master on/off switch for automatic invasion triggering. Manual /dev invasion start ignores this.</summary>
        public static bool Enabled
        {
            get => ServerConfig.invasion_enabled.Value;
            set => ServerConfig.SetValue("invasion_enabled", value);
        }

        /// <summary>When false, only the solo boss spawns. Flip to true when minion wave spawning is ready to test.</summary>
        public static bool SpawnMinions
        {
            get => ServerConfig.invasion_spawn_minions.Value;
            set => ServerConfig.SetValue("invasion_spawn_minions", value);
        }

        public static double CooldownMin
        {
            get => ServerConfig.invasion_cooldown_min.Value;
            set => ServerConfig.SetValue("invasion_cooldown_min", value);
        }

        public static double CooldownMax
        {
            get => ServerConfig.invasion_cooldown_max.Value;
            set => ServerConfig.SetValue("invasion_cooldown_max", value);
        }

        /// <summary>Returns a random cooldown between CooldownMin and CooldownMax (seconds).</summary>
        public static double GetRandomCooldown()
        {
            var min = CooldownMin;
            var max = Math.Max(min, CooldownMax);
            return min + (ThreadSafeRandom.Next(0, 100000) / 100000.0 * (max - min));
        }

        public static double ProximityTimeout
        {
            get => ServerConfig.invasion_proximity_timeout.Value;
            set => ServerConfig.SetValue("invasion_proximity_timeout", value);
        }

        public static long DamageThreshold
        {
            get => ServerConfig.invasion_damage_threshold.Value;
            set => ServerConfig.SetValue("invasion_damage_threshold", value);
        }

        public static long HealingThreshold
        {
            get => ServerConfig.invasion_healing_threshold.Value;
            set => ServerConfig.SetValue("invasion_healing_threshold", value);
        }

        public static int RequiredKills
        {
            get => (int)ServerConfig.invasion_required_kills.Value;
            set => ServerConfig.SetValue("invasion_required_kills", (long)value);
        }

        // Participation tracking (GUID to total amount)
        public static Dictionary<uint, long> PlayerDamageTracker { get; } = new();
        public static Dictionary<uint, long> PlayerHealingTracker { get; } = new();

        private static double _nextTickTime;

        // Startup grace period — invasions will not auto-trigger within 5 minutes of server start.
        private const double StartupGracePeriod = 300.0;
        private static bool _startupDelayInitialized = false;

        // Proximity radius (in world units) to detect invasion-related kills
        private const float InvasionKillRadius = 150.0f;

        // ----------------------------------------------------------------
        // Generator hooks (still used to store ActiveGenerator reference;
        // boss spawning no longer depends on these firing first)
        // ----------------------------------------------------------------

        public static void RegisterGenerator(WorldObject generator)
        {
            if (generator == null || string.IsNullOrEmpty(generator.GeneratorEvent)) return;
            if (!generator.GeneratorEvent.StartsWith("Invasion_", StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                ActiveGenerator = generator;
                log.Info($"[Invasion] Registered active spawner generator for {generator.GeneratorEvent} at {generator.Location}");
            }
        }

        public static void UnregisterGenerator(WorldObject generator)
        {
            lock (_lock)
            {
                if (ActiveGenerator == generator)
                {
                    ActiveGenerator = null;
                    log.Info($"[Invasion] Unregistered active spawner generator for {generator.GeneratorEvent}");
                }
            }
        }

        // ----------------------------------------------------------------
        // Damage / Healing tracking
        // ----------------------------------------------------------------

        /// <summary>
        /// Live per-hit damage credit. Called from DamageHistory.Add (every damage path:
        /// melee, missile, magic, DoT) on the creature's own combat thread. Cheap no-op when
        /// no invasion is running. Lets players see eligibility build during the fight instead
        /// of only when a mob dies.
        /// </summary>
        public static void OnDamageDealt(Creature target, WorldObject attacker, uint amount)
        {
            if (!IsActive || target == null || attacker == null || amount == 0) return;

            // Resolve to the owning player (direct hit or combat pet).
            var player = attacker as Player ?? (attacker as CombatPet)?.P_PetOwner;
            if (player == null) return;

            // The boss always counts. Other creatures must be invasion minions ("Invasion"
            // in the name) within the kill radius of the invasion origin.
            if (!(ActiveObjective?.IsBoss(target) ?? false))
            {
                if (target.Name == null || target.Name.IndexOf("Invasion", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                var origin = ActiveGenerator?.Location
                          ?? ActiveBoss?.Location
                          ?? (TownBossPositions.TryGetValue(ActiveTown, out var tp) ? tp : null);

                if (origin == null || target.Location == null || target.Location.DistanceTo(origin) > InvasionKillRadius)
                    return;
            }

            AddDamage(player, amount);
        }

        public static void AddDamage(Player player, long amount)
        {
            if (!IsActive || player == null || amount <= 0) return;

            var guid = player.Guid.Full;
            long oldDmg;
            long newDmg;

            lock (_lock)
            {
                PlayerDamageTracker.TryGetValue(guid, out oldDmg);
                newDmg = oldDmg + amount;
                PlayerDamageTracker[guid] = newDmg;
            }

            if (oldDmg < DamageThreshold && newDmg >= DamageThreshold)
            {
                TryClaimReward(player);
                player.Session?.Network.EnqueueSend(new GameMessageSystemChat("[Invasion] You have met the damage requirement and are now eligible for the reward portal!", ChatMessageType.System));
            }
        }

        public static void AddHealing(Player caster, Player target, long amount)
        {
            if (!IsActive || caster == null || target == null || amount <= 0) return;

            var guid = caster.Guid.Full;
            long oldHeal;
            long newHeal;

            lock (_lock)
            {
                PlayerHealingTracker.TryGetValue(guid, out oldHeal);
                newHeal = oldHeal + amount;
                PlayerHealingTracker[guid] = newHeal;
            }

            if (oldHeal < HealingThreshold && newHeal >= HealingThreshold)
            {
                TryClaimReward(caster);
                caster.Session?.Network.EnqueueSend(new GameMessageSystemChat("[Invasion] You have met the healing requirement and are now eligible for the reward portal!", ChatMessageType.System));
            }
        }

        public static bool IsEligible(Player player)
        {
            if (player == null) return false;

            long damage = 0;
            long healing = 0;

            lock (_lock)
            {
                PlayerDamageTracker.TryGetValue(player.Guid.Full, out damage);
                PlayerHealingTracker.TryGetValue(player.Guid.Full, out healing);
            }

            return damage >= DamageThreshold || healing >= HealingThreshold;
        }

        // ----------------------------------------------------------------
        // Invasion types (objective selection)
        // ----------------------------------------------------------------

        /// <summary>Invasion type ids accepted by /dev invasion start. "boss" is the default.</summary>
        public static readonly IReadOnlyList<string> InvasionTypes = new[] { "boss" };

        /// <summary>Case-insensitive check that a string names a known invasion type.</summary>
        public static bool IsKnownInvasionType(string typeId)
            => typeId != null && InvasionTypes.Any(t => t.Equals(typeId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Build the objective for a type id. Returns null for an unknown id.</summary>
        private static InvasionObjective CreateObjective(string typeId)
        {
            switch ((typeId ?? "boss").ToLowerInvariant())
            {
                case "":
                case "boss":
                    return new SingleBossObjective();
                // case "3boss": return new ThreeBossBurnObjective(); // Phase 2
                default:
                    return null;
            }
        }

        // ----------------------------------------------------------------
        // Start / Stop
        // ----------------------------------------------------------------

        public static bool StartInvasion(string town, string species, string type = "boss")
        {
            lock (_lock)
            {
                if (IsActive) return false;

                // Normalize town name casing from user/input matching
                var normalizedTown = Towns.FirstOrDefault(t => t.Equals(town, StringComparison.OrdinalIgnoreCase));
                var normalizedSpecies = SpeciesList.FirstOrDefault(s => s.Equals(species, StringComparison.OrdinalIgnoreCase));

                if (normalizedTown == null || normalizedSpecies == null)
                    return false;

                var objective = CreateObjective(type);
                if (objective == null)
                {
                    log.Warn($"[Invasion] Unknown invasion type '{type}'.");
                    return false;
                }

                // The generator event is only needed for minion spawning. Boss-only invasions
                // work for any town in the list even without a registered event/generator
                // (e.g. newly added locations that don't have generator SQL yet).
                var eventName = $"Invasion_{normalizedTown}_{normalizedSpecies}";
                bool eventAvailable = EventManager.IsEventAvailable(eventName);
                if (SpawnMinions && !eventAvailable)
                    log.Warn($"[Invasion] No generator event '{eventName}' registered — boss will spawn but minions will not.");

                // Reset states
                PlayerDamageTracker.Clear();
                PlayerHealingTracker.Clear();
                ResetRewardState();
                DespawnRewardPortal(); // clear any lingering portal from a prior invasion
                ActiveBoss = null;
                ActiveGenerator = null;
                KillCount = 0;

                ActiveObjective = objective;
                ActiveTown = normalizedTown;
                ActiveSpecies = normalizedSpecies;
                IsActive = true;
                InvasionStartTime = Time.GetUnixTime();

                // Start event to enable spawner generators (minion mobs) — only when enabled AND
                // a generator event actually exists for this town/species.
                if (SpawnMinions && eventAvailable)
                    EventManager.StartEvent(eventName, null, null);

                // Broadcast start message
                var announcement = $"[Invasion] A {normalizedSpecies} invasion has started in the town of {normalizedTown}!";
                BroadcastInvasion(announcement);

                // Spawn the objective's creatures (boss-only today; no generator dependency).
                ActiveObjective.OnStart();

                ForceSyncPush(); // push live state to plugin clients immediately
                return true;
            }
        }

        public static void StopInvasion(bool success)
        {
            lock (_lock)
            {
                if (!IsActive) return;

                var eventName = $"Invasion_{ActiveTown}_{ActiveSpecies}";
                if (SpawnMinions && EventManager.IsEventAvailable(eventName))
                    EventManager.StopEvent(eventName, null, null);

                ActiveObjective?.Cleanup();
                ActiveObjective = null;
                ActiveBoss = null;
                ActiveGenerator = null;

                IsActive = false;
                NextInvasionTime = Time.GetUnixTime() + GetRandomCooldown();

                if (success)
                {
                    var announcement = $"[Invasion] The town of {ActiveTown} has successfully defeated the {ActiveSpecies} invasion!";
                    BroadcastInvasion(announcement);
                }

                _lastInvasionEndedAt = Time.GetUnixTime(); // start the restart lockout
                ForceSyncPush(); // push idle/ended state to plugin clients immediately
            }
        }

        public static void FailInvasion()
        {
            lock (_lock)
            {
                if (!IsActive) return;

                var eventName = $"Invasion_{ActiveTown}_{ActiveSpecies}";
                if (SpawnMinions && EventManager.IsEventAvailable(eventName))
                    EventManager.StopEvent(eventName, null, null);

                ActiveObjective?.Cleanup();
                ActiveObjective = null;
                ActiveBoss = null;
                ActiveGenerator = null;

                var announcement = $"[Invasion] The town of {ActiveTown} has fallen!";
                BroadcastInvasion(announcement);

                IsActive = false;
                NextInvasionTime = Time.GetUnixTime() + GetRandomCooldown();

                _lastInvasionEndedAt = Time.GetUnixTime(); // start the restart lockout
                ForceSyncPush(); // push idle/ended state to plugin clients immediately
            }
        }

        // ----------------------------------------------------------------
        // Creature death hook
        // ----------------------------------------------------------------

        public static void HandleCreatureDeath(Creature creature)
        {
            if (creature == null) return;

            lock (_lock)
            {
                if (!IsActive) return;

                bool isBossDeath = ActiveObjective?.IsBoss(creature) ?? false;

                // Only track mobs that are part of the invasion (name contains "Invasion")
                if (!isBossDeath && (creature.Name == null || creature.Name.IndexOf("Invasion", StringComparison.OrdinalIgnoreCase) < 0))
                    return;

                // For non-boss creatures: also require proximity to where the boss spawned
                if (!isBossDeath)
                {
                    if (creature.Location == null)
                        return;

                    // Origin: generator location → boss location → hardcoded table fallback
                    var origin = ActiveGenerator?.Location
                              ?? ActiveBoss?.Location
                              ?? (TownBossPositions.TryGetValue(ActiveTown, out var tp) ? tp : null);

                    if (origin == null || creature.Location.DistanceTo(origin) > InvasionKillRadius)
                        return;
                }

                // Damage is now credited live per-hit via OnDamageDealt (see DamageHistory.Add),
                // so we no longer re-tally DamageHistory here (that would double-count).
                KillCount++;

                ActiveObjective?.OnCreatureDeath(creature);

                if (ActiveObjective != null && ActiveObjective.IsWon)
                {
                    HandleWin();
                }
            }
        }

        // ----------------------------------------------------------------
        // Boss spawn / death
        // ----------------------------------------------------------------

        /// <summary>
        /// Called once on server startup — before landblocks load — to remove any boss biotas
        /// that persisted in the shard DB from a previous session (e.g., after a force-kill).
        /// This prevents orphaned invasion mobs from reappearing when no invasion is active.
        /// </summary>
        public static void PurgeOrphanedEntities()
        {
            // All WCIDs that the invasion system can spawn as persistent creatures.
            // 72000001 = current invasion boss (Tyrant Darkspire Golem) — MUST match SpawnBoss().
            // 71600033 = legacy boss WCID (kept so old persisted bosses still get cleaned up).
            // 11 = tuskermale, in case it was used during testing.
            var invasionWcids = new uint[] { 72000001, 71600033, 11 };
            int totalRemoved = 0;

            foreach (var wcid in invasionWcids)
            {
                var biotas = DatabaseManager.Shard.BaseDatabase.GetBiotasByWcid(wcid);
                if (biotas.Count == 0)
                    continue;

                var ids = biotas.Select(b => b.Id).ToList();
                DatabaseManager.Shard.BaseDatabase.RemoveBiotasInParallel(ids);
                totalRemoved += ids.Count;
                log.Info($"[Invasion] Startup purge: removed {ids.Count} orphaned biota(s) for WCID {wcid}.");
            }

            if (totalRemoved == 0)
                log.Debug("[Invasion] Startup purge: no orphaned invasion entities found.");
            else
                log.Info($"[Invasion] Startup purge complete — {totalRemoved} total orphaned biota(s) removed.");
        }

        internal static void SpawnBoss()
        {
            // Determine spawn position: prefer registered generator location if available,
            // otherwise fall back to the hardcoded town position table.
            Position spawnPos;
            if (ActiveGenerator?.Location != null)
            {
                spawnPos = new Position(ActiveGenerator.Location);
            }
            else if (TownBossPositions.TryGetValue(ActiveTown, out var hardcoded))
            {
                // Copy so we can mutate Z without touching the shared table entry
                spawnPos = new Position(hardcoded);
            }
            else
            {
                log.Error($"[Invasion] Cannot spawn boss for '{ActiveTown}': no generator registered and no hardcoded position found.");
                return;
            }

            // Auto-snap Z to actual terrain height so the boss always lands on the ground
            // regardless of how the position table Z was originally set. Skip for indoor
            // (dungeon) cells — terrain height is meaningless there, the table Z is the floor.
            bool isOutdoor = (spawnPos.Cell & 0xFFFF) < 0x0100;
            try
            {
                if (!isOutdoor)
                {
                    log.Info($"[Invasion] Indoor spawn for '{ActiveTown}' — using table Z {spawnPos.PositionZ:F3} (no terrain snap).");
                }
                else
                {
                    var lb = LandblockManager.GetLandblock(spawnPos.LandblockId, false, spawnPos.Variation);
                    if (lb?.PhysicsLandblock != null)
                    {
                        var terrainZ = lb.PhysicsLandblock.GetZ(new Vector3(spawnPos.PositionX, spawnPos.PositionY, spawnPos.PositionZ));
                        // Add 1 unit buffer: creature physics origin needs to be above terrain surface, not exactly on it
                        var adjustedZ = terrainZ + 1.0f;
                        log.Info($"[Invasion] Boss spawn Z: table={spawnPos.PositionZ:F3}, terrain={terrainZ:F3}, final={adjustedZ:F3}");
                        spawnPos.PositionZ = adjustedZ;
                    }
                    else
                    {
                        log.Warn($"[Invasion] Landblock for '{ActiveTown}' not loaded — using table Z ({spawnPos.PositionZ:F3}). Boss may float.");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warn($"[Invasion] Terrain Z lookup failed for '{ActiveTown}': {ex.Message} — using table Z.");
            }

            // Spawn boss (Tyrant Darkspire Golem - WCID 72000001) at the town center
            var boss = WorldObjectFactory.CreateNewWorldObject(72000001) as Creature;
            if (boss == null)
            {
                log.Error("[Invasion] Failed to create boss object WCID 72000001");
                return;
            }

            boss.Location = spawnPos;
            ActiveBoss = boss;
            BossSpawnTime = Time.GetUnixTime();

            ApplyBossOverrides(boss); // live-tuning overrides (health/scale/damage rating) before spawn

            boss.EnterWorld();

            var announcement = $"[Invasion] The invasion boss, {boss.Name}, has spawned in the center of {ActiveTown}!";
            BroadcastInvasion(announcement);
            log.Info($"[Invasion] Boss '{boss.Name}' spawned at {spawnPos} for {ActiveTown}");
        }

        // Reward portal tracking (for force-despawn) + restart lockout.
        private static WorldObject _rewardPortal;
        private static double _rewardPortalExpires;
        private static double _lastInvasionEndedAt;
        private const int RewardPortalLifespan = 300; // seconds (5 minutes) — the reward window for players

        /// <summary>Seconds an admin must wait after an invasion ends before starting another
        /// (overridable with 'force'). Configurable; default 300s. Set short for testing.</summary>
        public static double RestartLockout
        {
            get => ServerConfig.invasion_restart_lockout.Value;
            set => ServerConfig.SetValue("invasion_restart_lockout", value);
        }

        /// <summary>Seconds remaining on the post-invasion restart lockout, else 0.</summary>
        public static double RewardLockoutRemaining
        {
            get
            {
                var rem = (_lastInvasionEndedAt + RestartLockout) - Time.GetUnixTime();
                return rem > 0 ? rem : 0;
            }
        }

        /// <summary>Despawn the active reward portal immediately (used when force-starting).</summary>
        public static void DespawnRewardPortal()
        {
            try { if (_rewardPortal != null && !_rewardPortal.IsDestroyed) _rewardPortal.Destroy(); }
            catch { }
            _rewardPortal = null;
            _rewardPortalExpires = 0;
        }

        /// <summary>Shared win path: spawn the reward portal, grant auto-loot, end successfully.
        /// Invoked when the active objective reports <see cref="InvasionObjective.IsWon"/>.</summary>
        private static void HandleWin()
        {
            // Spawn the reward portal where the boss actually died (falls back to the town
            // center if the boss location is somehow unavailable). The portal's destination
            // (the reward room) is defined by the portal weenie and is left unchanged.
            Position portalPos =
                  ActiveBoss?.Location != null ? new Position(ActiveBoss.Location)
                : TownBossPositions.TryGetValue(ActiveTown, out var hardcoded) ? new Position(hardcoded)
                : null;

            if (portalPos != null)
            {
                var portal = WorldObjectFactory.CreateNewWorldObject(694200296) as Portal;
                if (portal != null)
                {
                    portal.Location = portalPos;
                    portal.Lifespan = RewardPortalLifespan;
                    portal.CreationTimestamp = (int)Time.GetUnixTime();
                    portal.EnterWorld();

                    _rewardPortal = portal;
                    _rewardPortalExpires = Time.GetUnixTime() + RewardPortalLifespan;
                }
            }
            else
            {
                log.Error("[Invasion] Cannot spawn reward portal: no position available.");
            }

            // Auto-loot: credit/deliver rewards to eligible accounts' earners BEFORE StopInvasion
            // clears the trackers/reward state.
            GrantInvasionRewards();

            StopInvasion(true);
        }

        // ----------------------------------------------------------------
        // Random / automatic invasion trigger
        // ----------------------------------------------------------------

        public static void TriggerRandomInvasion()
        {
            lock (_lock)
            {
                if (IsActive) return;

                var town = Towns[ThreadSafeRandom.Next(0, Towns.Count)];
                var species = SpeciesList[ThreadSafeRandom.Next(0, SpeciesList.Count)];

                StartInvasion(town, species);
            }
        }

        // ----------------------------------------------------------------
        // Tick (proximity fail-safe)
        // ----------------------------------------------------------------

        public static void Tick()
        {
            var now = Time.GetUnixTime();
            if (now < _nextTickTime)
                return;

            _nextTickTime = now + 1.0; // Tick once per second

            // Push live state to plugin-enabled players (self-throttled; cheap).
            PushSync(now);

            lock (_lock)
            {
                // Apply startup grace period on first tick.
                if (!_startupDelayInitialized)
                {
                    var firstCooldown = GetRandomCooldown();
                    NextInvasionTime = now + StartupGracePeriod + firstCooldown;
                    _startupDelayInitialized = true;
                    log.Info($"[Invasion] Startup grace period active — first auto-invasion in {StartupGracePeriod}s grace + {firstCooldown:F0}s cooldown ({FormatMmSs(StartupGracePeriod + firstCooldown)} total).");
                }

                if (!IsActive)
                {
                    if (Enabled && now >= NextInvasionTime)
                    {
                        TriggerRandomInvasion();
                    }
                    return;
                }

                // Invasion is active. Let the objective advance its time-based rules and
                // surface any win/fail (e.g. a boss silently removed by landblock cleanup).
                ActiveObjective?.Tick(now);

                if (ActiveObjective != null && ActiveObjective.IsFailed)
                {
                    log.Warn($"[Invasion] Objective '{ActiveObjective.DisplayName}' reported failure in {ActiveTown}. Triggering FailInvasion.");
                    FailInvasion();
                    return;
                }

                if (ActiveObjective != null && ActiveObjective.IsWon)
                {
                    HandleWin();
                    return;
                }

                // Proximity fail-safe (shared across all objective types).
                var elapsed = now - InvasionStartTime;
                if (elapsed >= ProximityTimeout)
                {
                    Position targetLoc = null;
                    if (ActiveBoss != null && ActiveBoss.IsAlive)
                    {
                        targetLoc = ActiveBoss.Location;
                    }
                    else if (ActiveGenerator != null)
                    {
                        targetLoc = ActiveGenerator.Location;
                    }
                    else if (TownBossPositions.TryGetValue(ActiveTown, out var hardcoded))
                    {
                        targetLoc = hardcoded;
                    }

                    if (targetLoc != null)
                    {
                        if (!IsAnyPlayerNearby(targetLoc, 50.0f))
                        {
                            log.Info($"[Invasion] Fail-safe triggered: no living players within 50m of active target in {ActiveTown} after grace period.");
                            FailInvasion();
                        }
                    }
                }
            }
        }


        private static bool IsAnyPlayerNearby(Position targetLocation, float radius)
        {
            if (targetLocation == null) return false;

            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player == null || !player.IsAlive || player.Location == null) continue;

                if (targetLocation.DistanceTo(player.Location) <= radius)
                    return true;
            }

            return false;
        }

        // ----------------------------------------------------------------
        // Formatting helpers (used by DevCommands)
        // ----------------------------------------------------------------

        /// <summary>
        /// Formats a large number as e.g. "500k", "1.5m". Values under 10,000 use comma formatting.
        /// </summary>
        public static string FormatCompact(long val)
        {
            if (val >= 1_000_000)
            {
                var m = val / 1_000_000.0;
                return m == Math.Floor(m) ? $"{(long)m}m" : $"{m:G3}m";
            }
            if (val >= 10_000)
            {
                var k = val / 1_000.0;
                return k == Math.Floor(k) ? $"{(long)k}k" : $"{k:G3}k";
            }
            return $"{val:N0}";
        }

        /// <summary>
        /// Formats a duration in seconds as "m:ss".
        /// </summary>
        /// <summary>
        /// Broadcasts an [Invasion] message to all online players who have not opted out
        /// via /ilt invasion off.
        /// </summary>
        public static void BroadcastInvasion(string message)
        {
            var packet = new GameMessageSystemChat(message, ChatMessageType.Broadcast);
            foreach (var player in PlayerManager.GetAllOnline())
            {
                // Default ON: absent property or explicit true = show; false = hidden
                if (player.GetProperty(PropertyBool.ShowInvasionMessages) != false)
                    player.Session?.Network.EnqueueSend(packet);
            }
        }

        public static string FormatMmSs(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }

        /// <summary>Formats a duration as h:mm:ss (e.g. 2:28:01).</summary>
        public static string FormatHms(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        // ----------------------------------------------------------------
        // Threshold parsing
        // ----------------------------------------------------------------

        public static bool TryParseThreshold(string input, out long result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim().ToLower().Replace("_", "").Replace(",", "");

            long multiplier = 1;
            if (input.EndsWith("k"))
            {
                multiplier = 1000;
                input = input.Substring(0, input.Length - 1);
            }
            else if (input.EndsWith("m"))
            {
                multiplier = 1000000;
                input = input.Substring(0, input.Length - 1);
            }
            else if (input.EndsWith("b"))
            {
                multiplier = 1000000000;
                input = input.Substring(0, input.Length - 1);
            }

            if (double.TryParse(input, out double parsedVal))
            {
                result = (long)(parsedVal * multiplier);
                return true;
            }

            return false;
        }
    }
}
