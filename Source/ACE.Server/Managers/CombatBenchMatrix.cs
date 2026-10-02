using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using ACE.Entity.Enum;
using ACE.Server.Command.Handlers;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Combat Bench MATRIX (owner 2026-10-02: "a full suite of testing settings ... a full data set over the course of a
    /// few hours"). A plan file (BenchPlans\&lt;name&gt;.json beside the server) lists the axes - player augs x buffs x
    /// weapon x power bar x monster stat overrides - and the runner walks every combination unattended on the caller's
    /// own character: set augs, re-equip + rebuff when needed, apply the monster overrides to the bench's OWN spawns
    /// only (in memory, never stored), then Math both ways (the player's hits AND the monster's hits on the player) and
    /// optionally a live fight. Every row goes to BenchResults\&lt;time&gt;_&lt;plan&gt;.jsonl for reading back.
    /// The character's augs are put back at the end.
    /// </summary>
    public static partial class CombatBench
    {
        // =========================================================================================================
        // Test-only monster stat overrides
        // =========================================================================================================

        /// <summary>Stat overrides for bench-spawned monsters: "all" plus per rank ("regular" / "leader" / "boss").
        /// Scale multiplies a stat the profile has; Set then replaces or adds.</summary>
        public sealed class MobOverlay
        {
            public string Label = "base";
            public readonly Dictionary<string, Dictionary<string, double>> Set = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, Dictionary<string, double>> Scale = new(StringComparer.OrdinalIgnoreCase);
            public bool IsEmpty => Set.Count == 0 && Scale.Count == 0;
        }

        private static readonly ConcurrentDictionary<uint, MobOverlay> Overlays = new ConcurrentDictionary<uint, MobOverlay>();
        private static readonly ConcurrentDictionary<uint, (EvaluatedProfile Base, EvaluatedProfile Result)> OverlayCache =
            new ConcurrentDictionary<uint, (EvaluatedProfile, EvaluatedProfile)>();

        /// <summary>ZoneControlManager.ResolveForCreature's last step: the bench monster's profile with its matrix overrides
        /// laid over it. A no-op for every other creature (one empty-dictionary check while no matrix runs).</summary>
        public static EvaluatedProfile ApplyOverlay(Creature creature, ZcRank rank, EvaluatedProfile profile)
        {
            if (Overlays.IsEmpty || profile == null || creature == null) return profile;
            var guid = creature.Guid.Full;
            if (!Overlays.TryGetValue(guid, out var ov) || ov.IsEmpty) return profile;
            if (OverlayCache.TryGetValue(guid, out var cached) && ReferenceEquals(cached.Base, profile)) return cached.Result;

            var rankKey = ZoneRank.Key(rank);
            Dictionary<string, double> Merge(Dictionary<string, Dictionary<string, double>> byRank)
            {
                Dictionary<string, double> m = null;
                foreach (var k in new[] { "all", rankKey })
                    if (k != null && byRank.TryGetValue(k, out var d))
                        foreach (var kv in d)
                            (m ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase))[kv.Key] = kv.Value;
                return m;
            }
            var set = Merge(ov.Set);
            var scale = Merge(ov.Scale);
            var result = set == null && scale == null ? profile : profile.WithStatOverlay(set, scale);
            OverlayCache[guid] = (profile, result);
            return result;
        }

        private static void DropOverlay(uint guid)
        {
            Overlays.TryRemove(guid, out _);
            OverlayCache.TryRemove(guid, out _);
        }

        // =========================================================================================================
        // Plans
        // =========================================================================================================

        /// <summary>One combination of the matrix.</summary>
        public sealed class MatrixStep
        {
            public string Weapon;
            public float Power;
            public string AugLabel = "current";
            public uint[] Augs;          // null = leave the character's augs as they are
            public long? Triune;         // Triune Weave (+1 creature / item / life) AND the four attack growth charms
                                         // at the same count (owner 2026-10-02: the attack aug matches item + life); null = leave
            public int? GearTier;        // this step's gear tier (the plan's tier when null)
            public int? Variation;       // teleport to the same spot at this variation first (null = stay where you are)
            public bool Buffs = true;    // false = dispel the character and its worn gear after equipping
            public MobOverlay Mob = new MobOverlay();
            public string Label => $"{AugLabel}{(Variation.HasValue ? $" @v{Variation}" : "")}{(GearTier.HasValue ? $" T{GearTier} gear" : "")}{(Triune.HasValue ? $" triune {Triune}" : "")} / buffs {(Buffs ? "on" : "off")} / {Weapon} p{Power.ToString("0.##", CultureInfo.InvariantCulture)} / {Mob.Label}";
        }

        private sealed class PlanDto
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public int Tier { get; set; } = 11;
            public string Suit { get; set; } = "avg";
            public string Element { get; set; } = "fire";
            public int Quality { get; set; } = 850;
            public string Mode { get; set; } = "math";            // math | both | live
            public List<uint> Targets { get; set; }
            public bool Pack { get; set; }
            public int Fights { get; set; } = 1;
            public int Timed { get; set; } = 0;
            public int Switch { get; set; } = 10;
            public int Heal { get; set; } = 0;
            public int Samples { get; set; } = 0;                   // the player's hits on the monster (Math); 0 = off
            public int TakenSamples { get; set; } = 20000;          // the monster's hits on the player (Math); 0 = off
            public int SpellSamples { get; set; } = 0;              // the monster's SPELL on the player (Math); 0 = off
            public uint? SpellId { get; set; }                      // which spell; unset = its first harmful war / void projectile
            public double SwingRate { get; set; } = 1.0;            // monster swings per second, for the Math time-to-die
            public List<string> Weapons { get; set; }
            public List<float> Powers { get; set; }
            public List<AugDto> Augs { get; set; }
            public List<bool> Buffs { get; set; }
            public List<MobDto> Mob { get; set; }
            public bool Loop { get; set; }
        }

        private sealed class AugDto
        {
            public string Label { get; set; }
            public List<uint> Values { get; set; }
            public long? Triune { get; set; }      // T16+ (owner 2026-10-02): Triune is the only way past the aug caps
            public int? Tier { get; set; }         // gear tier for this aug set (a tier's own Average gear)
            public int? Variation { get; set; }    // 2026-10-02: run this aug set at that variation (the bench teleports, same spot)
            public List<MobDto> Mob { get; set; }  // this aug set's OWN monster overrides (a tier's values) instead of the plan's list
        }

        private sealed class MobRankDto
        {
            public Dictionary<string, double> Set { get; set; }
            public Dictionary<string, double> Scale { get; set; }
        }

        private sealed class MobDto
        {
            public string Label { get; set; }
            public Dictionary<string, double> Set { get; set; }
            public Dictionary<string, double> Scale { get; set; }
            public MobRankDto Regular { get; set; }
            public MobRankDto Leader { get; set; }
            public MobRankDto Boss { get; set; }
        }

        private static readonly JsonSerializerOptions PlanJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static string ServerDir => AppContext.BaseDirectory;
        private static string PlanDir => Path.Combine(ServerDir, "BenchPlans");
        private static string ResultDir => Path.Combine(ServerDir, "BenchResults");

        private static PlanDto LoadPlan(string name, out string error)
        {
            error = null;
            var file = Path.Combine(PlanDir, name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
            if (!File.Exists(file)) { error = $"no plan '{name}' in {PlanDir}"; return null; }
            try
            {
                var plan = JsonSerializer.Deserialize<PlanDto>(File.ReadAllText(file), PlanJson);
                if (plan == null) { error = "empty plan file"; return null; }
                plan.Name ??= Path.GetFileNameWithoutExtension(file);
                return plan;
            }
            catch (Exception ex) { error = "bad JSON: " + ex.Message; return null; }
        }

        /// <summary>The plan's combinations, augs outermost (a re-equip each), monster overrides innermost (free).</summary>
        private static List<MatrixStep> BuildSteps(PlanDto plan, out string error)
        {
            error = null;
            var weapons = plan.Weapons is { Count: > 0 } ? plan.Weapons.Select(w => w.ToLowerInvariant()).ToList() : new List<string> { "ua" };
            var bad = weapons.Where(w => !WeaponScalingCommands.ForgeClasses.Any(c => c.Key == w) || w == "crossbow" || w == "atlatl" || w == "wand").ToList();
            if (bad.Count > 0) { error = "unsupported weapon(s): " + string.Join(", ", bad); return null; }
            var powers = plan.Powers is { Count: > 0 } ? plan.Powers : new List<float> { 0f };
            var augs = plan.Augs is { Count: > 0 } ? plan.Augs : new List<AugDto> { new AugDto { Label = "current" } };
            foreach (var a in augs)
                if (a.Values != null && a.Values.Count != 10) { error = $"augs '{a.Label}': need 10 values (creature,item,life,war,void,duration,specialize,summon,melee,missile)"; return null; }
            var buffs = plan.Buffs is { Count: > 0 } ? plan.Buffs : new List<bool> { true };
            string mobError = null;
            List<MobOverlay> Convert(List<MobDto> list)
            {
            var mobs = new List<MobOverlay>();
            foreach (var m in list is { Count: > 0 } ? list : new List<MobDto> { new MobDto { Label = "base" } })
            {
                var ov = new MobOverlay { Label = m.Label ?? "mob" };
                void Add(string rank, Dictionary<string, double> set, Dictionary<string, double> scale)
                {
                    if (set is { Count: > 0 }) ov.Set[rank] = new Dictionary<string, double>(set, StringComparer.OrdinalIgnoreCase);
                    if (scale is { Count: > 0 }) ov.Scale[rank] = new Dictionary<string, double>(scale, StringComparer.OrdinalIgnoreCase);
                }
                Add("all", m.Set, m.Scale);
                Add("regular", m.Regular?.Set, m.Regular?.Scale);
                Add("leader", m.Leader?.Set, m.Leader?.Scale);
                Add("boss", m.Boss?.Set, m.Boss?.Scale);
                var unknown = ov.Set.Values.Concat(ov.Scale.Values).SelectMany(d => d.Keys).Where(k => !ZoneStat.IsKnownStat(k)).Distinct().ToList();
                if (unknown.Count > 0) { mobError = $"mob '{ov.Label}': unknown stat(s) {string.Join(", ", unknown)}"; return null; }
                mobs.Add(ov);
            }
            return mobs;
            }
            var planMobs = Convert(plan.Mob);
            if (planMobs == null) { error = mobError; return null; }
            var augMobs = new Dictionary<AugDto, List<MobOverlay>>();
            foreach (var a in augs.Where(a => a.Mob is { Count: > 0 }))
            {
                var am = Convert(a.Mob);
                if (am == null) { error = $"augs '{a.Label}': {mobError}"; return null; }
                augMobs[a] = am;
            }

            var steps = new List<MatrixStep>();
            foreach (var a in augs)
                foreach (var b in buffs)
                    foreach (var w in weapons)
                        foreach (var p in powers)
                            foreach (var m in augMobs.TryGetValue(a, out var own) ? own : planMobs)
                                steps.Add(new MatrixStep
                                {
                                    Weapon = w,
                                    Power = Math.Clamp(p, 0f, 1f),
                                    AugLabel = a.Label ?? (a.Values == null ? "current" : string.Join("/", a.Values.Take(3))),
                                    Augs = a.Values?.ToArray(),
                                    Triune = a.Triune,
                                    GearTier = a.Tier.HasValue ? Math.Clamp(a.Tier.Value, 10, 25) : null,
                                    Variation = a.Variation,
                                    Buffs = b,
                                    Mob = m,
                                });
            if (steps.Count == 0) error = "the plan has no combinations";
            return steps;
        }

        /// <summary>Rough plan length in seconds: a re-equip ~4 s, Math ~1 s per target, a live fight its timed length
        /// (or ~60 s to the kill) per fight.</summary>
        private static double EstimateSeconds(PlanDto plan, List<MatrixStep> steps)
        {
            var targets = plan.Targets?.Count ?? 0;
            var regears = 0;
            MatrixStep prev = null;
            foreach (var s in steps)
            {
                if (prev == null || prev.Weapon != s.Weapon || prev.AugLabel != s.AugLabel || prev.GearTier != s.GearTier || (!prev.Buffs && s.Buffs)) regears++;
                prev = s;
            }
            var live = plan.Mode == "math" && !plan.Pack ? 0 : plan.Fights * ((plan.Timed > 0 ? plan.Timed : 60) + 3);
            return regears * 4 + steps.Count * targets * (1.0 + live);
        }

        /// <summary>/bench matrix list: one [[CBP]] line per plan file (the plugin's plan list) + a readable log line.</summary>
        public static void ListPlans(Player player)
        {
            Directory.CreateDirectory(PlanDir);
            var files = Directory.GetFiles(PlanDir, "*.json").OrderBy(f => f).ToList();
            if (files.Count == 0)
            {
                Log(player, $"matrix: no plans in {PlanDir}.");
                return;
            }
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var plan = LoadPlan(name, out var err);
                List<MatrixStep> steps = null;
                if (plan != null) steps = BuildSteps(plan, out err);
                var est = plan != null && steps != null ? EstimateSeconds(plan, steps) : 0;
                Send(player, $"[[CBP]]name={Wire(name)}|steps={(steps?.Count ?? 0)}|targets={(plan?.Targets?.Count ?? 0)}|est={est:0}|mode={Wire(plan?.Mode ?? "")}|desc={Wire(plan?.Description ?? "")}|err={Wire(err ?? "")}");
                Log(player, err != null ? $"plan {name}: ERROR {err}" : $"plan {name}: {steps.Count} steps x {plan.Targets?.Count ?? 0} targets, ~{est / 60:0} min - {plan.Description}");
            }
        }

        /// <summary>/bench matrix &lt;name&gt;: loads the plan and starts it as a sweep whose steps are the matrix.</summary>
        public static void StartMatrix(Player player, string name)
        {
            var plan = LoadPlan(name, out var err);
            if (plan == null) { Log(player, "matrix: " + err); return; }
            if (plan.Targets is not { Count: > 0 }) { Log(player, "matrix: the plan lists no targets."); return; }
            var mode = (plan.Mode ?? "math").ToLowerInvariant();
            if (mode != "math" && mode != "both" && mode != "live") { Log(player, "matrix: mode must be math, both or live."); return; }
            var suit = (plan.Suit ?? "avg").ToLowerInvariant();
            if (suit != "bis" && suit != "avg") { Log(player, "matrix: suit must be bis or avg."); return; }
            var steps = BuildSteps(plan, out err);
            if (steps == null) { Log(player, "matrix: " + err); return; }

            Directory.CreateDirectory(ResultDir);
            var results = Path.Combine(ResultDir, $"{DateTime.Now:yyyyMMdd-HHmmss}_{plan.Name}.jsonl");
            var sweep = new SweepSpec
            {
                Weapons = steps.Select(s => s.Weapon).Distinct().ToList(),
                Tier = Math.Clamp(plan.Tier, 10, 25),
                Bis = suit == "bis",
                Element = (plan.Element ?? "fire").ToLowerInvariant(),
                Quality = Math.Clamp(plan.Quality, 0, 1000),
                Loop = plan.Loop,
                Matrix = steps,
                PlanName = plan.Name,
                ResultsPath = results,
                TakenSamples = Math.Clamp(plan.TakenSamples, 0, MaxMathSamples),
                SpellSamples = Math.Clamp(plan.SpellSamples, 0, MaxMathSamples),
                SpellId = plan.SpellId,
                DealtSamples = Math.Clamp(plan.Samples, 0, MaxMathSamples),
                SwingRate = plan.SwingRate > 0 ? plan.SwingRate : 1.0,
            };
            Log(player, $"matrix {plan.Name}: {steps.Count} steps x {plan.Targets.Count} targets, ~{EstimateSeconds(plan, steps) / 60:0} min. Results: {results}");
            log.Info($"[CombatBench] {player.Name}: matrix {plan.Name} started, {steps.Count} steps, results {results}");

            var anyMath = sweep.TakenSamples > 0 || sweep.DealtSamples > 0 || sweep.SpellSamples > 0;
            if (plan.Pack)
                StartPack(player, plan.Targets, plan.Fights, plan.Heal, 0f, plan.Timed, plan.Switch, sweep, anyMath ? Math.Max(sweep.DealtSamples, 100) : 0);
            else
                Start(player, mode == "live" && anyMath ? "both" : mode, plan.Targets, plan.Fights, Math.Max(sweep.DealtSamples, 100), 0f, sweep);
        }

        // =========================================================================================================
        // The runner's matrix steps (called from the sweep's Gear phase)
        // =========================================================================================================

        private static uint[] CurrentAugs(Player p) => new[]
        {
            (uint)(p.LuminanceAugmentCreatureCount ?? 0), (uint)(p.LuminanceAugmentItemCount ?? 0), (uint)(p.LuminanceAugmentLifeCount ?? 0),
            (uint)(p.LuminanceAugmentWarCount ?? 0), (uint)(p.LuminanceAugmentVoidCount ?? 0), (uint)(p.LuminanceAugmentSpellDurationCount ?? 0),
            (uint)(p.LuminanceAugmentSpecializeCount ?? 0), (uint)(p.LuminanceAugmentSummonCount ?? 0), (uint)(p.LuminanceAugmentMeleeCount ?? 0),
            (uint)(p.LuminanceAugmentMissileCount ?? 0),
        };

        /// <summary>Before a matrix step's Equip: logs it and sets the step's augs. True = this step needs a re-equip
        /// (another weapon, other augs - buffs keep the aug level they were cast at - or buffs back on after "off").</summary>
        private static bool MatrixPrepare(Run run, MatrixStep st)
        {
            var player = run.Player;
            Log(player, $"matrix {run.Sweep.PlanName} step {run.SweepStep + 1}/{run.Sweep.StepCount}{(run.Sweep.Loop ? $" (lap {run.SweepCycle})" : "")}: {st.Label}");
            if (st.Augs != null && !st.Augs.SequenceEqual(CurrentAugs(player)))
            {
                TestCharacterCommands.SetChLumAugs(player, st.Augs);
                run.AugsDirty = true;
                run.AugsChanged = true;
            }
            if (st.Triune.HasValue && CurrentCharms(player).Any(c => c != st.Triune.Value))
            {
                SetCharms(player, Enumerable.Repeat(st.Triune.Value, CharmProps.Length).ToArray());
                run.AugsDirty = true;
                run.AugsChanged = true;
            }
            var tier = st.GearTier ?? run.Sweep.Tier;
            return run.LastGearKey != st.Weapon || run.LastGearTier != tier || run.AugsDirty || (run.BuffsOff && st.Buffs);
        }

        /// <summary>Triune Weave + the attack growth charms (war / void / melee / missile) - the Effective*AugCount
        /// counters past the aug caps (owner 2026-10-02: from T16 the attack aug grows with Triune, matching item + life).</summary>
        private static readonly ACE.Entity.Enum.Properties.PropertyInt64[] CharmProps =
        {
            ACE.Entity.Enum.Properties.PropertyInt64.TriuneWeaveCount, ACE.Entity.Enum.Properties.PropertyInt64.BattlemagesWrathCharmCount,
            ACE.Entity.Enum.Properties.PropertyInt64.NetherVeilCharmCount, ACE.Entity.Enum.Properties.PropertyInt64.CrashingSteelCharmCount,
            ACE.Entity.Enum.Properties.PropertyInt64.TrueShotCharmCount,
        };

        private static long[] CurrentCharms(Player p) => CharmProps.Select(c => p.GetProperty(c) ?? 0).ToArray();

        private static void SetCharms(Player player, long[] values)
        {
            for (var i = 0; i < CharmProps.Length; i++)
            {
                player.SetProperty(CharmProps[i], values[i]);
                player.Session?.Network.EnqueueSend(new ACE.Server.Network.GameMessages.Messages.GameMessagePrivateUpdatePropertyInt64(player, CharmProps[i], values[i]));
            }
        }

        /// <summary>After a matrix step's Equip (or when none was needed): buffs off if the step says so, and the step's
        /// monster overrides for the spawns that follow.</summary>
        private static void MatrixAfterGear(Run run, MatrixStep st, bool geared)
        {
            var player = run.Player;
            if (geared)
            {
                run.LastGearKey = st.Weapon;
                run.LastGearTier = st.GearTier ?? run.Sweep.Tier;
                run.AugsDirty = false;
                run.BuffsOff = false;
            }
            if (!st.Buffs && !run.BuffsOff)
            {
                player.EnchantmentManager.DispelAllEnchantments();
                foreach (var worn in player.EquippedObjects.Values.ToList())
                    worn.EnchantmentManager.DispelAllEnchantments();
                run.BuffsOff = true;
                Log(player, "matrix: buffs OFF - you and your worn gear were dispelled for this step.");
            }
            run.Overlay = st.Mob.IsEmpty ? null : st.Mob;
            run.Power = st.Power;
        }

        /// <summary>End of a matrix run: the character's own augs back, and one re-equip so its buffs match them.</summary>
        private static void MatrixRestore(Run run)
        {
            var player = run.Player;
            var augsDiffer = run.SavedAugs != null && !run.SavedAugs.SequenceEqual(CurrentAugs(player));
            var charmsDiffer = run.SavedCharms != null && !run.SavedCharms.SequenceEqual(CurrentCharms(player));
            if (run.AugsChanged && (augsDiffer || charmsDiffer || run.LastGearTier != run.Sweep.Tier))
            {
                if (augsDiffer) TestCharacterCommands.SetChLumAugs(player, run.SavedAugs);
                if (charmsDiffer) SetCharms(player, run.SavedCharms);
                Log(player, $"matrix: your augs are back to {string.Join(",", run.SavedAugs ?? CurrentAugs(player))}, Triune + charms {string.Join(",", run.SavedCharms ?? CurrentCharms(player))} - re-equipping T{run.Sweep.Tier} so your buffs match.");
                var key = run.LastGearKey ?? run.Sweep.Weapons[0];
                run.SweepWeapons.TryGetValue(key + "@" + run.Sweep.Tier.ToString(CultureInfo.InvariantCulture), out var reuse);
                try { Gear(player.Session, run.Sweep.Tier, run.Sweep.Bis, key, run.Sweep.Element, run.LastGearTier != run.Sweep.Tier, run.Sweep.Quality, reuseWeaponGuid: reuse, charmsAfterForge: run.SavedCharms); }
                catch (Exception ex) { log.Error($"[CombatBench] matrix restore gear {player.Name}: {ex}"); }
            }
            else if (run.BuffsOff)
                Log(player, "matrix: the last step left you dispelled - Equip to buff again.");
            Log(player, $"matrix {run.Sweep.PlanName}: results in {run.Sweep.ResultsPath}");
            log.Info($"[CombatBench] {player.Name}: matrix {run.Sweep.PlanName} ended, results {run.Sweep.ResultsPath}");
        }

        /// <summary>The matrix columns of a row: the step and the player it ran on.</summary>
        private static void AddMatrixFields(Run run, Dictionary<string, string> row)
        {
            if (run.Sweep?.Matrix == null || run.SweepStep < 0 || run.SweepStep >= run.Sweep.Matrix.Count) return;
            var st = run.Sweep.Matrix[run.SweepStep];
            var p = run.Player;
            row["plan"] = run.Sweep.PlanName;
            row["step"] = (run.SweepStep + 1).ToString(CultureInfo.InvariantCulture);
            row["lap"] = run.SweepCycle.ToString(CultureInfo.InvariantCulture);
            row["slabel"] = st.Label;
            row["augs"] = st.AugLabel;
            row["buffs"] = st.Buffs ? "on" : "off";
            row["mob"] = st.Mob.Label;
            row["php"] = p.Health.MaxValue.ToString(CultureInfo.InvariantCulture);
            row["plife"] = p.EffectiveLifeAugCount.ToString(CultureInfo.InvariantCulture);
            row["pitem"] = p.EffectiveItemAugCount.ToString(CultureInfo.InvariantCulture);
            row["pcreature"] = p.EffectiveCreatureAugCount.ToString(CultureInfo.InvariantCulture);
            row["ptriune"] = (p.GetProperty(ACE.Entity.Enum.Properties.PropertyInt64.TriuneWeaveCount) ?? 0).ToString(CultureInfo.InvariantCulture);
            row["gtier"] = (st.GearTier ?? run.Sweep.Tier).ToString(CultureInfo.InvariantCulture);
            row["pdrr"] = p.GetDamageResistRating(null).ToString(CultureInfo.InvariantCulture);
            row["pcdrr"] = p.GetCritDamageResistRating().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A row into the run's results file (JSON Lines, one object per row, with the time).</summary>
        private static void AppendResult(string path, Dictionary<string, string> row)
        {
            try
            {
                var o = new Dictionary<string, string>(row) { ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
                File.AppendAllText(path, JsonSerializer.Serialize(o) + Environment.NewLine);
            }
            catch (Exception ex) { log.Warn($"[CombatBench] results write {path}: {ex.Message}"); }
        }

        // =========================================================================================================
        // Math both ways
        // =========================================================================================================

        /// <summary>The Math step: the player's hits on the monster (Samples / the plan's samples) and the monster's hits
        /// on the player (the plan's taken_samples).</summary>
        private static void MathFor(Run run)
        {
            var dealt = run.Sweep?.Matrix != null ? run.Sweep.DealtSamples : run.Samples;
            if (dealt > 0)
            {
                var keep = run.Samples;
                run.Samples = dealt;
                try { DoMath(run); }
                finally { run.Samples = keep; }
            }
            var taken = run.Sweep?.TakenSamples ?? 0;
            if (taken > 0)
                DoMathTaken(run, taken);
            var spells = run.Sweep?.SpellSamples ?? 0;
            if (spells > 0)
                DoMathTakenSpell(run, spells);
        }

        /// <summary>The monster's spell the spell Math samples: the plan's spell_id, else the first harmful health-damage
        /// projectile in its spell book or its zone's spell rules.</summary>
        private static ACE.Server.Entity.Spell PickMonsterSpell(Creature mob, uint? spellId)
        {
            var ids = new List<int>();
            if (spellId is uint forced) ids.Add((int)forced);
            if (mob.Biota.PropertiesSpellBook != null) ids.AddRange(mob.Biota.PropertiesSpellBook.Keys.ToList());
            var zp = ACE.Server.Managers.ZoneControl.ZoneControlManager.ResolveForCreature(mob);
            if (zp?.SpellRules != null) ids.AddRange(zp.SpellRules.Where(r => !r.Disabled).Select(r => (int)r.SpellId));
            foreach (var id in ids.Distinct())
            {
                var spell = new ACE.Server.Entity.Spell(id);
                if (spell.NotFound || !spell.IsProjectile || !spell.IsHarmful) continue;
                if (spell.DamageType == DamageType.Stamina || spell.DamageType == DamageType.Mana) continue;
                if (spell.School != MagicSchool.WarMagic && spell.School != MagicSchool.VoidMagic) continue;
                return spell;
            }
            return null;
        }

        /// <summary>
        /// Math for a monster's SPELL on the player (owner 2026-10-02: spells ~3x a melee hit, wider spread): a stand-in
        /// projectile that never enters the world runs SpellProjectile.CalculateDamage (resist, base roll + spell_variance,
        /// crit, protections incl. the aug curves), then the health block of DamageTarget - 🔴 MIRRORED here for monster ->
        /// player (no sneak / heritage / PK): rating chain, %HP floor, True Damage (true_damage_spell). Nothing is applied.
        /// </summary>
        private static void DoMathTakenSpell(Run run, int samples)
        {
            var player = run.Player;
            var mob = run.Mob;
            if (mob == null) return;
            var spell = PickMonsterSpell(mob, run.Sweep?.SpellId);
            if (spell == null)
            {
                Log(player, $"math spell: {mob.Name} has no harmful war / void projectile spell - skipped (set spell_id in the plan).");
                return;
            }
            if (WorldObjectFactory.CreateNewWorldObject(spell.Wcid) is not SpellProjectile sp)
            {
                Log(player, $"math spell: could not make a projectile for {spell.Name} - skipped.");
                return;
            }
            int hits = 0, crits = 0;
            double normal = 0, tru = 0, critNormal = 0, critTrue = 0, nMin = double.MaxValue, nMax = 0;
            try
            {
                sp.Setup(spell, SpellProjectile.GetProjectileSpellType(spell.Id), player.Location?.Variation);
                sp.ProjectileSource = mob;
                sp.ProjectileTarget = player;
                for (var i = 0; i < samples; i++)
                {
                    bool crit = false, critDefended = false, overpower = false;
                    var d = sp.CalculateDamage(mob, player, ref crit, ref critDefended, ref overpower);
                    if (d is not float damage || damage <= 0) continue;

                    // mirror of SpellProjectile.DamageTarget's health block, monster -> player
                    var dmgRatingMod = Creature.GetPositiveRatingMod(mob.GetDamageRating());
                    var drrMod = player.GetDamageResistRatingMod(CombatType.Magic);
                    if (crit)
                    {
                        dmgRatingMod = Creature.AdditiveCombine(dmgRatingMod, Creature.GetPositiveRatingMod(mob.GetCritDamageRating()));
                        drrMod = Creature.AdditiveCombine(drrMod, Creature.GetNegativeRatingMod(player.GetCritDamageResistRating()));
                    }
                    damage *= dmgRatingMod * drrMod;
                    var floor = Creature.GetPercentHpFloorDamage(mob, player, crit);
                    if (floor > damage) damage = floor;
                    var t = Creature.GetTrueDamage(mob, player, crit, isSpell: true);

                    hits++;
                    if (crit) { crits++; critNormal += damage; critTrue += t; }
                    else
                    {
                        normal += damage; tru += t;
                        if (damage < nMin) nMin = damage;
                        if (damage > nMax) nMax = damage;
                    }
                }
            }
            finally { sp.Destroy(); }

            var plain = hits - crits;
            var all = normal + tru + critNormal + critTrue;
            var maxHp = (double)player.Health.MaxValue;
            var rank = RankOf(player, mob.WeenieClassId);
            var row = BaseRow(run, "spell");
            row["n"] = $"{samples} casts of {spell.Name}";
            row["spell"] = spell.Name;
            row["thit"] = Num(100.0 * hits / Math.Max(1, samples));
            row["mcrit"] = hits > 0 ? Num(100.0 * crits / hits) : "";
            row["mavg"] = hits > 0 ? Num(all / hits) : "";
            row["mpct"] = hits > 0 && maxHp > 0 ? Num(100.0 * all / hits / maxHp) : "";
            row["mtrue"] = all > 0 ? Num(100.0 * (tru + critTrue) / all) : "";
            row["tnorm"] = plain > 0 ? Num(normal / plain) : "";
            row["ttrue"] = plain > 0 ? Num(tru / plain) : "";
            row["tnmin"] = plain > 0 ? Num(nMin) : "";
            row["tnmax"] = plain > 0 ? Num(nMax) : "";
            row["tcritn"] = crits > 0 ? Num(critNormal / crits) : "";
            row["tcritt"] = crits > 0 ? Num(critTrue / crits) : "";
            var curves = Creature.ZoneAugCurveProfile(mob);
            row["zcaug"] = curves != null ? "on" : "off";
            if (curves != null) row["lifecut"] = Num(100.0 * Creature.ZoneLifeAugCut(curves, player));
            var key = rank switch { "Regular" => "reg", "Leader" => "ldr", "Boss" => "boss", _ => null };
            if (key != null && plain > 0)
                row["s" + key] = $"{normal / plain:0} + {tru / plain:0}";
            row["note"] = $"Math, monster SPELL on you: {samples} casts of {spell.Name}, {hits} landed, {crits} crits; your max HP {maxHp:0}";
            SendRow(player, row);
            Log(player, $"math spell {mob.Name} ({rank}) {spell.Name}: {(plain > 0 ? $"{normal / plain:0.0} + {tru / plain:0.0}" : "-")} per cast (normal + true), crit {row["mcrit"]} pct.");
        }

        /// <summary>
        /// Math for damage TAKEN (owner 2026-10-02): the monster's melee hit on the player computed <paramref name="samples"/>
        /// times through the same DamageEvent the real swing uses - evade, the swing roll, ratings, crit, armor per body
        /// part (the hit height cycles High / Medium / Low), protections, Damage Resist, the %HP floor and True Damage -
        /// with nothing applied. Each hit is split into its normal part and its True Damage part. Spells are not sampled
        /// (a live fight covers them).
        /// </summary>
        private static void DoMathTaken(Run run, int samples)
        {
            var player = run.Player;
            var mob = run.Mob;
            if (mob == null) return;
            if (ServerConfig.damage_event_debug_server_log.Value)
                Log(player, "math taken: damage_event_debug_server_log is ON - every sample is logged; turn it off for big runs.");

            var weapon = mob.GetEquippedWeapon();
            var savedHeight = mob.AttackHeight;
            int hits = 0, crits = 0;
            double normal = 0, tru = 0, critNormal = 0, critTrue = 0, nMin = double.MaxValue, nMax = 0;
            try
            {
                for (var i = 0; i < samples; i++)
                {
                    mob.AttackHeight = (AttackHeight)(1 + i % 3);
                    var de = DamageEvent.CalculateDamage(mob, player, weapon);
                    if (!de.HasDamage) continue;
                    hits++;
                    var n = (double)de.Damage - de.TrueDamage;
                    if (de.IsCritical)
                    {
                        crits++;
                        critNormal += n;
                        critTrue += de.TrueDamage;
                    }
                    else
                    {
                        normal += n;
                        tru += de.TrueDamage;
                        if (n < nMin) nMin = n;
                        if (n > nMax) nMax = n;
                    }
                }
            }
            finally { mob.AttackHeight = savedHeight; }

            var plain = hits - crits;
            var all = normal + tru + critNormal + critTrue;
            var maxHp = (double)player.Health.MaxValue;
            var rank = RankOf(player, mob.WeenieClassId);
            var swing = run.Sweep?.SwingRate ?? 1.0;
            var perSecond = samples > 0 ? all / samples * swing : 0;   // misses included: per SWING, not per hit

            var row = BaseRow(run, "taken");
            row["n"] = $"{samples} monster swings";
            row["thit"] = Num(100.0 * hits / Math.Max(1, samples));
            row["mcrit"] = hits > 0 ? Num(100.0 * crits / hits) : "";
            row["mavg"] = hits > 0 ? Num(all / hits) : "";
            row["mpct"] = hits > 0 && maxHp > 0 ? Num(100.0 * all / hits / maxHp) : "";
            row["mtrue"] = all > 0 ? Num(100.0 * (tru + critTrue) / all) : "";
            row["tnorm"] = plain > 0 ? Num(normal / plain) : "";
            row["ttrue"] = plain > 0 ? Num(tru / plain) : "";
            row["tnmin"] = plain > 0 ? Num(nMin) : "";
            row["tnmax"] = plain > 0 ? Num(nMax) : "";
            row["tcritn"] = crits > 0 ? Num(critNormal / crits) : "";
            row["tcritt"] = crits > 0 ? Num(critTrue / crits) : "";
            row["tcritx"] = crits > 0 && plain > 0 ? Num((critNormal + critTrue) / crits / ((normal + tru) / plain)) : "";
            row["ttd"] = perSecond > 0 ? Num(maxHp / perSecond) : "";
            // aug curves (owner 2026-10-02): on for this monster's tier? and how much of the normal part each curve removed
            var curves = Creature.ZoneAugCurveProfile(mob);
            row["zcaug"] = curves != null ? "on" : "off";
            if (curves != null)
            {
                row["lifecut"] = Num(100.0 * Creature.ZoneLifeAugCut(curves, player));
                row["itemcut"] = Num(100.0 * Creature.ZoneItemAugCut(curves, player));
            }
            var key = rank switch { "Regular" => "reg", "Leader" => "ldr", "Boss" => "boss", _ => null };
            if (key != null && plain > 0)
            {
                row["h" + key] = $"{normal / plain:0} + {tru / plain:0}";
                row["n" + key] = Num(normal / plain);
                row["t" + key] = Num(tru / plain);
            }
            row["note"] = $"Math, monster on you: {samples} swings, {hits} hit, {crits} crits; your max HP {maxHp:0}; time to die assumes {swing:0.##} swings/s, this monster alone, no spells";
            SendRow(player, row);
            Log(player, $"math taken {mob.Name} ({rank}): {(plain > 0 ? $"{normal / plain:0.0} + {tru / plain:0.0}" : "-")} per hit (normal + true), crit {row["mcrit"]} pct ({row["tcritx"]}x), alone kills you in {row["ttd"]} s.");
        }
    }
}
