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
        /// constant, independent of any other system's tiering.
        ///
        /// SCOPE: the player-boundary minimum, and nothing else. It used to double as the endgame-content
        /// floor inside GetEffectiveVariation, which meant retuning the boundary rule would silently move a
        /// combat gate; that role now belongs to <see cref="VariationManager.EndgameMinVariation"/>. The two
        /// happen to share the value 11 — they are not required to.</summary>
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
            public ZoneAppearance AppearanceDefault;         // cosmetic default (separate from stats)
            public Dictionary<uint, ZoneAppearance> AppearanceByWcid; // per-WCID cosmetic overlays
        }

        private sealed class Snapshot
        {
            public readonly HashSet<ushort> EnabledLandblocks;
            public readonly Dictionary<ushort, List<ZoneRef>> ByLandblock;   // ENABLED zones only
            // Player-boundary allowlists: variation -> union of landblocks of all BOUNDED zones at that
            // variation, ENABLED OR NOT (the boundary is independent of the zone's stat controls).
            // A variation with no entry has no Zone Control boundary (free roam / legacy fallback).
            public readonly Dictionary<int, HashSet<ushort>> BoundedLandblocksByVariation;
            // Terrain-override spawn redirection (owner 2026-07-21): variation -> lb -> override tag, and
            // variation -> tag -> member landblocks whose BASE (DAT) terrain is that tag (encounter donors).
            // Built for zones carrying overrides, ENABLED OR NOT (a mechanic of the zone's territory, like
            // the boundary). Consumed by Landblock.SpawnEncounters via RedirectEncountersForTerrainOverride.
            public readonly Dictionary<int, Dictionary<ushort, string>> TerrainOverridesByVariation;
            public readonly Dictionary<int, Dictionary<string, List<ushort>>> TerrainDonorsByVariation;
            public Snapshot(HashSet<ushort> enabled, Dictionary<ushort, List<ZoneRef>> byLb,
                Dictionary<int, HashSet<ushort>> boundedByVar,
                Dictionary<int, Dictionary<ushort, string>> terrOvByVar,
                Dictionary<int, Dictionary<string, List<ushort>>> terrDonorsByVar)
            {
                EnabledLandblocks = enabled;
                ByLandblock = byLb;
                BoundedLandblocksByVariation = boundedByVar;
                TerrainOverridesByVariation = terrOvByVar;
                TerrainDonorsByVariation = terrDonorsByVar;
            }
            public static readonly Snapshot Empty =
                new Snapshot(new HashSet<ushort>(), new Dictionary<ushort, List<ZoneRef>>(),
                    new Dictionary<int, HashSet<ushort>>(),
                    new Dictionary<int, Dictionary<ushort, string>>(),
                    new Dictionary<int, Dictionary<string, List<ushort>>>());
        }

        private static volatile Snapshot _snapshot = Snapshot.Empty;

        private static readonly object _lock = new object();
        private static volatile bool _initialized;

        private class Store
        {
            public List<ControlledArea> Areas { get; set; } = new();

            /// <summary>Per-VARIATION Defaults (2026-07-30): variation -> the baseline layer every zone at
            /// that variation inherits, per stat. Absent on older stores = no Defaults = prior behavior.</summary>
            public Dictionary<int, VariationDefault> VariationDefaults { get; set; } = new();
        }

        // variation -> Default layer. Guarded by _lock; copied into the lock-free snapshot at rebuild.
        private static readonly Dictionary<int, VariationDefault> _variationDefaults = new();

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
            _variationDefaults.Clear();

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
                a.AppearanceDefault ??= new ZoneAppearance();
                a.AppearanceByWcid ??= new Dictionary<uint, ZoneAppearance>();
                MigrateAppearanceProps(a);
                _areas[a.Name] = a;
            }

            // Per-variation Defaults. Absent on pre-2026-07-30 stores -> empty -> zones resolve exactly as before.
            if (store.VariationDefaults != null)
            {
                foreach (var kv in store.VariationDefaults)
                {
                    if (kv.Value == null) continue;
                    kv.Value.Profile ??= new ZoneVariantProfile();
                    kv.Value.Effects ??= new ZoneEffects();
                    kv.Value.Appearance ??= new ZoneAppearance();
                    _variationDefaults[kv.Key] = kv.Value;
                }
            }

            RebuildIndexes();
        }

        /// <summary>One-time carry-over: Phase 1 stamped appearance through the generic prop pipe (PaletteTemplate
        /// int 3, Shade float 12, DefaultScale float 39, Translucency float 76, CreatureVariant int 9038). Move any
        /// such props out of the stat/prop buckets into the dedicated appearance layer so cosmetic edits no longer
        /// ride the per-WCID stat bucket. Idempotent: after the move there are no appearance-id props left to find.</summary>
        private static void MigrateAppearanceProps(ControlledArea a)
        {
            MigrateAppearancePropsFromVariant(a.Profile.Minion, a.AppearanceDefault);

            List<uint> emptiedStatBuckets = null;
            foreach (var kv in a.Profile.WcidOverrides)
            {
                if (kv.Value == null) continue;
                if (!a.AppearanceByWcid.TryGetValue(kv.Key, out var ap) || ap == null)
                    a.AppearanceByWcid[kv.Key] = ap = new ZoneAppearance();

                var moved = MigrateAppearancePropsFromVariant(kv.Value, ap);

                // If pulling the appearance props emptied this per-WCID STAT bucket entirely, drop it so the mob
                // resolves to the zone default again — a lingering empty bucket would otherwise keep detaching it
                // from scaling (the very coupling this split removes, for data authored under Phase 1).
                if (moved && VariantIsEmpty(kv.Value))
                    (emptiedStatBuckets ??= new List<uint>()).Add(kv.Key);

                if (ap.IsEmpty)
                    a.AppearanceByWcid.Remove(kv.Key); // never created an appearance override we didn't need
            }
            if (emptiedStatBuckets != null)
                foreach (var w in emptiedStatBuckets)
                    a.Profile.WcidOverrides.Remove(w);
        }

        private static bool MigrateAppearancePropsFromVariant(ZoneScaling.ZoneVariantProfile vp, ZoneAppearance ap)
        {
            var moved = false;
            if (vp?.PropInts != null)
            {
                if (vp.PropInts.TryGetValue(3, out var pal)) { ap.PaletteTemplate ??= (int)pal; vp.PropInts.Remove(3); moved = true; }
                if (vp.PropInts.TryGetValue(9038, out var shiny)) { ap.Shiny ??= shiny != 0; vp.PropInts.Remove(9038); moved = true; }
            }
            if (vp?.PropFloats != null)
            {
                if (vp.PropFloats.TryGetValue(12, out var shade)) { ap.Shade ??= shade; vp.PropFloats.Remove(12); moved = true; }
                if (vp.PropFloats.TryGetValue(39, out var scale)) { ap.Scale ??= scale; vp.PropFloats.Remove(39); moved = true; }
                if (vp.PropFloats.TryGetValue(76, out var trans)) { ap.Translucency ??= trans; vp.PropFloats.Remove(76); moved = true; }
            }
            return moved;
        }

        /// <summary>True when a stat/prop variant bucket carries nothing at all — used to prune a per-WCID bucket
        /// that held only appearance props before migration moved them to the appearance layer.</summary>
        private static bool VariantIsEmpty(ZoneScaling.ZoneVariantProfile vp)
        {
            return (vp.Stats == null || vp.Stats.Count == 0)
                && (vp.BodyParts == null || vp.BodyParts.Count == 0)
                && (vp.PropInts == null || vp.PropInts.Count == 0)
                && (vp.PropInt64s == null || vp.PropInt64s.Count == 0)
                && (vp.PropFloats == null || vp.PropFloats.Count == 0)
                && (vp.PropBools == null || vp.PropBools.Count == 0)
                && (vp.CustomCantrips == null || vp.CustomCantrips.Count == 0)
                && (vp.CustomCantripBands == null || vp.CustomCantripBands.Count == 0)
                && (vp.CustomCantripSlots == null || vp.CustomCantripSlots.Count == 0)
                && (vp.CurrencyDrops == null || vp.CurrencyDrops.Count == 0)
                && (vp.SpellRules == null || vp.SpellRules.Count == 0);
        }

        private static void Save()
        {
            var store = new Store
            {
                Areas = _areas.Values.ToList(),
                VariationDefaults = new Dictionary<int, VariationDefault>(_variationDefaults),
            };
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
            var terrOvByVar = new Dictionary<int, Dictionary<ushort, string>>();
            var terrDonorsByVar = new Dictionary<int, Dictionary<string, List<ushort>>>();
            foreach (var area in _areas.Values)
            {
                // Boundary allowlist: union the landblocks of every BOUNDED zone per variation —
                // INDEPENDENT of Enabled (owner 2026-07-21): the wall stays up while the zone's stat
                // controls are off. Enabled governs stats/mechanics/loot; Bounded governs the boundary.
                // Runtime zones (rifts) are deliberately excluded — they never bound players.
                if (area.Bounded)
                {
                    if (!boundedByVar.TryGetValue(area.Variation, out var set))
                        boundedByVar[area.Variation] = set = new HashSet<ushort>();
                    set.UnionWith(area.Landblocks);
                }

                // Terrain-override spawn redirection (owner 2026-07-21, independent of Enabled): record
                // this zone's overrides and, for zones that HAVE overrides, classify every member block's
                // BASE terrain once (DatManager caches the reads) as the donor index — "mark a block
                // obsidian and it draws its encounter camps from the zone's real obsidian blocks".
                if (area.TerrainOverrides is { Count: > 0 })
                {
                    if (!terrOvByVar.TryGetValue(area.Variation, out var ovMap))
                        terrOvByVar[area.Variation] = ovMap = new Dictionary<ushort, string>();
                    foreach (var kv in area.TerrainOverrides)
                        ovMap[kv.Key] = kv.Value;

                    if (!terrDonorsByVar.TryGetValue(area.Variation, out var donorMap))
                        terrDonorsByVar[area.Variation] = donorMap = new Dictionary<string, List<ushort>>();
                    foreach (var lb in area.Landblocks)
                    {
                        var baseTag = ClassifyLandblockTerrain(lb);
                        if (string.IsNullOrEmpty(baseTag)) continue;
                        if (!donorMap.TryGetValue(baseTag, out var list))
                            donorMap[baseTag] = list = new List<ushort>();
                        if (!list.Contains(lb)) list.Add(lb);
                    }
                }

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
            _snapshot = new Snapshot(enabledLbs, byLb, boundedByVar, terrOvByVar, terrDonorsByVar); // volatile publish

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

        /// <summary>
        /// Build the immutable per-zone read record. THIS is where the Default layer is applied: the
        /// <c>VariationDefault -&gt; zone -&gt; wcid</c> merge happens once here, at snapshot-build time, and the
        /// result is a fully-resolved <see cref="EvaluatedProfile"/> per bucket. The combat hot path therefore
        /// stays exactly one dictionary lookup with ZERO per-hit merge cost — the whole point of doing it here.
        ///
        /// Runtime zones (rifts, negative variations) get no Default: <see cref="DefaultFor"/> returns null for
        /// any variation without an authored entry, and rift variations never have one.
        /// </summary>
        private static ZoneRef BuildZoneRef(ControlledArea area)
        {
            var def = DefaultFor(area.Variation);
            var defProfile = def?.Profile;

            // zone layer = variation Default + the zone's own stats
            var zoneMerged = ZoneVariantProfile.Merge(defProfile, area.Profile.Minion);

            // per-WCID = the zone layer + that monster's bucket (per stat, NOT a wholesale replacement)
            var wcid = new Dictionary<uint, EvaluatedProfile>();
            foreach (var kv in area.Profile.WcidOverrides)
                wcid[kv.Key] = EvaluateVariant(area.Name, ZoneVariantProfile.Merge(defProfile, area.Profile.Minion, kv.Value));

            // appearance layers the same way: Default -> zone -> wcid, per field
            var apZone = ZoneAppearance.Merge(def?.Appearance, area.AppearanceDefault) ?? new ZoneAppearance();
            var apWcid = new Dictionary<uint, ZoneAppearance>();
            if (area.AppearanceByWcid != null)
                foreach (var kv in area.AppearanceByWcid)
                {
                    if (kv.Value == null || kv.Value.IsEmpty) continue;
                    apWcid[kv.Key] = kv.Value.Clone();
                }

            return new ZoneRef
            {
                Name = area.Name,
                Variation = area.Variation,
                LandblockCount = area.Landblocks.Count,
                Default = EvaluateVariant(area.Name, zoneMerged),
                Wcid = wcid,
                Effects = ZoneEffects.Merge(def?.Effects, area.Effects),
                AppearanceDefault = apZone,
                AppearanceByWcid = apWcid,
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
            Dictionary<int, CantripBand> cantripBands = null;
            List<ZoneCurrencyDrop> currencyDrops = null;
            List<ZoneSpellRule> spellRules = null;

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

                if (variantProfile.CustomCantripBands is { Count: > 0 })
                {
                    cantripBands = new Dictionary<int, CantripBand>(variantProfile.CustomCantripBands.Count);
                    foreach (var kvp in variantProfile.CustomCantripBands)
                        if (kvp.Value != null)
                            cantripBands[kvp.Key] = kvp.Value.Clone();
                }

                if (variantProfile.CurrencyDrops is { Count: > 0 })
                {
                    currencyDrops = new List<ZoneCurrencyDrop>(variantProfile.CurrencyDrops.Count);
                    foreach (var d in variantProfile.CurrencyDrops)
                        if (d != null && d.Wcid != 0)
                            currencyDrops.Add(d.Clone());
                }

                if (variantProfile.SpellRules is { Count: > 0 })
                {
                    spellRules = new List<ZoneSpellRule>(variantProfile.SpellRules.Count);
                    foreach (var r in variantProfile.SpellRules)
                        if (r != null && r.SpellId != 0)
                            spellRules.Add(r.Clone());
                }
            }

            return new EvaluatedProfile(zoneName, 1, ZoneVariant.Minion, values, bodyParts, propInts, propInt64s, propFloats, propBools, customCantrips, currencyDrops, spellRules, cantripBands,
                variantProfile?.CustomCantripSlots);
        }

        // CopyEffects removed 2026-07-30 — ZoneEffects.Merge(default, zone) both layers AND clones.

        /// <summary>The authored Default layer for a variation, or null when none exists (which is every
        /// variation until one is authored, and always for rift/runtime negative variations). Call under
        /// _lock, or from RebuildIndexes which already holds it.</summary>
        private static VariationDefault DefaultFor(int variation)
            => _variationDefaults.TryGetValue(variation, out var d) ? d : null;

        #endregion

        #region resolution / public API

        /// <summary>
        /// The variation Zone Control resolves a world object on: its real Location variation, except that an
        /// object carrying ForceEndgameSystems is treated as standing at its EndgameForcedVariation (floored to
        /// v11), so a test dummy in the normal world can still be governed by a variant zone.
        ///
        /// Delegates to <see cref="VariationManager.GetEffectiveEndgameVariation"/> — the shared endgame-layer
        /// resolver. This used to be a private copy floored on <see cref="MinBoundedVariation"/>, with an
        /// identical copy in PrestigeManager; the two drifted apart when the prestige offset moved and broke
        /// the ForceEndgameSystems test hook (2026-07-30). Kept as a named method because it is Zone Control's
        /// own vocabulary and has 9 call sites here and in the command surface.
        /// </summary>
        public static int GetEffectiveVariation(WorldObject wo) => VariationManager.GetEffectiveEndgameVariation(wo);

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
        /// Resolves the winning zone's DEFAULT stat profile for a creature, ignoring any per-WCID
        /// override bucket. Used by the v11 relief curves: the relief_* anchors are zone-level
        /// player-progression policy, and a per-WCID stat override REPLACES the whole default profile —
        /// resolving the curves here keeps them applying to override mobs (bosses) without every
        /// override having to carry the nine anchor stats.
        /// </summary>
        public static EvaluatedProfile ResolveZoneDefaultForCreature(Creature creature)
        {
            if (creature == null || creature is Player)
                return null;

            if (creature.GetProperty(PropertyBool.ExemptFromZoneScaling) == true)
                return null;

            var snap = _snapshot;
            var landblock = creature.Location?.LandblockId.Landblock ?? 0;
            if (!snap.EnabledLandblocks.Contains(landblock) || !snap.ByLandblock.TryGetValue(landblock, out var list))
                return null;

            var effVar = GetEffectiveVariation(creature);

            ZoneRef best = null;
            foreach (var zr in list)
            {
                if (zr.Variation != effVar)
                    continue;
                if (best == null || zr.LandblockCount < best.LandblockCount)
                    best = zr;
            }

            return best?.Default;
        }

        /// <summary>
        /// Resolves the winning zone's DEFAULT stat profile for a PLAYER standing somewhere - the
        /// player-side twin of <see cref="ResolveZoneDefaultForCreature"/> (same landblock + variation
        /// gating, same most-specific-wins, no per-WCID bucket). Used for the gear_cap_* knobs: the
        /// caller falls back to its C# default when this is null, so a knob read through here applies
        /// everywhere and a zone merely re-tunes it. Lock-free snapshot read; safe on the rating hot path.
        /// </summary>
        public static EvaluatedProfile ResolveZoneDefaultForPlayer(Player player)
        {
            if (player == null)
                return null;

            var snap = _snapshot;
            var landblock = player.Location?.LandblockId.Landblock ?? 0;
            if (!snap.EnabledLandblocks.Contains(landblock) || !snap.ByLandblock.TryGetValue(landblock, out var list))
                return null;

            var effVar = GetEffectiveVariation(player);

            ZoneRef best = null;
            foreach (var zr in list)
            {
                if (zr.Variation != effVar)
                    continue;
                if (best == null || zr.LandblockCount < best.LandblockCount)
                    best = zr;
            }

            return best?.Default;
        }

        /// <summary>Resolves a creature's COSMETIC appearance from the governing zone: the zone default overlaid by
        /// this monster's per-WCID entry (per-WCID non-null fields win). Returns null when no enabled zone governs
        /// the creature at its variation, or the zone defines no appearance. INDEPENDENT of the stat resolution —
        /// a per-WCID appearance never creates a stat bucket, so it can't detach the mob from zone stat scaling.</summary>
        public static ZoneAppearance ResolveAppearanceForCreature(Creature creature)
        {
            if (creature == null || creature is Player)
                return null;

            if (creature.GetProperty(PropertyBool.ExemptFromZoneScaling) == true)
                return null;

            var snap = _snapshot;
            var landblock = creature.Location?.LandblockId.Landblock ?? 0;
            if (!snap.EnabledLandblocks.Contains(landblock) || !snap.ByLandblock.TryGetValue(landblock, out var list))
                return null;

            var effVar = GetEffectiveVariation(creature);

            ZoneRef best = null;
            foreach (var zr in list)
            {
                if (zr.Variation != effVar) continue;
                if (best == null || zr.LandblockCount < best.LandblockCount) best = zr;
            }
            if (best == null) return null;

            best.AppearanceByWcid.TryGetValue(creature.WeenieClassId, out var wcidAp);
            var merged = ZoneAppearance.Merge(best.AppearanceDefault, wcidAp);
            return merged != null && !merged.IsEmpty ? merged : null;
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

        /// <summary>True when at least one BOUNDED zone exists at this variation — enabled or not (the
        /// boundary is independent of the zone's stat controls). Lock-free; safe on hot per-player paths.
        /// When true, <see cref="IsLandblockAllowed"/> is Zone Control's boundary authority for the
        /// variation (enforced by the player tick's CheckZoneBoundary — standalone, independent of any
        /// other boundary system).</summary>
        public static bool HasBoundedZonesAt(int? variation)
        {
            if (!variation.HasValue)
                return false;
            return _snapshot.BoundedLandblocksByVariation.ContainsKey(variation.Value);
        }

        /// <summary>Player-boundary allowlist check: a landblock is allowed at a variation when no bounded zone
        /// exists there (free roam), or it belongs to any bounded zone at that variation (union; enabled
        /// or not). Lock-free snapshot read.</summary>
        public static bool IsLandblockAllowed(int? variation, ushort landblock)
        {
            if (!variation.HasValue)
                return true;
            if (!_snapshot.BoundedLandblocksByVariation.TryGetValue(variation.Value, out var allowed))
                return true;
            return allowed.Contains(landblock);
        }

        /// <summary>
        /// Terrain-override encounter redirection (owner 2026-07-21). When a zone overrides this
        /// landblock's terrain at this variation, the block's encounter spawns are drawn from the
        /// zone's DONOR blocks — members whose real DAT terrain matches the override tag — so marking
        /// a grass block "obsidian" makes it spawn the zone's obsidian camps. The block's own row
        /// POSITIONS are kept (known-good spots); only the generator WCIDs are substituted. A block
        /// with no rows of its own imports a random donor block's full layout. Returns the original
        /// list untouched when no override applies or no donor exists. Lock-free snapshot read +
        /// cached DB reads — safe from the landblock init task. Independent of Enabled.
        /// </summary>
        public static List<Database.Models.World.Encounter> RedirectEncountersForTerrainOverride(
            ushort landblock, int? variationId, List<Database.Models.World.Encounter> own)
        {
            if (!variationId.HasValue)
                return own;

            var snap = _snapshot;
            if (!snap.TerrainOverridesByVariation.TryGetValue(variationId.Value, out var ovMap) ||
                !ovMap.TryGetValue(landblock, out var tag))
                return own;

            if (!snap.TerrainDonorsByVariation.TryGetValue(variationId.Value, out var donorMap) ||
                !donorMap.TryGetValue(tag, out var donorLbs) || donorLbs.Count == 0)
                return own;

            // donor pool: every encounter row on the zone's true-<tag> blocks (weighted naturally by
            // how often a generator appears there); the target block never donates to itself
            var pool = new List<Database.Models.World.Encounter>();
            foreach (var dlb in donorLbs)
            {
                if (dlb == landblock) continue;
                var rows = DatabaseManager.World.GetCachedEncountersByLandblock(dlb);
                if (rows != null) pool.AddRange(rows);
            }
            if (pool.Count == 0)
                return own;

            var result = new List<Database.Models.World.Encounter>();
            if (own is { Count: > 0 })
            {
                // keep this block's own spawn spots, swap what spawns there
                foreach (var e in own)
                    result.Add(new Database.Models.World.Encounter
                    {
                        Landblock = e.Landblock,
                        CellX = e.CellX,
                        CellY = e.CellY,
                        WeenieClassId = pool[Common.ThreadSafeRandom.Next(0, pool.Count - 1)].WeenieClassId,
                    });
            }
            else
            {
                // no encounters of its own: import a random donor block's full layout
                var donor = donorLbs[Common.ThreadSafeRandom.Next(0, donorLbs.Count - 1)];
                var rows = DatabaseManager.World.GetCachedEncountersByLandblock(donor);
                if (rows == null || rows.Count == 0)
                    return own;
                foreach (var e in rows)
                    result.Add(new Database.Models.World.Encounter
                    {
                        Landblock = landblock,
                        CellX = e.CellX,
                        CellY = e.CellY,
                        WeenieClassId = e.WeenieClassId,
                    });
            }

            log.Debug($"[ZoneControl] terrain override '{tag}' redirected {result.Count} encounter(s) on 0x{landblock:X4} v{variationId} ({donorLbs.Count} donor block(s))");
            return result;
        }

        /// <summary>True when the weenie carries a generator table (a camp/spawn generator).</summary>
        private static bool IsGeneratorWeenie(uint wcid)
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            return weenie?.PropertiesGenerator is { Count: > 0 };
        }

        /// <summary>
        /// Terrain-override redirection for PLACED generator instances — the live path on this server
        /// (encounter_spawn_base_layer_only=true keeps world encounters off explicit variations, so
        /// variant-zone camps come from per-variation landblock_instance generators). When a zone
        /// overrides this landblock's terrain at this variation, every STANDALONE generator instance
        /// on the block (no links, not a link child — quest chains and NPCs are never touched) keeps
        /// its position and instance guid but has its generator weenie substituted with a random pick
        /// from the zone's donor blocks (members whose real DAT terrain matches the override tag).
        /// Cached rows are cloned, never mutated. Returns the original list untouched when no override
        /// applies or no donor pool exists. Lock-free; safe from the landblock init path.
        /// </summary>
        public static List<Database.Models.World.LandblockInstance> RedirectInstancesForTerrainOverride(
            ushort landblock, int? variationId, List<Database.Models.World.LandblockInstance> own)
        {
            if (own == null || own.Count == 0 || !variationId.HasValue)
                return own;

            var snap = _snapshot;
            if (!snap.TerrainOverridesByVariation.TryGetValue(variationId.Value, out var ovMap) ||
                !ovMap.TryGetValue(landblock, out var tag))
                return own;

            if (!snap.TerrainDonorsByVariation.TryGetValue(variationId.Value, out var donorMap) ||
                !donorMap.TryGetValue(tag, out var donorLbs) || donorLbs.Count == 0)
                return own;

            // donor pool: standalone generator instances on the zone's true-<tag> blocks at this same
            // variation (duplicates kept - a generator common on donor blocks should be common here)
            var pool = new List<uint>();
            foreach (var dlb in donorLbs)
            {
                if (dlb == landblock) continue;
                var rows = DatabaseManager.World.GetCachedInstancesByLandblock(dlb, variationId);
                if (rows == null) continue;
                foreach (var r in rows)
                    if (!r.IsLinkChild && r.LandblockInstanceLink.Count == 0 && IsGeneratorWeenie(r.WeenieClassId))
                        pool.Add(r.WeenieClassId);
            }
            if (pool.Count == 0)
                return own;

            // Prefer generators NAMED for the tag ("T11 Tou Tou Obsidian Generator" for obsidian):
            // real blocks are rarely terrain-pure - F75C carries Land gens on its grassy fringes, so
            // an unfiltered donor pool leaked ~half off-theme camps (owner report 2026-07-21). Falls
            // back to the full donor pool when nothing is named for the tag (e.g. the generic "Land"
            // generators covering grass/dirt/rock).
            var themed = new List<uint>();
            foreach (var wcid in pool)
            {
                var name = DatabaseManager.World.GetCachedWeenie(wcid)?.GetName();
                if (name != null && name.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                    themed.Add(wcid);
            }
            var themedPool = themed.Count > 0;
            if (themedPool)
                pool = themed;

            var result = new List<Database.Models.World.LandblockInstance>(own.Count);
            var swapped = 0;
            foreach (var r in own)
            {
                if (r.IsLinkChild || r.LandblockInstanceLink.Count > 0 || !IsGeneratorWeenie(r.WeenieClassId))
                {
                    result.Add(r);
                    continue;
                }

                result.Add(new Database.Models.World.LandblockInstance
                {
                    Guid = r.Guid,
                    Landblock = r.Landblock,
                    WeenieClassId = pool[Common.ThreadSafeRandom.Next(0, pool.Count - 1)],
                    ObjCellId = r.ObjCellId,
                    OriginX = r.OriginX, OriginY = r.OriginY, OriginZ = r.OriginZ,
                    AnglesW = r.AnglesW, AnglesX = r.AnglesX, AnglesY = r.AnglesY, AnglesZ = r.AnglesZ,
                    VariationId = r.VariationId,
                });
                swapped++;
            }

            if (swapped > 0)
                log.Info($"[ZoneControl] terrain override '{tag}': swapped {swapped} generator instance(s) on 0x{landblock:X4} v{variationId} " +
                         $"({(themedPool ? "themed" : "unfiltered")} pool {pool.Count} from {donorLbs.Count} donor block(s))");
            return result;
        }

        /// <summary>Names of bounded zones at a variation, enabled or not (for command echoes / the
        /// plugin's shared-travel-space line). Locked display path — human-paced callers only.</summary>
        public static List<string> BoundedZoneNamesAt(int variation)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _areas.Values
                    .Where(a => a.Bounded && a.Variation == variation)
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

                // Same layering the combat snapshot uses, so the GUI/command readout matches what a mob
                // actually gets: VariationDefault -> zone -> wcid, merged per stat.
                var defProfile = DefaultFor(area.Variation)?.Profile;
                var variantProfile = hasWcidOverride
                    ? ZoneVariantProfile.Merge(defProfile, area.Profile.Minion, area.Profile.WcidOverrides[wcid.Value])
                    : ZoneVariantProfile.Merge(defProfile, area.Profile.Minion);

                var eval = EvaluateVariant(area.Name, variantProfile);
                _evalCache[cacheKey] = eval;
                return eval;
            }
        }

        /// <summary>
        /// The fully-layered profile for a zone (and optionally one WCID), as a live-shaped
        /// <see cref="ZoneVariantProfile"/> rather than a flattened <see cref="EvaluatedProfile"/> — so callers
        /// that need the authored <see cref="StatCurve"/> (the plugin sync payload, command readouts) see the
        /// SAME numbers combat resolves. Merges VariationDefault -&gt; zone -&gt; wcid. Null if no such zone.
        /// </summary>
        public static ZoneVariantProfile ResolveProfileForDisplay(string name, uint? wcid = null)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var area = FindArea(name);
                if (area == null)
                    return null;

                var defProfile = DefaultFor(area.Variation)?.Profile;
                if (wcid.HasValue && area.Profile.WcidOverrides.TryGetValue(wcid.Value, out var bucket))
                    return ZoneVariantProfile.Merge(defProfile, area.Profile.Minion, bucket);

                return ZoneVariantProfile.Merge(defProfile, area.Profile.Minion);
            }
        }

        /// <summary>Which layer a stat's winning value came from, for provenance readouts:
        /// "wcid" / "zone" / "default vN", or null when nothing authors it. Most specific wins.</summary>
        public static string ResolveStatSource(string name, uint? wcid, string stat)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var area = FindArea(name);
                if (area == null || string.IsNullOrEmpty(stat))
                    return null;

                if (wcid.HasValue
                    && area.Profile.WcidOverrides.TryGetValue(wcid.Value, out var bucket)
                    && bucket?.Stats != null && bucket.Stats.ContainsKey(stat))
                    return "wcid";

                if (area.Profile.Minion?.Stats != null && area.Profile.Minion.Stats.ContainsKey(stat))
                    return "zone";

                var def = DefaultFor(area.Variation);
                if (def?.Profile?.Stats != null && def.Profile.Stats.ContainsKey(stat))
                    return "default v" + area.Variation;

                return null;
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

        // ── per-variation Defaults (2026-07-30) ──

        /// <summary>Read-only snapshot of a variation's Default, or null when none is authored.</summary>
        public static VariationDefault GetVariationDefault(int variation)
        {
            EnsureInitialized();
            lock (_lock)
                return DefaultFor(variation);
        }

        /// <summary>Variations that currently have an authored Default, ascending.</summary>
        public static List<int> ListVariationDefaults()
        {
            EnsureInitialized();
            lock (_lock)
                return _variationDefaults.Keys.OrderBy(v => v).ToList();
        }

        /// <summary>
        /// Atomically read-modify-write a variation's Default, creating it on first touch, then persist and
        /// republish the snapshot (so every zone at that variation picks the change up live). Mirrors
        /// <see cref="MutateArea"/>. Prunes the entry again if the mutation left it empty.
        /// </summary>
        public static void MutateVariationDefault(int variation, Action<VariationDefault> mutate)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (!_variationDefaults.TryGetValue(variation, out var def) || def == null)
                    _variationDefaults[variation] = def = new VariationDefault();

                def.Profile ??= new ZoneVariantProfile();
                def.Effects ??= new ZoneEffects();
                def.Appearance ??= new ZoneAppearance();

                mutate(def);

                if (def.IsEmpty && string.IsNullOrWhiteSpace(def.Notes))
                    _variationDefaults.Remove(variation);

                Save();
            }
        }

        /// <summary>Drop a variation's Default entirely. Returns false when there wasn't one.</summary>
        public static bool ClearVariationDefault(int variation)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (!_variationDefaults.Remove(variation))
                    return false;
                Save();
                return true;
            }
        }

        /// <summary>Deep-copy one variation's Default over another (seed v12 from v11, then tweak).
        /// Returns false when the source has no Default.</summary>
        public static bool CopyVariationDefault(int from, int to)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var src = DefaultFor(from);
                if (src == null)
                    return false;

                _variationDefaults[to] = new VariationDefault
                {
                    // Merge against nothing = a clean deep copy with every nested value cloned
                    Profile = ZoneVariantProfile.Merge(src.Profile),
                    Effects = src.Effects?.Clone() ?? new ZoneEffects(),
                    Appearance = src.Appearance?.Clone() ?? new ZoneAppearance(),
                    Notes = src.Notes,
                };
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

        /// <summary>Which of a zone's WCIDs carry a per-monster override, for the plugin's roster badges.
        /// Bit 1 = stat/prop override (a non-empty WcidOverrides bucket - the full standalone stat set),
        /// bit 2 = appearance override. A WCID absent from the map runs on the zone defaults.</summary>
        public static Dictionary<uint, int> GetAreaMobOverrideFlags(string name)
        {
            EnsureInitialized();
            var flags = new Dictionary<uint, int>();
            lock (_lock)
            {
                var area = FindArea(name);
                if (area == null)
                    return flags;

                foreach (var kv in area.Profile.WcidOverrides)
                    if (kv.Value != null && !VariantIsEmpty(kv.Value))
                        flags[kv.Key] = 1;

                if (area.AppearanceByWcid != null)
                    foreach (var kv in area.AppearanceByWcid)
                        if (kv.Value != null && !kv.Value.IsEmpty)
                            flags[kv.Key] = flags.TryGetValue(kv.Key, out var f) ? f | 2 : 2;
            }
            return flags;
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
                // no return: creature-generators (Kingpin-style boss anchors) host their trash ring
                // in their own generator profiles - walk them too or ring mobs never list
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

        /// <summary>Distinct placed generator wcids (+ placement counts) across a zone's landblocks at
        /// the zone's variation — the plugin Generator Settings discovery list ([[ZCL]], genlist).
        /// Lean sibling of <see cref="SurveyArea"/>: no creature-tree expansion. Null = no such zone.</summary>
        public static List<SurveyPlacedRow> GetPlacedGenerators(string name)
        {
            EnsureInitialized();
            List<ushort> lbs;
            int variation;
            lock (_lock)
            {
                var area = FindArea(name);
                if (area == null)
                    return null;
                lbs = area.Landblocks.OrderBy(x => x).ToList();
                variation = area.Variation;
            }

            var placed = new Dictionary<uint, SurveyPlacedRow>();
            foreach (var lb in lbs)
                AccumulatePlacedGenerators(lb, variation, placed);
            return placed.Values.OrderBy(p => p.Wcid).ToList();
        }

        /// <summary>Same scan for one landblock — genlist's fallback when the player is outside any zone.</summary>
        public static List<SurveyPlacedRow> GetPlacedGeneratorsForLandblock(ushort landblock, int variation)
        {
            var placed = new Dictionary<uint, SurveyPlacedRow>();
            AccumulatePlacedGenerators(landblock, variation, placed);
            return placed.Values.OrderBy(p => p.Wcid).ToList();
        }

        private static void AccumulatePlacedGenerators(ushort lb, int variation, Dictionary<uint, SurveyPlacedRow> placed)
        {
            var instances = DatabaseManager.World.GetCachedInstancesByLandblock(lb, variation);
            foreach (var inst in instances)
            {
                var weenie = DatabaseManager.World.GetCachedWeenie(inst.WeenieClassId);
                if (!(weenie?.PropertiesGenerator is { Count: > 0 }))
                    continue;
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
