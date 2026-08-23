using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Live stat resolution (owner 2026-08-22, Plan_LiveStatResolution_2026-08-22.md): a T11+ piece carries
    /// a GRADE per Zone Control line (0-1000 = where in the band it rolled) plus the loot tier; the numbers
    /// retail code reads (Gear* ratings, Armor Level, the 502xx cantrip props) are a CACHE resolved from the
    /// grades against the LIVE ladder. Weapon scaling already works this way (quality 0-1000); this is the
    /// same model for armor / jewelry lines, the core four, Armor Level and the slot specials.
    ///
    /// Record format, PropertyString.ZcLines: "28:490;19:1000;c1:900;c2:850;c3:900;c4:880;25:500;41:650"
    ///   - positive key  = ZoneCantrips catalog key (lines AND specials), value = grade 0-1000
    ///   - c1..c4        = the core four (DamageResist / CritDamageResist / CritResist / NetherResist)
    ///   - Reinforced (49) is NOT recorded: an earned, frozen armor mod (owner ruling 2026-08-22 §8.3)
    /// PropertyInt.ZcTier = the ladder row; PropertyInt.ZcResolvedVersion = the per-tier ladder version the
    /// cache was last written against (ZoneControlManager.GetLadderVersion).
    ///
    /// Three entry points:
    ///   <see cref="Compute"/>      - PURE: grades -> resolved values. Appraisal uses this (read-only rule §3b).
    ///   <see cref="ApplyIfStale"/> - EQUIP ONLY: re-stamp the cache when the tier's ladder version moved.
    ///   <see cref="Apply"/>        - the unconditional stamp used at DROP time by the producers.
    /// Nothing here walks biotas; items catch up lazily when worn (plan §8.2: lazy only).
    /// </summary>
    public static class ZoneStatResolver
    {
        public const int GradeMax = 1000;

        /// <summary>Core-four pseudo keys inside the record (negative so they never collide with catalog keys).</summary>
        public const int CoreDamageResist = -1;
        public const int CoreCritDamageResist = -2;
        public const int CoreCritResist = -3;
        public const int CoreNetherResist = -4;

        public static readonly int[] CoreKeys = { CoreDamageResist, CoreCritDamageResist, CoreCritResist, CoreNetherResist };

        public static PropertyInt CoreProp(int coreKey) => coreKey switch
        {
            CoreDamageResist => PropertyInt.GearDamageResist,
            CoreCritDamageResist => PropertyInt.GearCritDamageResist,
            CoreCritResist => PropertyInt.GearCritResist,
            _ => PropertyInt.GearNetherResist,
        };

        public static string CoreName(int coreKey) => coreKey switch
        {
            CoreDamageResist => "Damage Resist",
            CoreCritDamageResist => "Crit Damage Resist",
            CoreCritResist => "Crit Resist",
            _ => "Nether Resist",
        };

        public static bool IsCoreKey(int key) => key <= CoreDamageResist && key >= CoreNetherResist;

        // ── grade math ──────────────────────────────────────────────────────────

        /// <summary>Grade -> value inside an inclusive band. Linear, rounded once.</summary>
        public static int ValueFor(int min, int max, int grade)
        {
            if (min > max) (min, max) = (max, min);
            grade = Math.Clamp(grade, 0, GradeMax);
            return min + (int)Math.Round((max - min) * (grade / (double)GradeMax));
        }

        /// <summary>Value -> grade (migration of pre-grade items). Flat band = 1000.</summary>
        public static int GradeFor(int min, int max, int value)
        {
            if (min > max) (min, max) = (max, min);
            if (max == min) return GradeMax;
            return Math.Clamp((int)Math.Round((value - min) * (double)GradeMax / (max - min)), 0, GradeMax);
        }

        /// <summary>
        /// Roll a grade 0-1000 with the tier-weighted third (ZoneCantrips.TierThirds, Option A: T11 uniform,
        /// T25 10/30/60). forceMax = 1000. The producers roll THIS and derive the value with
        /// <see cref="ValueFor"/>, so the grade is the truth and the value its projection.
        /// </summary>
        public static int RollGrade(int tier, bool forceMax = false)
        {
            if (forceMax) return GradeMax;
            var (wLo, wMid, wHi) = ZoneCantrips.TierThirds(tier);
            var pick = ThreadSafeRandom.Next(0, wLo + wMid + wHi - 1);
            if (pick < wLo) return ThreadSafeRandom.Next(0, 333);
            if (pick < wLo + wMid) return ThreadSafeRandom.Next(334, 666);
            return ThreadSafeRandom.Next(667, GradeMax);
        }

        // ── the live ladder ─────────────────────────────────────────────────────

        /// <summary>
        /// The band a catalog key resolves against at a tier: the tier's Default-layer override
        /// (CustomCantripBands on variation = tier, what real drops there roll from) when authored, else the
        /// catalog band scaled to the tier (ZoneCantrips.CatalogBandAt). A ZONE's own band override is a drop-time concern only - the piece does not remember
        /// its zone, and re-resolution only happens after an explicit ladder apply, when the tier's Default
        /// IS the published truth.
        /// </summary>
        public static (int Min, int Max) EffectiveBand(int key, int tier)
        {
            if (!ZoneCantrips.TryGet(key, out var def))
                return (0, 0);
            var vd = ZoneControlManager.GetVariationDefault(tier);
            if (vd?.Profile?.CustomCantripBands != null
                && vd.Profile.CustomCantripBands.TryGetValue(key, out var live)
                && live != null && live.Max > 0)
                return live.Min <= live.Max ? (live.Min, live.Max) : (live.Max, live.Min);
            return ZoneCantrips.CatalogBandAt(def, tier);
        }

        /// <summary>A core_anchor_* knob from the tier's Default layer, else the C# default.</summary>
        private static double DefaultLayerStat(int tier, string stat, double fallback)
        {
            var vd = ZoneControlManager.GetVariationDefault(tier);
            if (vd?.Profile?.Stats != null && vd.Profile.Stats.TryGetValue(stat, out var curve) && curve != null)
                return curve.Evaluate(1);
            return fallback;
        }

        /// <summary>
        /// The core-four window at a tier - THE SAME formula as LootGenerationFactory.ApplyT11GearStats.RollCore
        /// (cap = anchor/18 x (1+(t-11)/14), fixed T11 step = anchor/18/14, floor = cap - 1.5 step, T11 - 0.5 step,
        /// rounded at the end). Anchors: the tier's Default-layer core_anchor_dr / core_anchor_cdr, else 1250 / 750.
        /// anchorOverride lets a drop-time caller pass the ZONE's evaluated anchors instead.
        /// </summary>
        public static (int Min, int Max) CoreWindow(int coreKey, int tier, double? anchorOverride = null)
        {
            var isDr = coreKey == CoreDamageResist;
            var anchor = anchorOverride ?? DefaultLayerStat(tier, isDr ? ZoneStat.CoreAnchorDr : ZoneStat.CoreAnchorCdr, isDr ? 1250.0 : 750.0);
            var scale = 1.0 + (tier - 11) / 14.0;
            var cap = anchor / 18.0 * scale;
            var step = anchor / 18.0 / 14.0;
            var lo = cap - (tier == 11 ? 0.5 : 1.5) * step;
            var min = (int)Math.Round(lo);
            var max = (int)Math.Round(cap);
            if (min > max) min = max;
            return (min, max);
        }

        /// <summary>The fixed per-tier armor base (ApplyT11GearStats): 1100 + 100/tier above 11.</summary>
        public static int BaseArmorLevel(int tier) => 1100 + 100 * (tier - 11);

        // ── the record ──────────────────────────────────────────────────────────

        public struct LineRecord
        {
            public int Key;      // catalog key, or a Core* pseudo key
            public int Grade;    // 0-1000
        }

        public static bool HasRecord(WorldObject wo)
            => wo != null && !string.IsNullOrEmpty(wo.GetProperty(PropertyString.ZcLines));

        public static int TierOf(WorldObject wo) => wo?.GetProperty(PropertyInt.ZcTier) ?? 0;

        /// <summary>Parse the record. Unknown / malformed entries are skipped, never thrown on.</summary>
        public static List<LineRecord> Read(WorldObject wo)
        {
            var list = new List<LineRecord>();
            var raw = wo?.GetProperty(PropertyString.ZcLines);
            if (string.IsNullOrEmpty(raw))
                return list;
            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = part.IndexOf(':');
                if (i <= 0) continue;
                var keyTok = part.Substring(0, i).Trim();
                if (!int.TryParse(part.Substring(i + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var grade))
                    continue;
                int key;
                if (keyTok.Length == 2 && (keyTok[0] == 'c' || keyTok[0] == 'C') && keyTok[1] >= '1' && keyTok[1] <= '4')
                    key = -(keyTok[1] - '0');
                else if (!int.TryParse(keyTok, NumberStyles.Integer, CultureInfo.InvariantCulture, out key))
                    continue;
                list.Add(new LineRecord { Key = key, Grade = Math.Clamp(grade, 0, GradeMax) });
            }
            return list;
        }

        public static string Format(IEnumerable<LineRecord> lines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                if (sb.Length > 0) sb.Append(';');
                if (IsCoreKey(l.Key)) sb.Append('c').Append(-l.Key);
                else sb.Append(l.Key.ToString(CultureInfo.InvariantCulture));
                sb.Append(':').Append(Math.Clamp(l.Grade, 0, GradeMax).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static void Write(WorldObject wo, List<LineRecord> lines)
        {
            if (wo == null) return;
            if (lines == null || lines.Count == 0)
                wo.RemoveProperty(PropertyString.ZcLines);
            else
                wo.SetProperty(PropertyString.ZcLines, Format(lines));
        }

        /// <summary>Append one grade to the record (a key already present is REPLACED - one line per key per piece).</summary>
        public static void AddLine(WorldObject wo, int key, int grade)
        {
            if (wo == null) return;
            var lines = Read(wo);
            lines.RemoveAll(l => l.Key == key);
            lines.Add(new LineRecord { Key = key, Grade = Math.Clamp(grade, 0, GradeMax) });
            Write(wo, lines);
        }

        /// <summary>Stamp the tier + the CURRENT ladder version (a fresh drop is resolved by definition).</summary>
        public static void StampIdentity(WorldObject wo, int tier)
        {
            if (wo == null) return;
            wo.SetProperty(PropertyInt.ZcTier, tier);
            wo.SetProperty(PropertyInt.ZcResolvedVersion, ZoneControlManager.GetLadderVersion(tier).Version);
        }

        // ── resolution ──────────────────────────────────────────────────────────

        public class ResolvedLine
        {
            public LineRecord Record;
            public ZoneCantrips.Def Def;     // null for a core key
            public int Min, Max, Value;
            public string Name => Def?.Name ?? CoreName(Record.Key);
            /// <summary>The appraisal text for this line, in the stamp format ("Damage Rating +41 [14-69]").</summary>
            public string Text
            {
                get
                {
                    if (Def == null)
                        return $"{Name} +{Value} [{Min}-{Max}]";
                    if (Def.SlotSpecial)
                        return string.IsNullOrEmpty(Def.ValFmt) ? Name : $"{Name} {string.Format(Def.ValFmt, Value)}";
                    return $"{Name} {string.Format(Def.ValFmt ?? "+{0}", Value)} [{Min}-{Max}]";
                }
            }
        }

        public class Resolved
        {
            public int Tier;
            public List<ResolvedLine> Lines = new();
            /// <summary>Every int prop the cache should hold, SET semantics (retail Gear* + 502xx block).</summary>
            public Dictionary<PropertyInt, int> Ints = new();
            /// <summary>Resolved Armor Level (base for tier + the key-25 line), null when the piece has no AL.</summary>
            public int? ArmorLevel;
        }

        /// <summary>
        /// PURE: grades -> resolved values against the live ladder. Never touches the item. Returns null when
        /// the piece carries no record. Core keys resolve through <see cref="CoreWindow"/>; catalog keys
        /// through <see cref="EffectiveBand"/>; key 25 adds to the tier's base AL; every Ints prop of a def
        /// gets the value (All Attributes = six props, same number); Reinforced is skipped (frozen).
        /// </summary>
        public static Resolved Compute(WorldObject wo)
        {
            if (!HasRecord(wo))
                return null;
            var tier = TierOf(wo);
            if (tier <= 0) tier = 11;
            var r = new Resolved { Tier = tier };
            var hasArmor = wo.ArmorLevel.HasValue && wo.ItemType == ItemType.Armor;
            var alBonus = 0;
            var hasAlLine = false;

            foreach (var rec in Read(wo))
            {
                if (IsCoreKey(rec.Key))
                {
                    var (cmin, cmax) = CoreWindow(rec.Key, tier);
                    var cval = ValueFor(cmin, cmax, rec.Grade);
                    r.Lines.Add(new ResolvedLine { Record = rec, Def = null, Min = cmin, Max = cmax, Value = cval });
                    r.Ints[CoreProp(rec.Key)] = cval;
                    continue;
                }
                if (!ZoneCantrips.TryGet(rec.Key, out var def) || def.SetsProtection)
                    continue;
                var (min, max) = EffectiveBand(rec.Key, tier);
                var value = ValueFor(min, max, rec.Grade);
                r.Lines.Add(new ResolvedLine { Record = rec, Def = def, Min = min, Max = max, Value = value });
                if (def.ArmorOnly && def.Ints == null)
                {
                    alBonus += value;                       // key 25 Armor Level
                    hasAlLine = true;
                }
                else if (def.Ints != null)
                    foreach (var (propId, _) in def.Ints)
                        r.Ints[(PropertyInt)propId] = value;
            }

            // AL is resolved ONLY when the piece carries an Armor Level line (a piece without one keeps its tier base)
            if (hasArmor && hasAlLine)
                r.ArmorLevel = BaseArmorLevel(tier) + alBonus;
            return r;
        }

        /// <summary>
        /// Write a Resolved set onto the item (SET semantics - ZC owns these props on a T11+ piece). With
        /// allowNerf false a value that would DROP below what is stamped keeps the stamped number (owner
        /// policy: an accidental apply fixes under-tuned gear, never silently cuts anyone). Stamps the
        /// version. Returns the number of props that actually changed.
        /// </summary>
        public static int Apply(WorldObject wo, Resolved r, bool allowNerf = true)
        {
            if (wo == null || r == null) return 0;
            var changed = 0;
            foreach (var kv in r.Ints)
            {
                var cur = wo.GetProperty(kv.Key);
                var next = kv.Value;
                if (!allowNerf && cur.HasValue && next < cur.Value)
                    next = cur.Value;
                if (cur != next)
                {
                    wo.SetProperty(kv.Key, next);
                    changed++;
                }
            }
            if (r.ArmorLevel.HasValue)
            {
                var cur = wo.ArmorLevel;
                var next = r.ArmorLevel.Value;
                if (!allowNerf && cur.HasValue && next < cur.Value)
                    next = cur.Value;
                if (cur != next)
                {
                    wo.ArmorLevel = next;
                    changed++;
                }
            }
            wo.SetProperty(PropertyInt.ZcResolvedVersion, ZoneControlManager.GetLadderVersion(r.Tier).Version);
            return changed;
        }

        /// <summary>
        /// EQUIP hook: when the piece's ZcResolvedVersion lags its tier's ladder version, re-resolve and
        /// re-stamp (honouring that apply's AllowNerf), mark the biota dirty. Cheap no-op for every piece
        /// without a record, and for a resolved one. Bounded by what a character can wear - never called
        /// from appraisal (plan §3b).
        /// </summary>
        public static bool ApplyIfStale(WorldObject wo)
        {
            if (!HasRecord(wo))
                return false;
            var tier = TierOf(wo);
            if (tier <= 0) return false;
            var ladder = ZoneControlManager.GetLadderVersion(tier);
            var seen = wo.GetProperty(PropertyInt.ZcResolvedVersion) ?? 0;
            if (seen >= ladder.Version)
                return false;
            var r = Compute(wo);
            if (r == null) return false;
            var changed = Apply(wo, r, ladder.AllowNerf);
            wo.ChangesDetected = true;
            if (changed > 0)
                wo.SaveBiotaToDatabase();
            return changed > 0;
        }
    }
}
