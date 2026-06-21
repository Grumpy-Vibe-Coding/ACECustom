using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Command;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    public static class DevCommands
    {
        [CommandHandler("dev", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 0,
            "Developer administration commands.",
            "Usage: /dev [invasion|invasions]")]
        public static void HandleDev(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null) return;

            var sub = parameters.Length > 0 ? parameters[0].ToLower() : "help";

            if (sub == "invasion" || sub == "invasions")
            {
                HandleInvasionSubcommand(session, parameters.Skip(1).ToArray());
            }
            else
            {
                // Show main developer help menu
                ShowMainHelp(session);
            }
        }

        private static void ShowMainHelp(Session session)
        {
            var msg = "=== Developer Commands ===\n" +
                      "  /dev invasion(s) - Administer the random town invasion system.";
            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.System));
        }

        private static void HandleInvasionSubcommand(Session session, string[] parameters)
        {
            var sub = parameters.Length > 0 ? parameters[0].ToLower() : "help";

            switch (sub)
            {
                case "start":
                    HandleStart(session, parameters.Skip(1).ToArray());
                    break;
                case "stop":
                    HandleStop(session);
                    break;
                case "status":
                    HandleStatus(session);
                    break;
                case "cooldown":
                    HandleCooldown(session, parameters.Skip(1).ToArray());
                    break;
                case "threshold":
                    HandleThreshold(session, parameters.Skip(1).ToArray());
                    break;
                case "timeout":
                    HandleTimeout(session, parameters.Skip(1).ToArray());
                    break;
                case "minions":
                    HandleMinions(session, parameters.Skip(1).ToArray());
                    break;
                case "enable":
                case "disable":
                    HandleEnable(session, sub);
                    break;
                default:
                    ShowInvasionHelp(session);
                    break;
            }
        }

        private static void ShowInvasionHelp(Session session)
        {
            var msg = "=== Invasion Commands ===\n" +
                      "  /dev invasion enable|disable - Toggle automatic invasions on or off\n" +
                      "  /dev invasion start <town> <species> - Start a specific invasion\n" +
                      "  /dev invasion stop - Force stop the current invasion\n" +
                      "  /dev invasion status - Display current invasion status\n" +
                      "  /dev invasion cooldown [min|max] <value> - Set random cooldown range (e.g. cooldown min 1h  cooldown max 4h)\n" +
                      "  /dev invasion threshold <damage|healing> <value> - Set participation requirements (e.g. 500k, 10k)\n" +
                      "  /dev invasion timeout <seconds> - Set proximity check grace period\n" +
                      "  /dev invasion minions [on|off] - Toggle minion wave spawning (default: off)";
            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));
        }

        private static void HandleStart(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /dev invasion start <town> <species>", ChatMessageType.System));
                return;
            }

            var town = args[0];
            var species = args[1];

            if (InvasionManager.IsActive)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("An invasion is already active!", ChatMessageType.System));
                return;
            }

            if (InvasionManager.StartInvasion(town, species))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Started invasion in {town} ({species}).", ChatMessageType.System));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Failed to start invasion. Ensure the town and species are valid, and event exists in the database.", ChatMessageType.System));
            }
        }

        private static void HandleStop(Session session)
        {
            if (!InvasionManager.IsActive)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("No invasion is currently active.", ChatMessageType.System));
                return;
            }

            InvasionManager.StopInvasion(false);
            session.Network.EnqueueSend(new GameMessageSystemChat("Invasion stopped.", ChatMessageType.System));
        }

        private static void HandleStatus(Session session)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Invasion System Status ===");
            sb.AppendLine($"Auto-Invasions: {(InvasionManager.Enabled ? "ENABLED" : "DISABLED")}");
            sb.AppendLine($"Minion Spawning: {(InvasionManager.SpawnMinions ? "ON" : "OFF")}");
            sb.AppendLine($"Active: {InvasionManager.IsActive}");
            
            if (InvasionManager.IsActive)
            {
                sb.AppendLine($"Town: {InvasionManager.ActiveTown}");
                sb.AppendLine($"Species: {InvasionManager.ActiveSpecies}");
                sb.AppendLine($"Generator: {(InvasionManager.ActiveGenerator != null ? "Active" : "Null")}");
                if (InvasionManager.ActiveBoss != null)
                {
                    var boss = InvasionManager.ActiveBoss;
                    var healthPct = boss.Health.MaxValue > 0 ? (double)boss.Health.Current / boss.Health.MaxValue * 100.0 : 0.0;
                    sb.AppendLine($"Boss: {boss.Name} - Health: {boss.Health.Current:N0} / {boss.Health.MaxValue:N0} ({healthPct:F1}%)");
                }
                else
                {
                    sb.AppendLine("Boss: Not Spawned");
                }
                sb.AppendLine($"Elapsed Time: {InvasionManager.FormatMmSs(Time.GetUnixTime() - InvasionManager.InvasionStartTime)}");
            }
            else
            {
                var now = Time.GetUnixTime();
                var remaining = InvasionManager.NextInvasionTime - now;
                sb.AppendLine($"Cooldown remaining: {(remaining > 0 ? InvasionManager.FormatMmSs(remaining) : "0:00 (Ready)")}");
            }

            sb.AppendLine($"Cooldown Range: {InvasionManager.FormatMmSs(InvasionManager.CooldownMin)} – {InvasionManager.FormatMmSs(InvasionManager.CooldownMax)}");
            sb.AppendLine($"Proximity Timeout (Grace): {InvasionManager.ProximityTimeout}s");
            sb.AppendLine($"Damage Threshold: {InvasionManager.FormatCompact(InvasionManager.DamageThreshold)}");
            sb.AppendLine($"Healing Threshold: {InvasionManager.FormatCompact(InvasionManager.HealingThreshold)}");

            if (InvasionManager.IsActive)
            {
                sb.AppendLine("\n--- Participants ---");
                var anyParticipants = false;
                foreach (var kvp in InvasionManager.PlayerDamageTracker)
                {
                    var player = PlayerManager.GetOnlinePlayer(kvp.Key);
                    var name = player != null ? player.Name : $"0x{kvp.Key:X8}";
                    InvasionManager.PlayerHealingTracker.TryGetValue(kvp.Key, out var heal);
                    sb.AppendLine($"  {name} - Damage: {kvp.Value:N0}, Healing: {heal:N0} (Eligible: {InvasionManager.IsEligible(player)})");
                    anyParticipants = true;
                }
                if (!anyParticipants)
                    sb.AppendLine("  No participants yet.");
            }

            session.Network.EnqueueSend(new GameMessageSystemChat(sb.ToString().Replace("\r", ""), ChatMessageType.Broadcast));
        }

        private static void HandleCooldown(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                var min = InvasionManager.CooldownMin;
                var max = InvasionManager.CooldownMax;
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"Cooldown range: {FormatDuration(min)} – {FormatDuration(max)} ({min}s – {max}s)\n" +
                    "  /dev invasion cooldown min <value>\n" +
                    "  /dev invasion cooldown max <value>",
                    ChatMessageType.System));
                return;
            }

            var sub = args[0].ToLower();
            if ((sub == "min" || sub == "max") && args.Length >= 2)
            {
                if (!TryParseDuration(args[1], out double val))
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("Invalid value. Examples: 1h, 4h, 3600", ChatMessageType.System));
                    return;
                }
                if (sub == "min")
                {
                    InvasionManager.CooldownMin = val;
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Cooldown min set to {FormatDuration(val)} ({val}s).", ChatMessageType.System));
                }
                else
                {
                    InvasionManager.CooldownMax = val;
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Cooldown max set to {FormatDuration(val)} ({val}s).", ChatMessageType.System));
                }
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /dev invasion cooldown [min|max] <value>   e.g. cooldown min 1h   cooldown max 4h", ChatMessageType.System));
            }
        }

        private static void HandleThreshold(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /dev invasion threshold <damage|healing> <value>", ChatMessageType.System));
                return;
            }

            var type = args[0].ToLower();
            var valStr = args[1];

            if (!InvasionManager.TryParseThreshold(valStr, out long parsedVal))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Invalid threshold value '{valStr}'. Supported formats: 500k, 10k, 1.5m.", ChatMessageType.System));
                return;
            }

            if (type == "damage" || type == "dmg")
            {
                InvasionManager.DamageThreshold = parsedVal;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Damage threshold set to {parsedVal:N0}.", ChatMessageType.System));
            }
            else if (type == "healing" || type == "heal")
            {
                InvasionManager.HealingThreshold = parsedVal;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Healing threshold set to {parsedVal:N0}.", ChatMessageType.System));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Invalid threshold type. Use damage or healing.", ChatMessageType.System));
            }
        }

        private static void HandleTimeout(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                var cur = InvasionManager.ProximityTimeout;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Current proximity timeout: {FormatDuration(cur)} ({cur}s)", ChatMessageType.System));
                return;
            }

            if (TryParseDuration(args[0], out double val))
            {
                InvasionManager.ProximityTimeout = val;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Proximity timeout set to {FormatDuration(val)} ({val}s).", ChatMessageType.System));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Invalid timeout value. Examples: 120, 2m, 90s", ChatMessageType.System));
            }
        }

        /// <summary>
        /// Parses a duration string into seconds. Accepts:
        ///   raw number     → seconds  (e.g. "120" → 120)
        ///   Ns             → seconds  (e.g. "90s" → 90)
        ///   Nm             → minutes  (e.g. "2m"  → 120)
        ///   Nh             → hours    (e.g. "1h"  → 3600)
        ///   NhNm           → combined (e.g. "1h30m" → 5400)
        /// </summary>
        private static bool TryParseDuration(string input, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim().ToLower();

            // Raw number — treat as seconds
            if (double.TryParse(input, out seconds))
                return seconds >= 0;

            // Try combined NhNm (e.g. 1h30m)
            double total = 0;
            bool matched = false;
            var remaining = input;

            var hIdx = remaining.IndexOf('h');
            if (hIdx > 0 && double.TryParse(remaining[..hIdx], out double hours))
            {
                total += hours * 3600;
                remaining = remaining[(hIdx + 1)..];
                matched = true;
            }

            var mIdx = remaining.IndexOf('m');
            if (mIdx > 0 && double.TryParse(remaining[..mIdx], out double mins))
            {
                total += mins * 60;
                remaining = remaining[(mIdx + 1)..];
                matched = true;
            }

            var sIdx = remaining.IndexOf('s');
            if (sIdx > 0 && double.TryParse(remaining[..sIdx], out double secs))
            {
                total += secs;
                matched = true;
            }

            if (matched)
            {
                seconds = total;
                return seconds >= 0;
            }

            return false;
        }

        /// <summary>Formats seconds as a human-readable duration string (e.g. "1h 30m", "2m 30s", "45s").</summary>
        private static string FormatDuration(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(totalSeconds);
            var parts = new System.Collections.Generic.List<string>();
            if ((int)ts.TotalHours > 0) parts.Add($"{(int)ts.TotalHours}h");
            if (ts.Minutes > 0) parts.Add($"{ts.Minutes}m");
            if (ts.Seconds > 0 || parts.Count == 0) parts.Add($"{ts.Seconds}s");
            return string.Join(" ", parts);
        }

        private static void HandleMinions(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Minion spawning is currently: {(InvasionManager.SpawnMinions ? "ON" : "OFF")}", ChatMessageType.System));
                return;
            }

            var val = args[0].ToLower();
            if (val == "on" || val == "true" || val == "1")
            {
                InvasionManager.SpawnMinions = true;
                session.Network.EnqueueSend(new GameMessageSystemChat("Minion spawning enabled. Generators will activate on next invasion start.", ChatMessageType.System));
            }
            else if (val == "off" || val == "false" || val == "0")
            {
                InvasionManager.SpawnMinions = false;
                session.Network.EnqueueSend(new GameMessageSystemChat("Minion spawning disabled. Solo boss only.", ChatMessageType.System));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /dev invasion minions [on|off]", ChatMessageType.System));
            }
        }

        private static void HandleEnable(Session session, string sub)
        {
            InvasionManager.Enabled = sub == "enable";
            var state = InvasionManager.Enabled ? "ENABLED" : "DISABLED";
            session.Network.EnqueueSend(new GameMessageSystemChat($"Auto-invasions are now {state}.", ChatMessageType.System));
        }
    }
}
