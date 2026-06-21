using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Factories;
using Position = ACE.Entity.Position;

namespace ACE.Server.Managers
{
    public static class InvasionManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object _lock = new object();

        public static readonly List<string> Towns = new()
        {
            "Al-Arqas", "Al-Jalima", "Arwic", "Baishi", "Cragstone",
            "Eastham", "Glenden Wood", "Hebian-to", "Holtburg", "Kara",
            "Khayyaban", "Lytelthorpe", "Mayoi", "Nanto", "Neydisa",
            "Rithwic", "Samsur", "Sawato", "Shoushi", "Stonehold",
            "Tufa", "Uziz", "Yanshi", "Yaraq", "Zaikhal"
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
            { "Al-Arqas",    new Position(2404909115, 183.851f,  60.183f,  11.326f, 1f, 0f, 0f, 0f) },
            { "Al-Jalima",   new Position(2240282668, 120.359f,  95.47f,   92.049f, 1f, 0f, 0f, 0f) },
            { "Arwic",       new Position(3332964361,  46.805f,   4.219f,  44.005f, 1f, 0f, 0f, 0f) },
            { "Baishi",      new Position(3460366343,  12.6f,   152.8f,   57.1f,   1f, 0f, 0f, 0f) },
            { "Cragstone",   new Position(3147759680, 169.358f, 168.251f,  56.005f, 1f, 0f, 0f, 0f) },
            { "Eastham",     new Position(3465805877, 151.053f, 112.61f,   19.417f, 1f, 0f, 0f, 0f) },
            { "Glenden Wood",new Position(2695102501,  96.302f, 119.847f,  61.955f, 1f, 0f, 0f, 0f) },
            { "Hebian-to",   new Position(3863871535, 138.304f, 161.905f,  22.04f,  1f, 0f, 0f, 0f) },
            { "Holtburg",    new Position(2847146009,  84f,       7.1f,    96f,     1f, 0f, 0f, 0f) },
            { "Kara",        new Position(3122069531, 85.39f,   59.64f,  132.00f,  -0.81291f, 0f, 0f, -0.58239f) },
            { "Khayyaban",   new Position(2672033810,  90f,      24.553f,  33.885f, 1f, 0f, 0f, 0f) },
            { "Lytelthorpe", new Position(3229614087,  11.723f, 155.56f,   35.028f, 1f, 0f, 0f, 0f) },
            { "Mayoi",       new Position(3862036513, 107.417f,  10.763f,  31.908f, 1f, 0f, 0f, 0f) },
            { "Nanto",       new Position(3862822946,  96.96f,   37.722f,  76.542f, 1f, 0f, 0f, 0f) },
            { "Neydisa",     new Position(2513829939, 146.9f,    71.3f,   101.8f,   1f, 0f, 0f, 0f) },
            { "Rithwic",     new Position(3381395496, 113.666f, 190.259f,  24.005f, 1f, 0f, 0f, 0f) },
            { "Samsur",      new Position(2541420556,  25.811f,  73.853f,   2.005f, 1f, 0f, 0f, 0f) },
            { "Sawato",      new Position(3378184193,  14.8f,     0.3f,    14f,     1f, 0f, 0f, 0f) },
            { "Shoushi",     new Position(3663003677,  84.8f,    99f,      22f,     1f, 0f, 0f, 0f) },
            { "Stonehold",   new Position(1691680779,  30f,      50f,      80f,     1f, 0f, 0f, 0f) },
            { "Tufa",        new Position(2272002056,   2f,     186.9f,    20f,     1f, 0f, 0f, 0f) },
            { "Uziz",        new Position(2724200508, 182.919f,  87.934f,  22.005f, 1f, 0f, 0f, 0f) },
            { "Yanshi",      new Position(3027173406,  75.2f,   124.1f,    36.69f,  1f, 0f, 0f, 0f) },
            { "Yaraq",       new Position(2103705613,  31.9f,   104.6f,    13.9f,   1f, 0f, 0f, 0f) },
            { "Zaikhal",     new Position(2156920851,  64.863f,  55.687f, 126.005f, 1f, 0f, 0f, 0f) },
        };

