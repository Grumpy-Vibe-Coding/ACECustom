using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using ACE.Common.Performance;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers.WeaponScaling;
using ACE.Server.Managers.ZoneControl;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// /weaponscale — admin fallback + plugin sync for the weapon aug-scaling config
    /// (plan: C:\AI\ZoneControl\T11_WeaponRelevance_Plan_2026-07-31.md §9).
    ///
    /// The ZoneControl plugin's Weapons section is the intended editing surface; these commands are
    /// the same operations for when the plugin isn't available. Config edits are pure data — the
    /// combat wire-in (step 3) reads the config live, gated behind the master Enabled flag.
    /// </summary>
    public static class WeaponScalingCommands
    {
        private class SyncWatch
        {
            public string LastPayload;
            public DateTime LastSentUtc;
            /// <summary>Last [[ZCWK]] line sent per family — ladders are diffed individually so a
            /// one-cell edit re-sends one family, not all eleven.</summary>
            public readonly Dictionary<string, string> LastLadders = new(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly ConcurrentDictionary<Session, SyncWatch> _pluginSessions = new();
        private static readonly RateLimiter _pushTickRateLimiter = new RateLimiter(1, TimeSpan.FromSeconds(2));

        /// <summary>An unchanged [[ZCW]] payload is still re-sent this often as a stale-correction keepalive
        /// (same shape as the [[ZC]] sync — the config changes rarely, so idle sessions are near-silent).</summary>
        private const double SyncKeepaliveSeconds = 15.0;

        /// <summary>Called from WorldManager.UpdateGameWorld() every frame; rate-limited to once per 2s.</summary>
        public static void PushTick()
        {
            if (_pluginSessions.IsEmpty)
                return;
            if (_pushTickRateLimiter.GetSecondsToWaitBeforeNextEvent() > 0)
                return;
            _pushTickRateLimiter.RegisterEvent();

            var payload = BuildPayload();
            var ladders = BuildLadderPayloads();
            var now = DateTime.UtcNow;

            foreach (var kv in _pluginSessions)
            {
                var session = kv.Key;
                if (session.IsTerminated)
                {
                    _pluginSessions.TryRemove(session, out _);
                    continue;
                }

                var watch = kv.Value;
                var keepalive = (now - watch.LastSentUtc).TotalSeconds >= SyncKeepaliveSeconds;
                if (payload == watch.LastPayload && !keepalive)
                    continue;

                watch.LastSentUtc = now;

                if (payload != watch.LastPayload || keepalive)
                {
                    watch.LastPayload = payload;
                    ChatPacket.SendServerMessage(session, payload, ChatMessageType.Broadcast);
                }

                // Ladders ride their OWN per-family messages rather than the main payload: 11
                // melee families x 16 rungs would roughly triple a payload that is already ~800
                // chars, and a single cell edit only has to re-send its own ~150-char line.
                foreach (var kvL in ladders)
                {
                    if (!keepalive && watch.LastLadders.TryGetValue(kvL.Key, out var prev) && prev == kvL.Value)
                        continue;
                    watch.LastLadders[kvL.Key] = kvL.Value;
                    ChatPacket.SendServerMessage(session, kvL.Value, ChatMessageType.Broadcast);
                }

                // A family whose ladder was cleared stops appearing above; tell the plugin once
                // so its editor drops back to the lerp view instead of showing a stale ladder.
                foreach (var goneKey in watch.LastLadders.Keys.Where(k => !ladders.ContainsKey(k)).ToList())
                {
                    watch.LastLadders.Remove(goneKey);
                    ChatPacket.SendServerMessage(session, $"[[ZCWK]]|s={goneKey}|k=", ChatMessageType.Broadcast);
                }
            }
        }

        /// <summary>Pipe/tilde wire format (house style; the plugin has no JSON parser):
        /// [[ZCW]]|enabled=1|kc=0.6~0.8|tighten=0.7|critcap=3|grades=...|tiers=t~cap~minwield,...
        /// |scripts=name~kmin~kmax~variance,...   (grade LADDERS ride separate [[ZCWK]] messages)</summary>
        private static string BuildPayload()
        {
            var cfg = WeaponScalingManager.Current;
            var sb = new StringBuilder("[[ZCW]]");
            sb.Append("|enabled=").Append(cfg.Enabled ? '1' : '0');
            sb.Append("|kc=").Append(F(cfg.KcMin)).Append('~').Append(F(cfg.KcMax));
            sb.Append("|tighten=").Append(F(cfg.TightenStrength));
            // player_crit_damage_cap rides the payload so the plugin's Damage Chart crit row is the
            // SERVER's number. A manually-typed cap that drifts from the config is exactly the kind
            // of quietly-wrong figure the chart exists to eliminate.
            sb.Append("|critcap=").Append(F(Managers.ServerConfig.player_crit_damage_cap.Value));
            sb.Append("|grades=").Append(string.Join(",",
                Managers.WeaponScaling.WeaponScalingManager.GradeBands
                    .Select(b => $"{b.Grade}~{F(cfg.GradeWeights != null && cfg.GradeWeights.TryGetValue(b.Grade, out var gw) ? gw : 0)}")));
            sb.Append("|tiers=").Append(string.Join(",",
                cfg.Tiers.Select(t => $"{t.Tier}~{t.Cap}~{t.MinWieldAugs}")));
            sb.Append("|scripts=").Append(string.Join(",",
                cfg.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(s => $"{s.Key}~{F(s.Value.KMin)}~{F(s.Value.KMax)}~{F(s.Value.Variance)}")));
            return sb.ToString();
        }

        /// <summary>One message per family that has an authored grade ladder:
        /// [[ZCWK]]|s=unarmed|k=&lt;16 values, tilde-separated, in SubGradeBands order&gt;
        /// Sub-grade names are omitted — the order is the contract (plugin indexes by position).</summary>
        private static Dictionary<string, string> BuildLadderPayloads()
        {
            var cfg = WeaponScalingManager.Current;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in cfg.Scripts)
            {
                if (kv.Value == null || !kv.Value.HasLadder)
                    continue;
                var vals = WeaponScalingManager.SubGradeBands
                    .Select(b => F(kv.Value.Grades.TryGetValue(b.Grade, out var k) ? k : 0));
                result[kv.Key] = $"[[ZCWK]]|s={kv.Key}|k={string.Join("~", vals)}";
            }
            return result;
        }

        private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        [CommandHandler("weaponscale", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Weapon Scaling config (plugin fallback).",
            "show | enable on|off | tier <t> cap|minwield <n> | tier add <t> [cap] [minwield] | tier remove <t> | "
            + "script <name> kmin|kmax <v> | script add <name> [kmin] [kmax] | script remove <name> | "
            + "kc min|max <v> | sync on|off | reset | reload")]
        public static void HandleWeaponScale(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            if (parameters.Length == 0 || parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Msg("Weapon Scaling config (items store quality; these knobs give it meaning at swing time):");
                Msg("  /weaponscale show");
                Msg("  /weaponscale enable on|off       master switch; off = static-base-only combat (current behavior)");
                Msg("  /weaponscale tier <t> cap <n>    scaling stops growing at n item augs for tier-t weapons");
                Msg("  /weaponscale tier <t> minwield <n>   item augs required to WIELD tier-t weapons (economy gate)");
                Msg("  /weaponscale tier add <t> [cap] [minwield] | tier remove <t>");
                Msg("  /weaponscale script <name> kmin <v> | kmax <v> | variance <v>   per-loot-script k range + Scheme C family variance");
                Msg("  /weaponscale script <name> ladder <anchorS> | ladder clear   seed/drop the 16-rung grade ladder (+18 pct per grade)");
                Msg("  /weaponscale script <name> grade <S|A+|A|A-|..|F-> <k>   author ONE rung");
                Msg("  /weaponscale ladder <name>       print a family's 16 rungs with step pct");
                Msg("  /weaponscale script add <name> [kmin] [kmax] | script remove <name>");
                Msg("  /weaponscale kc min <v> | kc max <v>   crit-channel coefficient range");
                Msg("  /weaponscale tighten <v>         Scheme C: fraction of family variance a q1000 weapon sheds (0.7 = S keeps 30 pct)");
                Msg("  /weaponscale grade <S|A|B|C|D|F> <weight>   drop-frequency weight (relative; quality rolls uniform inside the band)");
                Msg("  /weaponscale reset               restore locked launch defaults (plan section 4)");
                Msg("  /weaponscale reload              re-read the store from the shard DB");
                return;
            }

            var sub = parameters[0].ToLowerInvariant();
            var args = parameters;

            switch (sub)
            {
                case "sync":
                {
                    // Machine handshake from the plugin — silent, same contract as /zonecontrol sync.
                    if (args.Length >= 2 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        _pluginSessions.TryRemove(session, out _);
                        return;
                    }
                    if (args.Length < 2 || !args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        Msg("Usage: /weaponscale sync on | sync off");
                        return;
                    }
                    _pluginSessions[session] = new SyncWatch();
                    return;
                }

                case "show":
                {
                    var cfg = WeaponScalingManager.Current;
                    var sb = new StringBuilder();
                    sb.AppendLine($"Weapon Scaling: {(cfg.Enabled ? "Enabled" : "Disabled")} (kc {cfg.KcMin:0.###}-{cfg.KcMax:0.###}, tighten {cfg.TightenStrength:0.###})");
                    sb.AppendLine("  tier | cap | minwield");
                    foreach (var t in cfg.Tiers)
                        sb.AppendLine($"  T{t.Tier} | {t.Cap:N0} | {t.MinWieldAugs:N0}");
                    sb.AppendLine("  script | kmin | kmax | variance | ladder");
                    foreach (var s in cfg.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine($"  {s.Key} | {s.Value.KMin:0.###} | {s.Value.KMax:0.###} | {s.Value.Variance:0.###} | "
                            + (s.Value.HasLadder
                                ? $"S {s.Value.Grades[WeaponScalingManager.SubGradeBands[0].Grade]:0.####} .. "
                                  + $"F- {s.Value.Grades[WeaponScalingManager.SubGradeBands[WeaponScalingManager.SubGradeBands.Length - 1].Grade]:0.####}"
                                : "(lerp)"));
                    var gwTotal = Managers.WeaponScaling.WeaponScalingManager.GradeBands
                        .Sum(b => cfg.GradeWeights != null && cfg.GradeWeights.TryGetValue(b.Grade, out var w) && w > 0 ? w : 0);
                    sb.AppendLine("  grade | weight | drop pct");
                    foreach (var b in Managers.WeaponScaling.WeaponScalingManager.GradeBands)
                    {
                        var w = cfg.GradeWeights != null && cfg.GradeWeights.TryGetValue(b.Grade, out var gw) ? gw : 0;
                        var pct = gwTotal > 0 ? w / gwTotal * 100.0 : 0;
                        sb.AppendLine($"  {b.Grade} (q{b.QMin}-{b.QMax}) | {w:0.####} | {pct:0.###} pct");
                    }
                    if (gwTotal <= 0)
                        sb.AppendLine("  (no grade weights authored - drops use the legacy uniform 0-1000 roll)");
                    Msg(sb.ToString().TrimEnd());
                    return;
                }

                case "ladder":
                {
                    if (args.Length < 2)
                    {
                        Msg("Usage: /weaponscale ladder <script>   (prints the 16 authored rungs)");
                        return;
                    }
                    var cfgL = WeaponScalingManager.Current;
                    if (!cfgL.Scripts.TryGetValue(args[1], out var row))
                    {
                        Msg($"No script '{args[1]}'.");
                        return;
                    }
                    if (!row.HasLadder)
                    {
                        Msg($"Script {args[1]} has no ladder - k lerps {row.KMin:0.###}..{row.KMax:0.###} across quality. "
                            + $"Seed one with: /weaponscale script {args[1]} ladder <anchorS>");
                        return;
                    }
                    var sbL = new StringBuilder();
                    sbL.AppendLine($"{args[1]} grade ladder (variance {row.Variance:0.###}, tighten {cfgL.TightenStrength:0.###}):");
                    // Two step columns on purpose. Raw k steps UNEVENLY by design — rungs are stored
                    // with EV normalization divided out, and v_eff rises as grade falls, so k has to
                    // slope to keep DEALT damage even. The dmg column is the one carrying the
                    // invariant (+5.7 pct per sub-grade); without it the only number on screen was
                    // the one nobody is tuning, which is how a three-day-old verify step ended up
                    // unrunnable (2026-08-06).
                    sbL.AppendLine("  grade | q band | k | k step | dmg step");
                    double? prevK = null, prevD = null;
                    foreach (var b in WeaponScalingManager.SubGradeBands)
                    {
                        var k = row.Grades.TryGetValue(b.Grade, out var kv2) ? kv2 : 0;
                        var dealt = WeaponScalingManager.DealtWeaponTerm(row, cfgL.TightenStrength, b.QMid);
                        var step = prevK.HasValue && k > 0 ? $"{(prevK.Value / k - 1) * 100:+0.0;-0.0} pct" : "";
                        var dStep = prevD.HasValue && dealt > 0 ? $"{(prevD.Value / dealt - 1) * 100:+0.0;-0.0} pct" : "";
                        sbL.AppendLine($"  {b.Grade,-2} | q{b.QMin}-{b.QMax} | {k:0.####} | {step} | {dStep}");
                        prevK = k;
                        prevD = dealt;
                    }
                    sbL.AppendLine($"  S -> F- spread: {WeaponScalingManager.DealtWeaponTerm(row, cfgL.TightenStrength, 1000) / Math.Max(1e-9, WeaponScalingManager.DealtWeaponTerm(row, cfgL.TightenStrength, 83)):0.###}x dealt damage");
                    Msg(sbL.ToString().TrimEnd());
                    return;
                }

                case "enable":
                {
                    if (args.Length < 2 || (!args[1].Equals("on", StringComparison.OrdinalIgnoreCase) && !args[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
                    {
                        Msg("Usage: /weaponscale enable on|off");
                        return;
                    }
                    var on = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    WeaponScalingManager.Mutate(cfg => cfg.Enabled = on);
                    Msg($"Weapon Scaling: {(on ? "Enabled" : "Disabled")}.");
                    return;
                }

                case "tier":
                {
                    if (args.Length >= 3 && args[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(args[2], out var tAdd)) { Msg("tier add: bad tier number."); return; }
                        var cap = args.Length >= 4 && int.TryParse(args[3], out var c) ? c : 0;
                        var minWield = args.Length >= 5 && int.TryParse(args[4], out var m) ? m : 0;
                        WeaponScalingManager.Mutate(cfg =>
                        {
                            var row = cfg.Tiers.FirstOrDefault(x => x.Tier == tAdd);
                            if (row == null)
                                cfg.Tiers.Add(new WeaponScalingTier { Tier = tAdd, Cap = cap, MinWieldAugs = minWield });
                            else { row.Cap = cap; row.MinWieldAugs = minWield; }
                        });
                        Msg($"Tier T{tAdd} set: cap {cap:N0}, minwield {minWield:N0}.");
                        return;
                    }
                    if (args.Length >= 3 && args[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(args[2], out var tRem)) { Msg("tier remove: bad tier number."); return; }
                        WeaponScalingManager.Mutate(cfg => cfg.Tiers.RemoveAll(x => x.Tier == tRem));
                        Msg($"Tier T{tRem} removed.");
                        return;
                    }
                    if (args.Length < 4 || !int.TryParse(args[1], out var tier) || !int.TryParse(args[3], out var value))
                    {
                        Msg("Usage: /weaponscale tier <t> cap|minwield <n>  |  tier add <t> [cap] [minwield]  |  tier remove <t>");
                        return;
                    }
                    var field = args[2].ToLowerInvariant();
                    if (field != "cap" && field != "minwield")
                    {
                        Msg("tier: field must be cap or minwield.");
                        return;
                    }
                    var found = false;
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        var row = cfg.Tiers.FirstOrDefault(x => x.Tier == tier);
                        if (row == null) return;
                        found = true;
                        if (field == "cap") row.Cap = value;
                        else row.MinWieldAugs = value;
                    });
                    Msg(found ? $"Tier T{tier} {field} = {value:N0}." : $"Tier T{tier} not found (use tier add).");
                    return;
                }

                case "script":
                {
                    if (args.Length >= 3 && args[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = args[2];
                        var kMin = args.Length >= 4 && TryParseDouble(args[3], out var a) ? a : 0.90;
                        var kMax = args.Length >= 5 && TryParseDouble(args[4], out var b) ? b : 1.15;
                        WeaponScalingManager.Mutate(cfg => cfg.Scripts[name] = new WeaponScalingScript { KMin = kMin, KMax = kMax });
                        Msg($"Script {name}: kmin {kMin:0.###}, kmax {kMax:0.###}.");
                        return;
                    }
                    if (args.Length >= 3 && args[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = args[2];
                        WeaponScalingManager.Mutate(cfg => cfg.Scripts.Remove(name));
                        Msg($"Script {name} removed.");
                        return;
                    }
                    // Grade ladder verbs (owner 2026-08-03). "ladder <anchorS>" seeds all 16 from
                    // the S anchor at +18 pct per full grade; "ladder clear" drops back to the
                    // KMin/KMax lerp; "grade <sub> <k>" authors one rung.
                    if (args.Length >= 3 && args[1].Equals("ladder", StringComparison.OrdinalIgnoreCase))
                    {
                        Msg("Usage: /weaponscale script <name> ladder <anchorS> | ladder clear");
                        return;
                    }
                    if (args.Length >= 4 && args[2].Equals("ladder", StringComparison.OrdinalIgnoreCase))
                    {
                        var lname = args[1];
                        var clearing = args[3].Equals("clear", StringComparison.OrdinalIgnoreCase);
                        if (!clearing && !TryParseDouble(args[3], out _))
                        {
                            Msg("Usage: /weaponscale script <name> ladder <anchorS> | ladder clear");
                            return;
                        }
                        TryParseDouble(args[3], out var anchor);
                        var lfound = false;
                        WeaponScalingManager.Mutate(cfg =>
                        {
                            if (!cfg.Scripts.TryGetValue(lname, out var s)) return;
                            lfound = true;
                            // Family-specific by necessity: the rungs divide out EV normalization,
                            // which depends on this family's variance (see BuildLadder).
                            s.Grades = clearing
                                ? null
                                : WeaponScalingManager.BuildLadder(anchor, s.Variance, cfg.TightenStrength);
                        });
                        Msg(!lfound ? $"Script {lname} not found (use script add)."
                            : clearing ? $"Script {lname} ladder cleared - back to the kmin/kmax lerp."
                            : $"Script {lname} ladder seeded from S = {anchor:0.####} (+18 pct per grade, 16 rungs).");
                        return;
                    }
                    if (args.Length >= 5 && args[2].Equals("grade", StringComparison.OrdinalIgnoreCase))
                    {
                        var gname = args[1];
                        var subGrade = args[3];
                        if (!TryParseDouble(args[4], out var gk) || gk < 0)
                        {
                            Msg("Usage: /weaponscale script <name> grade <S|A+|A|A-|...|F-> <k>");
                            return;
                        }
                        var band = WeaponScalingManager.SubGradeBands
                            .FirstOrDefault(b => b.Grade.Equals(subGrade, StringComparison.OrdinalIgnoreCase));
                        if (band.Grade == null)
                        {
                            Msg("script grade: unknown sub-grade. Use " +
                                string.Join(" ", WeaponScalingManager.SubGradeBands.Select(b => b.Grade)));
                            return;
                        }
                        var gfound = false;
                        WeaponScalingManager.Mutate(cfg =>
                        {
                            if (!cfg.Scripts.TryGetValue(gname, out var s)) return;
                            gfound = true;
                            // Authoring one rung on a lerp-only family promotes it to a ladder,
                            // seeded from the lerp so the other 15 rungs keep their current values
                            // (Normalize completes partials the same way).
                            s.Grades ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            s.Grades[band.Grade] = gk;
                        });
                        Msg(gfound ? $"Script {gname} grade {band.Grade} k = {gk:0.####}."
                                   : $"Script {gname} not found (use script add).");
                        return;
                    }

                    if (args.Length < 4 || !TryParseDouble(args[3], out var v))
                    {
                        Msg("Usage: /weaponscale script <name> kmin|kmax|variance <v>  |  script <name> ladder <anchorS>  |  "
                            + "script <name> grade <sub> <k>  |  script add <name> [kmin] [kmax]  |  script remove <name>");
                        return;
                    }
                    var scriptName = args[1];
                    var kField = args[2].ToLowerInvariant();
                    if (kField != "kmin" && kField != "kmax" && kField != "variance")
                    {
                        Msg("script: field must be kmin, kmax or variance.");
                        return;
                    }
                    var scriptFound = false;
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        if (!cfg.Scripts.TryGetValue(scriptName, out var s)) return;
                        scriptFound = true;
                        if (kField == "kmin") s.KMin = v;
                        else if (kField == "kmax") s.KMax = v;
                        else
                        {
                            // Keep dealt damage flat across a variance edit — the ladder stores k
                            // with EV normalization divided out, so it has to be re-priced.
                            var oldVar = s.Variance;
                            s.Variance = Math.Max(0.0, Math.Min(0.95, v));
                            WeaponScalingManager.RebaseLadder(s, oldVar, cfg.TightenStrength,
                                                              s.Variance, cfg.TightenStrength);
                        }
                    });
                    Msg(scriptFound ? $"Script {scriptName} {kField} = {v:0.###}." : $"Script {scriptName} not found (use script add).");
                    return;
                }

                case "kc":
                {
                    if (args.Length < 3 || !TryParseDouble(args[2], out var v) || (args[1] != "min" && args[1] != "max"))
                    {
                        Msg("Usage: /weaponscale kc min|max <v>");
                        return;
                    }
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        if (args[1] == "min") cfg.KcMin = v;
                        else cfg.KcMax = v;
                    });
                    Msg($"kc {args[1]} = {v:0.###}.");
                    return;
                }

                case "grade":
                {
                    if (args.Length < 3 || !TryParseDouble(args[2], out var gw))
                    {
                        Msg("Usage: /weaponscale grade <S|A|B|C|D|F> <weight>  (relative weight, >= 0; 0 = that grade never drops)");
                        return;
                    }
                    var gradeKey = args[1].ToUpperInvariant();
                    if (!Managers.WeaponScaling.WeaponScalingManager.GradeBands.Any(b => b.Grade == gradeKey))
                    {
                        Msg("grade: must be one of S A B C D F.");
                        return;
                    }
                    var gClamped = Math.Max(0.0, gw);
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        cfg.GradeWeights ??= new System.Collections.Generic.Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        cfg.GradeWeights[gradeKey] = gClamped;
                    });
                    Msg($"grade {gradeKey} weight = {gClamped:0.####}.");
                    return;
                }

                case "tighten":
                {
                    if (args.Length < 2 || !TryParseDouble(args[1], out var v))
                    {
                        Msg("Usage: /weaponscale tighten <v>  (0-1; 0.7 = a q1000 weapon sheds 70 pct of its family variance)");
                        return;
                    }
                    var clamped = Math.Max(0.0, Math.Min(1.0, v));
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        // Tighten is global, so EVERY authored ladder needs re-pricing — same
                        // EV-neutrality rule as a per-family variance edit.
                        var oldTighten = cfg.TightenStrength;
                        cfg.TightenStrength = clamped;
                        foreach (var s in cfg.Scripts.Values)
                            WeaponScalingManager.RebaseLadder(s, s.Variance, oldTighten, s.Variance, clamped);
                    });
                    Msg($"tighten = {clamped:0.###}. (Authored ladders re-priced to hold dealt damage flat.)");
                    return;
                }

                case "reset":
                {
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        var d = WeaponScalingManager.BuildDefaults();
                        cfg.Enabled = d.Enabled;
                        cfg.Tiers = d.Tiers;
                        cfg.Scripts = d.Scripts;
                        cfg.KcMin = d.KcMin;
                        cfg.KcMax = d.KcMax;
                        cfg.TightenStrength = d.TightenStrength;
                        cfg.GradeWeights = d.GradeWeights;
                    });
                    Msg("Weapon Scaling config reset to locked launch defaults (system Disabled).");
                    return;
                }

                case "reload":
                {
                    WeaponScalingManager.Reload();
                    Msg("Weapon Scaling config reloaded from store.");
                    return;
                }

                default:
                    Msg("Unknown subcommand. /weaponscale help");
                    return;
            }
        }

        private static bool TryParseDouble(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>Matched-quality test weapons for cross-family tuning (owner 2026-08-01): one
        /// UA / bow / sword / wand from the live loot-table weenies, stamped at the SAME chosen
        /// quality so families compare fairly. Goes through the normal item pipeline — never
        /// direct DB writes (guid allocation + biota caching make hand-inserted rows unsafe).
        /// The static damage stays the base weenie's (the scaling term dominates at T11); the
        /// wield gate + stamps match what the Creature_Death T11 sweep gives real drops.</summary>
        // ═════════════════ weapon forging (test items via the normal item pipeline) ═════════════════
        // One representative base weenie per scaling family (all verified in the world DB 08-01:
        // W_WeaponType + MultiStrike/thrust flags resolve to exactly the intended GetFamilyKey).
        private static readonly (string Key, uint Wcid, string CleanName)[] ForgeClasses =
        {
            ("sword",     30566, "Sword"),      // swordsabra — single strike
            ("sword_ms",   6853, "Rapier"),     // swordrapier — multi-strike
            ("dagger",    30596, "Poniard"),    // daggerponiard — single strike
            ("dagger_ms",  3779, "Dagger"),     // daggerelectric — multi-strike
            ("axe",         301, "Axe"),        // axebattle
            ("mace",        331, "Mace"),       // mace (jitte folds into this family)
            ("spear",       348, "Spear"),      // spear
            ("staff",       338, "Staff"),      // quarterstaff
            // FINESSE unarmed (owner 2026-08-11): the old pick (30612 knuckleselectric) was a
            // LightWeapons weenie, so a finesse-spec test char could not swing it. 31784 is
            // WeaponSkill 46 with the SAME weapon type / attack type / base damage, so it resolves
            // to the identical "unarmed" scaling family - a pure skill swap.
            ("ua",        31784, "Claw"),       // ace31784-claw — W_WeaponType Unarmed, Finesse
            ("cleaver",   40618, "Spadone"),    // spadone — 2H slash line
            ("spear2h",   40818, "Corsesca"),   // corsesca — 2H thrust line
            ("bow",       29243, "Bow"),        // bowpiercing
            ("crossbow",  29250, "Crossbow"),   // crossbowpiercing
            ("atlatl",    29254, "Atlatl"),     // atlatlelectric
            ("wand",      29265, "Sceptre"),    // wandslashing (gets EDM 1.5)
        };

        private static DamageType? ParseElement(string s)
        {
            return s?.ToLowerInvariant() switch
            {
                "slash" => DamageType.Slash,
                "pierce" => DamageType.Pierce,
                "bludge" or "bludgeon" => DamageType.Bludgeon,
                "acid" => DamageType.Acid,
                "fire" => DamageType.Fire,
                "cold" or "frost" => DamageType.Cold,
                "electric" or "lightning" => DamageType.Electric,
                "nether" => DamageType.Nether,
                _ => (DamageType?)null
            };
        }

        /// <summary>Mints one stamped test weapon into the player's pack through the normal item
        /// pipeline (never direct DB writes — guid allocation + biota caching make hand-inserted
        /// rows unsafe). Wield gate + stamps mirror the Creature_Death T11+ sweep; static damage
        /// stays the base weenie's (the scaling term/mod dominates). Element override also
        /// re-colors the icon underlay and renames coherently ("Nether Spadone (Test q800)").</summary>
        // ─────────────── Weapon loadout cards (/wsforge cards clause, owner 2026-08-11) ───────────────
        // Deterministic versions of the Zone Control loot special rolls (ZoneLootMutator.TrySpecialRolls):
        // the SAME properties and clamps, but exact configured values instead of chance rolls. Crafts
        // (hilt/bowstring) apply LAST, same rule as loot - their bonuses add on top of the other cards.

        private sealed class ForgeCards
        {
            public bool Proc; public uint ProcSpell; public double ProcRate = 0.15;
            public bool Rend;
            public double? RendPower;
            public int? Cleave;
            public int? Split; public double SplitRange = 8.0; public double SplitDmg = 1.0;
            public double? Bite;
            public double? Crush;
            public double? ArmorRend;
            public double? ShieldCleave;
            public bool Phantom;
            public double? Slayer; public CreatureType SlayerType = CreatureType.Drudge;
            public bool Paragon;
            public bool Hilt;
            public bool Bowstring;
        }

        private const ImbuedEffectType ForgeAllRends =
            ImbuedEffectType.SlashRending | ImbuedEffectType.PierceRending | ImbuedEffectType.BludgeonRending |
            ImbuedEffectType.AcidRending | ImbuedEffectType.ColdRending | ImbuedEffectType.ElectricRending |
            ImbuedEffectType.FireRending | ImbuedEffectType.NetherRending;

        /// <summary>Parses "key=val,key,..." (the part after "cards:"). Unknown keys are an error,
        /// not a silent skip - same contract as /asforge.</summary>
        private static string ParseForgeCards(string clause, ForgeCards cards)
        {
            foreach (var token in clause.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split('=');
                var key = parts[0].ToLowerInvariant();
                var val = parts.Length > 1 ? parts[1] : null;
                double Num(double dflt) => val != null && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : dflt;
                switch (key)
                {
                    case "proc": cards.Proc = true; cards.ProcSpell = (uint)Num(0); break;
                    case "procrate": cards.ProcRate = Math.Clamp(Num(0.15), 0.0, 1.0); break;
                    case "rend": cards.Rend = true; break;
                    case "rendpower": cards.RendPower = Math.Clamp(Num(1.5), 1.5, 10.0); break;
                    case "cleave": cards.Cleave = (int)Math.Clamp(Num(1), 1, 10); break;
                    case "split": cards.Split = (int)Math.Clamp(Num(1), 1, 10); break;
                    case "splitrange": cards.SplitRange = Math.Clamp(Num(8), 0.0, 50.0); break;
                    case "splitdmg": cards.SplitDmg = Math.Clamp(Num(1), 0.0, 1.0); break;
                    case "bite": cards.Bite = Math.Clamp(Num(0.5), 0.0, 1.0); break;
                    case "crush": cards.Crush = Math.Clamp(Num(2), 2.0, 10.0); break;
                    case "armorrend": cards.ArmorRend = Math.Clamp(Num(0.5), 0.0, 1.0); break;
                    case "shieldcleave": cards.ShieldCleave = Math.Clamp(Num(0.5), 0.0, 1.0); break;
                    case "phantom": cards.Phantom = true; break;
                    case "slayer": cards.Slayer = Math.Clamp(Num(1.5), 1.5, 10.0); break;
                    case "slayertype":
                        if (val == null || !Enum.TryParse<CreatureType>(val, true, out var ct) || ct == CreatureType.Invalid)
                            return $"cards: unknown slayertype '{val}' (use a CreatureType name, e.g. drudge, olthoi, virindi)";
                        cards.SlayerType = ct;
                        break;
                    case "paragon": cards.Paragon = true; break;
                    case "hilt": cards.Hilt = true; break;
                    case "bowstring": cards.Bowstring = true; break;
                    default: return $"cards: unknown key '{key}'";
                }
            }
            return null;
        }

        private static ImbuedEffectType ForgeMatchingRend(DamageType dt)
        {
            if (dt.HasFlag(DamageType.Slash)) return ImbuedEffectType.SlashRending;
            if (dt.HasFlag(DamageType.Pierce)) return ImbuedEffectType.PierceRending;
            if (dt.HasFlag(DamageType.Bludgeon)) return ImbuedEffectType.BludgeonRending;
            if (dt.HasFlag(DamageType.Acid)) return ImbuedEffectType.AcidRending;
            if (dt.HasFlag(DamageType.Cold)) return ImbuedEffectType.ColdRending;
            if (dt.HasFlag(DamageType.Electric)) return ImbuedEffectType.ElectricRending;
            if (dt.HasFlag(DamageType.Fire)) return ImbuedEffectType.FireRending;
            if (dt.HasFlag(DamageType.Nether)) return ImbuedEffectType.NetherRending;
            return ImbuedEffectType.Undef;
        }

        /// <summary>Max-level bolt matching the weapon's own damage type (test gear wants max).</summary>
        private static uint ForgeDefaultProcSpell(DamageType dt)
        {
            var list = dt.HasFlag(DamageType.Slash) ? SpellLevelProgression.WhirlingBlade
                     : dt.HasFlag(DamageType.Pierce) ? SpellLevelProgression.ForceBolt
                     : dt.HasFlag(DamageType.Bludgeon) ? SpellLevelProgression.ShockWave
                     : dt.HasFlag(DamageType.Acid) ? SpellLevelProgression.AcidStream
                     : dt.HasFlag(DamageType.Fire) ? SpellLevelProgression.FlameBolt
                     : dt.HasFlag(DamageType.Cold) ? SpellLevelProgression.FrostBolt
                     : dt.HasFlag(DamageType.Electric) ? SpellLevelProgression.LightningBolt
                     : dt.HasFlag(DamageType.Nether) ? SpellLevelProgression.HarmOther
                     : SpellLevelProgression.ForceBolt;   // plain bows etc. - element lives on the ammo
            return (uint)list[list.Count - 1];
        }

        /// <summary>Stamps the enabled cards; returns a " | cards: ... | skipped: ..." note for the
        /// forge message (empty when no cards).</summary>
        private static string ApplyForgeCards(WorldObject wo, ForgeCards c)
        {
            var isMelee = wo is MeleeWeapon;
            var isMissile = wo is MissileLauncher;
            var applied = new List<string>();
            var skipped = new List<string>();

            if (c.Proc)
            {
                if (isMelee || isMissile)
                {
                    var spellId = c.ProcSpell != 0 ? c.ProcSpell : ForgeDefaultProcSpell(wo.W_DamageType);
                    wo.ProcSpell = spellId;
                    wo.ProcSpellRate = c.ProcRate;
                    wo.ProcSpellSelfTargeted = false;
                    applied.Add($"proc {spellId} @ {c.ProcRate:0.##}");
                }
                else skipped.Add("proc (melee/missile only)");
            }

            if (c.Rend)
            {
                var rend = ForgeMatchingRend(wo.W_DamageType);
                if (rend != ImbuedEffectType.Undef) { wo.ImbuedEffect |= rend; applied.Add(rend.ToString()); }
                else skipped.Add("rend (no resolvable damage type)");
            }

            if (c.ArmorRend.HasValue)
            {
                if (isMelee || isMissile)
                {
                    wo.ImbuedEffect |= ImbuedEffectType.ArmorRending;
                    wo.SetProperty((PropertyFloat)ZoneLootMutator.ArmorRendOverridePropId, c.ArmorRend.Value);
                    applied.Add($"armorrend {c.ArmorRend.Value:0.##}");
                }
                else skipped.Add("armorrend (melee/missile only)");
            }

            if (c.RendPower.HasValue)
            {
                if ((wo.GetImbuedEffects() & ForgeAllRends) != 0)
                {
                    wo.SetProperty((PropertyFloat)ZoneLootMutator.RendingModOverridePropId, c.RendPower.Value);
                    applied.Add($"rendpower {c.RendPower.Value:0.##}");
                }
                else skipped.Add("rendpower (weapon carries no rend)");
            }

            if (c.Cleave.HasValue)
            {
                if (isMelee)
                {
                    wo.SetProperty(PropertyInt.Cleaving, c.Cleave.Value + 1);   // engine: CleaveTargets = Cleaving - 1
                    applied.Add($"cleave {c.Cleave.Value}");
                }
                else skipped.Add("cleave (melee only)");
            }

            if (c.Split.HasValue)
            {
                if (isMissile)
                {
                    wo.SetProperty((PropertyBool)ZoneLootMutator.SplitArrowsBoolId, true);
                    wo.SetProperty((PropertyInt)ZoneLootMutator.SplitArrowCountIntId, c.Split.Value);
                    wo.SetProperty((PropertyFloat)ZoneLootMutator.SplitArrowRangeFloatId, c.SplitRange);
                    wo.SetProperty((PropertyFloat)ZoneLootMutator.SplitArrowDmgFloatId, c.SplitDmg);
                    applied.Add($"split {c.Split.Value} r{c.SplitRange:0.#} d{c.SplitDmg:0.##}");
                }
                else skipped.Add("split (bows only)");
            }

            if (c.Bite.HasValue) { wo.CriticalFrequency = c.Bite.Value; applied.Add($"bite {c.Bite.Value:0.##}"); }

            // Crushing Blow: card value IS the final crit multiplier; engine computes 1 + CriticalMultiplier.
            if (c.Crush.HasValue) { wo.SetProperty(PropertyFloat.CriticalMultiplier, c.Crush.Value - 1.0); applied.Add($"crush {c.Crush.Value:0.##}x"); }

            if (c.ShieldCleave.HasValue) { wo.IgnoreShield = c.ShieldCleave.Value; applied.Add($"shieldcleave {c.ShieldCleave.Value:0.##}"); }

            if (c.Phantom)
            {
                wo.IgnoreMagicArmor = true;
                wo.IgnoreMagicResist = true;
                applied.Add("phantom");
            }

            if (c.Slayer.HasValue)
            {
                wo.SlayerCreatureType = c.SlayerType;
                wo.SlayerDamageBonus = c.Slayer.Value;
                applied.Add($"slayer {c.SlayerType} {c.Slayer.Value:0.##}x");
            }

            if (c.Paragon)
            {
                wo.ItemMaxLevel = (wo.ItemMaxLevel ?? 0) + 1;
                wo.ItemBaseXp = 2000000000;
                wo.ItemTotalXp = wo.ItemTotalXp ?? 0;
                applied.Add("paragon");
            }

            // crafts LAST - numbers mirror the live recipes exactly (ZoneLootMutator hilt/bowstring blocks)
            if (c.Hilt)
            {
                if (isMelee)
                {
                    wo.SetProperty(PropertyBool.Ivoryable, true);
                    wo.SetProperty(PropertyInt.WieldRequirements2, 8);   // WieldRequirement.Training
                    wo.SetProperty(PropertyInt.WieldSkillType2, 46);
                    wo.SetProperty(PropertyInt.WieldDifficulty2, 3);     // specialized
                    wo.Value = 0;
                    wo.SetProperty(PropertyFloat.ManaStoneDestroyChance, 0.01);   // hilt completion marker
                    wo.SetProperty(PropertyFloat.DamageMod, (wo.GetProperty(PropertyFloat.DamageMod) ?? 1.0) + 1.075);
                    wo.SetProperty(PropertyFloat.CriticalFrequency, (wo.GetProperty(PropertyFloat.CriticalFrequency) ?? 0.1) + 0.25);
                    wo.SetProperty(PropertyFloat.CriticalMultiplier, (wo.GetProperty(PropertyFloat.CriticalMultiplier) ?? 1.0) + 0.175);
                    applied.Add("hilt");
                }
                else skipped.Add("hilt (melee only)");
            }

            if (c.Bowstring)
            {
                if (isMissile)
                {
                    wo.SetProperty(PropertyInt.WieldRequirements2, 8);
                    wo.SetProperty(PropertyInt.WieldSkillType2, 47);     // Missile Weapons
                    wo.SetProperty(PropertyInt.WieldDifficulty2, 3);
                    wo.SetProperty((PropertyBool)ZoneLootMutator.SplitArrowsBoolId, true);
                    wo.SetProperty((PropertyInt)ZoneLootMutator.SplitArrowCountIntId,
                        (wo.GetProperty((PropertyInt)ZoneLootMutator.SplitArrowCountIntId) ?? 0) + 1);   // stacks with split
                    wo.SetProperty((PropertyFloat)ZoneLootMutator.SplitArrowRangeFloatId, 12.0);         // recipe SETS 12 - string goes on last
                    wo.SetProperty(PropertyFloat.DamageMod, (wo.GetProperty(PropertyFloat.DamageMod) ?? 1.0) + 0.05);
                    applied.Add("bowstring");
                }
                else skipped.Add("bowstring (bows only)");
            }

            var note = "";
            if (applied.Count > 0) note += " | cards: " + string.Join(", ", applied);
            if (skipped.Count > 0) note += " | skipped: " + string.Join(", ", skipped);
            return note;
        }

        /// <summary>The 102-slot pack (wcid 310025, the /testchar "Booster Pack" bag) a bagged forge
        /// run mints into. Found by name so repeat runs keep filling the same pack; created on first
        /// use, which costs one of the player's 7 visible pack slots.</summary>
        private const uint ForgePackWcid = 310025;
        private const string ForgePackName = "Weapon Pack";

        private static Container GetOrCreateForgePack(Player player)
        {
            var existing = player.Inventory.Values.OfType<Container>()
                .FirstOrDefault(c => string.Equals(c.Name, ForgePackName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            if (!(ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject(ForgePackWcid) is Container bag))
                return null;
            bag.Name = ForgePackName;
            bag.SetProperty(PropertyString.Name, ForgePackName);
            if (player.TryCreateInInventoryWithNetworking(bag))
                return bag;

            bag.Destroy();   // no free pack slot - caller falls back to the main pack
            return null;
        }

        /// <summary>Place a freshly minted item into a specific container the player already holds.
        /// TryCreateInInventoryWithNetworking only targets "main pack, else first side pack with
        /// room", so the client updates are done by hand here (same messages it sends).</summary>
        private static bool TryPlaceInPack(Player player, WorldObject wo, Container bag)
        {
            if (bag == null || !bag.TryAddToInventory(wo))
                return false;

            player.Session.Network.EnqueueSend(new GameMessageCreateObject(wo));
            player.Session.Network.EnqueueSend(
                new GameEventItemServerSaysContainId(player.Session, wo, bag),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.EncumbranceVal, player.EncumbranceVal ?? 0));
            wo.SaveBiotaToDatabase();
            return true;
        }

        private static string ForgeWeapon(ACE.Server.WorldObjects.Player player, uint wcid, string cleanName,
            int quality, int tier, DamageType? element, ForgeCards cards = null, Container bag = null)
        {
            var wo = ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject(wcid);
            if (wo == null)
                return $"forge: could not create wcid {wcid}";

            ACE.Server.Factories.LootGenerationFactory.StripWieldRequirements(wo);
            // T10 = the basic tier, no aug wield gate (same rule as /asforge armor). A minwield-0
            // tier row would NOT give that: ApplyT11WieldRequirement falls back to the global gate.
            if (tier >= 11)
                ACE.Server.Factories.LootGenerationFactory.ApplyT11WieldRequirement(wo, tier);
            wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.WeaponAugScaleQuality, quality);
            wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.WeaponAugScaleTier, tier);

            // representative caster: real T11 wands carry an elemental multiplier
            if (wo is ACE.Server.WorldObjects.Caster)
                wo.ElementalDamageMod = 1.5;

            if (element != null)
            {
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.DamageType, (int)element.Value);

                // icon underlay matches the element (the base weenie's stays otherwise)
                var uiEffect = element.Value switch
                {
                    DamageType.Slash => UiEffects.Slashing,
                    DamageType.Pierce => UiEffects.Piercing,
                    DamageType.Bludgeon => UiEffects.Bludgeoning,
                    DamageType.Acid => UiEffects.Acid,
                    DamageType.Fire => UiEffects.Fire,
                    DamageType.Cold => UiEffects.Frost,
                    DamageType.Electric => UiEffects.Lightning,
                    DamageType.Nether => UiEffects.Nether,
                    _ => UiEffects.Undef
                };
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.UiEffects, (int)uiEffect);

                wo.Name = $"{element.Value} {cleanName} (Test q{quality})";
            }
            else
                wo.Name = $"{wo.Name} (Test q{quality})";

            // Provenance, mirroring real drops' "Dropped by / Location" block (owner 2026-08-01):
            // who forged it + the tier it was stamped at. AppraiseInfo's per-viewer bonus line
            // anchors above "Created by" the same way it does "Dropped by".
            wo.LongDesc = $"Created by: {player.Name}\nTier: {tier}";

            // Everything the forge mints is Attuned + Bonded (owner 2026-08-02) — test gear
            // can't be traded, vendored, or dropped on death. Same stamps as /asforge.
            wo.Attuned = ACE.Entity.Enum.AttunedStatus.Attuned;
            wo.Bonded = ACE.Entity.Enum.BondedStatus.Bonded;

            var cardNote = cards != null ? ApplyForgeCards(wo, cards) : "";

            // Bagged runs fill the Weapon Pack; if it is full (or could not be made) the weapon still
            // lands normally rather than being lost.
            var placed = bag != null && TryPlaceInPack(player, wo, bag);
            if (!placed && !player.TryCreateInInventoryWithNetworking(wo))
            {
                wo.Destroy();
                return $"forge: could not place {wo.Name} in inventory (full?)";
            }

            return $"forged: {wo.Name} -> family {WeaponScalingCombat.GetFamilyKey(wo) ?? "none"}, " +
                   $"grade {WeaponScalingManager.GetQualityGrade(quality)} ({quality}/1000), tier {tier}" +
                   (placed ? " [" + ForgePackName + "]" : "") + cardNote;
        }

        [CommandHandler("wstestkit", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 0,
            "Grants Weapon Scaling test weapons (UA, bow, sword, wand) stamped at a fixed quality.",
            "[quality 0-1000, default 500] [element: slash|pierce|bludge|acid|fire|cold|electric|nether]")]
        public static void HandleWsTestKit(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            var player = session.Player;
            if (player == null)
                return;

            var quality = 500;
            if (parameters.Length > 0 && int.TryParse(parameters[0], out var q))
                quality = Math.Clamp(q, 0, 1000);

            DamageType? element = null;
            if (parameters.Length > 1)
            {
                element = ParseElement(parameters[1]);
                if (element == null)
                {
                    Msg("wstestkit: unknown element. Use slash|pierce|bludge|acid|fire|cold|electric|nether.");
                    return;
                }
            }

            foreach (var key in new[] { "ua", "bow", "sword", "wand" })
            {
                var cls = ForgeClasses.First(c => c.Key == key);
                Msg(ForgeWeapon(player, cls.Wcid, cls.CleanName, quality, 11, element));
            }
        }

        /// <summary>The Admin > Forge subtab's backend (owner 2026-08-01): any single weapon
        /// class at any quality/element/tier, minted live.</summary>
        [CommandHandler("wsforge", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 1,
            "Forges one Weapon Scaling test weapon of the given class (or a full set with 'all').",
            "<class|all> [quality 0-1000, default 500] [element] [tier 10-25, default 11; 10 = basic, no wield gate] [cards:key=val,key,...]\n" +
            "Classes: sword sword_ms dagger dagger_ms axe mace spear staff ua cleaver spear2h bow crossbow atlatl wand\n" +
            "cards: proc[=spellId] procrate=0-1 (Cast on Strike; no id = max-level bolt matching the element) rend (matching rend imbue)\n" +
            "rendpower=1.5-10 cleave=1-10 (melee) split=1-10 splitrange=0-50 splitdmg=0-1 (bows) bite=0-1 (crit chance)\n" +
            "crush=2-10 (crit damage mult) armorrend=0-1 shieldcleave=0-1 phantom slayer=1.5-10 slayertype=<name> paragon hilt bowstring\n" +
            "bag = mint into a 102-slot \"Weapon Pack\" instead of loose in the main pack (costs one pack slot, reused across runs)")]
        public static void HandleWsForge(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            var player = session.Player;
            if (player == null)
                return;

            // The cards clause may sit at any position - strip it out of the positional args first.
            ForgeCards cards = null;
            foreach (var p in parameters)
            {
                if (!p.StartsWith("cards:", StringComparison.OrdinalIgnoreCase))
                    continue;
                cards = new ForgeCards();
                var err = ParseForgeCards(p.Substring(6), cards);
                if (err != null)
                {
                    Msg("wsforge " + err);
                    return;
                }
            }
            // "bag" anywhere = mint into the Weapon Pack (owner 2026-08-11: a premade's weapon set
            // should not bury the main pack).
            var useBag = parameters.Any(p => p.Equals("bag", StringComparison.OrdinalIgnoreCase));
            var positional = parameters
                .Where(p => !p.StartsWith("cards:", StringComparison.OrdinalIgnoreCase)
                            && !p.Equals("bag", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (positional.Length == 0)
            {
                Msg("wsforge: missing class. Classes: all " + string.Join(" ", ForgeClasses.Select(c => c.Key)));
                return;
            }

            var classKey = positional[0].ToLowerInvariant();
            var all = classKey == "all";
            var cls = ForgeClasses.FirstOrDefault(c => c.Key == classKey);
            if (!all && cls.Wcid == 0)
            {
                Msg("wsforge: unknown class. Classes: all " + string.Join(" ", ForgeClasses.Select(c => c.Key)));
                return;
            }

            var quality = 500;
            if (positional.Length > 1 && int.TryParse(positional[1], out var q))
                quality = Math.Clamp(q, 0, 1000);

            DamageType? element = null;
            if (positional.Length > 2 && !positional[2].Equals("base", StringComparison.OrdinalIgnoreCase))
            {
                element = ParseElement(positional[2]);
                if (element == null)
                {
                    Msg("wsforge: unknown element. Use base|slash|pierce|bludge|acid|fire|cold|electric|nether.");
                    return;
                }
            }

            var tier = 11;
            if (positional.Length > 3 && int.TryParse(positional[3], out var t))
                tier = Math.Clamp(t, 10, 25);

            var bag = useBag ? GetOrCreateForgePack(player) : null;
            if (useBag && bag == null)
                Msg($"wsforge: no free pack slot for a {ForgePackName} - forging into the main pack instead.");

            if (all)
            {
                // one of every class at the same quality/element/tier (owner 2026-08-01)
                foreach (var c in ForgeClasses)
                    Msg(ForgeWeapon(player, c.Wcid, c.CleanName, quality, tier, element, cards, bag));
                return;
            }

            Msg(ForgeWeapon(player, cls.Wcid, cls.CleanName, quality, tier, element, cards, bag));
        }
    }
}
