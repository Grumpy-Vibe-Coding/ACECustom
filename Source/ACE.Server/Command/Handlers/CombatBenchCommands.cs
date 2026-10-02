using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// /bench - the Combat Bench (owner 2026-09-28). The Combat Bench plugin is its UI; every verb also works typed.
    /// Runs on the caller's OWN character only, and only while combat_bench_enabled is on (a test-shard tool).
    /// </summary>
    public static class CombatBenchCommands
    {
        private const string Usage =
            "status | gear <tier 10-25> <bis|avg> <weapon> <element> [destroy] [q=0-1000] | run <live|math|both> <wcid[,wcid...]> [fights 1-10] [samples 100-50000] [power 0-1] | pack <generator wcid[,wcid...]> [fights 1-10] [heal pct 0-95, 0 = kept full] [power 0-1] [timed s] [switch s] [math[=samples]] | sweep <weapon[,...]> <tier> <bis|avg> <element> <q> [powers=0,0.5,1] [loop] run|pack ... | matrix [list|<plan>] | stop\n" +
            "weapon: ua sword sword_ms dagger dagger_ms axe mace spear staff cleaver spear2h (melee) | bow (Infinite Deadly Prismatic Arrows)\n" +
            "element: fire cold electric acid slash pierce bludge nether";

        [CommandHandler("bench", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 0,
            "Combat Bench: builds a premade suit on you, spawns a monster, fights it server-side and reports time-to-kill / time-to-die.",
            Usage)]
        public static void HandleBench(Session session, params string[] parameters)
        {
            var player = session.Player;
            var verb = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "status";

            if (verb == "status")
            {
                CombatBench.SendStatus(player);
                return;
            }

            // lootsim needs no fight and makes no items, so it works with the bench off (2026-09-29)
            if (verb == "lootsim")
            {
                LootSim(player, parameters);
                return;
            }

            if (!ServerConfig.combat_bench_enabled.Value)
            {
                CombatBench.Log(player, "The bench is off on this server (combat_bench_enabled). /modifybool combat_bench_enabled true - test shard only.");
                CombatBench.SendStatus(player);
                return;
            }

            switch (verb)
            {
                case "gear":
                    {
                        if (CombatBench.IsRunning(player))
                        {
                            CombatBench.Log(player, "gear: a run is going - /bench stop first.");
                            return;
                        }
                        if (parameters.Length < 5 || !int.TryParse(parameters[1], out var tier) || tier < 10 || tier > 25)
                        {
                            CombatBench.Log(player, "Usage: /bench gear <tier 10-25> <bis|avg> <weapon> <element> [destroy] [q=0-1000]");
                            return;
                        }
                        var mode = parameters[2].ToLowerInvariant();
                        if (mode != "bis" && mode != "avg")
                        {
                            CombatBench.Log(player, "gear: suit must be bis or avg.");
                            return;
                        }
                        // "destroy" (owner 2026-09-28): old bench-made gear not in the new set is destroyed instead of packed
                        var extras = parameters.Skip(5).ToList();
                        var destroy = extras.Any(p => p.Equals("destroy", StringComparison.OrdinalIgnoreCase));
                        // "q=<0-1000>" (owner 2026-09-29): weapon quality + the fixed grade of its four cards; absent = 1000
                        var quality = 1000;
                        var qArg = extras.FirstOrDefault(p => p.StartsWith("q=", StringComparison.OrdinalIgnoreCase));
                        if (qArg != null && (!int.TryParse(qArg.Substring(2), out quality) || quality < 0 || quality > 1000))
                        {
                            CombatBench.Log(player, $"gear: {qArg} - the weapon quality must be q=0 to q=1000.");
                            return;
                        }
                        CombatBench.Gear(session, tier, mode == "bis", parameters[3].ToLowerInvariant(), parameters[4].ToLowerInvariant(), destroy, quality);
                        return;
                    }

                case "run":
                case "pack":
                    StartFrom(player, parameters, null);
                    return;

                case "sweep":
                    {
                        // owner 2026-10-01: Equip + the run that follows, once per weapon, in one command
                        const string sweepUsage = "Usage: /bench sweep <weapon[,weapon...]> <tier 10-25> <bis|avg> <element> <quality 0-1000> [powers=0,0.5,1] [loop] run|pack ...(as /bench run or /bench pack)";
                        if (parameters.Length < 8 || !int.TryParse(parameters[2], out var tier) || tier < 10 || tier > 25
                            || !int.TryParse(parameters[5], out var quality) || quality < 0 || quality > 1000)
                        {
                            CombatBench.Log(player, sweepUsage);
                            return;
                        }
                        var suit = parameters[3].ToLowerInvariant();
                        if (suit != "bis" && suit != "avg")
                        {
                            CombatBench.Log(player, "sweep: suit must be bis or avg.");
                            return;
                        }
                        var weapons = parameters[1].ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                        var unknown = weapons.Where(w => !WeaponScalingCommands.ForgeClasses.Any(c => c.Key == w) || w == "crossbow" || w == "atlatl" || w == "wand").ToList();
                        if (weapons.Count == 0 || unknown.Count > 0)
                        {
                            CombatBench.Log(player, $"sweep: unknown or unsupported weapon(s) {string.Join(", ", unknown)} - " + Usage.Split('\n')[1]);
                            return;
                        }
                        var sweep = new CombatBench.SweepSpec { Weapons = weapons, Tier = tier, Bis = suit == "bis", Element = parameters[4].ToLowerInvariant(), Quality = quality };
                        // owner 2026-10-01: "powers=0,0.5,1" = every weapon at the first bar, then every weapon at the next ...;
                        // "loop" = start over after the last one, until /bench stop. Both sit before run|pack.
                        var at = 6;
                        for (; at < parameters.Length; at++)
                        {
                            var opt = parameters[at].ToLowerInvariant();
                            if (opt == "run" || opt == "pack") break;
                            if (opt == "loop") { sweep.Loop = true; continue; }
                            if (opt.StartsWith("powers=", StringComparison.Ordinal))
                            {
                                var bars = new List<float>();
                                foreach (var b in opt.Substring(7).Split(',', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (!float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var bar) || bar < 0f || bar > 1f)
                                    {
                                        CombatBench.Log(player, $"sweep: power bar '{b}' must be 0 to 1.");
                                        return;
                                    }
                                    bars.Add(bar);
                                }
                                if (bars.Count > 0) sweep.Powers = bars;
                                continue;
                            }
                            CombatBench.Log(player, $"sweep: '{parameters[at]}' - expected powers=..., loop, run or pack. " + sweepUsage);
                            return;
                        }
                        StartFrom(player, parameters.Skip(at).ToArray(), sweep);
                        return;
                    }

                case "matrix":
                    // owner 2026-10-02: a plan file (BenchPlans\<name>.json beside the server) walked unattended
                    if (parameters.Length < 2 || parameters[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                    {
                        CombatBench.ListPlans(player);
                        return;
                    }
                    if (CombatBench.IsRunning(player))
                    {
                        CombatBench.Log(player, "matrix: a run is going - /bench stop first.");
                        return;
                    }
                    CombatBench.StartMatrix(player, parameters[1]);
                    return;

                case "stop":
                    CombatBench.Stop(player);
                    return;

                default:
                    CombatBench.Log(player, "Usage: /bench " + Usage.Split('\n')[0]);
                    return;
            }
        }

        /// <summary>/bench run and /bench pack (and the run/pack half of /bench sweep): <paramref name="p"/>[0] is "run"
        /// or "pack".</summary>
        private static void StartFrom(ACE.Server.WorldObjects.Player player, string[] p, CombatBench.SweepSpec sweep)
        {
            var verb = p.Length > 0 ? p[0].ToLowerInvariant() : "";
            if (verb == "run")
            {
                if (p.Length < 3)
                {
                    CombatBench.Log(player, "Usage: /bench run <live|math|both> <wcid[,wcid...]> [fights] [samples] [power]");
                    return;
                }
                var mode = p[1].ToLowerInvariant();
                if (mode != "live" && mode != "math" && mode != "both")
                {
                    CombatBench.Log(player, "run: mode must be live, math or both.");
                    return;
                }
                if (!ParseWcids(player, "run", p[2], out var wcids)) return;
                var fights = p.Length > 3 && int.TryParse(p[3], out var f) ? f : 3;
                var samples = p.Length > 4 && int.TryParse(p[4], out var s) ? s : 10000;
                var power = p.Length > 5 && float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var pw) ? pw : 0f;   // fast bar (owner 2026-10-01)
                CombatBench.Start(player, mode, wcids, fights, samples, power, sweep);
            }
            else if (verb == "pack")
            {
                // owner 2026-10-01: a real generator's pack fought at once, healing with the Eternal Health Kit; timed > 0 =
                // the monsters never die, each fight lasts that many seconds and the target rotates every switch seconds
                if (p.Length < 2)
                {
                    CombatBench.Log(player, "Usage: /bench pack <generator wcid[,wcid...]> [fights 1-10] [heal below pct HP, 0 = no kit - kept at full health] [power 0-1] [timed seconds, 0 = to the kill] [switch seconds] [math[=samples]]");
                    return;
                }
                // owner 2026-10-01: "math" (or math=<samples>) anywhere after the generators = math rows + the live fight
                var mathSamples = 0;
                foreach (var a in p.Skip(2).Where(a => a.StartsWith("math", StringComparison.OrdinalIgnoreCase)))
                    mathSamples = a.Length > 5 && int.TryParse(a.Substring(5), out var ms) ? ms : 10000;
                p = p.Where((a, i) => i < 2 || !a.StartsWith("math", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (!ParseWcids(player, "pack", p[1], out var gens)) return;
                var fights = p.Length > 2 && int.TryParse(p[2], out var f) ? f : 3;
                var heal = p.Length > 3 && int.TryParse(p[3], out var h) ? h : 30;
                var power = p.Length > 4 && float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var pw) ? pw : 0f;   // fast bar (owner 2026-10-01)
                var timed = p.Length > 5 && int.TryParse(p[5], out var t) ? t : 0;
                var switchEvery = p.Length > 6 && int.TryParse(p[6], out var sw) ? sw : 10;
                CombatBench.StartPack(player, gens, fights, heal, power, timed, switchEvery, sweep, mathSamples);
            }
            else
                CombatBench.Log(player, "Usage: /bench " + Usage.Split('\n')[0]);
        }

        private static bool ParseWcids(ACE.Server.WorldObjects.Player player, string verb, string csv, out List<uint> wcids)
        {
            wcids = new List<uint>();
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!uint.TryParse(part, out var w))
                {
                    CombatBench.Log(player, $"{verb}: '{part}' is not a wcid.");
                    return false;
                }
                wcids.Add(w);
            }
            return wcids.Count > 0;
        }

        /// <summary>/bench lootsim &lt;regular|leader|boss&gt; [n]: rolls n weapon qualities and n card / modifier value
        /// grades through the SAME functions a drop uses, with the profile a monster of that rank dying here would
        /// use, and prints the grade spread (rank loot, owner 2026-09-29). No world, no items.</summary>
        private static void LootSim(ACE.Server.WorldObjects.Player player, string[] parameters)
        {
            var rankArg = parameters.Length > 1 ? parameters[1].ToLowerInvariant() : "regular";
            var rank = rankArg switch { "leader" => ACE.Server.Managers.ZoneScaling.ZcRank.Leader, "boss" => ACE.Server.Managers.ZoneScaling.ZcRank.Boss, _ => ACE.Server.Managers.ZoneScaling.ZcRank.Regular };
            var n = parameters.Length > 2 && int.TryParse(parameters[2], out var nn) ? Math.Clamp(nn, 100, 1000000) : 100000;
            var p = ACE.Server.Managers.ZoneControl.ZoneControlManager.ResolveZoneRankForPlayer(player, rank);
            if (p == null)
            {
                CombatBench.Log(player, "lootsim: stand inside an enabled Zone Control zone (it reads that zone's rank rows).");
                return;
            }
            var floor = ACE.Server.Managers.ZoneControl.ZoneStatResolver.GradeFloorOf(p);
            var sOdds = (int)Math.Round(p.Get(ACE.Server.Managers.ZoneScaling.ZoneStat.GradeSOdds, 0.0));
            var tier = p.Tier >= 11 ? p.Tier : 11;   // zone profiles carry Tier 1 - the drop rolls clamp to T11 anyway

            var counts = new Dictionary<string, int> { ["S"] = 0, ["A"] = 0, ["B"] = 0, ["C"] = 0, ["D"] = 0, ["F"] = 0 };
            long qSum = 0, gSum = 0; var gMin = int.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var q = ACE.Server.Managers.WeaponScaling.WeaponScalingManager.RollQuality(floor, sOdds);
                qSum += q;
                counts[ACE.Server.Managers.WeaponScaling.WeaponScalingManager.GetQualityGrade(q)]++;
                var g = ACE.Server.Managers.ZoneControl.ZoneStatResolver.RollGrade(tier, false, floor);
                gSum += g; if (g < gMin) gMin = g;
            }
            string AtLeast(params string[] gs) { var c = 0; foreach (var x in gs) c += counts[x]; return (100.0 * c / n).ToString("0.00", CultureInfo.InvariantCulture); }
            var drops = p.Has(ACE.Server.Managers.ZoneScaling.ZoneStat.LootDropsMin)
                ? $"{p.Get(ACE.Server.Managers.ZoneScaling.ZoneStat.LootDropsMin, 0):0}-{p.Get(ACE.Server.Managers.ZoneScaling.ZoneStat.LootDropsMax, p.Get(ACE.Server.Managers.ZoneScaling.ZoneStat.LootDropsMin, 0)):0}"
                : "per-slot";
            CombatBench.Log(player, $"lootsim {rank} T{tier} ({n:N0} rolls): drops {drops}, grade floor {floor:0.00}, S odds {(sOdds > 0 ? "1 in " + sOdds : "grade table")}");
            CombatBench.Log(player, $"  weapon grade: S {AtLeast("S")} pct | A+ {AtLeast("S", "A")} | B+ {AtLeast("S", "A", "B")} | C+ {AtLeast("S", "A", "B", "C")} | D+ {AtLeast("S", "A", "B", "C", "D")} | avg quality {qSum / (double)n:0}");
            CombatBench.Log(player, $"  card / modifier value: avg {gSum / (double)n / 10.0:0.0} pct up the band, lowest {gMin / 10.0:0.0} pct");
        }
    }
}
