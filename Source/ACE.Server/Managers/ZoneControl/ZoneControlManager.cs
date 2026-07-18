using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Owns controlled ZONES: named landblock sets, each governing one world Variation, with a per-zone on/off
    /// toggle and a stat profile. A monster is governed when it stands on a zone's landblock AND its variation
    /// equals the zone's Variation. No prestige/tier/boss concepts — one DEFAULT stat set for all monsters in
    /// the zone, plus optional per-monster (WCID) overrides.
    ///
    /// Reuses the <see cref="ZoneScaling"/> models (<see cref="ZoneScalingProfile"/>, stat curves) purely as the
    /// stat payload; the "default variant" is the profile's minion slot (the boss slot is unused post-decouple).
    /// Consumers call <see cref="ResolveForCreature"/> and get a nullable <see cref="EvaluatedProfile"/>.
    /// </summary>
    public static class ZoneControlManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const string StoreKey = "zonecontrol_data";

        /// <summary>Variant instances begin at variation 11 (0 = normal world, 1..10 = retail layers).
        /// Bounded zones (player boundaries) are only allowed at variant instances — Zone Control's own
        /// constant, independent of any other system's tiering.</summary>
        public const int MinBoundedVariation = 11;

        // zone name (case-insensitive) -> zone
        private static readonly Dictionary<string, ControlledArea> _areas = new(StringComparer.OrdinalIgnoreCase);
        // RUNTIME zones (e.g. rift runs): live in the lock-free snapshot like any enabled zone, but are NEVER
        // persisted to the shard store and stay out of the admin display index / arealist.
        private static readonly Dictionary<string, ControlledArea> _runtimeAreas = new(StringComparer.OrdinalIgnoreCase);
        // landblock -> zones covering it (ALL zones incl. disabled; used by the locked display/diagnostic paths)
        private static readonly Dictionary<ushort, List<ControlledArea>> _areasByLandblock = new();
        // memo of evaluated bundles for the DISPLAY path (EvaluateForDisplay), keyed "zoneName|default"/"zoneName|w<wcid>"
        private static readonly Dictionary<string, EvaluatedProfile> _evalCache = new();

        // ── Lock-free read snapshot ──
        // The hot combat/effect resolve paths read this immutable snapshot with NO lock. It is rebuilt (copy-on-write)
        // under _lock on every mutation and atomically swapped in. Readers may briefly observe the previous snapshot
        // after an edit (same eventual-consistency the ~2s plugin sync already has) — never a torn/partial state.
        private sealed class ZoneRef
        {
            public string Name;
            public int Variation;
            public int LandblockCount;                       // for most-specific tie-break
            public EvaluatedProfile Default;                 // precomputed default stat set
            public Dictionary<uint, EvaluatedProfile> Wcid;  // precomputed per-WCID overrides
            public ZoneEffects Effects;                      // immutable copy (readers never touch the live zone)
        }

        private sealed class Snapshot
        {
            public readonly HashSet<ushort> EnabledLandblocks;
            public readonly Dictionary<ushort, List<ZoneRef>> ByLandblock;   // ENABLED zones only
            // Player-boundary allowlists: variation -> union of landblocks of all ENABLED+BOUNDED zones at that
            // variation. A variation with no entry has no Zone Control boundary (free roam / legacy fallback).
            public readonly Dictionary<int, HashSet<ushort>> BoundedLandblocksByVariation;
            public Snapshot(HashSet<ushort> enabled, Dictionary<ushort, List<ZoneRef>> byLb,
                Dictionary<int, HashSet<ushort>> boundedByVar)
            {
                EnabledLandblocks = enabled;
                ByLandblock = byLb;
                BoundedLandblocksByVariation = boundedByVar;
            }
            public static readonly Snapshot Empty =
                new Snapshot(new HashSet<ushort>(), new Dictionary<ushort, List<ZoneRef>>(),
                    new Dictionary<int, HashSet<ushort>>());
        }

        private static volatile Snapshot _snapshot = Snapshot.Empty;

        private static readonly object _lock = new object();
        private static volatile bool _initialized;

        private class Store
        {
            public List<ControlledArea> Areas { get; set; } = new();
        }

        #region init / persistence

        /// <summary>Public init hook for boot-time callers outside the command surface (e.g. landblock load
        /// deriving its boundary perimeter): guarantees the shard store has been loaded and the lock-free
        /// snapshot published. Cheap after the first call (volatile bool check).</summary>
        public static void EnsureLoaded() => EnsureInitialized();

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_lock)
            {
                if (_initialized)
                    return;

                try { Load(); }
                catch (Exception ex) { log.Error($"ZoneControlManager: failed to load store, starting empty. {ex}"); }

                _initialized = true;
            }
        }

        private static void Load()
        {
            _areas.Clear();
            _evalCache.Clear();

            string json = null;
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                json = DatabaseManager.ShardConfig.GetString(StoreKey)?.Value;

            var store = string.IsNullOrWhiteSpace(json)
                ? new Store()
                : (JsonConvert.DeserializeObject<Store>(json) ?? new Store());

            foreach (var a in store.Areas)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Name)) continue;
                a.Landblocks ??= new HashSet<ushort>();
                a.TerrainOverrides ??= new Dictionary<ushort, string>();
                a.Profile ??= new ZoneScalingProfile();
                a.Effects ??= new ZoneEffects();
                _areas[a.Name] = a;
            }

            RebuildIndexes();
        }

        private static void Save()
        {
            var store = new Store { Areas = _areas.Values.ToList() };
            var jsonOut = JsonConvert.SerializeObject(store);
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                DatabaseManager.ShardConfig.SaveString(new ConfigPropertiesString { Key = StoreKey, Value = jsonOut, Description = "Zone Control store (JSON)" });
            else
                DatabaseManager.ShardConfig.AddString(StoreKey, jsonOut, "Zone Control store (JSON)");
            _evalCache.Clear();
            RebuildIndexes();
        }

        /// <summary>Called under _lock after any load/mutation. Rebuilds the locked display index AND the immutable
        /// lock-free read snapshot, then atomically publishes the snapshot.</summary>
        private static void RebuildIndexes()
        {
            // (1) Display index: every zone (incl. disabled), holding live ControlledArea refs (locked readers only).
            _areasByLandblock.Clear();
            foreach (var area in _areas.Values)
                foreach (var lb in area.Landblocks)
                {
                    if (!_areasByLandblock.TryGetValue(lb, out var list))
                        _areasByLandblock[lb] = list = new List<ControlledArea>();
                    list.Add(area);
                }

            // (2) Lock-free snapshot: ENABLED zones only, fully precomputed + copied so readers touch nothing mutable.
            var enabledLbs = new HashSet<ushort>();
            var byLb = new Dictionary<ushort, List<ZoneRef>>();
            var boundedByVar = new Dictionary<int, HashSet<ushort>>();
            foreach (var area in _areas.Values)
            {
                if (!area.Enabled)
                    continue;

                var zr = BuildZoneRef(area);
                foreach (var lb in area.Landblocks)
                {
                    enabledLbs.Add(lb);
                    if (!byLb.TryGetValue(lb, out var list))
                        byLb[lb] = list = new List<ZoneRef>();
                    list.Add(zr);
                }

                // Boundary allowlist: union the landblocks of every enabled+bounded zone per variation.
                // Runtime zones (rifts) are deliberately excluded — they never bound players.
                if (area.Bounded)
                {
                    if (!boundedByVar.TryGetValue(area.Variation, out var set))
                        boundedByVar[area.Variation] = set = new HashSet<ushort>();
                    set.UnionWith(area.Landblocks);
                }
            }

            // (3) Runtime zones (never persisted, not in the display index): same snapshot treatment as (2).
            foreach (var area in _runtimeAreas.Values)
            {
                if (!area.Enabled)
                    continue;

                var zr = BuildZoneRef(area);
                foreach (var lb in area.Landblocks)
                {
                    enabledLbs.Add(lb);
                    if (!byLb.TryGetValue(lb, out var list))
                        byLb[lb] = list = new List<ZoneRef>();
                    list.Add(zr);
                }
            }

            var previous = _snapshot;
            _snapshot = new Snapshot(enabledLbs, byLb, boundedByVar); // volatile publish

            // Boundary perimeter upkeep: markers spawn at landblock load, so when a mutation changes any
            // variation's bounded union, already-loaded landblocks at that variation must re-derive their
            // lantern perimeter. Refresh only enqueues per-landblock actions (no locks taken), so calling
            // it here under _lock is safe.
            foreach (var v in previous.BoundedLandblocksByVariation.Keys.Union(boundedByVar.Keys))
            {
                previous.BoundedLandblocksByVariation.TryGetValue(v, out var before);
                boundedByVar.TryGetValue(v, out var after);
                var changed = before == null ? after != null : (after == null || !before.SetEquals(after));
                if (changed)
                    LandblockManager.EnqueueRefreshLoadedZoneBoundaryMarkers(v);
            }
        }

        /// <summary>Register (or replace) a RUNTIME zone: participates in lock-free resolution exactly like an
        /// enabled saved zone, but is never written to the shard store and never appears in admin listings.
        /// Used by transient systems (rift runs). Caller must remove it again via <see cref="RemoveRuntimeZone"/>.</summary>
        public static void RegisterRuntimeZone(ControlledArea area)
        {
            if (area == null || string.IsNullOrWhiteSpace(area.Name))
                return;

            EnsureInitialized();
            lock (_lock)
            {
                area.Landblocks ??= new HashSet<ushort>();
                area.Profile ??= new ZoneScalingProfile();
                area.Effects ??= new ZoneEffects();
                area.Bounded = false; // runtime zones (rift runs) never bound players
                _runtimeAreas[area.Name] = area;
                RebuildIndexes();
            }
        }

        /// <summary>Remove a runtime zone registered via <see cref="RegisterRuntimeZone"/>. No-op if absent.</summary>
        public static void RemoveRuntimeZone(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            EnsureInitialized();
            lock (_lock)
            {
                if (_runtimeAreas.Remove(name))
                    RebuildIndexes();
            }
        }

        /// <summary>Build the immutable per-zone read record: precompute the default + per-WCID evaluated profiles
        /// and copy the effects, so lock-free readers never dereference the live (mutable) zone object.</summary>
        private static ZoneRef BuildZoneRef(ControlledArea area)
        {
            var wcid = new Dictionary<uint, EvaluatedProfile>();
            foreach (var kv in area.Profile.WcidOverrides)
                wcid[kv.Key] = EvaluateVariant(area.Name, kv.Value);

            return new ZoneRef
            {
                Name = area.Name,
                Variation = area.Variation,
                LandblockCount = area.Landblocks.Count,
                Default = EvaluateVariant(area.Name, area.Profile.Variant(ZoneVariant.Minion)),
                Wcid = wcid,
                Effects = CopyEffects(area.Effects),
            };
        }

        /// <summary>Flatten a variant profile's stat curves to an immutable EvaluatedProfile (flat: tier 1 = base).
        /// Body-part overrides and prop stamps are deep-COPIED so lock-free readers never touch the live
        /// (admin-mutable) dictionaries.</summary>
        private static EvaluatedProfile EvaluateVariant(string zoneName, ZoneVariantProfile variantProfile)
        {
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, ZoneBodyPart> bodyParts = null;
            Dictionary<int, long> propInts = null, propInt64s = null;
            Dictionary<int, double> propFloats = null;
            Dictionary<int, bool> propBools = null;
            List<int> customCantrips = null;
            List<ZoneCurrencyDrop> currencyDrops = null;

            if (variantProfile != null)
            {
                foreach (var kvp in variantProfile.Stats)
                    values[kvp.Key] = kvp.Value.Evaluate(1);

                if (variantProfile.BodyParts is { Count: > 0 })
                {
                    bodyParts = new Dictionary<int, ZoneBodyPart>(variantProfile.BodyParts.Count);
                    foreach (var kvp in variantProfile.BodyParts)
                        if (kvp.Value != null && !kvp.Value.IsEmpty)
                            bodyParts[kvp.Key] = kvp.Value.Clone();
                }

                if (variantProfile.PropInts is { Count: > 0 }) propInts = new Dictionary<int, long>(variantProfile.PropInts);
                if (variantProfile.PropInt64s is { Count: > 0 }) propInt64s = new Dictionary<int, long>(variantProfile.PropInt64s);
                if (variantProfile.PropFloats is { Count: > 0 }) propFloats = new Dictionary<int, double>(variantProfile.PropFloats);
                if (variantProfile.PropBools is { Count: > 0 }) propBools = new Dictionary<int, bool>(variantProfile.PropBools);
                if (variantProfile.CustomCantrips is { Count: > 0 }) customCantrips = new List<int>(variantProfile.CustomCantrips);

                if (variantProfile.CurrencyDrops is { Count: > 0 })
                {
                    currencyDrops = new List<ZoneCurrencyDrop>(variantProfile.CurrencyDrops.Count);
                    foreach (var d in variantProfile.CurrencyDrops)
                        if (d != null && d.Wcid != 0)
                            currencyDrops.Add(d.Clone());
                }
            }

            return new EvaluatedProfile(zoneName, 1, ZoneVariant.Minion, values, bodyParts, propInts, propInt64s, propFloats, propBools, customCantrips, currencyDrops);
        }

        private static ZoneEffects CopyEffects(ZoneEffects e)
        {
            if (e == null) return new ZoneEffects();
            return new ZoneEffects
            {
                DotEnabled = e.DotEnabled,
                DotDamage = e.DotDamage,
                DotPercent = e.DotPercent,
                DotIntervalSeconds = e.DotIntervalSeconds,
                DotDamageType = e.DotDamageType,
                SlowEnabled = e.SlowEnabled,
                SlowPercent = e.SlowPercent,
                CharmEnabled = e.CharmEnabled,
            };
        }

        #endregion

        #region resolution / public API

        /// <summary>
        /// Zone Control's own effective-variation lookup (standalone — no other manager consulted): the world
        /// object's real Location variation, except that a retail-side object carrying ForceEndgameSystems is
        /// treated as standing at its EndgameForcedVariation (or the first variant instance when unset), so a
        /// test dummy in the normal world can still be governed by a variant zone.
        /// </summary>
        public static int GetEffectiveVariation(WorldObject wo)
        {
            var real = wo?.Location?.Variation ?? 0;
            if (real >= MinBoundedVariation)
                return real;

            if (wo?.GetProperty(PropertyBool.ForceEndgameSystems) == true)
            {
                var forced = wo.GetProperty(PropertyInt.EndgameForcedVariation) ?? 0;
                return forced >= MinBoundedVariation ? forced : MinBoundedVariation;
            }

            return real;
        }

        /// <summary>
        /// Resolves the winning zone for a creature and evaluates its stat profile. Returns null when the
        /// creature should NOT be zone-controlled: it's a player, it's exempt, no enabled zone covers its
        /// landblock, or no covering zone's Variation matches the creature's current variation.
        /// </summary>
        public static EvaluatedProfile ResolveForCreature(Creature creature)
        {
            if (creature == null || creature is Player)
                return null;

            if (creature.GetProperty(PropertyBool.ExemptFromZoneScaling) == true)
                return null;

            // Fully lock-free: read the immutable published snapshot (no _lock, no EnsureInitialized on the hot path).
            var snap = _snapshot;
            var landblock = creature.Location?.LandblockId.Landblock ?? 0;

            // Hot-path fast bail: most monsters are in landblocks with no enabled zone (O(1), no lock).
            if (!snap.EnabledLandblocks.Contains(landblock) || !snap.ByLandblock.TryGetValue(landblock, out var list))
                return null;

            var effVar = GetEffectiveVariation(creature);

            ZoneRef best = null;
            foreach (var zr in list)
            {
                if (zr.Variation != effVar)
                    continue;
                // most-specific wins: fewer landblocks (a one-block dungeon beats a multi-block region)
                if (best == null || zr.LandblockCount < best.LandblockCount)
                    best = zr;
            }

            if (best == null)
                return null;

            return best.Wcid.TryGetValue(creature.WeenieClassId, out var wp) ? wp : best.Default;
        }

        /// <summary>
        /// Resolves the winning zone's player EFFECTS for a player standing somewhere. Mirrors
        /// <see cref="ResolveForCreature"/>'s landblock + variation gating, but: (a) it's FOR players, and
        /// (b) it only considers zones whose <see cref="ZoneEffects.AnyActive"/> is true, so a stat-only zone
        /// never wins effect resolution. Returns null when no enabled, effect-authoring zone covers the player.
        /// Hot-path safe: bails on the lockless enabled-landblock set before touching the lock.
        /// </summary>
        public static ZoneEffects ResolveEffectsForPlayer(Player player)
        {
            if (player == null)
                return null;

            // Fully lock-free: read the immutable published snapshot.
            var snap = _snapshot;
            var landblock = player.Location?.LandblockId.Landblock ?? 0;

            // Hot-path fast bail: most players are in landblocks with no enabled zone (O(1), no lock).
            if (!snap.EnabledLandblocks.Contains(landblock) || !snap.ByLandblock.TryGetValue(landblock, out var list))
                return null;

            // Use the same effective-variation the monster resolver + here-readout use, so a zone that governs
            // monsters at variation N also applies effects at variation N (no split-brain).
            var effVar = GetEffectiveVariation(player);

            ZoneRef best = null;
            foreach (var zr in list)
            {
                if (zr.Variation != effVar)
                    continue;
                if (zr.Effects == null || !zr.Effects.AnyActive)
                    continue;
                // most-specific wins: fewer landblocks (a one-block dungeon beats a multi-block region)
                if (best == null || zr.LandblockCount < best.LandblockCount)
                    best = zr;
            }

            return best?.Effects;
        }

        /// <summary>True when at least one enabled BOUNDED zone exists at this variation. Lock-free; safe on
        /// hot per-player paths. When true, <see cref="IsLandblockAllowed"/> is Zone Control's boundary
        /// authority for the variation (enforced by the player tick's CheckZoneBoundary — standalone,
        /// independent of any other boundary system).</summary>
        public static bool HasBoundedZonesAt(int? variation)
        {
            if (!variation.HasValue)
                return false;
            return _snapshot.BoundedLandblocksByVariation.ContainsKey(variation.Value);
        }

        /// <summary>Player-boundary allowlist check: a landblock is allowed at a variation when no bounded zone
        /// exists there (free roam), or it belongs to any enabled bounded zone at that variation (union).
        /// Lock-free snapshot read.</summary>
        public static bool IsLandblockAllowed(int? variation, ushort landblock)
        {
            if (!variation.HasValue)
                return true;
            if (!_snapshot.BoundedLandblocksByVariation.TryGetValue(variation.Value, out var allowed))
                return true;
            return allowed.Contains(landblock);
        }

        /// <summary>Names of enabled bounded zones at a variation (for command echoes / the plugin's
        /// shared-travel-space line). Locked display path — human-paced callers only.</summary>
        public static List<string> BoundedZoneNamesAt(int variation)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _areas.Values
                    .Where(a => a.Enabled && a.Bounded && a.Variation == variation)
                    .Select(a => a.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>Evaluates a zone's profile: the default stat set, or a per-WCID override if one exists for
        /// this creature (a full replacement, not layered). Stats are flat (no tier curve).</summary>
        private static EvaluatedProfile Evaluate(ControlledArea area, uint? wcid = null)
        {
            var hasWcidOverride = wcid.HasValue && area.Profile.WcidOverrides.ContainsKey(wcid.Value);
            var cacheKey = area.Name + "|" + (hasWcidOverride ? "w" + wcid.Value : "default");

            lock (_lock)
            {
                if (_evalCache.TryGetValue(cacheKey, out var cached))
                    return cached;

                var variantProfile = hasWcidOverride
                    ? area.Profile.WcidOverrides[wcid.Value]
                    : area.Profile.Variant(ZoneVariant.Minion); // minion slot = the zone's DEFAULT stat set

                var eval = EvaluateVariant(area.Name, variantProfile);
                _evalCache[cacheKey] = eval;
                return eval;
            }
        }

        /// <summary>Evaluate a zone's profile for display/inspection, ignoring the enabled flag. Null if no such zone.</summary>
        public static EvaluatedProfile EvaluateForDisplay(string name, uint? wcid = null)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var area = FindArea(name);
                return area != null ? Evaluate(area, wcid) : null;
            }
        }

        /// <summary>
        /// The zone that ACTUALLY governs a spot: enabled, its Variation matches <paramref name="variation"/>, and
        /// most-specific (fewest landblocks) among the candidates — the same rule <see cref="ResolveForCreature"/>
        /// uses. Returns null if nothing governs here. Use for the GUI "governed by" readout so it reflects the real
        /// winner rather than just any covering zone.
        /// </summary>
        public static ControlledArea ResolveWinnerForLocation(ushort landblock, int variation)
        {
            EnsureInitialized();
            lock (_lock)
            {
                ControlledArea best = null;
                if (_areasByLandblock.TryGetValue(landblock, out var list))
                {
                    foreach (var area in list)
                    {
                        if (!area.Enabled || area.Variation != variation)
                            continue;
                        if (best == null || area.Landblocks.Count < best.Landblocks.Count)
                            best = area;
                    }
                }
                return best;
            }
        }

        /// <summary>Zones whose landblock set contains <paramref name="landblock"/> (for "here"/diagnostics).</summary>
        public static IReadOnlyList<ControlledArea> AreasCovering(ushort landblock)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _areasByLandblock.TryGetValue(landblock, out var list) ? list.ToList() : new List<ControlledArea>();
            }
        }

        #endregion

        #region mutation

        /// <summary>Zone lookup: case-insensitive, and accepts underscores in place of spaces so a typed
        /// my_zone finds "My Zone" (names may contain spaces; commands without quotes can't). Call under _lock.</summary>
        private static ControlledArea FindArea(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (_areas.TryGetValue(name, out var a))
                return a;
            if (name.IndexOf('_') >= 0 && _areas.TryGetValue(name.Replace('_', ' '), out a))
                return a;
            return null;
        }

        public static void UpsertArea(ControlledArea area)
        {
            EnsureInitialized();
            lock (_lock)
            {
                area.Landblocks ??= new HashSet<ushort>();
                area.Profile ??= new ZoneScalingProfile();
                area.Effects ??= new ZoneEffects();
                _areas[area.Name] = area;
                Save();
            }
        }

        /// <summary>
        /// Atomically read-modify-write a zone: runs <paramref name="mutate"/> on the live zone object while
        /// holding the manager lock, then persists. Use this for any change that mutates the zone's Profile or
        /// Effects, so two admins editing the same zone at once can't race on the underlying dictionaries.
        /// The mutate callback must only touch the passed <see cref="ControlledArea"/> (no re-entrant manager
        /// calls). Returns false if the zone doesn't exist.
        /// </summary>
        public static bool MutateArea(string name, Action<ControlledArea> mutate)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null)
                    return false;
                a.Landblocks ??= new HashSet<ushort>();
                a.Profile ??= new ZoneScalingProfile();
                a.Effects ??= new ZoneEffects();
                mutate(a);
                Save();
                return true;
            }
        }

        public static bool RemoveArea(string name)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                _areas.Remove(a.Name);
                Save();
                return true;
            }
        }

        /// <summary>Rename a zone. Returns false if the old name is missing or the new name is taken by a
        /// DIFFERENT zone (renaming a zone to a different casing of its own name is allowed).</summary>
        public static bool RenameArea(string oldName, string newName)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(newName))
                    return false;
                var area = FindArea(oldName);
                if (area == null)
                    return false;
                if (_areas.TryGetValue(newName, out var clash) && !ReferenceEquals(clash, area))
                    return false;
                _areas.Remove(area.Name);
                area.Name = newName;
                _areas[newName] = area;
                Save();
                return true;
            }
        }

        public static bool SetEnabled(string name, bool enabled)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                a.Enabled = enabled;
                Save();
                return true;
            }
        }

        public static bool SetBounded(string name, bool bounded)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                a.Bounded = bounded;
                Save();
                return true;
            }
        }

        public static bool SetVariation(string name, int variation)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                a.Variation = variation;
                Save();
                return true;
            }
        }

        /// <summary>Set (tag non-null/non-empty) or clear (tag null/empty) a manual terrain override for one
        /// landblock. Returns false only when the zone doesn't exist. Display-only — no snapshot rebuild needed,
        /// but Save() persists it to the shard store so it survives restarts and shows on every client.</summary>
        public static bool SetTerrainOverride(string name, ushort landblock, string tag)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                a.TerrainOverrides ??= new Dictionary<ushort, string>();
                if (string.IsNullOrEmpty(tag))
                    a.TerrainOverrides.Remove(landblock);
                else
                    a.TerrainOverrides[landblock] = tag;
                Save();
                return true;
            }
        }

        public static bool AddLandblock(string name, ushort landblock)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                a.Landblocks.Add(landblock);
                Save();
                return true;
            }
        }

        public static bool RemoveLandblock(string name, ushort landblock)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var a = FindArea(name);
                if (a == null) return false;
                var changed = a.Landblocks.Remove(landblock);
                if (changed) Save();
                return changed;
            }
        }

        public static ControlledArea GetArea(string name)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return FindArea(name);
            }
        }

        public static IReadOnlyList<ControlledArea> ListAreas()
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _areas.Values.ToList();
            }
        }

        /// <summary>Distinct Creature WCIDs spawnable in a zone's landblocks at its variation (for the plugin's
        /// per-monster override dropdown).</summary>
        public static List<(uint Wcid, string Name, bool IsMonster)> GetAreaMobs(string name)
        {
            EnsureInitialized();
            ControlledArea area;
            lock (_lock)
            {
                area = FindArea(name);
                if (area == null)
                    return new List<(uint, string, bool)>();
            }
            return GetLandblockMobs(area.Landblocks, area.Variation);
        }

        /// <summary>Force a reload from the shard store (e.g. after out-of-band edits).</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _initialized = false;
                _questRows = null;   // quest registry re-reads from ace_world on next pull
                EnsureInitialized();
            }
        }

        // ── Quest registry (plugin "Quests" tab) ──
        // Authored rows live in ace_world.zonecontrol_quest (one row per quest; each content wave's SQL
        // artifact appends its own rows). The registry is display data only — stamps/emotes/KillQuest props
        // are the real quest machinery. NPC coords are resolved from landblock_instance by npc_wcid so
        // moving an NPC never stales the tab, and "wired" flags rows whose stamp or NPC is missing.

        public sealed class ZoneQuestRow
        {
            public string Zone;
            public string QuestKey;       // counter stamp; empty = planned-only row
            public string CompletedKey;   // cooldown stamp
            public string Title;
            public string Category;       // kill | collect | story | boss | event
            public string Wave;           // plan key (B1, A3, ...) for grouping/sorting
            public string NpcName;
            public uint NpcWcid;
            public string Objective;
            public string Targets;        // '~'-separated display list
            public int Count;
            public int RepeatHours;
            public string Reward;
            public string Stage;          // planned | testing | live
            public int SortOrder;
            // resolved at load:
            public string LandblockHex = "";   // NPC's landblock, "F659"
            public string Coords = "";         // NPC map coords, "30.3S, 94.9E"
            public bool Wired = true;          // stamp exists (if keyed) AND npc placed (if wcid set)
        }

        private static List<ZoneQuestRow> _questRows;   // guarded by _lock; null = load on next request

        /// <summary>Registry rows for one zone, sort_order then wave. Loads (and bootstraps the table) lazily.</summary>
        public static List<ZoneQuestRow> GetZoneQuests(string zoneName)
        {
            List<ZoneQuestRow> rows;
            lock (_lock)
            {
                if (_questRows == null)
                    _questRows = LoadQuestRegistry();
                rows = _questRows;
            }
            var area = GetArea(zoneName);
            var name = area?.Name ?? zoneName;
            return rows.Where(q => string.Equals(q.Zone, name, StringComparison.OrdinalIgnoreCase))
                       .OrderBy(q => q.SortOrder)
                       .ThenBy(q => q.Wave, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        private static List<ZoneQuestRow> LoadQuestRegistry()
        {
            var result = new List<ZoneQuestRow>();
            try
            {
                using var ctx = new ACE.Database.Models.World.WorldDbContext();
                var conn = ctx.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    ctx.Database.OpenConnection();

                using (var create = conn.CreateCommand())
                {
                    create.CommandText = @"CREATE TABLE IF NOT EXISTS `zonecontrol_quest` (
                        `id`            INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
                        `zone`          VARCHAR(64)  NOT NULL,
                        `quest_key`     VARCHAR(64)  NULL,
                        `completed_key` VARCHAR(64)  NULL,
                        `title`         VARCHAR(64)  NOT NULL,
                        `category`      VARCHAR(16)  NOT NULL,
                        `wave`          VARCHAR(16)  NULL,
                        `npc_name`      VARCHAR(64)  NULL,
                        `npc_wcid`      INT UNSIGNED NULL,
                        `objective`     VARCHAR(255) NOT NULL,
                        `targets`       VARCHAR(255) NULL,
                        `count`         INT          NULL,
                        `repeat_hours`  INT          NULL,
                        `reward`        VARCHAR(128) NULL,
                        `stage`         VARCHAR(16)  NOT NULL DEFAULT 'planned',
                        `sort_order`    INT          NOT NULL DEFAULT 0,
                        `notes`         VARCHAR(255) NULL,
                        KEY `idx_zone` (`zone`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
                    create.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT `zone`, `quest_key`, `completed_key`, `title`, `category`, `wave`, " +
                                      "`npc_name`, `npc_wcid`, `objective`, `targets`, `count`, `repeat_hours`, " +
                                      "`reward`, `stage`, `sort_order` FROM `zonecontrol_quest`";
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        result.Add(new ZoneQuestRow
                        {
                            Zone         = rdr.IsDBNull(0)  ? "" : rdr.GetString(0),
                            QuestKey     = rdr.IsDBNull(1)  ? "" : rdr.GetString(1),
                            CompletedKey = rdr.IsDBNull(2)  ? "" : rdr.GetString(2),
                            Title        = rdr.IsDBNull(3)  ? "" : rdr.GetString(3),
                            Category     = rdr.IsDBNull(4)  ? "" : rdr.GetString(4),
                            Wave         = rdr.IsDBNull(5)  ? "" : rdr.GetString(5),
                            NpcName      = rdr.IsDBNull(6)  ? "" : rdr.GetString(6),
                            NpcWcid      = rdr.IsDBNull(7)  ? 0u : Convert.ToUInt32(rdr.GetValue(7)),
                            Objective    = rdr.IsDBNull(8)  ? "" : rdr.GetString(8),
                            Targets      = rdr.IsDBNull(9)  ? "" : rdr.GetString(9),
                            Count        = rdr.IsDBNull(10) ? 0  : Convert.ToInt32(rdr.GetValue(10)),
                            RepeatHours  = rdr.IsDBNull(11) ? 0  : Convert.ToInt32(rdr.GetValue(11)),
                            Reward       = rdr.IsDBNull(12) ? "" : rdr.GetString(12),
                            Stage        = rdr.IsDBNull(13) ? "planned" : rdr.GetString(13),
                            SortOrder    = rdr.IsDBNull(14) ? 0  : Convert.ToInt32(rdr.GetValue(14)),
                        });
                    }
                }

                // Resolve NPC placement (coords + landblock) once per distinct wcid; prefer the base(NULL)
                // row, else the lowest variation — coordinates are identical across mirrored rows.
                var placements = new Dictionary<uint, (string lb, string co)>();
                foreach (var wcid in result.Where(r => r.NpcWcid != 0).Select(r => r.NpcWcid).Distinct())
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z` FROM `landblock_instance` " +
                                      "WHERE `weenie_Class_Id` = @w ORDER BY (`variation_Id` IS NULL) DESC, `variation_Id` LIMIT 1";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@w";
                    p.Value = wcid;
                    cmd.Parameters.Add(p);
                    using var rdr = cmd.ExecuteReader();
                    if (rdr.Read())
                    {
                        var cell = Convert.ToUInt32(rdr.GetValue(0));
                        var pos = new ACE.Entity.Position(cell,
                            Convert.ToSingle(rdr.GetValue(1)), Convert.ToSingle(rdr.GetValue(2)), Convert.ToSingle(rdr.GetValue(3)),
                            0f, 0f, 0f, 1f);
                        var co = ACE.Server.Entity.PositionExtensions.GetMapCoordStr(pos) ?? "";
                        placements[wcid] = (((ushort)(cell >> 16)).ToString("X4"), co);
                    }
                }

                foreach (var q in result)
                {
                    var stampOk = string.IsNullOrEmpty(q.QuestKey) || DatabaseManager.World.GetCachedQuest(q.QuestKey) != null;
                    var npcOk = q.NpcWcid == 0 || placements.ContainsKey(q.NpcWcid);
                    if (q.NpcWcid != 0 && placements.TryGetValue(q.NpcWcid, out var pl))
                    {
                        q.LandblockHex = pl.lb;
                        q.Coords = pl.co;
                    }
                    // planned rows aren't expected to be wired yet — only flag testing/live rows
                    q.Wired = string.Equals(q.Stage, "planned", StringComparison.OrdinalIgnoreCase) || (stampOk && npcOk);
                }
            }
            catch (Exception ex)
            {
                log.Error($"[ZONECONTROL] LoadQuestRegistry failed: {ex.Message}");
            }
            return result;
        }

        #endregion

        #region mob enumeration

        private const int MaxGeneratorDepth = 6;

        /// <summary>Distinct Creature WCIDs reachable (through any number of nested generator layers) on the
        /// given landblocks at a specific variation, sorted by name. A live landblock at variation N loads
        /// strictly VariationId==N; placed WCIDs are frequently GENERATOR weenies whose real spawns live in
        /// nested PropertiesGenerator, so we walk that tree (depth-capped, cycle-safe) to actual Creature weenies.</summary>
        private static List<(uint Wcid, string Name, bool IsMonster)> GetLandblockMobs(IEnumerable<ushort> landblocks, int variation)
        {
            var seen = new Dictionary<uint, (string Name, bool IsMonster, string Type)>();
            var visited = new HashSet<uint>();
            foreach (var lb in landblocks)
            {
                var instances = DatabaseManager.World.GetCachedInstancesByLandblock(lb, variation);
                foreach (var inst in instances)
                    ExpandGeneratorTree(inst.WeenieClassId, seen, visited, 0);
            }

            return seen.Select(kv => (kv.Key, kv.Value.Name, kv.Value.IsMonster))
                .OrderBy(kv => kv.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ExpandGeneratorTree(uint wcid, Dictionary<uint, (string Name, bool IsMonster, string Type)> seen, HashSet<uint> visited, int depth)
        {
            if (depth > MaxGeneratorDepth || !visited.Add(wcid))
                return;

            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie == null)
                return;

            if (weenie.WeenieType == WeenieType.Creature)
            {
                if (!seen.ContainsKey(wcid))
                    seen[wcid] = (weenie.GetName() ?? ("wcid " + wcid), IsMonsterWeenie(weenie), GetCreatureTypeName(weenie));
                return;
            }

            if (weenie.PropertiesGenerator != null)
                foreach (var g in weenie.PropertiesGenerator)
                    ExpandGeneratorTree(g.WeenieClassId, seen, visited, depth + 1);
        }

        /// <summary>The weenie's CreatureType enum name ("" when unset/invalid) — the survey's "Types" column.</summary>
        private static string GetCreatureTypeName(Weenie weenie)
        {
            if (weenie.PropertiesInt == null || !weenie.PropertiesInt.TryGetValue(PropertyInt.CreatureType, out var ct) || ct == 0)
                return "";
            var typed = (CreatureType)ct;
            return Enum.IsDefined(typeof(CreatureType), typed) ? typed.ToString() : "";
        }

        // ── Per-landblock survey (Territory tab) ──
        // DB-backed (landblock_instance rows + weenie cache via the same generator-tree walk as the Monsters
        // tab), so UNLOADED landblocks survey fine. Human-paced admin path — never called from combat.

        public sealed class SurveyCreatureRow
        {
            public uint Wcid;
            public string Name;
            public string Type;
            public bool IsMonster;
        }

        public sealed class SurveyPlacedRow
        {
            public uint Wcid;
            public string Name;
            public int Count;
        }

        public sealed class SurveyRow
        {
            public ushort Landblock;
            public int Generators;                       // placed instances carrying a generator table
            public List<SurveyCreatureRow> Creatures;    // distinct creatures reachable on this landblock
            public List<SurveyPlacedRow> PlacedGenerators; // the generator instances, grouped by wcid
            public string Terrain;                       // EFFECTIVE terrain shown on the map (override ?? DAT), "" if unknown
            public string TerrainBase;                   // raw DAT-derived terrain (what "Auto/clear" reverts to), "" if unknown
        }

        /// <summary>The terrain tags the survey/plugin understand — the nine <see cref="ClassifyLandblockTerrain"/>
        /// buckets. Used to validate manual terrain overrides.</summary>
        public static readonly string[] TerrainTags =
            { "water", "beach", "obsidian", "snow", "ice", "swamp", "grass", "dirt", "rock" };

        /// <summary>Dominant terrain category of a landblock, read LIVE from the cell DAT (the same terrain the
        /// physics engine loads). Classifies the 81 terrain vertices via <see cref="LandDefs.TerrainType"/> into
        /// nine buckets — water 0x10-0x14, beach/sand 0x0A-0x0C, obsidian 0x06, snow 0x0F, ice 0x02, swamp 0x04,
        /// grass {0x01,0x03,0x09}, dirt {0x05,0x07,0x08}, else rock {0x00,0x0D,0x0E} — and returns the dominant.
        /// (Previously everything non-water/beach/obsidian collapsed to one "land" tag, so normal grassy/rocky
        /// zones rendered a single flat green; the finer buckets let the map actually differentiate blocks.)
        /// "" when the block isn't in the dat. Cached by DatManager, so repeated survey reads are cheap.</summary>
        public static string ClassifyLandblockTerrain(ushort landblock)
        {
            CellLandblock cl;
            try { cl = DatManager.CellDat.ReadFromDat<CellLandblock>(((uint)landblock << 16) | 0xFFFF); }
            catch { return ""; }
            if (cl?.Terrain == null || cl.Terrain.Count == 0) return "";

            int water = 0, beach = 0, obsidian = 0, snow = 0, ice = 0, swamp = 0, grass = 0, dirt = 0, rock = 0;
            foreach (var raw in cl.Terrain)
            {
                var tt = (raw >> 2) & 0x1F;   // TerrainType lives in bits 2-6 (same decode as LandblockStruct)
                switch (tt)
                {
                    case 0x10: case 0x11: case 0x12: case 0x13: case 0x14: water++; break;    // running/standing/sea water
                    case 0x0A: case 0x0B: case 0x0C: beach++; break;                          // sand: yellow/grey/rock-strewn
                    case 0x06: obsidian++; break;                                             // obsidian plain (volcanic)
                    case 0x0F: snow++; break;                                                 // snow
                    case 0x02: ice++; break;                                                  // ice
                    case 0x04: swamp++; break;                                                // marsh / sparse swamp
                    case 0x01: case 0x03: case 0x09: grass++; break;                          // grassland / lush / patchy grass
                    case 0x05: case 0x07: case 0x08: dirt++; break;                           // mud-rich / packed / patchy dirt
                    default: rock++; break;                                                   // barren/sedimentary/semi-barren rock (0,D,E)
                }
            }

            // Dominant category wins; ties resolve toward the more "notable" terrain (listed first) so a block
            // that is, say, half grass / half water reads as water rather than washing back into a green sea.
            var buckets = new (string Tag, int Count)[]
            {
                ("water", water), ("obsidian", obsidian), ("swamp", swamp), ("ice", ice), ("snow", snow),
                ("beach", beach), ("rock", rock), ("dirt", dirt), ("grass", grass),
            };
            var bestTag = ""; var bestN = 0;
            foreach (var b in buckets)
                if (b.Count > bestN) { bestN = b.Count; bestTag = b.Tag; }
            return bestN == 0 ? "" : bestTag;
        }

        /// <summary>Per-landblock content survey of a zone at its variation. Null when the zone doesn't exist.
        /// One row per member landblock (ordered), each with distinct reachable creatures (name/type/monster)
        /// and the placed generator instances grouped by wcid.</summary>
        public static List<SurveyRow> SurveyArea(string name)
        {
            EnsureInitialized();
            List<ushort> lbs;
            int variation;
            Dictionary<ushort, string> terrainOverrides;
            lock (_lock)
            {
                var area = FindArea(name);
                if (area == null)
                    return null;
                lbs = area.Landblocks.OrderBy(x => x).ToList();
                variation = area.Variation;
                // Snapshot the overrides so the (lock-free) survey loop below reads a stable copy.
                terrainOverrides = area.TerrainOverrides != null
                    ? new Dictionary<ushort, string>(area.TerrainOverrides)
                    : new Dictionary<ushort, string>();
            }

            var rows = new List<SurveyRow>(lbs.Count);
            foreach (var lb in lbs)
            {
                var seen = new Dictionary<uint, (string Name, bool IsMonster, string Type)>();
                var visited = new HashSet<uint>();
                var placed = new Dictionary<uint, SurveyPlacedRow>();
                var gens = 0;

                var instances = DatabaseManager.World.GetCachedInstancesByLandblock(lb, variation);
                foreach (var inst in instances)
                {
                    ExpandGeneratorTree(inst.WeenieClassId, seen, visited, 0);

                    var weenie = DatabaseManager.World.GetCachedWeenie(inst.WeenieClassId);
                    if (weenie?.PropertiesGenerator is { Count: > 0 })
                    {
                        gens++;
                        if (placed.TryGetValue(inst.WeenieClassId, out var row))
                            row.Count++;
                        else
                            placed[inst.WeenieClassId] = new SurveyPlacedRow
                            {
                                Wcid = inst.WeenieClassId,
                                Name = weenie.GetName() ?? ("wcid " + inst.WeenieClassId),
                                Count = 1,
                            };
                    }
                }

                var baseTerrain = ClassifyLandblockTerrain(lb);
                rows.Add(new SurveyRow
                {
                    Landblock = lb,
                    Generators = gens,
                    Creatures = seen
                        .Select(kv => new SurveyCreatureRow { Wcid = kv.Key, Name = kv.Value.Name, Type = kv.Value.Type, IsMonster = kv.Value.IsMonster })
                        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                    PlacedGenerators = placed.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                    // TerrainBase = the raw DAT terrain (what clearing an override reverts to); Terrain = the
                    // effective value shown on the map (a manual override wins). Both are display-only.
                    TerrainBase = baseTerrain,
                    Terrain = terrainOverrides.TryGetValue(lb, out var ov) && !string.IsNullOrEmpty(ov)
                        ? ov
                        : baseTerrain,
                });
            }

            return rows;
        }

        /// <summary>Mirrors Creature.IsMonster (Attackable || TargetingTactic != None) at the weenie level.</summary>
        private static bool IsMonsterWeenie(Weenie weenie)
        {
            var attackable = weenie.PropertiesBool != null && weenie.PropertiesBool.TryGetValue(PropertyBool.Attackable, out var a) ? a : true;
            if (attackable)
                return true;
            var tactic = weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.TargetingTactic, out var t) ? t : 0;
            return tactic != 0;
        }

        #endregion
    }
}
