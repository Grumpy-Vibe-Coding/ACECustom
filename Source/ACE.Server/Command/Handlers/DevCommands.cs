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
                default:
                    ShowInvasionHelp(session);
                    break;
            }
        }

        private static void ShowInvasionHelp(Session session)
        {
            var msg = "=== Invasion Commands ===\n" +
                      "  /dev invasion start <town> <species> - Start a specific invasion\n" +
                      "  /dev invasion stop - Force stop the current invasion\n" +
                      "  /dev invasion status - Display current invasion status\n" +
                      "  /dev invasion cooldown [<seconds>] - View or set the invasion cooldown\n" +
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

            sb.AppendLine($"Configured Cooldown: {InvasionManager.FormatMmSs(InvasionManager.CooldownTime)}");
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
                session.Network.EnqueueSend(new GameMessageSystemChat($"Current cooldown: {InvasionManager.CooldownTime} seconds", ChatMessageType.System));
                return;
            }

            if (double.TryParse(args[0], out double val))
            {
                InvasionManager.CooldownTime = val;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Cooldown set to {val} seconds.", ChatMessageType.System));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Invalid cooldown value.", ChatMessageType.System));
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
                session.Network.EnqueueSend(new GameMessageSystemChat($"Current proximity timeout (grace period): {InvasionManager.ProximityTimeout} seconds", ChatMessageType.System));
                return;
            }

            if (double.TryParse(args[0], out double val))
            {
                InvasionManager.ProximityTimeout = val;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Proximity timeout set to {val} seconds.", ChatMessageType.System));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Invalid timeout value.", ChatMessageType.System));
            }
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
    }
}
