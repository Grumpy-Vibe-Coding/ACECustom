using System;
using System.Globalization;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Command;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using ACE.Server.Managers;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// Developer command handler for smart ring settings.
    /// All commands require AccessLevel.Developer.
    /// </summary>
    public static class RingCommands
    {
        [CommandHandler("smartring", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Toggle or configure smart ring settings.",
            "Usage:\n" +
            "  /smartring                          — toggle personal smart ring setting\n" +
            "  /smartring [on|off]                 — turn personal smart ring on or off\n" +
            "  /smartring radius <value>           — (Dev) adjust default radius\n" +
            "  /smartring height <value>           — (Dev) adjust default height\n" +
            "  /smartring double <value>           — (Dev) adjust double proc chance (0.0 to 1.0)\n" +
            "  /smartring triple <value>           — (Dev) adjust triple proc chance (0.0 to 1.0)\n")]
        public static void HandleSmartRing(Session session, params string[] parameters)
        {
            var player = session.Player;
            if (player == null) return;

            if (parameters.Length == 0)
            {
                var classic = player.GetProperty(PropertyBool.ClassicRingAoe) ?? false;
                classic = !classic;

                if (classic)
                    player.SetProperty(PropertyBool.ClassicRingAoe, true);
                else
                    player.RemoveProperty(PropertyBool.ClassicRingAoe);

                player.SaveBiotaToDatabase(enqueueSave: true);

                SendStatusMessage(session, classic);
                return;
            }

            var key = parameters[0].ToLower();

            if (key == "on" || key == "off" || key == "enable" || key == "disable" || key == "true" || key == "false")
            {
                bool classic = key == "off" || key == "disable" || key == "false";

                if (classic)
                    player.SetProperty(PropertyBool.ClassicRingAoe, true);
                else
                    player.RemoveProperty(PropertyBool.ClassicRingAoe);

                player.SaveBiotaToDatabase(enqueueSave: true);

                SendStatusMessage(session, classic);
                return;
            }

            // Developer options beyond this point
            if (session.AccessLevel < AccessLevel.Developer)
            {
                Reply(session, "You do not have access to global smartring tuning parameters.");
                return;
            }

            if (parameters.Length >= 2)
            {
                var value = parameters[1];
                var (success, found, message) = SmartRingSettingsManager.TrySet(key, value);

                if (!found)
                {
                    Reply(session, $"Unknown key '{key}' for /smartring. Valid keys: radius, height, double, triple, or [on|off].");
                    return;
                }

                if (!success)
                {
                    Reply(session, $"[Smart Ring Error] {message}");
                    return;
                }

                Broadcast(session, $"[Smart Ring] {message}");
                return;
            }

            Reply(session, "Usage:\n" +
                           "  /smartring                          — toggle personal smart ring setting\n" +
                           "  /smartring [on|off]                 — turn personal smart ring on or off\n" +
                           "  /smartring radius <value>           — (Dev) adjust default radius\n" +
                           "  /smartring height <value>           — (Dev) adjust default height\n" +
                           "  /smartring double <value>           — (Dev) adjust double proc chance (0.0 to 1.0)\n" +
                           "  /smartring triple <value>           — (Dev) adjust triple proc chance (0.0 to 1.0)");
        }

        private static void SendStatusMessage(Session session, bool classic)
        {
            if (classic)
            {
                var msg = "[Smart Ring] Disabled\n" +
                          "  • Reverted to Classic Physics Mode\n" +
                          "  • Allows multi-hits\n" +
                          "  • Fixed number of projectiles\n" +
                          "  • Rings can miss targets";
                session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.System));
            }
            else
            {
                var msg = "[Smart Ring] Enabled\n" +
                          $"  • Radius: {SmartRingSettingsManager.Radius.ToString("0.0", CultureInfo.InvariantCulture)}\n" +
                          $"  • Height: {SmartRingSettingsManager.Height.ToString("0.0", CultureInfo.InvariantCulture)}\n" +
                          $"  • Double Proc Chance: {(SmartRingSettingsManager.DoubleChance * 100.0f).ToString("0.0", CultureInfo.InvariantCulture)}%\n" +
                          $"  • Triple Proc Chance: {(SmartRingSettingsManager.TripleChance * 100.0f).ToString("0.0", CultureInfo.InvariantCulture)}%";
                session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.System));
            }
        }

        private static void Reply(Session session, string msg)
        {
            if (session == null) { Console.WriteLine(msg); return; }
            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));
        }

        private static void Broadcast(Session session, string msg)
        {
            PlayerManager.BroadcastToAuditChannel(session?.Player, msg);

            if (session != null)
                session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));
        }
    }
}
