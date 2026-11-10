using System;
using System.Globalization;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// Player Traders, player side (owner 2026-10-28): /trader. One trader per account, set down where the owner stands; buyers
    /// open it like a vendor and pay from their bank. See TraderManager.
    /// </summary>
    public static class TraderCommands
    {
        private const string Usage =
            "/trader place [coins|lum] - set your trader down here (one per account), selling for Enlightened Coins or Luminance (1 = 1 billion)\n"
            + "/trader summon - your trader comes to where you stand\n"
            + "/trader dismiss - your trader packs up (its stock is kept)\n"
            + "/trader list - your trader's items, numbered, with their prices\n"
            + "/trader price <amount> - price the item you last appraised in your trader's window\n"
            + "/trader price <n|all> <amount> - price item n from /trader list (or every item)\n"
            + "/trader unprice <n|all> - take an item off sale (it stays in the trader)\n"
            + "/trader take <n|all> - take items back into your packs\n"
            + "/trader currency <coins|lum> - what your trader sells for (only while nothing is priced)\n"
            + "Stock your trader by opening it and using the Sell tab (nothing is paid). Buyers pay from their bank.";

        [CommandHandler("trader", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "Your account's trader: a vendor that sells your items for Enlightened Coins or Luminance, even while you are offline.",
            Usage)]
        public static void HandleTrader(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null)
                return;

            var verb = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "";
            var args = parameters.Skip(1).ToArray();

            switch (verb)
            {
                case "place":
                {
                    var currency = TraderManager.CurrencyCoins;
                    if (args.Length > 0 && !TryCurrency(args[0], out currency))
                    {
                        TraderManager.Say(player, "Usage: /trader place [coins|lum]");
                        return;
                    }
                    TraderManager.Place(player, currency);
                    return;
                }

                case "summon":
                    TraderManager.Summon(player);
                    return;

                case "dismiss":
                    TraderManager.Dismiss(player);
                    return;

                case "list":
                case "":
                    TraderManager.WithOwnTrader(player, trader => TraderManager.List(player, trader));
                    return;

                case "price":
                {
                    // "/trader price 500" (the item last appraised), "/trader price 3 500", "/trader price all 500"
                    var which = args.Length >= 2 ? args[0] : "";
                    var amountText = args.Length >= 2 ? string.Join("", args.Skip(1)) : args.Length == 1 ? args[0] : "";
                    if (!TryAmount(amountText, out var price) || price <= 0)
                    {
                        TraderManager.Say(player, "Usage: /trader price [n|all] <amount>  (a whole number above 0; 1.5k and 2m work too)");
                        return;
                    }
                    TraderManager.WithOwnTrader(player, trader => TraderManager.SetPrice(player, trader, TraderManager.Pick(player, trader, which), price));
                    return;
                }

                case "unprice":
                {
                    var which = args.Length > 0 ? args[0] : "";
                    TraderManager.WithOwnTrader(player, trader => TraderManager.SetPrice(player, trader, TraderManager.Pick(player, trader, which), 0));
                    return;
                }

                case "take":
                {
                    if (args.Length == 0)
                    {
                        TraderManager.Say(player, "Usage: /trader take <n|all>");
                        return;
                    }
                    TraderManager.WithOwnTrader(player, trader => TraderManager.Take(player, trader, TraderManager.Pick(player, trader, args[0])));
                    return;
                }

                case "currency":
                {
                    if (args.Length == 0 || !TryCurrency(args[0], out var currency))
                    {
                        TraderManager.Say(player, "Usage: /trader currency <coins|lum>");
                        return;
                    }
                    TraderManager.WithOwnTrader(player, trader => TraderManager.SetCurrency(player, trader, currency));
                    return;
                }

                default:
                    foreach (var line in Usage.Split('\n'))
                        TraderManager.Say(player, line);
                    return;
            }
        }

        private static bool TryCurrency(string text, out int currency)
        {
            switch (text.ToLowerInvariant())
            {
                case "coins":
                case "coin":
                case "ec":
                    currency = TraderManager.CurrencyCoins;
                    return true;
                case "lum":
                case "luminance":
                    currency = TraderManager.CurrencyLuminance;
                    return true;
                default:
                    currency = 0;
                    return false;
            }
        }

        /// <summary>"1500", "1,500", "1.5k", "2m", "1b".</summary>
        private static bool TryAmount(string text, out long amount)
        {
            amount = 0;
            text = text.Replace(",", "").Trim().ToLowerInvariant();
            if (text.Length == 0)
                return false;
            long scale = 1;
            switch (text[^1])
            {
                case 'k': scale = 1_000; break;
                case 'm': scale = 1_000_000; break;
                case 'b': scale = 1_000_000_000; break;
            }
            if (scale > 1)
                text = text[..^1];
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                return false;
            try
            {
                var value = number * scale;
                if (value != decimal.Truncate(value) || value > long.MaxValue)
                    return false;
                amount = (long)value;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
