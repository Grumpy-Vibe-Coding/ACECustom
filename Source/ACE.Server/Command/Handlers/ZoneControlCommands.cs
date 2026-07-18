using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using ACE.Common.Performance;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers;
using ACE.Server.Managers.ZoneControl;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// /zonecontrol — author + toggle controlled ZONES (Developer). A zone is a named set of landblocks
    /// governing one world Variation (0 = normal world, 11+ = variants), with an on/off switch, a DEFAULT
    /// stat set for all its monsters, and optional per-monster (WCID) overrides. No prestige/tier/boss concepts.
    /// Disable reverts monsters to baseline (live stats instantly, HP on respawn).
    /// </summary>
    public static class ZoneControlCommands
    {
        /// <summary>What a plugin "sync on" session is watching; rebuilt + pushed each <see cref="PushTick"/>.</summary>
        private class SyncWatch
        {
            public string Name;
            public uint? Wcid;
            public string LastPayload;       // change-detection: identical payloads aren't re-sent
            public DateTime LastSentUtc;     // ... except as a periodic keepalive/correction resend
        }

        private static readonly ConcurrentDictionary<Session, SyncWatch> _pluginSessions = new();
        private static readonly RateLimiter _pushTickRateLimiter = new RateLimiter(1, TimeSpan.FromSeconds(2));

        /// <summary>An unchanged [[ZC]] payload is still re-sent this often, so a plugin holding a stale
        /// optimistic value (e.g. after a failed command) gets corrected without the old every-2s spam.</summary>
        private const double SyncKeepaliveSeconds = 15.0;

        /// <summary>Called from WorldManager.UpdateGameWorld() every frame; rate-limited to once per 2s.
        /// Pushes [[ZC]] to registered plugin sessions — but only when the payload actually CHANGED since
        /// that session's last push (movement, live target values, any zone edit), or the keepalive is due.
        /// An idle GUI session generates ~one line per 15s instead of one per 2s.</summary>
        public static void PushTick()
        {
            if (_pluginSessions.IsEmpty)
                return;
            if (_pushTickRateLimiter.GetSecondsToWaitBeforeNextEvent() > 0)
                return;
            _pushTickRateLimiter.RegisterEvent();

            foreach (var kv in _pluginSessions)
            {
                var session = kv.Key;
                if (session.IsTerminated)
                {
                    _pluginSessions.TryRemove(session, out _);
                    continue;
                }

                var watch = kv.Value;
                var payload = BuildZonePayload(watch.Name, watch.Wcid, session);
                var now = DateTime.UtcNow;
                if (payload == watch.LastPayload && (now - watch.LastSentUtc).TotalSeconds < SyncKeepaliveSeconds)
                    continue;

                watch.LastPayload = payload;
                watch.LastSentUtc = now;
                ChatPacket.SendServerMessage(session, payload, ChatMessageType.Broadcast);
            }
        }

        [CommandHandler("zonecontrol", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Author/toggle Zone Control zones (any world area).",
            "help | list | here | create <name> <variation> [here|hex] | rename <old> <new> | delete <name> | "
            + "enable <name> | disable <name> | addlb <name> <hex|here> | removelb <name> <hex> | "
            + "set <name> <stat> <value> [--wcid <id>] | clearstat <name> <stat> [--wcid <id>] | show <name> [--wcid <id>] | "
            + "part <name> <part> <armor|damage|variance|dmgtype> <value> [--wcid <id>] | clearpart <name> <part> [field] [--wcid <id>] | "
            + "prop <name> <int|int64|float|bool> <idOrName> <value> [--wcid <id>] | clearprop <name> <type> <idOrName> [--wcid <id>] | "
            + "cantrip <name> <add|remove|list> [spellId] [--wcid <id>] | "
            + "currency <name> <add|remove|list> [itemWcid] [amount] [chance] [direct|corpse] [--wcid <id>] | "
            + "boundary <name> <on|off|show> | survey <name> [lbHex] | quests <name> | terrain <name> <hex> <type|clear> | "
            + "mobinfo <wcid> | effect <name> [dot on|off | dmg <amount> | type <name|percent> | interval <secs>] | reload")]
        public static void HandleZoneControl(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            if (parameters.Length == 0 || parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Msg("Zone Control commands (zones — any world area):");
                Msg("  /zonecontrol list | here | reload");
                Msg("  /zonecontrol create <name...> <variation|here> [here|<hex>]   variation: 0 = normal world, 11+ = variants");
                Msg("      multi-word names work unquoted (create Tou Tou here); quote names containing number words (create \"Zone 2\" 11)");
                Msg("  /zonecontrol rename <old> <new...> | delete <name>");
                Msg("  /zonecontrol enable <name> | disable <name> | setvar <name> <variation|here>");
                Msg("  /zonecontrol addlb <name> <hex|here> | removelb <name> <hex>");
                Msg("  /zonecontrol set <name> <stat> <value> [--wcid <id>]   (--wcid = a specific monster's override)");
                Msg("  /zonecontrol clearstat <name> <stat> [--wcid <id>] | show <name> [--wcid <id>]");
                Msg("  /zonecontrol effect <name> [show | dot on|off | dmg <amount> | type <fire|cold|...|percent> | interval <secs>]   (player DoT)");
                Msg("  /zonecontrol part <name> <part> <armor|damage|variance|dmgtype> <value> [--wcid <id>]   (per-body-part override; damage 0 = part stops attacking)");
                Msg("  /zonecontrol clearpart <name> <part> [armor|damage|variance|dmgtype] [--wcid <id>]   (no field = clear the whole part)");
                Msg("  /zonecontrol prop <name> <int|int64|float|bool> <idOrName> <value> [--wcid <id>]   (stamped on monsters at respawn)");
                Msg("  /zonecontrol clearprop <name> <int|int64|float|bool> <idOrName> [--wcid <id>]");
                Msg("  /zonecontrol cantrip <name> <add|remove|list> [spellId] [--wcid <id>]   (custom cantrip pool for the extra-loot-cantrip roll)");
                Msg("  /zonecontrol currency <name> add <itemWcid> <amount> [chance 0..1] [direct|corpse] | remove <itemWcid> | list   [--wcid <id>]   (per-kill bonus-currency drop table; direct = into the killer's inventory)");
                Msg("  /zonecontrol boundary <name> <on|off|show>   (bounded: players at the zone's variation may only roam bounded-zone landblocks; variation 11+ only)");
                Msg("  /zonecontrol survey <name> [lbHex]   (per-landblock content: generator + creature summary; lbHex = full detail for one landblock)");
                Msg("  /zonecontrol quests <name>   (quest registry for the plugin Quests tab; throttled to one pull per 60s)");
                Msg("  /zonecontrol terrain <name> <hex> <type|clear>   (override the map terrain color for one landblock; type = " + string.Join("/", ZoneControlManager.TerrainTags) + "; display-only)");
                Msg("  /zonecontrol mobinfo <wcid>   (weenie base data: body parts, resists, wields)");
                Msg("  parts = " + string.Join(", ", Enum.GetNames(typeof(CombatBodyPart)).Where(n => n != "Undefined")));
                Msg("  stats = " + string.Join(", ", ZoneStat.All));
                return;
            }

            // Re-tokenize honoring double quotes so zone names may contain spaces (ACE's CommandManager
            // splits purely on spaces): /zonecontrol enable "My Zone". Case is preserved everywhere;
            // lookups are case-insensitive, and my_zone is accepted for "My Zone" when typed without quotes.
            var args = RetokenizeParameters(parameters);
            if (args.Count == 0) { Msg("See /zonecontrol help."); return; }
            uint? wcid = ExtractWcidFlag(args);
            var sub = args[0].ToLowerInvariant();

            // Unquoted multi-word zone names: for any subcommand whose <name> is args[1], collapse the
            // LONGEST token-join that names an EXISTING zone into one arg — "enable Tou Tou" and
            // "set Tou Tou max_health 5000" work without quotes. create scans for its variation token
            // instead (the zone doesn't exist yet); sync's name sits at args[2].
            if (sub == "sync")
                CollapseZoneNameTokens(args, 2);
            else if (sub != "create")
                CollapseZoneNameTokens(args, 1);

            try
            {
                switch (sub)
                {
                    case "list":
                    {
                        var zones = ZoneControlManager.ListAreas();
                        if (zones.Count == 0) { Msg("(no zones)"); return; }
                        foreach (var z in zones.OrderBy(z => z.Name))
                            Msg($"  {z.Name,-20} {(z.Enabled ? "ENABLED " : "disabled")}  v{z.Variation}  lbs:{z.Landblocks.Count}  " +
                                $"stats:{z.Profile.Minion.Stats.Count} overrides:{z.Profile.WcidOverrides.Count}");
                        return;
                    }

                    case "here":
                    {
                        var loc = session.Player?.Location;
                        if (loc == null) { Msg("No location."); return; }
                        var lb = loc.LandblockId.Landblock;
                        var effVar = ZoneControlManager.GetEffectiveVariation(session.Player);
                        Msg($"Here: lb:{lb:X4}  variation v{effVar}");
                        var covering = ZoneControlManager.AreasCovering(lb);
                        if (covering.Count == 0) { Msg("  (no zone covers this landblock)"); return; }
                        foreach (var z in covering)
                            Msg($"  zone '{z.Name}' {(z.Enabled ? "ENABLED" : "disabled")} v{z.Variation}");
                        return;
                    }

                    case "reload":
                        ZoneControlManager.Reload();
                        Msg("Zone Control store reloaded from shard.");
                        return;

                    case "arealist":
                    {
                        // Machine-parseable zone list for the plugin dropdown: name,enabled,variation,lbCount
                        var sb = new StringBuilder("[[ZCA]]");
                        bool first = true;
                        foreach (var z in ZoneControlManager.ListAreas().OrderBy(z => z.Name))
                        {
                            if (!first) sb.Append('|');
                            first = false;
                            sb.Append(z.Name).Append(',').Append(z.Enabled ? 1 : 0).Append(',')
                              .Append(z.Variation).Append(',').Append(z.Landblocks.Count).Append(',')
                              .Append(z.Bounded ? 1 : 0);
                        }
                        Msg(sb.ToString());
                        return;
                    }

                    case "get":
                    {
                        if (args.Count < 2) { Msg("Usage: get <name> [--wcid <id>]"); return; }
                        Msg(BuildZonePayload(args[1], wcid, session));
                        return;
                    }

                    case "mobs":
                    {
                        if (args.Count < 2) { Msg("Usage: mobs <name>"); return; }
                        var name = args[1];
                        var mobs = ZoneControlManager.GetAreaMobs(name);
                        var sb = new StringBuilder("[[ZCM]]scope=").Append(name);
                        foreach (var m in mobs)
                            sb.Append('|').Append(m.Wcid).Append(',').Append(m.IsMonster ? 1 : 0).Append(',').Append(m.Name.Replace('|', ' ').Replace(',', ' '));
                        Msg(sb.ToString());
                        return;
                    }

                    case "sync":
                    {
                        // Machine handshake from the plugin — stay SILENT (no chat confirmation) so the periodic
                        // live-feed handshake never spams the player's chat window. Only a mistyped manual command
                        // gets the usage hint below.
                        if (args.Count >= 2 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                        {
                            _pluginSessions.TryRemove(session, out _);
                            return;
                        }
                        if (args.Count < 3 || !args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                        {
                            Msg("Usage: sync on <name> [--wcid <id>]  |  sync off");
                            return;
                        }
                        _pluginSessions[session] = new SyncWatch { Name = args[2], Wcid = wcid };
                        return;
                    }

                    case "create":
                    {
                        if (args.Count < 3) { Msg("Usage: create <name...> <variation|here> [here|<hex>]   (multi-word names ok: create Tou Tou here)"); return; }

                        // The name may be several unquoted tokens: everything BEFORE the first token that
                        // reads as a variation (a number or 'here'). A name WORD that is itself a number
                        // needs quotes: create "Zone 2" 11.
                        var varIdx = -1;
                        for (var i = 2; i < args.Count; i++)
                        {
                            if (args[i].Equals("here", StringComparison.OrdinalIgnoreCase) ||
                                int.TryParse(args[i].TrimStart('v', 'V'), out _))
                            { varIdx = i; break; }
                        }
                        if (varIdx < 0)
                        {
                            Msg("variation must be a number >= 0 (0 = normal world) or 'here' - none found after the name.");
                            Msg("  e.g. create Tou Tou here   |   create \"Zone 2\" 11   (quote names containing number words)");
                            return;
                        }

                        var name = SanitizeZoneName(string.Join(" ", args.Skip(1).Take(varIdx - 1)));
                        if (name.Length == 0) { Msg("Zone name required."); return; }
                        if (ZoneControlManager.GetArea(name) != null) { Msg($"Zone '{name}' already exists."); return; }

                        int variation;
                        if (args[varIdx].Equals("here", StringComparison.OrdinalIgnoreCase))
                            variation = ZoneControlManager.GetEffectiveVariation(session.Player);
                        else
                            int.TryParse(args[varIdx].TrimStart('v', 'V'), out variation);
                        if (variation < 0) { Msg($"variation must be >= 0 (you're at v{variation} - zones can't be created on rift design variations)."); return; }

                        ushort lb;
                        if (args.Count >= varIdx + 2)
                        {
                            if (!TryLandblockToken(session, args[varIdx + 1], out lb, out var lbErr)) { Msg(lbErr); return; }
                        }
                        else
                        {
                            var here = session.Player?.Location?.LandblockId.Landblock;
                            if (here == null) { Msg("No location — pass a hex landblock."); return; }
                            lb = here.Value;
                        }

                        ZoneControlManager.UpsertArea(new ControlledArea
                        {
                            Name = name, Variation = variation, Enabled = false,
                            Landblocks = new HashSet<ushort> { lb },
                        });
                        Msg($"Created zone '{name}' (v{variation}) with lb {lb:X4} — DISABLED. Use 'set' then 'enable'.");
                        return;
                    }

                    case "rename":
                    {
                        if (args.Count < 3) { Msg("Usage: rename <old> <new...>   (multi-word names ok; the old name matches an existing zone)"); return; }
                        var oldN = args[1];   // multi-word old names were collapsed above (existing-zone match)
                        var newN = SanitizeZoneName(string.Join(" ", args.Skip(2)));
                        if (newN.Length == 0) { Msg("New name required."); return; }
                        Msg(ZoneControlManager.RenameArea(oldN, newN)
                            ? $"Renamed '{oldN}' -> '{newN}'."
                            : $"Rename failed (no '{oldN}', or '{newN}' already exists).");
                        return;
                    }

                    case "delete":
                    {
                        if (args.Count < 2) { Msg("Usage: delete <name>"); return; }
                        var name = args[1];
                        Msg(ZoneControlManager.RemoveArea(name) ? $"Deleted '{name}'." : $"No zone '{name}'.");
                        return;
                    }

                    case "enable":
                    {
                        if (args.Count < 2) { Msg("Usage: enable <name>"); return; }
                        var name = args[1];
                        Msg(ZoneControlManager.SetEnabled(name, true)
                            ? $"'{name}' ENABLED. Live stats apply now; HP/attributes on respawn."
                            : $"No zone '{name}' (create it first).");
                        return;
                    }

                    case "disable":
                    {
                        if (args.Count < 2) { Msg("Usage: disable <name>"); return; }
                        var name = args[1];
                        Msg(ZoneControlManager.SetEnabled(name, false)
                            ? $"'{name}' disabled. Live stats revert now; HP/attributes on respawn."
                            : $"No zone '{name}'.");
                        return;
                    }

                    case "setvar":
                    {
                        if (args.Count < 3) { Msg("Usage: setvar <name> <variation>   (0 = normal world, 11+ = variants; use 'here' to read yours)"); return; }
                        var name = args[1];
                        int variation;
                        if (args[2].Equals("here", StringComparison.OrdinalIgnoreCase))
                            variation = ZoneControlManager.GetEffectiveVariation(session.Player);
                        else if (!int.TryParse(args[2].TrimStart('v', 'V'), out variation) || variation < 0)
                        { Msg("variation must be a number >= 0 (or 'here')."); return; }
                        if (!ZoneControlManager.SetVariation(name, variation)) { Msg($"No zone '{name}'."); return; }
                        Msg($"'{name}' Variation set to v{variation}. Now governs monsters/effects at that variation.");
                        // A boundary can't live on a retail variation — moving a bounded zone to <= 10 drops it.
                        var moved = ZoneControlManager.GetArea(name);
                        if (moved != null && moved.Bounded && variation < ZoneControlManager.MinBoundedVariation)
                        {
                            ZoneControlManager.SetBounded(name, false);
                            Msg($"'{name}' was BOUNDED — boundary removed (boundaries need variation 11+).");
                        }
                        return;
                    }

                    case "addlb":
                    {
                        if (args.Count < 3) { Msg("Usage: addlb <name> <hex|here> [more...]   (comma lists ok, e.g. F559,F55A)"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}' (create it first - and note zone names are one word)."); return; }

                        // Accept any mix of tokens after the name: 'here', hex ids, comma-separated lists.
                        var added = new List<string>();
                        for (var i = 2; i < args.Count; i++)
                        {
                            foreach (var tok in args[i].Split(','))
                            {
                                if (string.IsNullOrWhiteSpace(tok)) continue;
                                if (!TryLandblockToken(session, tok.Trim(), out var lb, out var lbErr)) { Msg(lbErr); return; }
                                ZoneControlManager.AddLandblock(name, lb);
                                added.Add(lb.ToString("X4"));
                            }
                        }
                        Msg(added.Count > 0 ? $"'{name}' += lb {string.Join(", ", added)}" : "Nothing to add.");
                        return;
                    }

                    case "removelb":
                    {
                        if (args.Count < 3) { Msg("Usage: removelb <name> <hex>"); return; }
                        var name = args[1];
                        if (!TryHex(args[2], out var lb)) { Msg("hex landblock required, e.g. F559"); return; }
                        Msg(ZoneControlManager.RemoveLandblock(name, (ushort)lb) ? $"'{name}' -= lb {lb:X4}" : $"No zone '{name}' or lb not a member.");
                        return;
                    }

                    case "terrain":
                    {
                        if (args.Count < 4) { Msg("Usage: terrain <name> <hex> <type|clear>   (types: " + string.Join(", ", ZoneControlManager.TerrainTags) + ")"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}'."); return; }
                        if (!TryHex(args[2], out var lb)) { Msg("hex landblock required, e.g. F559"); return; }
                        var type = args[3].ToLowerInvariant();
                        var clearing = type == "clear" || type == "none" || type == "auto";
                        if (!clearing && !ZoneControlManager.TerrainTags.Contains(type))
                        { Msg("Unknown terrain '" + args[3] + "'. Types: " + string.Join(", ", ZoneControlManager.TerrainTags) + ", or 'clear'."); return; }
                        ZoneControlManager.SetTerrainOverride(name, (ushort)lb, clearing ? null : type);
                        Msg(clearing
                            ? $"'{name}' lb {lb:X4} terrain override cleared (back to auto DAT terrain)."
                            : $"'{name}' lb {lb:X4} terrain override = {type}.");
                        return;
                    }

                    case "set":
                    {
                        if (args.Count < 4) { Msg("Usage: set <name> <stat> <value> [--wcid <id>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var stat = NormalizeStat(args[2]); if (stat == null) { Msg("Unknown stat. Stats: " + string.Join(", ", ZoneStat.All)); return; }
                        if (!TryDouble(args[3], out var value)) { Msg("value must be a number."); return; }

                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion, create: true) : a.Profile.Variant(ZoneVariant.Minion);
                            vp.Stats[stat] = new StatCurve { Base = value, Growth = 1.0, Additive = false };
                        });
                        Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} {stat} = {value:0.####}. " +
                            $"{(area.Enabled ? "" : "Zone still DISABLED - /zonecontrol enable " + name)}");
                        return;
                    }

                    case "clearstat":
                    {
                        if (args.Count < 3) { Msg("Usage: clearstat <name> <stat> [--wcid <id>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}'."); return; }
                        var stat = NormalizeStat(args[2]); if (stat == null) { Msg("Unknown stat."); return; }
                        var removed = false;
                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : a.Profile.Variant(ZoneVariant.Minion);
                            if (vp != null) removed = vp.Stats.Remove(stat);
                        });
                        Msg(removed ? $"'{name}' {stat} cleared." : "That stat wasn't set.");
                        return;
                    }

                    case "cantrip":
                    {
                        // Custom cantrip pool for the extra-loot-cantrip roll (weapon/armor_cantrip_chance).
                        if (args.Count < 3) { Msg("Usage: cantrip <name> <add|remove|list> [spellId] [--wcid <id>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var op = args[2].ToLowerInvariant();

                        if (op == "list")
                        {
                            var vp = wcid.HasValue ? area.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : area.Profile.Variant(ZoneVariant.Minion);
                            var ids = vp?.CustomCantrips;
                            if (ids == null || ids.Count == 0) { Msg("(no zone cantrips in the pool)"); return; }
                            foreach (var id in ids)
                                Msg(ZoneCantrips.TryGet(id, out var d) ? $"  {id}  {d.Name} - {d.Effect}" : $"  {id}  (unknown key)");
                            return;
                        }

                        if (op == "catalog")
                        {
                            foreach (var d in ZoneCantrips.Catalog.Values)
                                Msg($"  {d.Key,3}  {d.Name} - {d.Effect}");
                            return;
                        }

                        if (args.Count < 4 || !int.TryParse(args[3], out var cantripKey) || cantripKey <= 0)
                        { Msg("Usage: cantrip <name> add|remove <key>  (see 'cantrip <name> catalog')"); return; }

                        if (op == "add")
                        {
                            if (!ZoneCantrips.TryGet(cantripKey, out var def))
                            { Msg($"No zone cantrip with key {cantripKey}. See 'cantrip <name> catalog'."); return; }
                            ZoneControlManager.MutateArea(name, a =>
                            {
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion, create: true) : a.Profile.Variant(ZoneVariant.Minion);
                                if (!vp.CustomCantrips.Contains(cantripKey)) vp.CustomCantrips.Add(cantripKey);
                            });
                            Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} zone cantrip added: {def.Name} ({def.Effect}).");
                        }
                        else if (op == "remove")
                        {
                            var removed = false;
                            ZoneControlManager.MutateArea(name, a =>
                            {
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : a.Profile.Variant(ZoneVariant.Minion);
                                if (vp != null) removed = vp.CustomCantrips.Remove(cantripKey);
                            });
                            Msg(removed ? $"'{name}' zone cantrip {cantripKey} removed." : "That key wasn't in the pool.");
                        }
                        else
                            Msg("op must be add | remove | list | catalog");
                        return;
                    }

                    case "currency":
                    {
                        // Per-zone bonus-currency drop table: each entry = item wcid + stack amount + independent
                        // per-kill chance, injected onto every governed corpse. Loot-table independent; stacks
                        // with the legacy bonus_currency stat (which uses the server-wide token wcid).
                        if (args.Count < 3) { Msg("Usage: currency <name> <add|remove|list> [itemWcid] [amount] [chance] [--wcid <id>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var op = args[2].ToLowerInvariant();

                        if (op == "list")
                        {
                            var vp = wcid.HasValue ? area.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : area.Profile.Variant(ZoneVariant.Minion);
                            var drops = vp?.CurrencyDrops;
                            if (drops == null || drops.Count == 0) { Msg("(no currency drops defined)"); return; }
                            foreach (var d in drops)
                            {
                                var w = ACE.Database.DatabaseManager.World.GetCachedWeenie(d.Wcid);
                                Msg($"  {d.Wcid}  {w?.GetName() ?? "(unknown weenie)"}  x{d.Amount}  chance {d.Chance.ToString(CultureInfo.InvariantCulture)}  -> {(d.Direct ? "killer inventory" : "corpse")}");
                            }
                            return;
                        }

                        if (args.Count < 4 || !uint.TryParse(args[3], out var itemWcid) || itemWcid == 0)
                        { Msg("Usage: currency <name> add <itemWcid> <amount> [chance 0..1] [direct|corpse] | remove <itemWcid>"); return; }

                        if (op == "add")
                        {
                            var weenie = ACE.Database.DatabaseManager.World.GetCachedWeenie(itemWcid);
                            if (weenie == null) { Msg($"No weenie {itemWcid} in the world db."); return; }

                            var amount = 1;
                            if (args.Count >= 5 && (!int.TryParse(args[4], out amount) || amount < 1))
                            { Msg("amount must be a positive integer."); return; }

                            // Safeguard: the spawn path delivers ONE stack, so cap the count at the item's own
                            // max stack size (1 for non-stackables) — a typo like 5000000 can't be stored.
                            var maxStack = 1;
                            if (weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.MaxStackSize, out var ms) && ms > 1)
                                maxStack = ms;
                            if (amount > maxStack)
                            {
                                amount = maxStack;
                                Msg($"amount capped at {weenie.GetName() ?? "this item"}'s max stack size: {maxStack}.");
                            }

                            // optional trailing args in any order: chance (0..1] and/or direct|corpse
                            var chance = 1.0;
                            var direct = false;
                            for (var i = 5; i < args.Count; i++)
                            {
                                var tok = args[i].ToLowerInvariant();
                                if (tok == "direct" || tok == "inventory") direct = true;
                                else if (tok == "corpse") direct = false;
                                else if (double.TryParse(tok, NumberStyles.Any, CultureInfo.InvariantCulture, out var c) && c > 0 && c <= 1) chance = c;
                                else { Msg($"'{args[i]}' - optional args are a chance in (0..1] and/or direct|corpse."); return; }
                            }

                            ZoneControlManager.MutateArea(name, a =>
                            {
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion, create: true) : a.Profile.Variant(ZoneVariant.Minion);
                                var existing = vp.CurrencyDrops.FirstOrDefault(d => d.Wcid == itemWcid);
                                if (existing != null) { existing.Amount = amount; existing.Chance = chance; existing.Direct = direct; }
                                else vp.CurrencyDrops.Add(new ZoneCurrencyDrop { Wcid = itemWcid, Amount = amount, Chance = chance, Direct = direct });
                            });
                            Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} currency drop set: {weenie.GetName() ?? "?"} ({itemWcid}) x{amount}, chance {chance.ToString(CultureInfo.InvariantCulture)}, to {(direct ? "killer inventory" : "corpse")}.");
                        }
                        else if (op == "remove")
                        {
                            var removed = false;
                            ZoneControlManager.MutateArea(name, a =>
                            {
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : a.Profile.Variant(ZoneVariant.Minion);
                                if (vp != null) removed = vp.CurrencyDrops.RemoveAll(d => d.Wcid == itemWcid) > 0;
                            });
                            Msg(removed ? $"'{name}' currency drop {itemWcid} removed." : "That item wasn't in the drop table.");
                        }
                        else
                            Msg("op must be add | remove | list");
                        return;
                    }

                    case "part":
                    {
                        if (args.Count < 5) { Msg("Usage: part <name> <part> <armor|damage|variance|dmgtype> <value> [--wcid <id>]"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        if (!TryParseBodyPart(args[2], out var partKey)) { Msg("Unknown body part. Parts: " + string.Join(", ", Enum.GetNames(typeof(CombatBodyPart)).Where(n => n != "Undefined"))); return; }
                        var field = args[3].ToLowerInvariant();
                        if (field != "armor" && field != "damage" && field != "variance" && field != "dmgtype")
                        { Msg("field must be armor | damage | variance | dmgtype"); return; }

                        double value;
                        if (field == "dmgtype")
                        {
                            if (!TryParseDamageMask(args[4], out var mask)) { Msg("dmgtype must be a DamageType flag int or name (multi-flag ok, e.g. 24 = Cold+Fire)."); return; }
                            value = mask;
                        }
                        else if (!TryDouble(args[4], out value) || value < 0) { Msg("value must be a number >= 0."); return; }

                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion, create: true) : a.Profile.Variant(ZoneVariant.Minion);
                            if (!vp.BodyParts.TryGetValue((int)partKey, out var bp))
                                vp.BodyParts[(int)partKey] = bp = new ZoneBodyPart();
                            switch (field)
                            {
                                case "armor": bp.Armor = value; break;
                                case "damage": bp.Damage = value; break;
                                case "variance": bp.Variance = value; break;
                                case "dmgtype": bp.DamageType = (int)value; break;
                            }
                        });
                        Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} part {partKey} {field} = " +
                            (field == "dmgtype" ? ((DamageType)(int)value).ToString() : value.ToString("0.####", CultureInfo.InvariantCulture)) + ".");
                        return;
                    }

                    case "clearpart":
                    {
                        if (args.Count < 3) { Msg("Usage: clearpart <name> <part> [armor|damage|variance|dmgtype] [--wcid <id>]"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}'."); return; }
                        if (!TryParseBodyPart(args[2], out var partKey)) { Msg("Unknown body part."); return; }
                        var field = args.Count >= 4 ? args[3].ToLowerInvariant() : null;
                        var removed = false;
                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : a.Profile.Variant(ZoneVariant.Minion);
                            if (vp == null || !vp.BodyParts.TryGetValue((int)partKey, out var bp))
                                return;
                            switch (field)
                            {
                                case null: removed = vp.BodyParts.Remove((int)partKey); return;
                                case "armor": removed = bp.Armor != null; bp.Armor = null; break;
                                case "damage": removed = bp.Damage != null; bp.Damage = null; break;
                                case "variance": removed = bp.Variance != null; bp.Variance = null; break;
                                case "dmgtype": removed = bp.DamageType != null; bp.DamageType = null; break;
                            }
                            if (bp.IsEmpty)
                                vp.BodyParts.Remove((int)partKey);
                        });
                        Msg(removed ? $"'{name}' part {partKey} {(field ?? "override")} cleared." : "Nothing to clear for that part.");
                        return;
                    }

                    case "prop":
                    {
                        if (args.Count < 5) { Msg("Usage: prop <name> <int|int64|float|bool> <idOrName> <value> [--wcid <id>]"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var type = args[2].ToLowerInvariant();
                        if (!TryParsePropId(type, args[3], out var propId, out var propLabel)) { Msg($"Unknown {type} property '{args[3]}' (use a raw id or the enum name)."); return; }
                        if (IsPropBlocked(type, propId)) { Msg($"Property {propLabel} is protected and cannot be stamped by a zone."); return; }

                        Action<ZoneVariantProfile> applyProp;
                        string valueEcho;
                        switch (type)
                        {
                            case "int":
                            case "int64":
                                if (!long.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv)) { Msg("value must be an integer."); return; }
                                applyProp = type == "int" ? vp => vp.PropInts[propId] = lv : vp => vp.PropInt64s[propId] = lv;
                                valueEcho = lv.ToString(CultureInfo.InvariantCulture);
                                break;
                            case "float":
                                if (!TryDouble(args[4], out var dv)) { Msg("value must be a number."); return; }
                                applyProp = vp => vp.PropFloats[propId] = dv;
                                valueEcho = dv.ToString("0.####", CultureInfo.InvariantCulture);
                                break;
                            case "bool":
                                var bv = args[4].Equals("true", StringComparison.OrdinalIgnoreCase) || args[4] == "1" || args[4].Equals("on", StringComparison.OrdinalIgnoreCase);
                                applyProp = vp => vp.PropBools[propId] = bv;
                                valueEcho = bv ? "true" : "false";
                                break;
                            default:
                                Msg("type must be int | int64 | float | bool"); return;
                        }

                        ZoneControlManager.MutateArea(name, a => applyProp(
                            wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion, create: true) : a.Profile.Variant(ZoneVariant.Minion)));
                        Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} prop {propLabel} = {valueEcho}. Applies on (re)spawn.");
                        return;
                    }

                    case "clearprop":
                    {
                        if (args.Count < 4) { Msg("Usage: clearprop <name> <int|int64|float|bool> <idOrName> [--wcid <id>]"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}'."); return; }
                        var type = args[2].ToLowerInvariant();
                        if (!TryParsePropId(type, args[3], out var propId, out var propLabel)) { Msg($"Unknown {type} property '{args[3]}'."); return; }
                        var removed = false;
                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : a.Profile.Variant(ZoneVariant.Minion);
                            if (vp == null) return;
                            removed = type switch
                            {
                                "int" => vp.PropInts.Remove(propId),
                                "int64" => vp.PropInt64s.Remove(propId),
                                "float" => vp.PropFloats.Remove(propId),
                                "bool" => vp.PropBools.Remove(propId),
                                _ => false,
                            };
                        });
                        Msg(removed ? $"'{name}' prop {propLabel} cleared (reverts on respawn)." : "That prop wasn't set.");
                        return;
                    }

                    case "boundary":
                    {
                        if (args.Count < 3) { Msg("Usage: boundary <name> <on|off|show>"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var op = args[2].ToLowerInvariant();

                        if (op == "show")
                        {
                            Msg($"'{name}' v{area.Variation}: {(area.Bounded ? "BOUNDED" : "free roam")} " +
                                $"({area.Landblocks.Count} landblock(s), {(area.Enabled ? "ENABLED" : "disabled — boundary inactive until enabled")}).");
                            if (area.Bounded)
                            {
                                var sharing = ZoneControlManager.BoundedZoneNamesAt(area.Variation).Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                                Msg(sharing.Count > 0
                                    ? $"  Travel space at v{area.Variation} is shared with: {string.Join(", ", sharing)}."
                                    : $"  This is the only bounded zone at v{area.Variation}.");
                            }
                            return;
                        }

                        if (op != "on" && op != "off") { Msg("op must be on | off | show"); return; }
                        var bounded = op == "on";

                        if (bounded && area.Variation < ZoneControlManager.MinBoundedVariation)
                        {
                            Msg($"Boundaries need variation 11+ — '{name}' is on v{area.Variation}. " +
                                "A bounded zone on a retail variation would confine every player there.");
                            return;
                        }

                        ZoneControlManager.SetBounded(name, bounded);
                        Msg(bounded
                            ? $"'{name}' is now BOUNDED: players at v{area.Variation} may only roam bounded-zone landblocks there. " +
                              $"{(area.Enabled ? "Active now." : "Zone is DISABLED — boundary activates on /zonecontrol enable " + name + ".")}"
                            : $"'{name}' boundary removed (free roam unless another bounded zone covers v{area.Variation}).");
                        return;
                    }

                    case "survey":
                    {
                        if (args.Count < 2) { Msg("Usage: survey <name> [lbHex]"); return; }
                        var name = args[1];
                        var rows = ZoneControlManager.SurveyArea(name);
                        if (rows == null) { Msg($"No zone '{name}'."); return; }
                        // Echo the STORED name so the plugin's zone= match works whatever form was typed.
                        name = ZoneControlManager.GetArea(name)?.Name ?? name;

                        ushort? detailLb = null;
                        if (args.Count >= 3)
                        {
                            if (!TryHex(args[2], out var lbHex)) { Msg("hex landblock required, e.g. F559"); return; }
                            detailLb = (ushort)lbHex;
                        }

                        if (detailLb.HasValue)
                        {
                            var row = rows.FirstOrDefault(r => r.Landblock == detailLb.Value);
                            if (row == null) { Msg($"lb {detailLb.Value:X4} is not a member of '{name}'."); return; }
                            Msg(BuildSurveyDetailPayload(name, row));
                            return;
                        }

                        // Summary: one [[ZCS]] line per landblock so the plugin can render rows as they arrive.
                        foreach (var row in rows)
                            Msg(BuildSurveySummaryPayload(name, row));
                        Msg($"[[ZCS]]zone={name}|done={rows.Count}");
                        return;
                    }

                    case "quests":
                    {
                        if (args.Count < 2) { Msg("Usage: quests <name>"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}'."); return; }
                        name = area.Name;   // echo the STORED name so the plugin's zone= match works

                        // Owner rule: quest data may be pulled at most once per 60s per player. A throttled
                        // pull gets a short notice line so the plugin can show a countdown on its Refresh.
                        var nowUtc = DateTime.UtcNow;
                        if (_questPulls.TryGetValue(session, out var lastPull) &&
                            (nowUtc - lastPull).TotalSeconds < QuestPullCooldownSeconds)
                        {
                            var wait = (int)Math.Ceiling(QuestPullCooldownSeconds - (nowUtc - lastPull).TotalSeconds);
                            Msg($"[[ZCQ]]zone={name}|throttle={wait}");
                            return;
                        }
                        _questPulls[session] = nowUtc;
                        if (_questPulls.Count > 128)
                            foreach (var dead in _questPulls.Keys.Where(s => s.IsTerminated).ToList())
                                _questPulls.TryRemove(dead, out _);

                        var quests = ZoneControlManager.GetZoneQuests(name);
                        var qi = 0;
                        foreach (var q in quests)
                            Msg(BuildQuestPayload(name, q, ++qi, session));
                        Msg($"[[ZCQ]]zone={name}|done={quests.Count}");
                        return;
                    }

                    case "mobinfo":
                    {
                        if (args.Count < 2 || !uint.TryParse(args[1], out var infoWcid)) { Msg("Usage: mobinfo <wcid>"); return; }
                        Msg(BuildMobInfoPayload(infoWcid));
                        return;
                    }

                    case "show":
                    {
                        if (args.Count < 2) { Msg("Usage: show <name> [--wcid <id>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}'."); return; }
                        var eval = ZoneControlManager.EvaluateForDisplay(name, wcid);
                        Msg($"'{name}' v{area.Variation} {(area.Enabled ? "ENABLED" : "disabled")}" +
                            $"{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : " [default]")}:");
                        if (eval == null || eval.Values.Count == 0) { Msg("  (no stats set)"); return; }
                        foreach (var kv in eval.Values.OrderBy(k => k.Key))
                            Msg($"    {kv.Key,-22} = {kv.Value:0.####}");
                        return;
                    }

                    case "effect":
                    {
                        if (args.Count < 2) { Msg("Usage: effect <name> [show | dot on|off | dmg <amount> | type <fire|cold|acid|electric|nether|stamina|mana|health|percent> | interval <seconds>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}'."); return; }

                        if (args.Count < 3 || args[2].Equals("show", StringComparison.OrdinalIgnoreCase))
                        {
                            Msg($"'{name}' effects: {DescribeDot(area.Effects ?? new ZoneEffects())}");
                            return;
                        }

                        // Validate the field/value FIRST (outside the lock), building the mutation to apply atomically.
                        Action<ZoneEffects> apply;
                        var field = args[2].ToLowerInvariant();
                        switch (field)
                        {
                            case "dot":
                                if (args.Count < 4) { Msg("Usage: effect <name> dot on|off"); return; }
                                var on = args[3].Equals("on", StringComparison.OrdinalIgnoreCase) || args[3] == "1"
                                         || args[3].Equals("true", StringComparison.OrdinalIgnoreCase);
                                apply = e => e.DotEnabled = on;
                                break;
                            case "dmg":
                            case "dotdmg":
                                if (args.Count < 4 || !TryDouble(args[3], out var d) || d < 0) { Msg("dmg must be a number >= 0 (flat points, or percent when type=percent)."); return; }
                                apply = e => e.DotDamage = d;
                                break;
                            case "interval":
                                if (args.Count < 4 || !TryDouble(args[3], out var iv)) { Msg("interval must be a number of seconds (min 1)."); return; }
                                var interval = Math.Max(1.0, iv);
                                apply = e => e.DotIntervalSeconds = interval;
                                break;
                            case "type":
                            case "dottype":
                                if (args.Count < 4) { Msg("type must be 'percent' or one of: " + string.Join(", ", DamageTypeNames)); return; }
                                if (args[3].Equals("percent", StringComparison.OrdinalIgnoreCase) || args[3].Equals("%", StringComparison.Ordinal))
                                    apply = e => { e.DotPercent = true; e.DotDamageType = (int)DamageType.Health; }; // percent drains health
                                else if (TryParseDamageType(args[3], out var dt))
                                    apply = e => { e.DotPercent = false; e.DotDamageType = (int)dt; };
                                else { Msg("type must be 'percent' or one of: " + string.Join(", ", DamageTypeNames)); return; }
                                break;
                            default:
                                Msg("Unknown effect field. Use: dot on|off | dmg <amount> | type <name|percent> | interval <seconds> | show");
                                return;
                        }

                        ZoneControlManager.MutateArea(name, a => apply(a.Effects));
                        var updated = ZoneControlManager.GetArea(name);
                        Msg($"'{name}' effects: {DescribeDot(updated?.Effects ?? new ZoneEffects())}." +
                            $"{(area.Enabled ? "" : " Zone still DISABLED - /zonecontrol enable " + name)}");
                        return;
                    }

                    default:
                        Msg($"Unknown subcommand '{sub}'. See /zonecontrol help.");
                        return;
                }
            }
            catch (Exception ex)
            {
                Msg("Error: " + ex.Message);
            }
        }

        /// <summary>Builds the "[[ZC]]scope=..|found=..|enabled=..|variation=..|&lt;stat&gt;=defined,value|..|
        /// live_&lt;stat&gt;=..|here_lb=..|here_var=..|here_zone=.." payload the plugin's grid parses. Shared by
        /// "get" and the sync push tick. Values are flat.</summary>
        private static string BuildZonePayload(string name, uint? wcid, Session session)
        {
            var area = ZoneControlManager.GetArea(name);
            var vp = area == null ? null : (wcid.HasValue ? area.Profile.VariantForWcid(wcid.Value, ZoneVariant.Minion) : area.Profile.Variant(ZoneVariant.Minion));

            var sb = new StringBuilder();
            sb.Append("[[ZC]]scope=").Append(name)
              .Append("|found=").Append(area != null ? 1 : 0)
              .Append("|enabled=").Append(area?.Enabled == true ? 1 : 0)
              .Append("|wcid=").Append(wcid?.ToString() ?? "")
              .Append("|variation=").Append(area?.Variation ?? 0)
              .Append("|bounded=").Append(area?.Bounded == true ? 1 : 0);

            // Other bounded zones sharing this zone's variation (for the Territory tab's union line).
            if (area?.Bounded == true)
            {
                var sharing = ZoneControlManager.BoundedZoneNamesAt(area.Variation)
                    .Where(n => !n.Equals(area.Name, StringComparison.OrdinalIgnoreCase));
                sb.Append("|bshared=").Append(string.Join(",", sharing.Select(n => n.Replace('|', ' ').Replace(',', ' ').Replace('=', ' '))));
            }

            // Member landblocks (hex), so the plugin can show a selectable list with per-row removal.
            if (area?.Landblocks is { Count: > 0 })
            {
                sb.Append("|lbs=");
                bool firstLb = true;
                foreach (var lb in area.Landblocks.OrderBy(x => x))
                {
                    if (!firstLb) sb.Append(',');
                    firstLb = false;
                    sb.Append(lb.ToString("X4"));
                }
            }

            foreach (var stat in ZoneStat.All)
            {
                int defined = 0;
                double value = 0;
                if (vp != null && vp.TryGet(stat, out var curve)) { defined = 1; value = curve.Base; }
                sb.Append('|').Append(stat).Append('=').Append(defined).Append(',').Append(value.ToString(CultureInfo.InvariantCulture));
            }

            // Body-part overrides: bp_<key>=<armor|->,<damage|->,<variance|->,<dmgtype|-> ('-' = not overridden)
            if (vp?.BodyParts is { Count: > 0 })
            {
                foreach (var kv in vp.BodyParts.OrderBy(k => k.Key))
                {
                    var bp = kv.Value;
                    if (bp == null || bp.IsEmpty) continue;
                    sb.Append("|bp_").Append(kv.Key).Append('=')
                      .Append(bp.Armor?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(',')
                      .Append(bp.Damage?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(',')
                      .Append(bp.Variance?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(',')
                      .Append(bp.DamageType?.ToString(CultureInfo.InvariantCulture) ?? "-");
                }
            }

            // Prop stamps: prop_<i|l|f|b>_<id>=<value>
            if (vp != null)
            {
                foreach (var kv in vp.PropInts.OrderBy(k => k.Key))
                    sb.Append("|prop_i_").Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                foreach (var kv in vp.PropInt64s.OrderBy(k => k.Key))
                    sb.Append("|prop_l_").Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                foreach (var kv in vp.PropFloats.OrderBy(k => k.Key))
                    sb.Append("|prop_f_").Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                foreach (var kv in vp.PropBools.OrderBy(k => k.Key))
                    sb.Append("|prop_b_").Append(kv.Key).Append('=').Append(kv.Value ? 1 : 0);
            }

            // Custom cantrip pool (for the plugin's Loot cards).
            if (vp?.CustomCantrips is { Count: > 0 })
                sb.Append("|cantrips=").Append(string.Join(",", vp.CustomCantrips));

            // Currency drop table: curr=wcid~amount~chance~direct~name,... (sparse; rebuilt each sync like
            // cantrips=). Name is display-only for the plugin; sanitized of the wire's separator chars.
            if (vp?.CurrencyDrops is { Count: > 0 })
            {
                sb.Append("|curr=");
                bool firstCd = true;
                foreach (var d in vp.CurrencyDrops)
                {
                    if (d == null || d.Wcid == 0) continue;
                    if (!firstCd) sb.Append(',');
                    firstCd = false;
                    var cdName = (ACE.Database.DatabaseManager.World.GetCachedWeenie(d.Wcid)?.GetName() ?? "")
                        .Replace('|', ' ').Replace(',', ' ').Replace('~', ' ').Replace('=', ' ');
                    sb.Append(d.Wcid).Append('~').Append(d.Amount).Append('~').Append(d.Chance.ToString(CultureInfo.InvariantCulture))
                      .Append('~').Append(d.Direct ? 1 : 0).Append('~').Append(cdName);
                }
            }

            // Zone player-effects (for the plugin's Effects tab).
            var effects = area?.Effects ?? new ZoneEffects();
            sb.Append("|fx_dot=").Append(effects.DotEnabled ? 1 : 0)
              .Append("|fx_dotdmg=").Append(effects.DotDamage.ToString(CultureInfo.InvariantCulture))
              .Append("|fx_dottype=").Append(effects.DotDamageType)
              .Append("|fx_dotpercent=").Append(effects.DotPercent ? 1 : 0)
              .Append("|fx_dotinterval=").Append(effects.DotIntervalSeconds.ToString(CultureInfo.InvariantCulture));

            // Live hints from the admin's in-game target. When the plugin is watching a SPECIFIC monster
            // (--wcid), only send them if the in-game target IS that monster — otherwise targeting some other
            // mob would overwrite the watched monster's weenie base values in the GUI. With no wcid watch
            // ("All monsters"), any target's live stats are useful context and flow through as before.
            var target = session.Player?.SelectedTarget as ACE.Server.WorldObjects.Creature;
            if (target != null && (!wcid.HasValue || target.WeenieClassId == wcid.Value))
            {
                foreach (var stat in ZoneStat.All)
                {
                    var liveVal = GetLiveStatValue(target, stat);
                    if (liveVal.HasValue)
                        sb.Append("|live_").Append(stat).Append('=').Append(liveVal.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            var loc = session.Player?.Location;
            if (loc != null)
            {
                var hereLb = loc.LandblockId.Landblock;
                var hereVar = ZoneControlManager.GetEffectiveVariation(session.Player);
                // The zone that actually governs here (enabled + variation-match + most-specific), not just any cover.
                var winner = ZoneControlManager.ResolveWinnerForLocation(hereLb, hereVar);
                sb.Append("|here_lb=").Append(hereLb.ToString("X4"))
                  .Append("|here_var=").Append(hereVar)
                  .Append("|here_zone=").Append(winner?.Name ?? "");

                // Every zone whose landblocks cover this spot (any variation) as name~enabled~varMatch, so the GUI
                // can show overlaps and which are shadowed. ',' separates entries, '~' separates sub-fields.
                var covering = ZoneControlManager.AreasCovering(hereLb);
                if (covering.Count > 0)
                {
                    sb.Append("|here_covers=");
                    bool first = true;
                    foreach (var z in covering.OrderBy(z => z.Landblocks.Count))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        var safeName = z.Name.Replace('~', '-').Replace(',', ' ').Replace('|', ' ');
                        sb.Append(safeName).Append('~').Append(z.Enabled ? 1 : 0).Append('~').Append(z.Variation == hereVar ? 1 : 0);
                    }
                }
            }

            return sb.ToString();
        }

        private static double? GetLiveStatValue(ACE.Server.WorldObjects.Creature creature, string stat)
        {
            switch (stat)
            {
                case ZoneStat.Strength: return creature.Attributes[PropertyAttribute.Strength].Base;
                case ZoneStat.Endurance: return creature.Attributes[PropertyAttribute.Endurance].Base;
                case ZoneStat.Coordination: return creature.Attributes[PropertyAttribute.Coordination].Base;
                case ZoneStat.Quickness: return creature.Attributes[PropertyAttribute.Quickness].Base;
                case ZoneStat.Focus: return creature.Attributes[PropertyAttribute.Focus].Base;
                case ZoneStat.Self: return creature.Attributes[PropertyAttribute.Self].Base;
                case ZoneStat.MaxHealth: return creature.Health.MaxValue;
                case ZoneStat.MaxStamina: return creature.Stamina.MaxValue;
                case ZoneStat.MaxMana: return creature.Mana.MaxValue;
                case ZoneStat.DamageRating: return creature.GetProperty(PropertyInt.DamageRating) ?? 0;
                case ZoneStat.DamageResistRating: return creature.GetProperty(PropertyInt.DamageResistRating) ?? 0;
                case ZoneStat.AttackSkill: return creature.GetCreatureSkill(creature.GetCurrentAttackSkill()).Base;
                case ZoneStat.MeleeDefense: return creature.GetCreatureSkill(Skill.MeleeDefense).Base;
                case ZoneStat.MissileDefense: return creature.GetCreatureSkill(Skill.MissileDefense).Base;
                case ZoneStat.MagicDefense: return creature.GetCreatureSkill(Skill.MagicDefense).Base;

                case ZoneStat.ResistSlash: return creature.GetProperty(PropertyFloat.ResistSlash) ?? 1.0;
                case ZoneStat.ResistPierce: return creature.GetProperty(PropertyFloat.ResistPierce) ?? 1.0;
                case ZoneStat.ResistBludgeon: return creature.GetProperty(PropertyFloat.ResistBludgeon) ?? 1.0;
                case ZoneStat.ResistFire: return creature.GetProperty(PropertyFloat.ResistFire) ?? 1.0;
                case ZoneStat.ResistCold: return creature.GetProperty(PropertyFloat.ResistCold) ?? 1.0;
                case ZoneStat.ResistAcid: return creature.GetProperty(PropertyFloat.ResistAcid) ?? 1.0;
                case ZoneStat.ResistElectric: return creature.GetProperty(PropertyFloat.ResistElectric) ?? 1.0;
                case ZoneStat.ResistNether: return creature.GetProperty(PropertyFloat.ResistNether) ?? 1.0;

                case ZoneStat.ArmorVsSlash: return creature.GetProperty(PropertyFloat.ArmorModVsSlash) ?? 1.0;
                case ZoneStat.ArmorVsPierce: return creature.GetProperty(PropertyFloat.ArmorModVsPierce) ?? 1.0;
                case ZoneStat.ArmorVsBludgeon: return creature.GetProperty(PropertyFloat.ArmorModVsBludgeon) ?? 1.0;
                case ZoneStat.ArmorVsFire: return creature.GetProperty(PropertyFloat.ArmorModVsFire) ?? 1.0;
                case ZoneStat.ArmorVsCold: return creature.GetProperty(PropertyFloat.ArmorModVsCold) ?? 1.0;
                case ZoneStat.ArmorVsAcid: return creature.GetProperty(PropertyFloat.ArmorModVsAcid) ?? 1.0;
                case ZoneStat.ArmorVsElectric: return creature.GetProperty(PropertyFloat.ArmorModVsElectric) ?? 1.0;
                case ZoneStat.ArmorVsNether: return creature.GetProperty(PropertyFloat.ArmorModVsNether) ?? 1.0;

                case ZoneStat.ArmorLevel:
                {
                    // weenie/biota base body armor (max across parts — parts are uniform on nearly all mobs)
                    var parts = creature.Biota.PropertiesBodyPart;
                    if (parts == null || parts.Count == 0) return null;
                    return parts.Values.Max(p => p.BaseArmor);
                }
                case ZoneStat.AttackDamage:
                {
                    // what the mob hits for today: weapon damage if wielding, else best attacking part's DVal
                    var weapon = creature.GetEquippedMeleeWeapon();
                    if (weapon != null) return weapon.GetProperty(PropertyInt.Damage) ?? 0;
                    var parts = creature.Biota.PropertiesBodyPart;
                    if (parts == null || parts.Count == 0) return null;
                    return parts.Values.Max(p => p.DVal);
                }
                case ZoneStat.AttackVariance:
                {
                    var weapon = creature.GetEquippedMeleeWeapon();
                    if (weapon != null) return weapon.GetProperty(PropertyFloat.DamageVariance) ?? 0.0;
                    var parts = creature.Biota.PropertiesBodyPart;
                    if (parts == null || parts.Count == 0) return null;
                    var best = parts.Values.OrderByDescending(p => p.DVal).First();
                    return best.DVar;
                }

                default: return null;
            }
        }

        /// <summary>Re-tokenize the space-split parameters honoring double quotes, so zone names may contain
        /// spaces: /zonecontrol enable "My Zone". (ACE's CommandManager splits purely on spaces, so runs of
        /// spaces inside quotes collapse to one — cosmetic only.)</summary>
        private static List<string> RetokenizeParameters(string[] parameters)
        {
            var joined = string.Join(" ", parameters ?? Array.Empty<string>());
            var list = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            foreach (var ch in joined)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (ch == ' ' && !inQuotes)
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    continue;
                }
                sb.Append(ch);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>Finds the LONGEST join of consecutive args (from <paramref name="startIndex"/>) that
        /// names an EXISTING zone and collapses those tokens into a single arg, so multi-word zone names
        /// work without quotes in every name-taking subcommand. No-op when nothing matches.</summary>
        private static void CollapseZoneNameTokens(List<string> args, int startIndex)
        {
            var available = args.Count - startIndex;
            if (available < 2)
                return;
            for (var take = available; take >= 2; take--)
            {
                var candidate = string.Join(" ", args.Skip(startIndex).Take(take));
                if (ZoneControlManager.GetArea(candidate) != null)
                {
                    args.RemoveRange(startIndex, take);
                    args.Insert(startIndex, candidate);
                    return;
                }
            }
        }

        /// <summary>Display-name cleanup for create/rename: strip the wire separator chars (| , ~ =),
        /// collapse whitespace runs, trim. CASE IS PRESERVED (lookups are case-insensitive).</summary>
        private static string SanitizeZoneName(string raw)
        {
            var s = (raw ?? "").Replace('|', ' ').Replace(',', ' ').Replace('~', ' ').Replace('=', ' ');
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static bool TryLandblockToken(Session session, string token, out ushort landblock, out string error)
        {
            error = null; landblock = 0;
            if (token.Equals("here", StringComparison.OrdinalIgnoreCase))
            {
                var lb = session.Player?.Location?.LandblockId.Landblock;
                if (lb == null) { error = "No location for 'here'."; return false; }
                landblock = lb.Value;
                return true;
            }
            if (!TryHex(token, out var hex)) { error = "hex landblock required, e.g. F559 (or 'here')"; return false; }
            landblock = (ushort)hex;
            return true;
        }

        private static uint? ExtractWcidFlag(List<string> args)
        {
            var idx = args.FindIndex(a => a.Equals("--wcid", StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx + 1 >= args.Count) return null;
            var valStr = args[idx + 1];
            args.RemoveRange(idx, 2);
            return uint.TryParse(valStr, out var id) ? id : null;
        }

        private static bool TryHex(string s, out int value)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryDouble(string s, out double value)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        /// <summary>Human-readable one-liner for a zone's DoT config (command echoes + show).</summary>
        private static string DescribeDot(ZoneEffects e)
        {
            if (!e.DotEnabled) return "DoT off";
            var amount = e.DotPercent ? $"{e.DotDamage:0.##}% max health" : $"{e.DotDamage:0.##} {(DamageType)e.DotDamageType}";
            return $"DoT ON: {amount} every {Math.Max(1.0, e.DotIntervalSeconds):0.##}s";
        }

        private static readonly string[] DamageTypeNames =
            { "slash", "pierce", "bludgeon", "cold", "fire", "acid", "electric", "health", "stamina", "mana", "nether" };

        /// <summary>Parse a single-flag damage type by name (case-insensitive) or by raw flag int (what the
        /// plugin sends); rejects Undef and multi-flag values.</summary>
        private static bool TryParseDamageType(string s, out DamageType dt)
        {
            dt = DamageType.Undef;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            // raw flag int (e.g. 16 = Fire) — the plugin combo sends these
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                dt = (DamageType)iv;
            else if (!Enum.TryParse(s, true, out dt))
                return false;
            return dt != DamageType.Undef && Enum.IsDefined(typeof(DamageType), dt) && !dt.IsMultiDamage();
        }

        private static string NormalizeStat(string s)
        {
            s = s.Trim().ToLowerInvariant();
            return ZoneStat.All.FirstOrDefault(k => k.Equals(s, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Parse a CombatBodyPart by enum name (case-insensitive) or raw int; rejects Undefined.</summary>
        private static bool TryParseBodyPart(string s, out CombatBodyPart part)
        {
            part = CombatBodyPart.Undefined;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                part = (CombatBodyPart)iv;
            else if (!Enum.TryParse(s, true, out part))
                return false;
            return part != CombatBodyPart.Undefined && Enum.IsDefined(typeof(CombatBodyPart), part);
        }

        /// <summary>Parse a DamageType MASK: raw flag int (multi-flag ok) or a single enum name.</summary>
        private static bool TryParseDamageMask(string s, out int mask)
        {
            mask = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) && iv > 0)
            {
                mask = iv;
                return true;
            }
            if (Enum.TryParse<DamageType>(s, true, out var dt) && dt != DamageType.Undef)
            {
                mask = (int)dt;
                return true;
            }
            return false;
        }

        /// <summary>Resolve a property id from a raw int or the matching Property{Int,Int64,Float,Bool} enum
        /// name. Label comes back as "Name (id)" for command echoes.</summary>
        private static bool TryParsePropId(string type, string s, out int id, out string label)
        {
            id = 0; label = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            Type enumType = type switch
            {
                "int" => typeof(PropertyInt),
                "int64" => typeof(PropertyInt64),
                "float" => typeof(PropertyFloat),
                "bool" => typeof(PropertyBool),
                _ => null,
            };
            if (enumType == null) return false;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
            {
                id = iv;
                var enumName = Enum.IsDefined(enumType, iv) ? Enum.GetName(enumType, iv) : null;
                label = enumName != null ? $"{enumName} ({iv})" : $"#{iv}";
                return iv > 0;
            }

            try
            {
                var parsed = Enum.Parse(enumType, s, true);
                id = Convert.ToInt32(parsed);
                label = $"{parsed} ({id})";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPropBlocked(string type, int id) => type switch
        {
            "int" => ZonePropGuard.IsBlockedInt(id),
            "int64" => ZonePropGuard.IsBlockedInt64(id),
            "float" => ZonePropGuard.IsBlockedFloat(id),
            "bool" => ZonePropGuard.IsBlockedBool(id),
            _ => true,
        };

        /// <summary>Wire-safe display string: the [[ZCS]] separators (| , ~ =) replaced with spaces.</summary>
        private static string SurveySafe(string s)
            => (s ?? "").Replace('|', ' ').Replace(',', ' ').Replace('~', ' ').Replace('=', ' ');

        // ── Quests tab ([[ZCQ]]) ──
        private const double QuestPullCooldownSeconds = 60.0;
        private static readonly ConcurrentDictionary<Session, DateTime> _questPulls = new();

        /// <summary>[[ZCQ]] text fields keep commas (fields split on '|', k=v on FIRST '='); '~' only
        /// matters inside the tg= list, escaped separately.</summary>
        private static string QuestSafe(string s)
            => (s ?? "").Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ');

        /// <summary>One [[ZCQ]] line per quest. Static registry fields plus the REQUESTING player's live
        /// progress: pr=solves/max while the task is started, cd=cooldown seconds remaining (0 = ready).</summary>
        private static string BuildQuestPayload(string zone, ZoneControlManager.ZoneQuestRow q, int index, Session session)
        {
            var sb = new StringBuilder();
            sb.Append("[[ZCQ]]zone=").Append(zone)
              .Append("|i=").Append(index)
              .Append("|w=").Append(QuestSafe(q.Wave))
              .Append("|cat=").Append(QuestSafe(q.Category))
              .Append("|st=").Append(QuestSafe(q.Stage))
              .Append("|ok=").Append(q.Wired ? 1 : 0)
              .Append("|t=").Append(QuestSafe(q.Title))
              .Append("|npc=").Append(QuestSafe(q.NpcName))
              .Append("|wcid=").Append(q.NpcWcid)
              .Append("|lb=").Append(q.LandblockHex)
              .Append("|co=").Append(QuestSafe(q.Coords))
              .Append("|n=").Append(q.Count)
              .Append("|rep=").Append(q.RepeatHours)
              .Append("|rw=").Append(QuestSafe(q.Reward))
              .Append("|obj=").Append(QuestSafe(q.Objective))
              .Append("|tg=").Append(string.Join("~",
                  (q.Targets ?? "").Split('~').Select(t => QuestSafe(t).Trim()).Where(t => t.Length > 0)));

            // Live per-player progress (only meaningful for live rows with a real stamp)
            var qm = session?.Player?.QuestManager;
            var pr = "";
            var cd = 0;
            if (qm != null && !string.IsNullOrEmpty(q.QuestKey) &&
                string.Equals(q.Stage, "live", StringComparison.OrdinalIgnoreCase))
            {
                var reg = qm.GetQuest(q.QuestKey);
                if (reg != null)
                    pr = reg.NumTimesCompleted + "/" + q.Count;
                if (!string.IsNullOrEmpty(q.CompletedKey))
                {
                    var next = qm.GetNextSolveTime(q.CompletedKey);
                    if (next != TimeSpan.MinValue && next != TimeSpan.MaxValue)
                        cd = (int)Math.Max(0, next.TotalSeconds);
                }
            }
            sb.Append("|pr=").Append(pr).Append("|cd=").Append(cd);
            return sb.ToString();
        }

        /// <summary>One survey SUMMARY line per landblock:
        /// [[ZCS]]zone=x|lb=F559|gens=4|creatures=5|monsters=4|types=Drudge~3,Skeleton~1|g=wcid~name~count,...
        /// (types = distinct MONSTER CreatureTypes with distinct-wcid counts, most common first;
        /// g = the top-level placed generators grouped by wcid — lets the plugin tint the map by generator).</summary>
        private static string BuildSurveySummaryPayload(string zone, ZoneControlManager.SurveyRow row)
        {
            var monsters = row.Creatures.Where(c => c.IsMonster).ToList();
            var types = monsters
                .GroupBy(c => string.IsNullOrEmpty(c.Type) ? "Other" : c.Type)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.Append("[[ZCS]]zone=").Append(zone)
              .Append("|lb=").Append(row.Landblock.ToString("X4"))
              .Append("|gens=").Append(row.Generators)
              .Append("|creatures=").Append(row.Creatures.Count)
              .Append("|monsters=").Append(monsters.Count)
              .Append("|types=").Append(string.Join(",", types.Select(g => SurveySafe(g.Key) + "~" + g.Count())))
              .Append("|g=").Append(string.Join(",", row.PlacedGenerators.Select(g => g.Wcid + "~" + SurveySafe(g.Name) + "~" + g.Count)))
              .Append("|terr=").Append(row.Terrain ?? "")
              .Append("|terrbase=").Append(row.TerrainBase ?? "");
            return sb.ToString();
        }

        /// <summary>Survey DETAIL for one landblock:
        /// [[ZCS]]zone=x|lb=F559|detail=1|c=wcid~name~type~isMonster,...|g=wcid~name~count,...</summary>
        private static string BuildSurveyDetailPayload(string zone, ZoneControlManager.SurveyRow row)
        {
            var sb = new StringBuilder();
            sb.Append("[[ZCS]]zone=").Append(zone)
              .Append("|lb=").Append(row.Landblock.ToString("X4"))
              .Append("|detail=1");

            sb.Append("|c=").Append(string.Join(",", row.Creatures.Select(c =>
                c.Wcid + "~" + SurveySafe(c.Name) + "~" + SurveySafe(string.IsNullOrEmpty(c.Type) ? "-" : c.Type) + "~" + (c.IsMonster ? 1 : 0))));

            sb.Append("|g=").Append(string.Join(",", row.PlacedGenerators.Select(g =>
                g.Wcid + "~" + SurveySafe(g.Name) + "~" + g.Count)));

            return sb.ToString();
        }

        /// <summary>Builds the "[[ZCI]]" weenie base-data payload for the plugin's Body Parts / Resists /
        /// Weapon tabs: body-part table, creature resist + armor-vs floats, wielded weapons, spell count.
        /// All data comes from the WEENIE (authoring baseline), not a live instance.</summary>
        private static string BuildMobInfoPayload(uint wcid)
        {
            var weenie = ACE.Database.DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie == null)
                return $"[[ZCI]]wcid={wcid}|found=0";

            var sb = new StringBuilder();
            sb.Append("[[ZCI]]wcid=").Append(wcid)
              .Append("|found=1|name=").Append((weenie.GetName() ?? ("wcid " + wcid)).Replace('|', ' ').Replace(',', ' ').Replace('=', ' '));

            // attributes (weenie InitLevel): st=str,end,coord,quick,focus,self
            uint A(PropertyAttribute a) => weenie.PropertiesAttribute != null && weenie.PropertiesAttribute.TryGetValue(a, out var pa) ? pa.InitLevel : 0;
            sb.Append("|st=")
              .Append(A(PropertyAttribute.Strength)).Append(',')
              .Append(A(PropertyAttribute.Endurance)).Append(',')
              .Append(A(PropertyAttribute.Coordination)).Append(',')
              .Append(A(PropertyAttribute.Quickness)).Append(',')
              .Append(A(PropertyAttribute.Focus)).Append(',')
              .Append(A(PropertyAttribute.Self));

            // vitals (weenie InitLevel — the SQL-authored base): vt=health,stamina,mana
            uint V(PropertyAttribute2nd a) => weenie.PropertiesAttribute2nd != null && weenie.PropertiesAttribute2nd.TryGetValue(a, out var pv) ? pv.InitLevel : 0;
            sb.Append("|vt=")
              .Append(V(PropertyAttribute2nd.MaxHealth)).Append(',')
              .Append(V(PropertyAttribute2nd.MaxStamina)).Append(',')
              .Append(V(PropertyAttribute2nd.MaxMana));

            // skills: sk=attack(best of the weapon/magic attack skills),melee_d,missile_d,magic_d
            uint S(Skill s) => weenie.PropertiesSkill != null && weenie.PropertiesSkill.TryGetValue(s, out var ps) ? ps.InitLevel : 0;
            var attackSkill = new[]
            {
                S(Skill.HeavyWeapons), S(Skill.LightWeapons), S(Skill.FinesseWeapons), S(Skill.MissileWeapons),
                S(Skill.TwoHandedCombat), S(Skill.UnarmedCombat), S(Skill.WarMagic), S(Skill.VoidMagic),
            }.Max();
            sb.Append("|sk=")
              .Append(attackSkill).Append(',')
              .Append(S(Skill.MeleeDefense)).Append(',')
              .Append(S(Skill.MissileDefense)).Append(',')
              .Append(S(Skill.MagicDefense));

            // ratings: rt=damage_rating,damage_resist_rating
            int I(PropertyInt p) => weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(p, out var iv) ? iv : 0;
            sb.Append("|rt=").Append(I(PropertyInt.DamageRating)).Append(',').Append(I(PropertyInt.DamageResistRating));

            // body parts: part=<key>,<baseArmor>,<dval>,<dvar>,<dtype>
            if (weenie.PropertiesBodyPart != null)
            {
                foreach (var kv in weenie.PropertiesBodyPart.OrderBy(k => (int)k.Key))
                {
                    sb.Append("|part=").Append((int)kv.Key).Append(',')
                      .Append(kv.Value.BaseArmor).Append(',')
                      .Append(kv.Value.DVal).Append(',')
                      .Append(kv.Value.DVar.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append((int)kv.Value.DType);
                }
            }

            // creature-level resist + armor-vs multipliers (default 1.0), slash..nether order
            double F(PropertyFloat p) => weenie.PropertiesFloat != null && weenie.PropertiesFloat.TryGetValue(p, out var v) ? v : 1.0;
            sb.Append("|rs=")
              .Append(F(PropertyFloat.ResistSlash).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistPierce).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistBludgeon).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistFire).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistCold).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistAcid).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistElectric).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ResistNether).ToString(CultureInfo.InvariantCulture));
            sb.Append("|am=")
              .Append(F(PropertyFloat.ArmorModVsSlash).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsPierce).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsBludgeon).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsFire).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsCold).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsAcid).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsElectric).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(F(PropertyFloat.ArmorModVsNether).ToString(CultureInfo.InvariantCulture));

            // wielded weapons from the create list: wield=<wcid>,<name>,<damage>,<variance>,<dtypeMask>,<speed>
            if (weenie.PropertiesCreateList != null)
            {
                foreach (var cl in weenie.PropertiesCreateList)
                {
                    if ((cl.DestinationType & DestinationType.Wield) == 0)
                        continue;
                    var item = ACE.Database.DatabaseManager.World.GetCachedWeenie(cl.WeenieClassId);
                    if (item?.PropertiesInt == null || !item.PropertiesInt.TryGetValue(PropertyInt.Damage, out var dmg))
                        continue; // wielded but not a damage-dealing weapon (armor, clothing)
                    var dvar = item.PropertiesFloat != null && item.PropertiesFloat.TryGetValue(PropertyFloat.DamageVariance, out var v) ? v : 0.0;
                    var dtype = item.PropertiesInt.TryGetValue(PropertyInt.DamageType, out var t) ? t : 0;
                    var speed = item.PropertiesInt.TryGetValue(PropertyInt.WeaponTime, out var sp) ? sp : 0;
                    sb.Append("|wield=").Append(cl.WeenieClassId).Append(',')
                      .Append((item.GetName() ?? "?").Replace('|', ' ').Replace(',', ' ').Replace('=', ' ')).Append(',')
                      .Append(dmg).Append(',')
                      .Append(dvar.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(dtype).Append(',')
                      .Append(speed);
                }
            }

            sb.Append("|spells=").Append(weenie.PropertiesSpellBook?.Count ?? 0);
            return sb.ToString();
        }
    }
}
