using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

using log4net;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>Which matrix COLUMN a target item sits in. The owner has ruled that jewelry, trinkets
    /// and cloaks all read as "Armor" in the plugin UI, but they stay SEPARATE here so a rule can still
    /// single one of them out - collapsing them at the data layer would make that impossible to author
    /// later without a store migration.</summary>
    public enum CraftItemClass
    {
        Weapon = 0,
        Armor = 1,
        Shield = 2,
        Jewelry = 3,
        Cloak = 4,
    }

    /// <summary>A matrix cell. <see cref="Auto"/> is the default and is NEVER stored - it means "no
    /// opinion, fall through to the downgrade rule" (layer 2).</summary>
    public enum CraftRuleMode
    {
        Auto = 0,
        Allow = 1,
        Deny = 2,
    }

    /// <summary>One authored cell: (material x item class) -> Allow / Deny. Persisted shape; the wire
    /// and the JSON both use the NAMES, never the ordinals, so reordering either enum is safe.</summary>
    public class CraftRule
    {
        /// <summary>PropertyInt.MaterialType (131) of the SALVAGE item.</summary>
        public int Material { get; set; }
        public string ItemType { get; set; }
        public string Mode { get; set; }
    }

    /// <summary>
    /// LAYER 1 of the T11+ crafting gate (Craft_Gate_Plan_2026-08-24.md): the authorable
    /// (item type x salvage material) matrix that sits ABOVE the downgrade rule in
    /// <see cref="ZoneCraftGate"/>.
    ///
    ///   1. MATRIX     explicit Allow / Deny  -> obey it, stop
    ///   2. DOWNGRADE  layer 2, unchanged     -> "a weaker imbue cant go on, but not imbued could"
    ///   3. DEFAULT    allow
    ///
    /// The matrix is SPARSE: only non-Auto cells are stored, so an untouched install persists `{}` and
    /// behaves exactly as the deployed layer-2-only gate does.
    ///
    /// PERSISTENCE follows <see cref="ZoneControlManager"/> exactly - one JSON blob in
    /// ace_shard.config_properties_string, and <see cref="Load"/> builds into LOCALS and commits only
    /// after the read and the parse have both succeeded. That ordering is not cosmetic: the zone store
    /// was fixed on 2026-08-23 because clearing the live collections before the read meant a DB blip
    /// emptied memory, and the next edit then wrote that empty store straight over the real one.
    /// </summary>
    public static class ZoneCraftGateStore
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const string StoreKey = "craftgate_data";

        /// <summary>The matrix column order the wire publishes, and the order `craft list` prints.
        /// APPEND-ONLY - the plugin renders columns from this list by NAME.</summary>
        public static readonly CraftItemClass[] Columns =
        {
            CraftItemClass.Weapon,
            CraftItemClass.Armor,
            CraftItemClass.Shield,
            CraftItemClass.Jewelry,
            CraftItemClass.Cloak,
        };

        private class Store
        {
            /// <summary>Master switch. False bypasses the WHOLE gate (layer 2 included), so the gate can
            /// be turned off without clearing authored rules.</summary>
            [DefaultValue(true)]
            public bool Enabled { get; set; } = true;

            /// <summary>The tier at which the gate starts applying. Replaces the hardcoded
            /// LootGenerationFactory.ZoneLootSetMinTier comparison; 11 is still the default.</summary>
            [DefaultValue(LootGenerationFactory.ZoneLootSetMinTier)]
            public int MinTier { get; set; } = LootGenerationFactory.ZoneLootSetMinTier;

            /// <summary>Non-Auto cells only. Null (not []) when empty, so an untouched store is `{}`.</summary>
            public List<CraftRule> Rules { get; set; }
        }

        /// <summary>Serializer settings that make the sparse store actually sparse: a store at every
        /// default serializes to `{}`.</summary>
        private static readonly JsonSerializerSettings SparseJson = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
        };

        private static readonly object _lock = new object();
        private static volatile bool _initialized;

        // ── lock-free read snapshot ──
        // IsBlocked runs on the crafting path; it reads these volatiles with no lock. Mutations rebuild
        // a fresh dictionary under _lock and swap it in, so a reader sees the old map or the new one,
        // never a half-written one.
        private static volatile Dictionary<(int Material, CraftItemClass Class), CraftRuleMode> _rules
            = new Dictionary<(int, CraftItemClass), CraftRuleMode>();
        private static volatile bool _enabled = true;
        private static volatile int _minTier = LootGenerationFactory.ZoneLootSetMinTier;

        #region init / persistence

        public static void EnsureLoaded() => EnsureInitialized();

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_lock)
            {
                if (_initialized)
                    return;

                // Load() is atomic (build-then-commit): a failure leaves whatever was already loaded
                // untouched, which on first init is the safe all-Auto default.
                try { Load(); }
                catch (Exception ex) { log.Error($"ZoneCraftGateStore: failed to load {StoreKey}; keeping the current matrix ({_rules.Count} rule(s)). {ex}"); }

                _initialized = true;
            }
        }

        /// <summary>Re-read the store from the shard. Same atomic build-then-commit as
        /// ZoneControlManager.Load - nothing live is disturbed until the parse has succeeded.</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                Load();
                _initialized = true;
            }
        }

        private static void Load()
        {
            string json = null;
            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                json = DatabaseManager.ShardConfig.GetString(StoreKey)?.Value;

            var store = string.IsNullOrWhiteSpace(json)
                ? new Store()
                : (JsonConvert.DeserializeObject<Store>(json) ?? new Store());

            // ── build into locals; nothing live is disturbed if any of this throws ──
            var rules = new Dictionary<(int, CraftItemClass), CraftRuleMode>();
            if (store.Rules != null)
            {
                foreach (var r in store.Rules)
                {
                    if (r == null) continue;
                    if (!Enum.TryParse<CraftItemClass>(r.ItemType, true, out var cls)) continue;
                    if (!Enum.TryParse<CraftRuleMode>(r.Mode, true, out var mode)) continue;
                    if (mode == CraftRuleMode.Auto) continue;   // sparse: Auto is never stored
                    rules[(r.Material, cls)] = mode;
                }
            }
            var minTier = store.MinTier > 0 ? store.MinTier : LootGenerationFactory.ZoneLootSetMinTier;

            // ── commit: from here on nothing can throw ──
            _rules = rules;
            _enabled = store.Enabled;
            _minTier = minTier;
        }

        private static void Save()
        {
            var list = _rules.Count == 0
                ? null
                : _rules.OrderBy(kv => kv.Key.Material).ThenBy(kv => (int)kv.Key.Class)
                        .Select(kv => new CraftRule
                        {
                            Material = kv.Key.Material,
                            ItemType = kv.Key.Class.ToString(),
                            Mode = kv.Value.ToString(),
                        }).ToList();

            var store = new Store { Enabled = _enabled, MinTier = _minTier, Rules = list };
            var jsonOut = JsonConvert.SerializeObject(store, SparseJson);

            if (DatabaseManager.ShardConfig.StringExists(StoreKey))
                DatabaseManager.ShardConfig.SaveString(new ConfigPropertiesString { Key = StoreKey, Value = jsonOut, Description = "T11+ crafting gate matrix (JSON)" });
            else
                DatabaseManager.ShardConfig.AddString(StoreKey, jsonOut, "T11+ crafting gate matrix (JSON)");
        }

        #endregion

        #region read

        /// <summary>Master switch. False = the whole gate is bypassed, layer 2 included.</summary>
        public static bool Enabled
        {
            get { EnsureInitialized(); return _enabled; }
        }

        /// <summary>The tier the gate starts applying at (default 11).</summary>
        public static int MinTier
        {
            get { EnsureInitialized(); return _minTier; }
        }

        /// <summary>The authored cell for (material, item class), or Auto when nothing is authored.</summary>
        public static CraftRuleMode GetMode(int material, CraftItemClass cls)
        {
            EnsureInitialized();
            return _rules.TryGetValue((material, cls), out var m) ? m : CraftRuleMode.Auto;
        }

        /// <summary>Every authored (non-Auto) cell, in a stable order.</summary>
        public static List<(int Material, CraftItemClass Class, CraftRuleMode Mode)> ListRules()
        {
            EnsureInitialized();
            return _rules.OrderBy(kv => kv.Key.Material).ThenBy(kv => (int)kv.Key.Class)
                         .Select(kv => (kv.Key.Material, kv.Key.Class, kv.Value)).ToList();
        }

        public static int RuleCount { get { EnsureInitialized(); return _rules.Count; } }

        #endregion

        #region write

        /// <summary>Author one cell. Auto REMOVES the row (the matrix stays sparse). Returns false when
        /// nothing changed, so a caller can say so instead of writing the shard for no reason.</summary>
        public static bool SetMode(int material, CraftItemClass cls, CraftRuleMode mode)
        {
            EnsureInitialized();
            lock (_lock)
            {
                var next = new Dictionary<(int, CraftItemClass), CraftRuleMode>(_rules);
                var had = next.TryGetValue((material, cls), out var cur);
                if (mode == CraftRuleMode.Auto)
                {
                    if (!had) return false;
                    next.Remove((material, cls));
                }
                else
                {
                    if (had && cur == mode) return false;
                    next[(material, cls)] = mode;
                }
                _rules = next;
                Save();
                return true;
            }
        }

        public static void SetEnabled(bool on)
        {
            EnsureInitialized();
            lock (_lock) { _enabled = on; Save(); }
        }

        public static void SetMinTier(int tier)
        {
            EnsureInitialized();
            lock (_lock) { _minTier = tier; Save(); }
        }

        #endregion

        #region item classification

        /// <summary>Which matrix column this target sits in, or null when the item is not something the
        /// matrix has a column for (a container, a component, a gem) - such a target skips layer 1 and
        /// falls through to the downgrade rule exactly as before.
        ///
        /// ORDER MATTERS. Cloak is tested before Jewelry and before the armour-level test because a
        /// cloak carries an ArmorLevel and EquipMask.Jewelry includes EquipMask.Cloak; shields are
        /// tested before armour for the same reason. This mirrors GetZoneLootDisplayOrder
        /// (LootGenerationFactory_ZoneSet.cs), which is the classification the loot pipeline already
        /// uses, so a piece lands in the same bucket on both sides.</summary>
        public static CraftItemClass? Classify(WorldObject wo)
        {
            if (wo == null)
                return null;

            if (wo is MeleeWeapon || wo is MissileLauncher || wo is Missile || wo is Caster)
                return CraftItemClass.Weapon;

            if (wo.IsShield)
                return CraftItemClass.Shield;

            if (ACE.Server.Entity.Cloak.IsCloak(wo))
                return CraftItemClass.Cloak;

            if (wo.ItemType == ItemType.Jewelry)
                return CraftItemClass.Jewelry;

            if ((wo.ArmorLevel ?? 0) > 0 || wo is Clothing)
                return CraftItemClass.Armor;

            return null;
        }

        #endregion

        #region material naming + catalog

        /// <summary>Parse a material given by NAME (BlackOpal, "black opal", black_opal) or by numeric
        /// MaterialType id. Name is the owner-facing form; the id is what the store and the wire carry.</summary>
        public static bool TryParseMaterial(string s, out int material)
        {
            material = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            var t = s.Trim();
            if (int.TryParse(t, out var num) && num > 0 && Enum.IsDefined(typeof(MaterialType), (uint)num))
            {
                material = num;
                return true;
            }

            var squashed = t.Replace(" ", "").Replace("_", "").Replace("-", "");
            if (Enum.TryParse<MaterialType>(squashed, true, out var mt) && mt != MaterialType.Unknown)
            {
                material = (int)mt;
                return true;
            }
            return false;
        }

        public static string MaterialName(int material)
        {
            var name = Enum.GetName(typeof(MaterialType), (uint)material);
            return string.IsNullOrEmpty(name) ? "Material " + material : name;
        }

        /// <summary>One row of the material catalog the plugin renders as matrix rows: the salvage
        /// material, its name, and (when it is a stock salvage whose recipe carries a mapped mutation
        /// DataId) the imbue it applies.</summary>
        public readonly struct ImbueMaterial
        {
            public readonly int Material;
            public readonly string Name;
            public readonly ImbuedEffectType Effect;

            public ImbueMaterial(int material, string name, ImbuedEffectType effect)
            {
                Material = material; Name = name; Effect = effect;
            }
        }

        private static volatile List<ImbueMaterial> _catalog;

        /// <summary>The materials that carry an imbue (salvage_Type 2) recipe - 33 of the 78 MaterialType
        /// values as of 2026-08-24. Read once from the world DB and cached: this is static content, and
        /// the only callers are the admin verbs and the wire payload.
        ///
        /// Query equivalent:
        ///   SELECT DISTINCT wi.value FROM cook_book cb JOIN recipe r ON r.id=cb.recipe_Id
        ///   JOIN weenie_properties_int wi ON wi.object_Id=cb.source_W_C_I_D AND wi.type=131
        ///   WHERE r.salvage_Type=2
        ///
        /// A DB failure returns an EMPTY list rather than throwing - the matrix does not depend on this,
        /// it is a naming convenience for the UI, and a craft decision must never fail on it.</summary>
        public static List<ImbueMaterial> ImbueMaterials()
        {
            var cached = _catalog;
            if (cached != null)
                return cached;

            var rows = new List<ImbueMaterial>();
            try
            {
                using var ctx = new WorldDbContext();

                // material id -> the mutation DataIds of every salvage_Type 2 recipe that salvage feeds
                var pairs = ctx.CookBook
                    .Join(ctx.Recipe.Where(r => r.SalvageType == 2), cb => cb.RecipeId, r => r.Id,
                          (cb, r) => new { cb.SourceWCID, r.Id })
                    .Join(ctx.WeeniePropertiesInt.Where(wi => wi.Type == (ushort)ACE.Entity.Enum.Properties.PropertyInt.MaterialType),
                          x => x.SourceWCID, wi => wi.ObjectId, (x, wi) => new { Material = wi.Value, RecipeId = x.Id })
                    .Distinct().AsNoTracking().ToList();

                var recipeIds = pairs.Select(p => p.RecipeId).Distinct().ToList();
                var dataIds = ctx.RecipeMod.Where(m => recipeIds.Contains(m.RecipeId) && m.DataId != 0)
                    .Select(m => new { m.RecipeId, m.DataId }).AsNoTracking().ToList()
                    .GroupBy(m => m.RecipeId).ToDictionary(g => g.Key, g => g.Select(m => (uint)m.DataId).ToList());

                foreach (var g in pairs.GroupBy(p => p.Material).OrderBy(g => g.Key))
                {
                    var effect = ImbuedEffectType.Undef;
                    foreach (var rid in g.Select(p => p.RecipeId))
                    {
                        if (!dataIds.TryGetValue(rid, out var dids)) continue;
                        foreach (var did in dids)
                            if (ZoneCraftGate.TryGetImbueForDataId(did, out var e)) { effect = e; break; }
                        if (effect != ImbuedEffectType.Undef) break;
                    }
                    rows.Add(new ImbueMaterial(g.Key, MaterialName(g.Key), effect));
                }
            }
            catch (Exception ex)
            {
                log.Warn($"ZoneCraftGateStore: could not read the imbue material catalog; the matrix is unaffected. {ex.Message}");
                rows.Clear();
            }

            _catalog = rows;
            return rows;
        }

        #endregion
    }
}
