using System;
using System.Globalization;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// Account Vault, player side (owner 2026-10-28): /storage. Players call it Storage - /vault is the Vaulted Dungeons roster.
    /// Players see Strongboxes (100 items each) and Bound Strongboxes (attuned items, one character's own); in code they are
    /// pages (VaultPage). They open beside the player (a Quartermaster within vault_butler_range, unless vault_anywhere). The item
    /// lines ("[Storage] item 0x... box N tier T stack S | Name", "bound box N" for a Bound Strongbox) are one per item so a client
    /// plugin can read them. "page" / "attuned" / "a<n>" still work as old names.
    /// </summary>
    public static class StorageCommands
    {
        /// <summary>"b3" (Bound Strongbox 3) - or the old "a3".</summary>
        private static bool IsBoundPrefix(string text) =>
            text.Length > 1 && (char.ToLowerInvariant(text[0]) == 'b' || char.ToLowerInvariant(text[0]) == 'a') && char.IsDigit(text[1]);

        private const string Usage =
            "/storage - the Quartermaster's Ledger: take a strongbox from it to open that strongbox\n"
            + "/storage summon - your Quartermaster comes to you and stays until you walk away (or /storage dismiss)\n"
            + "/storage box <n> - open strongbox n\n"
            + "/storage bound [n] - open your Bound Strongbox (attuned items go only there)\n"
            + "/storage boxes - how full each strongbox is\n"
            + "/storage list <n> - every item in strongbox n (a Bound Strongbox: /storage list b<n>)\n"
            + "/storage find <text> - every stored item whose name has the text\n"
            + "/storage get <item id or name> - take one item back (its strongbox opens); a name that fits several lists them\n"
            + "/storage name <n> <name> - name strongbox n (bound: b<n>); /storage name <n> clears it\n"
            + "/storage put <item id> - store one item you carry in the strongbox you have open\n"
            + "/storage sort - the Quartermaster re-files every strongbox by kind (Bows, Armor, Salvage ...); ones you named stay as they are\n"
            + "/storage close - close the open strongbox";

        [CommandHandler("storage", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "Your account storage: strongboxes of 100 items, shared by every character on the account (attuned items in a Bound Strongbox of their own).",
            Usage)]
        public static void HandleStorage(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null)
                return;

            var verb = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "";
            var arg = parameters.Length > 1 ? string.Join(" ", parameters.Skip(1)) : "";

            switch (verb)
            {
                case "sort":
                    VaultManager.Sort(player);
                    return;

                case "summon":
                    VaultManager.Summon(player);
                    return;

                case "dismiss":
                    VaultManager.Dismiss(player);
                    return;

                case "bell":   // admins, for testing: how players get a bell is the owner's call
                    if (session.AccessLevel < AccessLevel.Admin)
                        CommandHandlerHelper.WriteOutputInfo(session, Usage, ChatMessageType.Broadcast);
                    else
                        VaultManager.GiveBell(player);
                    return;

                case "":
                case "index":
                    VaultManager.OpenIndex(player);
                    return;

                case "name":
                {
                    var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    var which = words.Length > 0 ? words[0] : "";
                    var attunedPage = IsBoundPrefix(which);
                    if (!int.TryParse(attunedPage ? which.Substring(1) : which, out var pageNo))
                    {
                        VaultManager.Say(player, "Usage: /storage name <n> <name>  (a Bound Strongbox: b<n>; no name clears it)");
                        return;
                    }
                    VaultManager.NamePage(player, attunedPage ? VaultManager.PageKind.Attuned : VaultManager.PageKind.Account, pageNo, words.Length > 1 ? words[1] : "");
                    return;
                }

                case "box":
                case "strongbox":
                case "page":   // the old name
                    if (int.TryParse(arg, out var n))
                        VaultManager.OpenPage(player, VaultManager.PageKind.Account, n);
                    else
                        VaultManager.Say(player, "Usage: /storage box <n>");
                    return;

                case "bound":
                case "attuned":   // the old name
                    VaultManager.OpenPage(player, VaultManager.PageKind.Attuned, int.TryParse(arg, out var a) ? a : 1);
                    return;

                case "close":
                    VaultManager.CloseFor(player);
                    return;

                case "boxes":
                case "pages":   // the old name
                    VaultManager.Query(player, (pages, items) =>
                    {
                        if (pages.Count == 0)
                        {
                            player.SendMessage("[Storage] You have no strongboxes yet - /storage box 1 opens your first.");
                            return;
                        }
                        foreach (var p in pages)
                            player.SendMessage($"[Storage] {(p.Attuned ? "Bound Strongbox" : "Strongbox")} {p.Number}: {items.Count(i => i.Page.Id == p.Id)} of 100{(string.IsNullOrEmpty(p.Label) ? "" : " - " + p.Label)}");
                        player.SendMessage($"[Storage] {items.Count(i => !i.Page.Attuned)} items in {pages.Count(p => !p.Attuned)} strongbox(es), "
                            + $"{items.Count(i => i.Page.Attuned)} in Bound Strongbox(es). Allowed: {ServerConfig.vault_max_pages.Value} strongboxes, {ServerConfig.vault_max_attuned_pages.Value} bound.");
                    });
                    return;

                case "list":
                {
                    var attuned = IsBoundPrefix(arg);
                    if (!int.TryParse(attuned ? arg.Substring(1) : arg, out var page))
                    {
                        VaultManager.Say(player, "Usage: /storage list <n>  (a Bound Strongbox: /storage list b<n>)");
                        return;
                    }
                    VaultManager.Query(player, (pages, items) =>
                    {
                        var rows = items.Where(i => i.Page.Number == page && i.Page.Attuned == attuned).OrderBy(i => i.Name).ToList();
                        foreach (var r in rows)
                            player.SendMessage(VaultManager.Line(r));
                        player.SendMessage($"[Storage] end of {(attuned ? "Bound Strongbox" : "Strongbox")} {page}: {rows.Count} item(s)");
                    });
                    return;
                }

                case "find":
                    if (arg.Trim().Length < 2)
                    {
                        VaultManager.Say(player, "Usage: /storage find <at least 2 letters of the name>");
                        return;
                    }
                    VaultManager.Query(player, (pages, items) =>
                    {
                        var rows = items.Where(i => i.Name.Contains(arg.Trim(), StringComparison.OrdinalIgnoreCase))
                            .OrderBy(i => i.Page.Attuned).ThenBy(i => i.Page.Number).ThenBy(i => i.Name).ToList();
                        foreach (var r in rows.Take(200))
                            player.SendMessage(VaultManager.Line(r));
                        player.SendMessage($"[Storage] end of find '{arg.Trim()}': {rows.Count} item(s){(rows.Count > 200 ? " (first 200 shown)" : "")}");
                    });
                    return;

                case "put":
                {
                    var text = arg.Trim();
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        text = text.Substring(2);
                    if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var guid))
                    {
                        VaultManager.Say(player, "Usage: /storage put <item id>  (into the strongbox you have open)");
                        return;
                    }
                    VaultManager.PutItem(player, guid);
                    return;
                }

                case "get":
                {
                    var text = arg.Trim();
                    if (text.Length == 0)
                    {
                        VaultManager.Say(player, "Usage: /storage get <item id or name>");
                        return;
                    }
                    // an id is 0x... or exactly 8 hex digits; anything else is (part of) a name
                    var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.Substring(2) : text;
                    var isId = (hex.Length == 8 || text.Length != hex.Length) && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
                    var guid = isId ? uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0u;
                    VaultManager.Query(player, (pages, items) =>
                    {
                        VaultManager.ItemRow row;
                        if (isId)
                            row = items.FirstOrDefault(i => i.Guid == guid);
                        else
                        {
                            // a whole-name match wins over a part-name match
                            var exact = items.Where(i => i.Name.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
                            var part = exact.Count > 0 ? exact : items.Where(i => i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
                            if (part.Count > 1)
                            {
                                foreach (var r in part.OrderBy(i => i.Page.Attuned).ThenBy(i => i.Page.Number).Take(15))
                                    player.SendMessage(VaultManager.Line(r));
                                VaultManager.Say(player, $"{part.Count} items fit '{text}'{(part.Count > 15 ? " (first 15 shown)" : "")} - /storage get <id> takes one.");
                                return;
                            }
                            row = part.FirstOrDefault();
                        }
                        if (row == null)
                        {
                            VaultManager.Say(player, isId ? "That item is not in your storage." : $"Nothing in your storage is called '{text}'.");
                            return;
                        }
                        VaultManager.OpenPage(player, row.Page.Attuned ? VaultManager.PageKind.Attuned : VaultManager.PageKind.Account, row.Page.Number, row.Guid);
                    });
                    return;
                }

                default:
                    CommandHandlerHelper.WriteOutputInfo(session, Usage, ChatMessageType.Broadcast);
                    return;
            }
        }
    }
}