        // State variables
        public static bool IsActive { get; private set; } = false;
        public static string ActiveTown { get; private set; }
        public static string ActiveSpecies { get; private set; }
        public static WorldObject ActiveGenerator { get; private set; }
        public static Creature ActiveBoss { get; set; } // Set or read when boss is active
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

        public static double CooldownTime
        {
            get => ServerConfig.invasion_cooldown.Value;
            set => ServerConfig.SetValue("invasion_cooldown", value);
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
                caster.Session?.Network.EnqueueSend(new GameMessageSystemChat("[Invasion] You have met the healing requirement and are now eligible for the reward portal!", ChatMessageType.System));
            }
        }

        public static bool IsEligible(Player player)
        {
            if (player == null) return false;

            // Developer bypass
            if (player.Session?.AccessLevel >= AccessLevel.Developer)
                return true;

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
        // Start / Stop
        // ----------------------------------------------------------------

        public static bool StartInvasion(string town, string species)
        {
            lock (_lock)
            {
                if (IsActive) return false;

                // Normalize town name casing from user/input matching
                var normalizedTown = Towns.FirstOrDefault(t => t.Equals(town, StringComparison.OrdinalIgnoreCase));
                var normalizedSpecies = SpeciesList.FirstOrDefault(s => s.Equals(species, StringComparison.OrdinalIgnoreCase));

                if (normalizedTown == null || normalizedSpecies == null)
                    return false;

                var eventName = $"Invasion_{normalizedTown}_{normalizedSpecies}";
                if (!EventManager.IsEventAvailable(eventName))
                {
                    log.Error($"Invasion event '{eventName}' is not registered in the database!");
                    return false;
                }

                // Reset states
                PlayerDamageTracker.Clear();
                PlayerHealingTracker.Clear();
                ActiveBoss = null;
                ActiveGenerator = null;
                KillCount = 0;

                ActiveTown = normalizedTown;
                ActiveSpecies = normalizedSpecies;
                IsActive = true;
                InvasionStartTime = Time.GetUnixTime();

                // Start event to enable spawner generators (minion mobs) — disabled until SpawnMinions is true
                if (SpawnMinions)
                    EventManager.StartEvent(eventName, null, null);

                // Broadcast start message
                var announcement = $"[Invasion] A {normalizedSpecies} invasion has started in the town of {normalizedTown}!";
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(announcement, ChatMessageType.Broadcast));

                // Directly spawn the boss at the known town position (no generator dependency)
                SpawnBoss();

                return true;
            }
        }

        public static void StopInvasion(bool success)
        {
            lock (_lock)
            {
                if (!IsActive) return;

                var eventName = $"Invasion_{ActiveTown}_{ActiveSpecies}";
                if (SpawnMinions)
                    EventManager.StopEvent(eventName, null, null);

                if (ActiveBoss != null && ActiveBoss.IsAlive)
                {
                    ActiveBoss.Destroy();
                }
                ActiveBoss = null;
                ActiveGenerator = null;

                IsActive = false;
                NextInvasionTime = Time.GetUnixTime() + CooldownTime;

                if (success)
                {
                    var announcement = $"[Invasion] The town of {ActiveTown} has successfully repelled the {ActiveSpecies} invasion!";
                    PlayerManager.BroadcastToAll(new GameMessageSystemChat(announcement, ChatMessageType.Broadcast));
                }
            }
        }

        public static void FailInvasion()
        {
            lock (_lock)
            {
                if (!IsActive) return;

                var eventName = $"Invasion_{ActiveTown}_{ActiveSpecies}";
                if (SpawnMinions)
                    EventManager.StopEvent(eventName, null, null);

                if (ActiveBoss != null && ActiveBoss.IsAlive)
                {
                    ActiveBoss.Destroy();
                }
                ActiveBoss = null;
                ActiveGenerator = null;

                var announcement = $"[Invasion] The town of {ActiveTown} has fallen!";
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(announcement, ChatMessageType.Broadcast));

                IsActive = false;
                NextInvasionTime = Time.GetUnixTime() + CooldownTime;
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

                bool isBossDeath = creature == ActiveBoss;

                // For non-boss creatures: only track if they died near the active town
                if (!isBossDeath)
                {
                    // Check proximity to the active town boss spawn position
                    if (!TownBossPositions.TryGetValue(ActiveTown, out var townPos))
                        return;

                    if (creature.Location == null)
                        return;

                    if (creature.Location.DistanceTo(townPos) > InvasionKillRadius)
                        return;
                }

                // Accumulate damage contributions from all players who hit this creature
                foreach (var damager in creature.DamageHistory.Damagers)
                {
                    var attackerObj = damager.TryGetPetOwnerOrAttacker();
                    if (attackerObj is Player player)
                    {
                        AddDamage(player, (long)damager.TotalDamage);
                    }
                }

                KillCount++;

                if (isBossDeath)
                {
                    HandleBossDeath();
                }
            }
        }

        // ----------------------------------------------------------------
        // Boss spawn / death
        // ----------------------------------------------------------------

        private static void SpawnBoss()
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
                spawnPos = hardcoded;
            }
            else
            {
                log.Error($"[Invasion] Cannot spawn boss for '{ActiveTown}': no generator registered and no hardcoded position found.");
                return;
            }

            // Spawn boss (Tyrant Darkspire Golem - WCID 71600033) at the town center
            var boss = WorldObjectFactory.CreateNewWorldObject(71600033) as Creature;
            if (boss == null)
            {
                log.Error("[Invasion] Failed to create boss object WCID 71600033");
                return;
            }

            boss.Location = spawnPos;
            ActiveBoss = boss;
            BossSpawnTime = Time.GetUnixTime();

            boss.EnterWorld();

            var announcement = $"[Invasion] The invasion boss, {boss.Name}, has spawned in the center of {ActiveTown}!";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(announcement, ChatMessageType.Broadcast));
            log.Info($"[Invasion] Boss '{boss.Name}' spawned at {spawnPos} for {ActiveTown}");
        }

        private static void HandleBossDeath()
        {
            // Determine reward portal spawn position
            Position portalPos;
            if (ActiveGenerator?.Location != null)
                portalPos = new Position(ActiveGenerator.Location);
            else if (TownBossPositions.TryGetValue(ActiveTown, out var hardcoded))
                portalPos = hardcoded;
            else
                portalPos = null;

            if (portalPos != null)
            {
                // Spawn reward portal (WCID 694200296) at the town center
                var portal = WorldObjectFactory.CreateNewWorldObject(694200296) as Portal;
                if (portal != null)
                {
                    portal.Location = portalPos;
                    portal.Lifespan = 300; // 5 minutes
                    portal.CreationTimestamp = (int)Time.GetUnixTime();
                    portal.EnterWorld();
                }
            }
            else
            {
                log.Error("[Invasion] Cannot spawn reward portal: no position available.");
            }

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

            lock (_lock)
            {
                // Apply startup grace period on first tick.
                if (!_startupDelayInitialized)
                {
                    NextInvasionTime = now + StartupGracePeriod;
                    _startupDelayInitialized = true;
                    log.Info($"[Invasion] Startup grace period active — first auto-invasion not before {StartupGracePeriod}s from now.");
                }

                if (!IsActive)
                {
                    if (Enabled && now >= NextInvasionTime)
                    {
                        TriggerRandomInvasion();
                    }
                    return;
                }

                // Invasion is active. Handle proximity check.
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
        public static string FormatMmSs(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
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
