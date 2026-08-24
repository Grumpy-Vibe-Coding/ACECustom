using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using ACE.Common.Performance;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers;
using ACE.Server.Managers.ZoneControl;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.Network;

using Microsoft.EntityFrameworkCore;

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

        /// <summary>One look-preview spawn per player (previewmob) - a new preview replaces the old one.</summary>
        private static readonly Dictionary<uint, ACE.Server.WorldObjects.WorldObject> ZcPreviews = new Dictionary<uint, ACE.Server.WorldObjects.WorldObject>();

        /// <summary>The five reserved "Drafted Look" weenies (Target Dummy clones, Add_DraftedLook_Dummies_2026-08-10.sql)
        /// backing the Look Lab's Drafted Look target. Each drafting player gets ONE slot per zone (draftslot);
        /// the look is crafted in that slot's zone appearance bucket, then copydraft moves it onto a real wcid.</summary>
        private static readonly uint[] ZcDraftSlotWcids = { 739999995, 739999996, 739999997, 739999998, 739999999 };

        /// <summary>Live draft-slot claims: (zone name lower, player guid) -> slot wcid. Claims of players no
        /// longer online are evicted (bucket cleared) whenever someone asks for a slot.</summary>
        private static readonly Dictionary<(string Zone, uint Player), uint> ZcDraftClaims = new();

        /// <summary>Drop a claim and wipe its slot's appearance bucket so the next claimant starts clean.</summary>
        private static void ReleaseDraftClaim(string zoneKey, uint playerGuid)
        {
            if (!ZcDraftClaims.TryGetValue((zoneKey, playerGuid), out var slot))
                return;
            ZcDraftClaims.Remove((zoneKey, playerGuid));
            ZoneControlManager.MutateArea(zoneKey, a => a.AppearanceByWcid?.Remove(slot));
        }

        [CommandHandler("zonecontrol", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Author/toggle Zone Control zones (any world area).",
            "help | list | here | create <name> <variation> [here|hex] | rename <old> <new> | delete <name> | "
            + "default <variation> <show|set|clearstat|copyfrom|clear> | default list | "
            + "enable <name> | disable <name> | addlb <name> <hex|here> | removelb <name> <hex> | "
            + "set <name> <stat> <value> [--wcid <id>] | clearstat <name> <stat> [--wcid <id>] | show <name> [--wcid <id>] | "
            + "part <name> <part> <armor|damage|variance|dmgtype> <value> [--wcid <id>] | clearpart <name> <part> [field] [--wcid <id>] | "
            + "prop <name> <int|int64|float|bool> <idOrName> <value> [--wcid <id>] | clearprop <name> <type> <idOrName> [--wcid <id>] | "
            + "appearance <name> <palette|shade|scale|translucency|shiny|setup|clothing|palettebase|motion|sound|icon> <value> [--wcid <id>] | clearappearance <name> [field] [--wcid <id>] | copylook <name> <donorWcid> [--wcid <id>] | draftslot <name> [release] | copydraft <name> <destWcid> | becomemob <donorWcid> --wcid <id> | seticon <wcid> <iconDid|clear> [layer] | "
            + "cantrip <name> <add|remove|list|catalog|band|slots|special|lines|weight> [args] [--wcid <id>] | "
            + "currency <name> <add|remove|list> [itemWcid] [amount] [chance] [direct|corpse] [--wcid <id>] | "
            + "boundary <name> <on|off|show> | survey <name> [lbHex] | quests <name> | terrain <name> <hex> <type|clear> | "
            + "mobinfo <wcid> | geninfo <wcid> | genlist [zone] | genedit <wcid> delay|radius|stagger|init|max <value> | "
            + "effect <name> [dot on|off | dmg <amount> | type <name|percent> | interval <secs>] | reload")]
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
                Msg("  /zonecontrol default <variation> <show|set|clearstat|copyfrom|clear> | default list");
                Msg("      the per-variation BASELINE every zone at that variation inherits, per stat;");
                Msg("      a zone (or a --wcid) overrides only the stats it sets. v11-v25 = the progression.");
                Msg("  /zonecontrol set <name> <stat> <value> [--wcid <id>]   (--wcid = a specific monster's override)");
                Msg("  /zonecontrol clearstat <name> <stat> [--wcid <id>] | show <name> [--wcid <id>]");
                Msg("  /zonecontrol effect <name> [show | dot on|off | dmg <amount> | type <fire|cold|...|percent> | interval <secs>]   (player DoT)");
                Msg("  /zonecontrol part <name> <part> <armor|damage|variance|dmgtype> <value> [--wcid <id>]   (per-body-part override; damage 0 = part stops attacking)");
                Msg("  /zonecontrol clearpart <name> <part> [armor|damage|variance|dmgtype] [--wcid <id>]   (no field = clear the whole part)");
                Msg("  /zonecontrol prop <name> <int|int64|float|bool> <idOrName> <value> [--wcid <id>]   (stamped on monsters at respawn)");
                Msg("  /zonecontrol clearprop <name> <int|int64|float|bool> <idOrName> [--wcid <id>]");
                Msg("  /zonecontrol appearance <name> <palette|shade|scale|translucency|shiny|setup|clothing|palettebase|motion|sound|icon> <value> [--wcid <id>]   (cosmetic; separate from stats; DataId fields take hex 0x.. ; reload landblock to see)");
                Msg("  /zonecontrol clearappearance <name> [field] [--wcid <id>]   (no field = clear all)");
                Msg("  /zonecontrol copylook <name> <donorWcid> [--wcid <id>]   (make the target look like another monster - copies its model + palette + parts)");
                Msg("  /zonecontrol previewmob <wcid> [distance]   (spawn an inert look-preview in front of you for 60s; a new preview replaces the old one; distance 1-120, default 5)");
                Msg("  /zonecontrol becomemob <donorWcid> --wcid <targetWcid>   (target BECOMES a full copy of the donor - stats/loot/spells/everything; keeps its name, class_Name and scale)");
                Msg("  /zonecontrol draftslot <name> [release]   (claim your Drafted Look slot in this zone - a scratch wcid to craft a look on; release discards)");
                Msg("  /zonecontrol copydraft <name> <destWcid>   (save your Drafted Look onto destWcid's zone appearance, then bakemob/clonemob to keep it)");
                Msg("  /zonecontrol seticon <wcid> <iconDid|clear> [icon|overlay|overlay2|underlay]   (PERMANENT world-db icon change, layer defaults to icon; logged to zc_seticon_<date>.sql. 'appearance icon' is the zone-only version)");
                Msg("  /zonecontrol listparts <wcid | 0xSetupId>   (dump a mob's body-part layout: index -> model piece; head = index 16)");
                Msg("  /zonecontrol appearance <name> animpart <index> <gfxObjHex> [--wcid <id>]   (swap ONE body part, e.g. animpart 16 = head; clear with clearappearance <name> animpart <index>)");
                Msg("  /zonecontrol cantrip <name> <add|remove|list|catalog> [key] [--wcid <id>]   (custom Zone Cantrip pool for the extra-loot-cantrip roll; Retired keys are rejected)");
                Msg("  /zonecontrol cantrip <name> band <key> <min> <max> [procMin procMax]   (override a key's roll band; 'band <key> clear' drops the override; 'cantrip default <var> band ...' authors the variation Default)");
                Msg("  /zonecontrol cantrip <name> lines <min> <max> [c1] [c2] [c3]   (Armor v2 line ladder: min guaranteed, extra slots up to max roll c1/c2/c3 in order, first miss stops; writes cantrip_lines_*)");
                Msg("  /zonecontrol cantrip <name> weight <filler|average|chase> <n>   (key pick weight per class; writes cantrip_weight_*)");
                Msg("  /zonecontrol cantrip <name> special <key> <on|off|clear>   (per-special on/off for the per-kill slot-special roll; clear = drop the override)");
                Msg("  /zonecontrol currency <name> add <itemWcid> <amount> [chance 0..1] [direct|corpse] | remove <itemWcid> | list   [--wcid <id>]   (per-kill bonus-currency drop table; direct = into the killer's inventory)");
                Msg("  /zonecontrol boundary <name> <on|off|show>   (bounded: players at the zone's variation may only roam bounded-zone landblocks; variation 11+ only)");
                Msg("  /zonecontrol survey <name> [lbHex]   (per-landblock content: generator + creature summary; lbHex = full detail for one landblock)");
                Msg("  /zonecontrol quests <name>   (quest registry for the plugin Quests tab; throttled to one pull per 60s)");
                Msg("  /zonecontrol terrain <name> <hex> <type|clear>   (override the map terrain color for one landblock; type = " + string.Join("/", ZoneControlManager.TerrainTags) + "; display-only)");
                Msg("  /zonecontrol mobinfo <wcid>   (weenie base data: body parts, resists, wields)");
                Msg("  /zonecontrol genlist [zone]   (placed generator wcids + counts for the plugin's Generator Settings table; no zone = where you stand)");
                Msg("  /zonecontrol ladder status | apply [tier|all] | migrate [here|<player>] [--dry] | show   (live stat resolution: per-tier ladder versions, re-resolve on next equip, dev migration of pre-grade pieces, inspect the appraised item)");
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
            else if (sub != "create" && sub != "default" && sub != "ladder")
                // 'default' takes a VARIATION at args[1], not a zone name — collapsing would mangle it.
                // 'ladder' takes a verb / tier / player name, never a zone.
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
                        // Bare get = the GM Tools state alone (session flags + shard-combat rules).
                        // Those are not zone state, so the plugin asks for them zoneless.
                        if (args.Count < 2) { Msg(BuildSessionPayload(session)); return; }
                        Msg(BuildZonePayload(args[1], wcid, session));
                        return;
                    }

                    case "simstats":
                    {
                        // One-shot effective offense values for the Curves-tab player simulator: what
                        // combat actually uses for this wcid (its override bucket if one exists - a
                        // bucket REPLACES the zone default wholesale - else the zone default).
                        if (args.Count < 2) { Msg("Usage: simstats <name> --wcid <id>"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        var hasOverride = wcid.HasValue && area != null && area.Profile.WcidOverrides.ContainsKey(wcid.Value);
                        // layered (Default -> zone -> wcid), same as combat
                        var vp = ZoneControlManager.ResolveProfileForDisplay(name, wcid);
                        var sb = new StringBuilder("[[ZCSIM]]scope=").Append(name)
                            .Append("|wcid=").Append(wcid?.ToString() ?? "")
                            .Append("|found=").Append(area != null ? 1 : 0)
                            .Append("|override=").Append(hasOverride ? 1 : 0);
                        foreach (var stat in new[] { ZoneStat.PercentHpBase, ZoneStat.SpellDamage, ZoneStat.SpellVariance, ZoneStat.CritDamageRating })
                        {
                            int defined = 0;
                            double value = 0;
                            if (vp != null && vp.TryGet(stat, out var curve)) { defined = 1; value = curve.Base; }
                            sb.Append('|').Append(stat).Append('=').Append(defined).Append(',').Append(value.ToString(CultureInfo.InvariantCulture));
                        }
                        Msg(sb.ToString());
                        return;
                    }

                    case "mobs":
                    {
                        if (args.Count < 2) { Msg("Usage: mobs <name>"); return; }
                        var name = args[1];
                        var mobs = ZoneControlManager.GetAreaMobs(name);
                        // Override flags ride AFTER the name so an older plugin (which reads exactly three
                        // fields and takes parts[2] as the name) keeps working against this reply.
                        var ovFlags = ZoneControlManager.GetAreaMobOverrideFlags(name);
                        var sb = new StringBuilder("[[ZCM]]scope=").Append(name);
                        foreach (var m in mobs)
                            sb.Append('|').Append(m.Wcid).Append(',').Append(m.IsMonster ? 1 : 0).Append(',')
                              .Append(m.Name.Replace('|', ' ').Replace(',', ' ')).Append(',')
                              .Append(ovFlags.TryGetValue(m.Wcid, out var ov) ? ov : 0);
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

                    case "geninfo":
                    {
                        // Current generator-weenie knobs for the plugin's Generator Settings boxes.
                        if (args.Count < 2 || !uint.TryParse(args[1], out var genWcid)) { Msg("Usage: geninfo <wcid>"); return; }
                        var genWeenie = ACE.Database.DatabaseManager.World.GetCachedWeenie(genWcid);
                        if (genWeenie == null) { Msg($"[[ZCG]]w={genWcid}~found=0"); return; }
                        Msg(BuildGenInfoPayload(genWcid, genWeenie));
                        return;
                    }

                    case "genlist":
                    {
                        // Discovery for the plugin's Generator Settings table (owner 2026-08-08): distinct
                        // placed generator wcids + counts across a zone's landblocks at the zone's variation.
                        // No name = the zone covering the player's landblock (their effective variation
                        // preferred); outside any zone the scan falls back to the single current landblock.
                        // Emits one [[ZCL]] header then a [[ZCG]] knob line per wcid.
                        string scopeLabel;
                        List<ZoneControlManager.SurveyPlacedRow> placedGens;
                        if (args.Count >= 2)
                        {
                            placedGens = ZoneControlManager.GetPlacedGenerators(args[1]);
                            if (placedGens == null) { Msg($"No zone '{args[1]}'."); return; }
                            scopeLabel = args[1];
                        }
                        else
                        {
                            var loc = session.Player?.Location;
                            if (loc == null) { Msg("No location."); return; }
                            var hereLb = loc.LandblockId.Landblock;
                            var hereVar = ZoneControlManager.GetEffectiveVariation(session.Player);
                            var covering = ZoneControlManager.AreasCovering(hereLb);
                            var hereArea = covering.FirstOrDefault(a => a.Variation == hereVar) ?? covering.FirstOrDefault();
                            if (hereArea != null)
                            {
                                placedGens = ZoneControlManager.GetPlacedGenerators(hereArea.Name) ?? new List<ZoneControlManager.SurveyPlacedRow>();
                                scopeLabel = hereArea.Name;
                            }
                            else
                            {
                                placedGens = ZoneControlManager.GetPlacedGeneratorsForLandblock(hereLb, hereVar);
                                scopeLabel = $"lb {hereLb:X4}";
                            }
                        }

                        const int GenListCap = 200;
                        var genListTrunc = placedGens.Count > GenListCap;
                        if (genListTrunc)
                            placedGens = placedGens.Take(GenListCap).ToList();

                        var zcl = new StringBuilder("[[ZCL]]zone=")
                            .Append(scopeLabel.Replace('|', ' ').Replace('~', ' ').Replace('=', ' ').Replace(',', ' '));
                        if (genListTrunc)
                            zcl.Append("|trunc=1");
                        zcl.Append("|g=").Append(string.Join(",", placedGens.Select(p => p.Wcid + "~" + p.Count)));
                        Msg(zcl.ToString());

                        foreach (var p in placedGens)
                        {
                            var w = ACE.Database.DatabaseManager.World.GetCachedWeenie(p.Wcid);
                            Msg(w == null ? $"[[ZCG]]w={p.Wcid}~found=0" : BuildGenInfoPayload(p.Wcid, w));
                        }
                        return;
                    }

                    case "genedit":
                    {
                        // WEENIE edit (single source of truth): updates ace_world + clears the weenie
                        // cache. Live generators keep their old profile until their landblock reloads.
                        if (args.Count < 4 || !uint.TryParse(args[1], out var genWcid))
                        { Msg("Usage: genedit <wcid> delay|radius|stagger|init|max <value>"); return; }
                        var genField = args[2].ToLowerInvariant();
                        if (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var genVal) || genVal < 0)
                        { Msg("Value must be a non-negative number."); return; }
                        if (ACE.Database.DatabaseManager.World.GetCachedWeenie(genWcid) == null)
                        { Msg($"No weenie {genWcid}."); return; }

                        // parsed-numeric interpolation only — nothing user-typed reaches the SQL as text
                        var sqls = new List<string>();
                        var vs = genVal.ToString("0.####", CultureInfo.InvariantCulture);
                        switch (genField)
                        {
                            case "delay":
                                sqls.Add($"UPDATE `weenie_properties_generator` SET `delay` = {vs} WHERE `object_Id` = {genWcid}");
                                sqls.Add($"INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`) VALUES ({genWcid},41,{vs}) ON DUPLICATE KEY UPDATE `value` = {vs}");
                                break;
                            case "radius":
                                sqls.Add($"INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`) VALUES ({genWcid},43,{vs}) ON DUPLICATE KEY UPDATE `value` = {vs}");
                                break;
                            case "stagger":
                                sqls.Add($"INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`) VALUES ({genWcid},9034,{vs}) ON DUPLICATE KEY UPDATE `value` = {vs}");
                                break;
                            case "init":
                                sqls.Add($"INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES ({genWcid},82,{(int)genVal}) ON DUPLICATE KEY UPDATE `value` = {(int)genVal}");
                                break;
                            case "max":
                                sqls.Add($"INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES ({genWcid},81,{(int)genVal}) ON DUPLICATE KEY UPDATE `value` = {(int)genVal}");
                                break;
                            default:
                                Msg("Field must be delay, radius, stagger, init or max."); return;
                        }

                        using (var genCtx = new WorldDbContext())
                            foreach (var s in sqls)
                                genCtx.Database.ExecuteSqlRaw(s);
                        ACE.Database.DatabaseManager.World.ClearCachedWeenie(genWcid);

                        Msg($"genedit: wcid {genWcid} {genField} = {vs} (weenie updated + cache cleared). " +
                            "Live generators keep the OLD value until their landblock next reloads.");
                        var refreshed = ACE.Database.DatabaseManager.World.GetCachedWeenie(genWcid);
                        if (refreshed != null)
                            Msg(BuildGenInfoPayload(genWcid, refreshed));
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
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion;
                            vp.Stats[stat] = new StatCurve { Base = value, Growth = 1.0, Additive = false };
                        });
                        Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} {stat} = {FmtStatEcho(value)}. " +
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
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
                            if (vp != null) removed = vp.Stats.Remove(stat);
                        });
                        Msg(removed ? $"'{name}' {stat} cleared." : "That stat wasn't set.");
                        return;
                    }

                    case "cantrip":
                    {
                        // Custom cantrip pool + banded rolls for the extra-loot-cantrip roll
                        // (weapon/armor_cantrip_chance). Scope is a zone (+ optional --wcid), or
                        // 'cantrip default <var> ...' = the variation Default layer (band/slots/special/lines/weight only —
                        // the pool itself stays zone-scope).
                        if (args.Count < 3) { Msg("Usage: cantrip <name> <add|remove|list|catalog|band|slots|special|lines|weight> [args] [--wcid <id>]"); return; }

                        var isDefaultScope = args[1].Equals("default", StringComparison.OrdinalIgnoreCase);
                        var defaultVar = -1;
                        var opIdx = 2;
                        string name = null;
                        if (isDefaultScope)
                        {
                            if (args.Count < 4 || !int.TryParse(args[2].TrimStart('v', 'V'), out defaultVar) || defaultVar < 0)
                            { Msg("Usage: cantrip default <variation> <band|slots|special|lines|weight> ..."); return; }
                            opIdx = 3;
                        }
                        else
                        {
                            name = args[1];
                            if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        }

                        var op = args[opIdx].ToLowerInvariant();
                        var scopeTag = isDefaultScope
                            ? $"Default v{defaultVar}"
                            : $"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")}";

                        // One writer for both scopes: variation Default / zone default / --wcid bucket.
                        void MutateScope(Action<ZoneVariantProfile> edit)
                        {
                            if (isDefaultScope)
                                ZoneControlManager.MutateVariationDefault(defaultVar, d => edit(d.Profile));
                            else
                                ZoneControlManager.MutateArea(name, a =>
                                    edit(wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion));
                        }

                        if (op == "catalog")
                        {
                            foreach (var d in ZoneCantrips.Catalog.Values)
                                Msg($"  {d.Key,3}  {d.Name} - {d.Effect}  [rolls {d.Min}-{d.Max}]");
                            return;
                        }

                        if (op == "band")
                        {
                            // cantrip <scope> band <key> clear — drop the override, back to the catalog band.
                            if (args.Count >= opIdx + 3 && int.TryParse(args[opIdx + 1], out var clearKey)
                                && args[opIdx + 2].Equals("clear", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!ZoneCantrips.TryGet(clearKey, out var clearDef))
                                { Msg($"No Zone Cantrip with key {clearKey}. See 'cantrip <name> catalog'."); return; }
                                MutateScope(vp => vp.CustomCantripBands.Remove(clearKey));
                                // do not claim the catalog band applies - a lower layer (variation
                                // Default) may still author this key; the merge decides per key
                                Msg($"{scopeTag} cantrip band: {clearDef.Name} override cleared at this scope - drops roll the next authored layer, or the catalog band {clearDef.Min}-{clearDef.Max}.");
                                AutoApplyForDefault(session, isDefaultScope, defaultVar, Msg);
                                return;
                            }

                            // cantrip <scope> band <key> <min> <max> — override the roll band.
                            if (args.Count < opIdx + 4
                                || !int.TryParse(args[opIdx + 1], out var bandKey)
                                || !int.TryParse(args[opIdx + 2], out var bandMin)
                                || !int.TryParse(args[opIdx + 3], out var bandMax))
                            { Msg("Usage: cantrip <name> band <key> <min> <max>"); return; }
                            if (!ZoneCantrips.TryGet(bandKey, out var bandDef))
                            { Msg($"No Zone Cantrip with key {bandKey}. See 'cantrip <name> catalog'."); return; }
                            if (bandMin < 0) { Msg("min must be >= 0."); return; }
                            if (bandMin > bandMax) { Msg("min must be <= max."); return; }

                            MutateScope(vp => vp.CustomCantripBands[bandKey] =
                                new CantripBand { Min = bandMin, Max = bandMax });
                            Msg($"{scopeTag} cantrip band: {bandDef.Name} rolls {bandMin}-{bandMax}.");
                            AutoApplyForDefault(session, isDefaultScope, defaultVar, Msg);
                            return;
                        }

                        if (op == "special")
                        {
                            // cantrip <scope> special <key> <on|off|clear> (owner 2026-08-23): per-special on/off for the
                            // per-kill special roll. clear = drop the override (back to the next layer / on).
                            if (args.Count < opIdx + 3 || !int.TryParse(args[opIdx + 1], out var spKey))
                            { Msg("Usage: cantrip <name> special <key> <on|off|clear>"); return; }
                            if (!ZoneCantrips.TryGet(spKey, out var spDef) || !spDef.SlotSpecial)
                            { Msg($"Key {spKey} is not a slot special."); return; }
                            var spTok = args[opIdx + 2].ToLowerInvariant();
                            if (spTok == "clear")
                            {
                                MutateScope(vp => vp.CustomSpecials.Remove(spKey));
                                Msg($"{scopeTag} special: {spDef.Name} override cleared (rolls unless a lower layer turns it off).");
                                return;
                            }
                            if (spTok != "on" && spTok != "off") { Msg("Usage: cantrip <name> special <key> <on|off|clear>"); return; }
                            var spOn = spTok == "on";
                            MutateScope(vp => vp.CustomSpecials[spKey] = spOn);
                            Msg($"{scopeTag} special: {spDef.Name} {(spOn ? "ON" : "OFF")} - {(spOn ? "rolls" : "never rolls")} on kills here.");
                            return;
                        }

                        if (op == "slots")
                        {
                            // cantrip <scope> slots <key> <any|armor|shield|jewelry|clothing|cloak[,...] | mask | clear>
                            // (owner 2026-08-22): which piece kinds this line may roll on, live. clear = back to the catalog default.
                            if (args.Count < opIdx + 3 || !int.TryParse(args[opIdx + 1], out var slotKey))
                            { Msg("Usage: cantrip <name> slots <key> <any|armor|shield|jewelry|clothing|cloak[,...]|clear>"); return; }
                            if (!ZoneCantrips.TryGet(slotKey, out var slotDef))
                            { Msg($"No Zone Cantrip with key {slotKey}. See 'cantrip <name> catalog'."); return; }
                            var spec = string.Join(" ", args.Skip(opIdx + 2));
                            if (slotDef.SlotSpecial)
                            {
                                // a SPECIAL has ONE home slot: helm|chest|shoulders|bracers|gauntlets|girth|tassets|greaves|boots|shield|neck|trinket|ring|bracelet|cloak
                                if (!ZoneCantrips.TryParseSpecialSlot(spec, out var specialSlot))
                                { Msg("Special slots: helm, chest, shoulders, bracers, gauntlets, girth, tassets, greaves, boots, shield, neck, trinket, ring, bracelet, cloak - or clear."); return; }
                                if (specialSlot < 0)
                                {
                                    MutateScope(vp => vp.CustomCantripSlots.Remove(slotKey));
                                    Msg($"{scopeTag} cantrip slots: {slotDef.Name} override cleared at this scope - next authored layer, else the catalog slot ({ZoneCantrips.DefaultSpecialSlot(slotDef)}).");
                                }
                                else
                                {
                                    MutateScope(vp => vp.CustomCantripSlots[slotKey] = specialSlot);
                                    Msg($"{scopeTag} cantrip slots: {slotDef.Name} special now lands on {ZoneCantrips.SpecialSlotName(specialSlot)}.");
                                }
                                return;
                            }
                            if (!ZoneCantrips.TryParseSlotSpec(spec, out var slotMask))
                            { Msg("Slot names: any, armor, shield, jewelry, clothing, cloak (comma-separated), or clear."); return; }
                            if (slotMask < 0)
                            {
                                MutateScope(vp => vp.CustomCantripSlots.Remove(slotKey));
                                Msg($"{scopeTag} cantrip slots: {slotDef.Name} override cleared at this scope - the next authored layer applies, else the catalog default ({ZoneCantrips.SlotMaskName((int)ZoneCantrips.DefaultSlotMask(slotDef))}).");
                            }
                            else
                            {
                                MutateScope(vp => vp.CustomCantripSlots[slotKey] = slotMask);
                                Msg($"{scopeTag} cantrip slots: {slotDef.Name} rolls on {ZoneCantrips.SlotMaskName(slotMask)}.");
                            }
                            return;
                        }

                        // Armor v2 (2026-08-21): the line-count ladder, class weights and special odds are plain
                        // stats on the scope - same StatCurve write as 'default <var> set'.
                        void SetStat(string stat, double value)
                            => MutateScope(vp => vp.Stats[stat] = new StatCurve { Base = value, Growth = 1.0, Additive = false });

                        if (op == "lines")
                        {
                            // cantrip <scope> lines <min> <max> [c1] [c2] [c3]
                            if (args.Count < opIdx + 3
                                || !int.TryParse(args[opIdx + 1], out var linesMin)
                                || !int.TryParse(args[opIdx + 2], out var linesMax))
                            { Msg("Usage: cantrip <name> lines <min> <max> [c1] [c2] [c3]   (min/max 0-8; c1..c3 = 0..1 chance per extra slot)"); return; }
                            if (linesMin < 0 || linesMax > 8 || linesMin > linesMax) { Msg("lines: need 0 <= min <= max <= 8."); return; }
                            var chances = new double?[3];
                            for (int ci = 0; ci < 3; ci++)
                            {
                                if (args.Count <= opIdx + 3 + ci) break;
                                if (!double.TryParse(args[opIdx + 3 + ci], NumberStyles.Float, CultureInfo.InvariantCulture, out var c) || c < 0 || c > 1)
                                { Msg($"c{ci + 1} must be a chance in 0..1."); return; }
                                chances[ci] = c;
                            }
                            SetStat(ZoneStat.CantripLinesMin, linesMin);
                            SetStat(ZoneStat.CantripLinesMax, linesMax);
                            if (chances[0].HasValue) SetStat(ZoneStat.CantripLinesChance1, chances[0].Value);
                            if (chances[1].HasValue) SetStat(ZoneStat.CantripLinesChance2, chances[1].Value);
                            if (chances[2].HasValue) SetStat(ZoneStat.CantripLinesChance3, chances[2].Value);
                            Msg($"{scopeTag} cantrip lines: {linesMin} guaranteed, up to {linesMax}"
                                + (chances[0].HasValue ? $", extra slot chances {string.Join(" / ", chances.Where(c => c.HasValue).Select(c => c.Value.ToString("0.###", CultureInfo.InvariantCulture)))}" : " (slot chances unchanged)") + ".");
                            return;
                        }

                        if (op == "weight")
                        {
                            // cantrip <scope> weight <filler|average|chase> <n>   (old words trash / mid still accepted)
                            if (args.Count < opIdx + 3
                                || !double.TryParse(args[opIdx + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) || weight < 0)
                            { Msg("Usage: cantrip <name> weight <filler|average|chase> <n>   (n >= 0)"); return; }
                            var weightStat = args[opIdx + 1].ToLowerInvariant() switch
                            {
                                "filler" or "trash" => ZoneStat.CantripWeightTrash,
                                "average" or "mid" => ZoneStat.CantripWeightMid,
                                "chase" => ZoneStat.CantripWeightChase,
                                _ => null,
                            };
                            if (weightStat == null) { Msg("Class must be filler, average or chase."); return; }
                            SetStat(weightStat, weight);
                            Msg($"{scopeTag} {weightStat} = {weight.ToString("0.###", CultureInfo.InvariantCulture)}.");
                            return;
                        }

                        if (isDefaultScope)
                        { Msg("Default scope supports band | slots | lines | weight | special only (the pool add | remove | list are zone-scope)."); return; }

                        if (op == "list")
                        {
                            // effective pool: variation Default + zone + this wcid (union across layers)
                            var vp = ZoneControlManager.ResolveProfileForDisplay(name, wcid);
                            var ids = vp?.CustomCantrips;
                            if (ids == null || ids.Count == 0) { Msg("(no zone cantrips in the pool)"); return; }
                            foreach (var id in ids)
                                Msg(ZoneCantrips.TryGet(id, out var d) ? $"  {id}  {d.Name} - {d.Effect}" : $"  {id}  (unknown key)");
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
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion;
                                if (!vp.CustomCantrips.Contains(cantripKey)) vp.CustomCantrips.Add(cantripKey);
                            });
                            Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} zone cantrip added: {def.Name} ({def.Effect}).");
                        }
                        else if (op == "remove")
                        {
                            var removed = false;
                            ZoneControlManager.MutateArea(name, a =>
                            {
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
                                if (vp != null) removed = vp.CustomCantrips.Remove(cantripKey);
                            });
                            Msg(removed ? $"'{name}' zone cantrip removed: {(ZoneCantrips.TryGet(cantripKey, out var rdef) ? rdef.Name : "key " + cantripKey)}." : "That line wasn't in the pool.");
                        }
                        else
                            Msg("op must be add | remove | list | catalog | band | slots | special | lines | weight");
                        return;
                    }

                    case "spell":
                    {
                        // Spell-book rules: disable/re-enable a governed mob's spells, override cast
                        // chances (percent per cast opportunity), or ADD spells the weenie doesn't know.
                        // Read-time (Monster_Magic.TryRollSpell) - changes apply LIVE, no respawn needed.
                        if (args.Count < 3) { Msg("Usage: spell <name> <off|on|add|chance|remove|list> [spellId] [chancePct] [--wcid <id>]"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var op = args[2].ToLowerInvariant();
                        var wcidTag = wcid.HasValue ? " [wcid " + wcid.Value + "]" : "";

                        if (op == "list")
                        {
                            // effective rules: variation Default + zone + this wcid (union, most specific wins)
                            var vpl = ZoneControlManager.ResolveProfileForDisplay(name, wcid);
                            var rules = vpl?.SpellRules;
                            if (rules == null || rules.Count == 0) { Msg("(no spell rules)"); return; }
                            foreach (var r in rules)
                            {
                                var spl = new ACE.Server.Entity.Spell(r.SpellId);
                                Msg($"  {r.SpellId}  {(spl.NotFound ? "(unknown)" : spl.Name)}  {(r.Disabled ? "OFF" : "on")}{(r.Chance.HasValue ? "  chance " + r.Chance.Value.ToString(CultureInfo.InvariantCulture) + " pct" : "")}");
                            }
                            return;
                        }

                        if (args.Count < 4 || !int.TryParse(args[3], out var ruleSpellId) || ruleSpellId <= 0)
                        { Msg("Usage: spell <name> off|on|add|chance|remove <spellId> [chancePct]"); return; }

                        var spellCheck = new ACE.Server.Entity.Spell(ruleSpellId);
                        if (spellCheck.NotFound && op != "remove" && op != "on")
                        { Msg($"No spell with id {ruleSpellId}."); return; }
                        var spellLabel = spellCheck.NotFound ? "#" + ruleSpellId : spellCheck.Name;

                        double? chanceArg = null;
                        if (args.Count >= 5 && double.TryParse(args[4], System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var chanceVal))
                            chanceArg = Math.Clamp(chanceVal, 0.0, 100.0);

                        switch (op)
                        {
                            case "off":
                                ZoneControlManager.MutateArea(name, a =>
                                {
                                    var vp2 = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion;
                                    var rule = vp2.SpellRules.Find(r => r.SpellId == ruleSpellId);
                                    if (rule == null) vp2.SpellRules.Add(new ZoneSpellRule { SpellId = ruleSpellId, Disabled = true });
                                    else rule.Disabled = true;
                                });
                                Msg($"'{name}'{wcidTag} spell OFF: {spellLabel}. Applies live.");
                                return;

                            case "on":
                                ZoneControlManager.MutateArea(name, a =>
                                {
                                    var vp2 = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
                                    var rule = vp2?.SpellRules.Find(r => r.SpellId == ruleSpellId);
                                    if (rule != null)
                                    {
                                        rule.Disabled = false;
                                        if (!rule.Chance.HasValue) vp2.SpellRules.Remove(rule);   // empty rule = no rule
                                    }
                                });
                                Msg($"'{name}'{wcidTag} spell ON: {spellLabel}.");
                                return;

                            case "add":
                            case "chance":
                                ZoneControlManager.MutateArea(name, a =>
                                {
                                    var vp2 = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion;
                                    var rule = vp2.SpellRules.Find(r => r.SpellId == ruleSpellId);
                                    if (rule == null) vp2.SpellRules.Add(new ZoneSpellRule { SpellId = ruleSpellId, Chance = chanceArg ?? 2.0 });
                                    else { rule.Disabled = false; rule.Chance = chanceArg ?? rule.Chance ?? 2.0; }
                                });
                                Msg($"'{name}'{wcidTag} spell {(op == "add" ? "added" : "chance set")}: {spellLabel} at {(chanceArg ?? 2.0).ToString(CultureInfo.InvariantCulture)} pct per cast roll.");
                                return;

                            case "remove":
                                var removedRule = false;
                                ZoneControlManager.MutateArea(name, a =>
                                {
                                    var vp2 = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
                                    if (vp2 != null) removedRule = vp2.SpellRules.RemoveAll(r => r.SpellId == ruleSpellId) > 0;
                                });
                                Msg(removedRule ? $"'{name}'{wcidTag} spell rule removed for {spellLabel} (back to book default)." : "No rule for that spell.");
                                return;

                            default:
                                Msg("op must be off | on | add | chance | remove | list");
                                return;
                        }
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
                            // effective table: variation Default + zone + this wcid (union, most specific wins)
                            var vp = ZoneControlManager.ResolveProfileForDisplay(name, wcid);
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
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion;
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
                                var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
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
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion;
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
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
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
                            wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value, create: true) : a.Profile.Minion));
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
                            var vp = wcid.HasValue ? a.Profile.VariantForWcid(wcid.Value) : a.Profile.Minion;
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

                    case "appearance":
                    {
                        // Cosmetic appearance layer, kept SEPARATE from stats. Field-based; the default set and
                        // per-WCID (--wcid) overlays layer (per-WCID non-null wins). Never creates a stat bucket.
                        if (args.Count < 4) { Msg("Usage: appearance <name> <palette|shade|scale|translucency|shiny> <value> [--wcid <id>]"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        var field = args[2].ToLowerInvariant();

                        // Per-part model override: "appearance <name> animpart <index> <gfxObjHex> [--wcid]" -
                        // layers ONE body-part swap over the base model (e.g. index 16 = head on humanoid setups).
                        // Different arg shape (index + id) than the single-value levers, so handled up here. Each
                        // index is stored once (re-setting the same index replaces it); it accumulates with copylook'd
                        // parts. Clear with "clearappearance <name> animpart <index>".
                        if (field == "animpart" || field == "part")
                        {
                            if (args.Count < 5) { Msg("Usage: appearance <name> animpart <index 0-255> <gfxObjHex 0x01......> [--wcid <id>]"); return; }
                            if (!byte.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pIdx)) { Msg("index must be 0-255."); return; }
                            if (!TryParseDid(args[4], out var gfx) || (gfx >> 24) != 0x01) { Msg("gfxObj must be a 0x01...... DataId (a model piece, e.g. from listparts)."); return; }
                            ZoneControlManager.MutateArea(name, a =>
                            {
                                var ap = a.AppearanceFor(wcid, create: true);
                                ap.AnimParts ??= new List<AnimPartEntry>();
                                ap.AnimParts.RemoveAll(p => p.Index == pIdx);
                                ap.AnimParts.Add(new AnimPartEntry { Index = pIdx, GfxObj = gfx });
                            });
                            Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} body part [{pIdx}] = 0x{gfx:X8}. Reload the landblock to see it.");
                            return;
                        }

                        string valueEcho;
                        Action<ZoneAppearance> applyAp;
                        switch (field)
                        {
                            case "name":
                                // Display-name override (owner 2026-08-09). Per-WCID ONLY: a zone-wide
                                // default would rename every mob in the zone identically. Value = the rest
                                // of the args (--wcid was already extracted), so spaces need no quotes.
                                if (!wcid.HasValue) { Msg("name is per-monster only - target a specific monster (--wcid)."); return; }
                                var newName = string.Join(" ", args.Skip(3)).Trim().Trim('"').Trim();
                                newName = new string(newName.Where(c => c >= 32 && c < 127 && c != '|' && c != '~' && c != '=').ToArray()).Trim();
                                if (newName.Length == 0) { Msg("Usage: appearance <zone> name <new name> --wcid <id>"); return; }
                                if (newName.Length > 64) newName = newName.Substring(0, 64);
                                var nmFinal = newName;
                                applyAp = ap => ap.Name = nmFinal;
                                valueEcho = nmFinal;
                                break;
                            case "palette":
                            case "palettetemplate":
                                if (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pal)) { Msg("palette must be an integer id."); return; }
                                applyAp = ap => ap.PaletteTemplate = pal;
                                valueEcho = pal.ToString(CultureInfo.InvariantCulture);
                                break;
                            case "shade":
                                if (!TryDouble(args[3], out var sh)) { Msg("shade must be a number 0..1."); return; }
                                applyAp = ap => ap.Shade = sh;
                                valueEcho = sh.ToString("0.####", CultureInfo.InvariantCulture);
                                break;
                            case "scale":
                                if (!TryDouble(args[3], out var sc)) { Msg("scale must be a number."); return; }
                                applyAp = ap => ap.Scale = sc;
                                valueEcho = sc.ToString("0.####", CultureInfo.InvariantCulture);
                                break;
                            case "translucency":
                            case "trans":
                                if (!TryDouble(args[3], out var tr)) { Msg("translucency must be a number 0..1."); return; }
                                applyAp = ap => ap.Translucency = tr;
                                valueEcho = tr.ToString("0.####", CultureInfo.InvariantCulture);
                                break;
                            case "shiny":
                                var on = args[3].Equals("true", StringComparison.OrdinalIgnoreCase) || args[3] == "1" || args[3].Equals("on", StringComparison.OrdinalIgnoreCase);
                                applyAp = ap => ap.Shiny = on;
                                valueEcho = on ? "on" : "off";
                                break;
                            case "setup":
                            case "setuptableid":
                                if (!TryParseDid(args[3], out var didSetup)) { Msg("setup must be a DataId (hex like 0x02001234 or decimal)."); return; }
                                applyAp = ap => ap.SetupTableId = didSetup;
                                valueEcho = "0x" + didSetup.ToString("X8");
                                break;
                            case "clothing":
                            case "clothingbase":
                                if (!TryParseDid(args[3], out var didClo)) { Msg("clothing must be a DataId (0x10......)."); return; }
                                applyAp = ap => ap.ClothingBase = didClo;
                                valueEcho = "0x" + didClo.ToString("X8");
                                break;
                            case "palettebase":
                            case "palbase":
                                if (!TryParseDid(args[3], out var didPb)) { Msg("palettebase must be a DataId (0x04......)."); return; }
                                applyAp = ap => ap.PaletteBase = didPb;
                                valueEcho = "0x" + didPb.ToString("X8");
                                break;
                            case "motion":
                            case "motiontable":
                                if (!TryParseDid(args[3], out var didMt)) { Msg("motion must be a DataId (0x09......)."); return; }
                                applyAp = ap => ap.MotionTable = didMt;
                                valueEcho = "0x" + didMt.ToString("X8");
                                break;
                            case "sound":
                            case "soundtable":
                                if (!TryParseDid(args[3], out var didSt)) { Msg("sound must be a DataId (0x20......)."); return; }
                                applyAp = ap => ap.SoundTable = didSt;
                                valueEcho = "0x" + didSt.ToString("X8");
                                break;
                            case "icon":
                                if (!TryParseDid(args[3], out var didIcon)) { Msg("icon must be a DataId (0x06......)."); return; }
                                applyAp = ap => ap.Icon = didIcon;
                                valueEcho = "0x" + didIcon.ToString("X8");
                                break;
                            default:
                                Msg("field must be name | palette | shade | scale | translucency | shiny | setup | clothing | palettebase | motion | sound | icon"); return;
                        }

                        ZoneControlManager.MutateArea(name, a => applyAp(a.AppearanceFor(wcid, create: true)));
                        Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} appearance {field} = {valueEcho}. Applies on (re)spawn (reload the landblock).");
                        return;
                    }

                    case "clearappearance":
                    {
                        if (args.Count < 2) { Msg("Usage: clearappearance <name> [field] [--wcid <id>]  (no field = clear ALL, incl. model/clothing/palette)"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}'."); return; }
                        var field = args.Count >= 3 ? args[2].ToLowerInvariant() : null;
                        var cleared = false;
                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var ap = a.AppearanceFor(wcid, create: false);
                            if (ap == null) return;
                            if (field == null)
                            {
                                cleared = !ap.IsEmpty;
                                ap.Clear();   // ALL fields incl. the DataId swaps (Setup/Clothing/PaletteBase/Motion/Sound/Icon)
                            }
                            else
                            {
                                switch (field)
                                {
                                    case "name": cleared = !string.IsNullOrEmpty(ap.Name); ap.Name = null; break;
                                    case "palette": case "palettetemplate": cleared = ap.PaletteTemplate.HasValue; ap.PaletteTemplate = null; break;
                                    case "shade": cleared = ap.Shade.HasValue; ap.Shade = null; break;
                                    case "scale": cleared = ap.Scale.HasValue; ap.Scale = null; break;
                                    case "translucency": case "trans": cleared = ap.Translucency.HasValue; ap.Translucency = null; break;
                                    case "shiny": cleared = ap.Shiny.HasValue; ap.Shiny = null; break;
                                    case "setup": case "setuptableid": cleared = ap.SetupTableId.HasValue; ap.SetupTableId = null; break;
                                    case "clothing": case "clothingbase": cleared = ap.ClothingBase.HasValue; ap.ClothingBase = null; break;
                                    case "palettebase": case "palbase": cleared = ap.PaletteBase.HasValue; ap.PaletteBase = null; break;
                                    case "motion": case "motiontable": cleared = ap.MotionTable.HasValue; ap.MotionTable = null; break;
                                    case "sound": case "soundtable": cleared = ap.SoundTable.HasValue; ap.SoundTable = null; break;
                                    case "icon": cleared = ap.Icon.HasValue; ap.Icon = null; break;
                                    case "parts": case "bodyparts": cleared = ap.PartCount > 0; ap.AnimParts = null; ap.TextureMaps = null; break;
                                    case "animpart":
                                        // "clearappearance <name> animpart <index>" removes ONE part override;
                                        // "clearappearance <name> animpart" (no index) removes all part overrides.
                                        if (args.Count >= 4 && byte.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci))
                                        {
                                            int before = ap.AnimParts?.Count ?? 0;
                                            ap.AnimParts?.RemoveAll(p => p.Index == ci);
                                            if (ap.AnimParts != null && ap.AnimParts.Count == 0) ap.AnimParts = null;
                                            cleared = (ap.AnimParts?.Count ?? 0) != before;
                                        }
                                        else { cleared = (ap.AnimParts?.Count ?? 0) > 0; ap.AnimParts = null; }
                                        break;
                                    default: Msg("field must be name | palette | shade | scale | translucency | shiny | setup | clothing | palettebase | motion | sound | icon | parts | animpart <index>"); return;
                                }
                            }
                            if (wcid.HasValue && ap.IsEmpty) a.AppearanceByWcid.Remove(wcid.Value);
                        });
                        Msg(cleared ? $"'{name}' appearance {(field ?? "all")} cleared (reverts on respawn)." : "That appearance wasn't set.");
                        return;
                    }

                    case "resetmob":
                    {
                        // One-shot "back to zone defaults" for ONE monster (owner 2026-08-09): drops the
                        // wcid's whole stat/prop/loot override bucket AND its appearance bucket (incl.
                        // parts + name). The zone layer and the variation Default are untouched.
                        if (args.Count < 2 || !wcid.HasValue) { Msg("Usage: resetmob <zone> --wcid <id>  (removes ALL of that monster's overrides - stats, props, loot, appearance)"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}'."); return; }
                        bool hadStats = false, hadAp = false;
                        ZoneControlManager.MutateArea(name, a =>
                        {
                            hadStats = a.Profile.WcidOverrides != null && a.Profile.WcidOverrides.Remove(wcid.Value);
                            hadAp = a.AppearanceByWcid != null && a.AppearanceByWcid.Remove(wcid.Value);
                        });
                        Msg(hadStats || hadAp
                            ? $"'{name}' wcid {wcid.Value} reset to zone defaults ({(hadStats ? "stats/props/loot" : "")}{(hadStats && hadAp ? " + " : "")}{(hadAp ? "appearance" : "")} removed). Reload the landblock to see it."
                            : "That monster had no overrides - it already runs on zone defaults.");
                        return;
                    }

                    case "bakemob":
                    {
                        // The OPPOSITE of resetmob (owner 2026-08-09): write the monster's current
                        // effective LOOK (zone default overlaid by its per-WCID entry) into its weenie
                        // in ace_world - permanent, global (every zone and variation), survives zone
                        // deletion. APPEARANCE ONLY by design: stats stay ZC-tuned (weak-baseline
                        // philosophy). After a successful bake the wcid's appearance bucket is removed
                        // (redundant); the zone-wide appearance default is left alone.
                        if (args.Count < 2 || !wcid.HasValue) { Msg("Usage: bakemob <zone> --wcid <id>  (writes the monster's current look into its base template permanently)"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}'."); return; }
                        var w = wcid.Value;
                        if (ACE.Database.DatabaseManager.World.GetCachedWeenie(w) == null) { Msg($"No weenie {w} in the world db."); return; }

                        var apOverlay = area.AppearanceByWcid != null && area.AppearanceByWcid.TryGetValue(w, out var apw3) ? apw3 : null;
                        var merged = ZoneAppearance.Merge(area.AppearanceDefault, apOverlay);
                        if (merged == null || merged.IsEmpty) { Msg("Nothing to bake - this monster has no appearance overrides here."); return; }

                        var bake = BuildAppearanceBakeSql(w, merged);

                        using (var bakeCtx = new WorldDbContext())
                            foreach (var s in bake)
                                bakeCtx.Database.ExecuteSqlRaw(s);
                        ACE.Database.DatabaseManager.World.ClearCachedWeenie(w);

                        ZoneControlManager.MutateArea(name, a => a.AppearanceByWcid?.Remove(w));

                        Msg($"bakemob: wcid {w} - {bake.Count} change(s) written to the WORLD DB (permanent; applies in every zone and variation). " +
                            "This monster's appearance overrides here were removed (now baked in). Reload the landblock to see it.");
                        return;
                    }

                    case "clonemob":
                    {
                        // Mint a BRAND-NEW monster (owner 2026-08-09): full weenie clone of the source
                        // wcid + the source's current effective ZC look baked in + optional new display
                        // name, under a NEW wcid. The SOURCE weenie and its zone overrides are UNTOUCHED
                        // (other devs keep iterating on it). Ends by exporting the finished weenie to the
                        // Content sql folder - the shippable per-wcid file (import-discord compatible).
                        // Usage: clonemob <zone> <newWcid> [new display name...] --wcid <srcWcid>
                        if (args.Count < 3 || !wcid.HasValue)
                        { Msg("Usage: clonemob <zone> <newWcid> [new display name] --wcid <srcWcid>"); return; }
                        var name = args[1];
                        var area = ZoneControlManager.GetArea(name);
                        if (area == null) { Msg($"No zone '{name}'."); return; }
                        if (!uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var newWcid) || newWcid == 0)
                        { Msg("newWcid must be a number."); return; }
                        var srcWcid = wcid.Value;
                        if (newWcid == srcWcid) { Msg("newWcid must differ from the source wcid."); return; }
                        var srcWeenie = ACE.Database.DatabaseManager.World.GetWeenie(srcWcid);
                        if (srcWeenie == null) { Msg($"No weenie {srcWcid} in the world db."); return; }
                        if (ACE.Database.DatabaseManager.World.GetCachedWeenie(newWcid) != null)
                        { Msg($"Weenie {newWcid} already exists - pick an unused wcid."); return; }

                        // optional new display name = the remaining tokens (same sanitize as the name lever)
                        var cloneName = string.Join(" ", args.Skip(3)).Trim().Trim('"').Trim();
                        cloneName = new string(cloneName.Where(c => c >= 32 && c < 127 && c != '|' && c != '~' && c != '=').ToArray()).Trim();
                        if (cloneName.Length > 64) cloneName = cloneName.Substring(0, 64);

                        // 1) Full-fidelity clone: the Adapter's SQL writer emits the source weenie's
                        //    complete per-wcid SQL (every table incl. emotes); re-pointing every
                        //    whole-word occurrence of the source wcid also remaps self-references.
                        if (Processors.DeveloperContentCommands.WeenieSQLWriter == null)
                        {
                            Processors.DeveloperContentCommands.WeenieSQLWriter = new ACE.Database.SQLFormatters.World.WeenieSQLWriter
                            {
                                WeenieNames = ACE.Database.DatabaseManager.World.GetAllWeenieNames(),
                                SpellNames = ACE.Database.DatabaseManager.World.GetAllSpellNames(),
                                TreasureDeath = ACE.Database.DatabaseManager.World.GetAllTreasureDeath(),
                                TreasureWielded = ACE.Database.DatabaseManager.World.GetAllTreasureWielded(),
                                PacketOpCodes = ACE.Entity.PacketOpCodeNames.Values,
                            };
                        }
                        string cloneSql;
                        using (var ms = new System.IO.MemoryStream())
                        {
                            using (var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8, 4096, leaveOpen: true))
                            {
                                Processors.DeveloperContentCommands.WeenieSQLWriter.CreateSQLDELETEStatement(srcWeenie, sw);
                                sw.WriteLine();
                                Processors.DeveloperContentCommands.WeenieSQLWriter.CreateSQLINSERTStatement(srcWeenie, sw);
                                sw.Flush();
                            }
                            cloneSql = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                        }
                        cloneSql = System.Text.RegularExpressions.Regex.Replace(
                            cloneSql, $@"\b{srcWcid}\b", newWcid.ToString(CultureInfo.InvariantCulture));

                        using (var cloneCtx = new WorldDbContext())
                        {
                            cloneCtx.Database.SetCommandTimeout(0);
                            cloneCtx.Database.ExecuteSqlRaw(cloneSql.Replace("\r\n", "\n"));
                            // unique class_Name (the clone inherited the source's)
                            cloneCtx.Database.ExecuteSqlRaw(
                                $"UPDATE `weenie` SET `class_Name` = CONCAT(`class_Name`, '_{newWcid}') WHERE `class_Id` = {newWcid}");
                        }

                        // 1b) Copy the source's per-monster ZONE buckets too (stats/props/loot/cantrips/
                        //     currency/spell rules) so the clone behaves identically IN THIS ZONE (owner
                        //     2026-08-09: "loot table, stats etc - would need it"). Deep copy via the same
                        //     JSON round-trip the store itself persists with; source bucket untouched.
                        ZoneControlManager.MutateArea(name, a =>
                        {
                            if (a.Profile.WcidOverrides != null && a.Profile.WcidOverrides.TryGetValue(srcWcid, out var srcVp) && srcVp != null)
                                a.Profile.WcidOverrides[newWcid] = Newtonsoft.Json.JsonConvert.DeserializeObject<ZoneVariantProfile>(
                                    Newtonsoft.Json.JsonConvert.SerializeObject(srcVp));
                        });

                        // 2) Bake the source's effective ZC look (+ the new display name) onto the CLONE.
                        //    Source buckets stay exactly as they are.
                        var apOv = area.AppearanceByWcid != null && area.AppearanceByWcid.TryGetValue(srcWcid, out var apw4) ? apw4 : null;
                        var look = ZoneAppearance.Merge(area.AppearanceDefault, apOv) ?? new ZoneAppearance();
                        if (cloneName.Length > 0)
                            look.Name = cloneName;
                        if (!look.IsEmpty)
                        {
                            var bakeSql = BuildAppearanceBakeSql(newWcid, look);
                            using var lookCtx = new WorldDbContext();
                            foreach (var s in bakeSql)
                                lookCtx.Database.ExecuteSqlRaw(s);
                        }
                        ACE.Database.DatabaseManager.World.ClearCachedWeenie(newWcid);

                        // 3) Export the FINISHED weenie to the Content sql folder - the permanent,
                        //    shippable per-wcid file.
                        Processors.DeveloperContentCommands.ExportSQLWeenie(session, newWcid.ToString(CultureInfo.InvariantCulture));

                        Msg($"clonemob: {srcWcid} cloned to NEW wcid {newWcid}" +
                            (cloneName.Length > 0 ? $" named '{cloneName}'" : "") +
                            " with the current look baked in. The source monster is untouched. " +
                            "SQL exported to the Content sql folder (see above). Summon it anywhere (e.g. /ci " + newWcid + ").");
                        return;
                    }

                    case "becomemob":
                    {
                        // Convert an EXISTING monster into a full copy of another (owner 2026-08-10,
                        // after the 36-PH-mobs-to-drudge hand-SQL): everything the donor is - stats,
                        // skills, body parts, spell book, loot, emotes - lands on the target wcid.
                        // The target keeps exactly four things: class_Id, class_Name, display Name,
                        // and its current DefaultScale. Zone buckets are untouched (stats stay
                        // zone-tuned). Zone-less on purpose: this edits the WORLD DB, not a zone.
                        // Usage: becomemob <donorWcid> --wcid <targetWcid>
                        if (args.Count < 2 || !wcid.HasValue ||
                            !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bmDonor))
                        { Msg("Usage: becomemob <donorWcid> --wcid <targetWcid>   (target becomes a full copy of the donor; keeps name + scale)"); return; }
                        var bmTarget = wcid.Value;
                        if (bmDonor == bmTarget) { Msg("Donor and target must differ."); return; }
                        if (ZcDraftSlotWcids.Contains(bmTarget)) { Msg("The target cannot be a Drafted Look slot."); return; }
                        var bmDonorWeenie = ACE.Database.DatabaseManager.World.GetWeenie(bmDonor);
                        if (bmDonorWeenie == null) { Msg($"No weenie {bmDonor} in the world db."); return; }
                        var bmTargetWeenie = ACE.Database.DatabaseManager.World.GetCachedWeenie(bmTarget);
                        if (bmTargetWeenie == null) { Msg($"No weenie {bmTarget} in the world db."); return; }

                        // What the target KEEPS.
                        var bmKeepName = bmTargetWeenie.GetProperty(PropertyString.Name);
                        var bmKeepScale = bmTargetWeenie.GetProperty(PropertyFloat.DefaultScale);
                        string bmKeepClassName;
                        using (var bmCtx = new WorldDbContext())
                            bmKeepClassName = bmCtx.Weenie.Where(x => x.ClassId == bmTarget).Select(x => x.ClassName).FirstOrDefault();

                        // Same engine as clonemob: full-fidelity donor SQL, every occurrence of the
                        // donor wcid re-pointed at the target (covers emote self-references too).
                        if (Processors.DeveloperContentCommands.WeenieSQLWriter == null)
                        {
                            Processors.DeveloperContentCommands.WeenieSQLWriter = new ACE.Database.SQLFormatters.World.WeenieSQLWriter
                            {
                                WeenieNames = ACE.Database.DatabaseManager.World.GetAllWeenieNames(),
                                SpellNames = ACE.Database.DatabaseManager.World.GetAllSpellNames(),
                                TreasureDeath = ACE.Database.DatabaseManager.World.GetAllTreasureDeath(),
                                TreasureWielded = ACE.Database.DatabaseManager.World.GetAllTreasureWielded(),
                                PacketOpCodes = ACE.Entity.PacketOpCodeNames.Values,
                            };
                        }
                        string bmSql;
                        using (var ms = new System.IO.MemoryStream())
                        {
                            using (var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8, 4096, leaveOpen: true))
                            {
                                Processors.DeveloperContentCommands.WeenieSQLWriter.CreateSQLDELETEStatement(bmDonorWeenie, sw);
                                sw.WriteLine();
                                Processors.DeveloperContentCommands.WeenieSQLWriter.CreateSQLINSERTStatement(bmDonorWeenie, sw);
                                sw.Flush();
                            }
                            bmSql = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                        }
                        bmSql = System.Text.RegularExpressions.Regex.Replace(
                            bmSql, $@"\b{bmDonor}\b", bmTarget.ToString(CultureInfo.InvariantCulture));

                        using (var bmCtx = new WorldDbContext())
                        {
                            bmCtx.Database.SetCommandTimeout(0);
                            // Wipe the target's OLD self first (the remapped DELETE also targets it,
                            // but be explicit), then import the donor copy.
                            bmCtx.Database.ExecuteSqlRaw($"DELETE FROM `weenie` WHERE `class_Id` = {bmTarget}");
                            bmCtx.Database.ExecuteSqlRaw(bmSql.Replace("\r\n", "\n"));
                            // Restore the kept identity.
                            if (!string.IsNullOrEmpty(bmKeepClassName))
                            {
                                var cn = bmKeepClassName.Replace("'", "''");
                                bmCtx.Database.ExecuteSqlRaw($"UPDATE `weenie` SET `class_Name` = '{cn}' WHERE `class_Id` = {bmTarget}");
                            }
                            if (!string.IsNullOrEmpty(bmKeepName))
                            {
                                var nm = bmKeepName.Replace("'", "''");
                                bmCtx.Database.ExecuteSqlRaw($"DELETE FROM `weenie_properties_string` WHERE `object_Id` = {bmTarget} AND `type` = 1");
                                bmCtx.Database.ExecuteSqlRaw($"INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`) VALUES ({bmTarget}, 1, '{nm}')");
                            }
                            if (bmKeepScale.HasValue)
                            {
                                bmCtx.Database.ExecuteSqlRaw($"DELETE FROM `weenie_properties_float` WHERE `object_Id` = {bmTarget} AND `type` = 39");
                                bmCtx.Database.ExecuteSqlRaw($"INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`) VALUES ({bmTarget}, 39, {bmKeepScale.Value.ToString(CultureInfo.InvariantCulture)})");
                            }
                        }
                        ACE.Database.DatabaseManager.World.ClearCachedWeenie(bmTarget);

                        // Fresh shippable per-wcid SQL so the pack file matches the new reality.
                        Processors.DeveloperContentCommands.ExportSQLWeenie(session, bmTarget.ToString(CultureInfo.InvariantCulture));

                        var bmDonorName = bmDonorWeenie.GetProperty(PropertyString.Name) ?? bmDonor.ToString();
                        Msg($"becomemob: {bmKeepName ?? bmTarget.ToString()} ({bmTarget}) is now a full copy of {bmDonorName} ({bmDonor}) - " +
                            "stats, loot, spells, everything. Kept: name, class_Name" + (bmKeepScale.HasValue ? $", scale {bmKeepScale.Value:0.##}" : "") + ". " +
                            "Zone tuning unaffected. SQL exported. Reload the landblock to see it.");
                        return;
                    }

                    case "copylook":
                    {
                        // "Make the target look like <donorWcid>": read the donor weenie's model + palette data and
                        // stamp it into this zone's appearance layer (default set, or a single --wcid overlay).
                        if (args.Count < 3) { Msg("Usage: copylook <name> <donorWcid> [--wcid <id>]"); return; }
                        var name = args[1];
                        if (ZoneControlManager.GetArea(name) == null) { Msg($"No zone '{name}' (create it first)."); return; }
                        if (!uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var donorWcid)) { Msg("donorWcid must be a number."); return; }
                        var donor = ACE.Database.DatabaseManager.World.GetCachedWeenie(donorWcid);
                        if (donor == null) { Msg($"No weenie {donorWcid} in the world db."); return; }

                        var setup = donor.GetProperty(PropertyDataId.Setup);
                        var motion = donor.GetProperty(PropertyDataId.MotionTable);
                        var sound = donor.GetProperty(PropertyDataId.SoundTable);
                        var palBase = donor.GetProperty(PropertyDataId.PaletteBase);
                        var clothing = donor.GetProperty(PropertyDataId.ClothingBase);
                        var icon = donor.GetProperty(PropertyDataId.Icon);
                        var palTemplate = donor.GetProperty(PropertyInt.PaletteTemplate);
                        var shade = donor.GetProperty(PropertyFloat.Shade);
                        var scale = donor.GetProperty(PropertyFloat.DefaultScale);
                        // Per-part body swaps (e.g. Tusgian's 21 anim + 27 texture rows): copy the whole set so the
                        // target reproduces the donor's custom body. Only set when the donor actually has parts, so
                        // copying a plain mob never blanks the target's own parts.
                        var donorAnim = donor.PropertiesAnimPart;
                        var donorTex = donor.PropertiesTextureMap;

                        ZoneControlManager.MutateArea(name, a =>
                        {
                            var ap = a.AppearanceFor(wcid, create: true);
                            if (setup.HasValue) ap.SetupTableId = setup;
                            if (motion.HasValue) ap.MotionTable = motion;
                            if (sound.HasValue) ap.SoundTable = sound;
                            if (palBase.HasValue) ap.PaletteBase = palBase;
                            if (clothing.HasValue) ap.ClothingBase = clothing;
                            if (icon.HasValue) ap.Icon = icon;
                            if (palTemplate.HasValue) ap.PaletteTemplate = palTemplate;
                            if (shade.HasValue) ap.Shade = shade;
                            if (scale.HasValue) ap.Scale = scale;
                            ap.AnimParts = (donorAnim != null && donorAnim.Count > 0)
                                ? donorAnim.Select(p => new AnimPartEntry { Index = p.Index, GfxObj = p.AnimationId }).ToList() : null;
                            ap.TextureMaps = (donorTex != null && donorTex.Count > 0)
                                ? donorTex.Select(t => new TextureMapEntry { Index = t.PartIndex, OldTex = t.OldTexture, NewTex = t.NewTexture }).ToList() : null;
                        });
                        var donorName = donor.GetProperty(PropertyString.Name) ?? donorWcid.ToString();
                        var partN = (donorAnim?.Count ?? 0) + (donorTex?.Count ?? 0);
                        Msg($"'{name}'{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : "")} appearance copied from {donorName} ({donorWcid}){(partN > 0 ? $", incl. {partN} body-part swaps" : "")}. Reload the landblock to see it.");
                        return;
                    }

                    case "previewmob":
                    {
                        // Look-preview (owner 2026-08-09): spawn an INERT copy of a weenie in front of the
                        // player - not attackable, never aggros, 60s lifespan (heartbeat-driven, ~5s grain).
                        // One preview slot per player: a new preview replaces the previous one. Spawned inside
                        // a governed zone it picks up that zone's appearance overrides like any spawn, so it
                        // can also preview an override set, not just stock looks.
                        if (session?.Player == null) { Msg("previewmob needs an in-world player."); return; }
                        if (args.Count < 2 || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pvWcid))
                        { Msg("Usage: previewmob <wcid> [distance]"); return; }
                        // Optional spawn distance (owner 2026-08-10). Explicit distance wins over the
                        // automatic scale push-out below.
                        float pvDist = 5f;
                        var pvDistExplicit = args.Count >= 3 &&
                            float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out pvDist);
                        if (pvDistExplicit) pvDist = Math.Clamp(pvDist, 1f, 120f); else pvDist = 5f;
                        var pvWeenie = ACE.Database.DatabaseManager.World.GetCachedWeenie(pvWcid);
                        if (pvWeenie == null) { Msg($"No weenie {pvWcid} in the world db."); return; }

                        if (ZcPreviews.TryGetValue(session.Player.Guid.Full, out var prevPv)
                            && prevPv != null && !prevPv.IsDestroyed)
                            prevPv.DeleteObject();

                        var pv = ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject(pvWcid);
                        if (pv == null) { Msg($"Weenie {pvWcid} could not be instantiated."); return; }
                        if (!(pv is ACE.Server.WorldObjects.Creature pvCreature))
                        { pv.Destroy(); Msg($"{pv.Name ?? pvWcid.ToString()} is not a creature - nothing to preview."); return; }

                        pv.Location = session.Player.Location.InFrontOf(pvDist, true);
                        pv.Location.LandblockId = new ACE.Entity.LandblockId(ACE.Server.Entity.PositionExtensions.GetCell(pv.Location));
                        // The single-object factory path skips the zone spawn snapshot (only landblock/
                        // generator spawns get it), so previews never wore zone appearance overrides
                        // (found 2026-08-10: zone-default scale did not show on a preview). Location is
                        // set, so the scaler's ordering guard is satisfied; stats on an inert preview
                        // are harmless.
                        ZoneSpawnScaler.ApplyToSpawn(pvCreature);
                        // Scale-aware spawn distance (found 2026-08-10): a zone scale like 5x placed the
                        // preview's huge model ON the player - from inside a mesh you see nothing, which
                        // reads as "it despawned and never came back". Push big previews out proportionally
                        // UNLESS the caller chose a distance - their number is respected as-is.
                        var pvScale = (float)(pv.ObjScale ?? 1.0);
                        if (!pvDistExplicit && pvScale > 1.5f)
                        {
                            pv.Location = session.Player.Location.InFrontOf(pvDist * pvScale, true);
                            pv.Location.LandblockId = new ACE.Entity.LandblockId(ACE.Server.Entity.PositionExtensions.GetCell(pv.Location));
                        }
                        pv.Lifespan = 60;
                        pv.Attackable = false;
                        pvCreature.Tolerance = Tolerance.NoAttack;
                        pv.Name = (pv.Name ?? "Preview") + " (Preview)";

                        if (!pv.EnterWorld()) { Msg("Preview failed to spawn (physics placement)."); return; }
                        ZcPreviews[session.Player.Guid.Full] = pv;
                        Msg($"Previewing {pvWeenie.GetProperty(PropertyString.Name) ?? pvWcid.ToString()} ({pvWcid}) for 60s.");
                        return;
                    }

                    case "draftslot":
                    {
                        // Look Lab "Drafted Look" target (owner 2026-08-10): hand the asking player ONE of the
                        // five reserved Drafted Look wcids for this zone, so up to five admins can craft looks
                        // in the same zone without clobbering each other. Re-asking returns the same slot.
                        // "draftslot <zone> release" frees the slot and wipes its scratch bucket.
                        // Machine reply line for the plugin: [[ZCDRAFT]]<wcid>  (0 = all slots busy / released).
                        if (session?.Player == null) { Msg("draftslot needs an in-world player."); return; }
                        if (args.Count < 2) { Msg("Usage: draftslot <zone> [release]"); return; }
                        var dsArea = ZoneControlManager.GetArea(args[1]);
                        if (dsArea == null) { Msg($"No zone '{args[1]}'."); return; }
                        var dsZone = dsArea.Name;
                        var dsMe = session.Player.Guid.Full;

                        if (args.Count > 2 && args[2].Equals("release", StringComparison.OrdinalIgnoreCase))
                        {
                            ReleaseDraftClaim(dsZone, dsMe);
                            Msg("Draft slot released (scratch look discarded).");
                            Msg("[[ZCDRAFT]]0");
                            return;
                        }

                        // Evict claims of players no longer online (their scratch buckets are wiped too).
                        foreach (var dead in ZcDraftClaims.Keys.Where(k => PlayerManager.GetOnlinePlayer(k.Player) == null).ToList())
                            ReleaseDraftClaim(dead.Zone, dead.Player);

                        if (ZcDraftClaims.TryGetValue((dsZone, dsMe), out var mine))
                        { Msg("[[ZCDRAFT]]" + mine); return; }

                        var free = ZcDraftSlotWcids.FirstOrDefault(s =>
                            !ZcDraftClaims.Any(kv => kv.Key.Zone.Equals(dsZone, StringComparison.OrdinalIgnoreCase) && kv.Value == s));
                        if (free == 0)
                        { Msg("All 5 draft slots in this zone are in use."); Msg("[[ZCDRAFT]]0"); return; }

                        ZcDraftClaims[(dsZone, dsMe)] = free;
                        // Start clean even if something left junk in the bucket.
                        ZoneControlManager.MutateArea(dsZone, a => a.AppearanceByWcid?.Remove(free));
                        Msg("[[ZCDRAFT]]" + free);
                        return;
                    }

                    case "copydraft":
                    {
                        // Save the crafted Drafted Look: copy YOUR slot's zone appearance bucket onto the
                        // destination wcid's bucket in the same zone (REPLACES the destination's bucket),
                        // then clear + release the slot. bakemob / clonemob take it from there.
                        if (session?.Player == null) { Msg("copydraft needs an in-world player."); return; }
                        if (args.Count < 3) { Msg("Usage: copydraft <zone> <destWcid>   (saves your Drafted Look onto the destination's zone appearance)"); return; }
                        var cdArea = ZoneControlManager.GetArea(args[1]);
                        if (cdArea == null) { Msg($"No zone '{args[1]}'."); return; }
                        var cdZone = cdArea.Name;
                        if (!uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cdDest) || cdDest == 0)
                        { Msg("destWcid must be a number."); return; }
                        if (ZcDraftSlotWcids.Contains(cdDest))
                        { Msg("The destination cannot be a Drafted Look slot."); return; }
                        if (ACE.Database.DatabaseManager.World.GetCachedWeenie(cdDest) == null)
                        { Msg($"No weenie {cdDest} in the world db."); return; }

                        if (!ZcDraftClaims.TryGetValue((cdZone, session.Player.Guid.Full), out var cdSlot))
                        { Msg($"You have no draft slot in '{cdZone}' - nothing to save."); return; }

                        var cdBucket = cdArea.AppearanceByWcid != null && cdArea.AppearanceByWcid.TryGetValue(cdSlot, out var cdb) ? cdb : null;
                        if (cdBucket == null || cdBucket.IsEmpty)
                        { Msg("Your Drafted Look is empty - apply a look or set a lever first."); return; }

                        // Deep copy via the same JSON round-trip the store persists with; REPLACES dest's bucket.
                        ZoneControlManager.MutateArea(cdZone, a =>
                        {
                            a.AppearanceByWcid ??= new Dictionary<uint, ZoneAppearance>();
                            a.AppearanceByWcid[cdDest] = Newtonsoft.Json.JsonConvert.DeserializeObject<ZoneAppearance>(
                                Newtonsoft.Json.JsonConvert.SerializeObject(cdBucket));
                        });
                        ReleaseDraftClaim(cdZone, session.Player.Guid.Full);

                        var cdName = ACE.Database.DatabaseManager.World.GetCachedWeenie(cdDest)?.GetProperty(PropertyString.Name) ?? cdDest.ToString();
                        Msg($"copydraft: your Drafted Look now overrides {cdName} ({cdDest}) in '{cdZone}' (its previous appearance overrides here were replaced). " +
                            "Slot released. Make it permanent with bakemob, or mint a new monster with clonemob.");
                        return;
                    }

                    case "seticon":
                    {
                        // PERMANENT world-db edit: sets PropertyDataId.Icon (type 8) on a weenie.
                        // Unlike `appearance icon`, which is a per-zone override living in the ZC
                        // store, this rewrites the weenie itself and affects every server.
                        //
                        // Every edit is mirrored to zc_seticon_<date>.sql next to the server dll so
                        // the standing "no orphan MariaDB edits" rule still holds - that file is the
                        // migration artifact for ILT.
                        // LAYERS (owner 2026-08-12): the client composites an icon from four
                        // PropertyDataIds - Icon 8 (base), IconOverlay 50 (corner badge),
                        // IconOverlaySecondary 51, IconUnderlay 52. Overlay art is a FULL 32x32
                        // texture that is mostly transparent, not a small image.
                        if (args.Count < 3)
                        {
                            Msg("Usage: seticon <wcid> <iconDid|clear> [icon|overlay|overlay2|underlay]   "
                                + "(layer defaults to icon; 'clear' removes an overlay/underlay. "
                                + "Permanent world-db edit - use 'appearance icon' for a zone-only override)");
                            return;
                        }
                        if (!uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var siWcid) || siWcid == 0)
                        { Msg("wcid must be a number."); return; }

                        var siLayerArg = args.Count > 3 ? args[3].ToLowerInvariant() : "icon";
                        PropertyDataId siProp;
                        switch (siLayerArg)
                        {
                            case "icon": siProp = PropertyDataId.Icon; break;
                            case "overlay": siProp = PropertyDataId.IconOverlay; break;
                            case "overlay2":
                            case "secondary": siProp = PropertyDataId.IconOverlaySecondary; break;
                            case "underlay": siProp = PropertyDataId.IconUnderlay; break;
                            default:
                                Msg($"Unknown layer '{siLayerArg}'. Use icon | overlay | overlay2 | underlay.");
                                return;
                        }
                        var siType = (int)siProp;

                        var siClear = args[2].Equals("clear", StringComparison.OrdinalIgnoreCase)
                                   || args[2].Equals("none", StringComparison.OrdinalIgnoreCase)
                                   || args[2] == "0";

                        uint siIcon = 0;
                        if (!siClear)
                        {
                            if (!uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out siIcon) || siIcon == 0)
                            { Msg("iconDid must be a number (decimal, eg 100668365), or 'clear'."); return; }

                            // Icons live in the 0x06 texture band. Anything else is a typo - most likely
                            // a hex DID pasted without conversion - and would render as a missing icon.
                            if (siIcon < 0x06000000 || siIcon > 0x06FFFFFF)
                            { Msg($"{siIcon} is not in the icon range 0x06000000-0x06FFFFFF ({0x06000000}-{0x06FFFFFF}). Paste the DECIMAL DID."); return; }
                        }
                        else if (siProp == PropertyDataId.Icon)
                        {
                            // An item with no base icon draws nothing at all. Overlays and underlays
                            // are the layers you legitimately want to remove; to change the base,
                            // set a different DID rather than clearing it.
                            Msg("Refusing to clear the BASE icon - the item would render blank. Set a different iconDid instead, or clear an overlay/underlay layer.");
                            return;
                        }

                        var siWeenie = ACE.Database.DatabaseManager.World.GetCachedWeenie(siWcid);
                        if (siWeenie == null) { Msg($"No weenie {siWcid} in the world db."); return; }
                        var siName = siWeenie.GetProperty(PropertyString.Name) ?? siWcid.ToString();
                        var siOld = siWeenie.GetProperty(siProp);

                        try
                        {
                            // Upsert: plenty of weenies have no row for a given layer at all, so a
                            // bare UPDATE would silently affect 0 rows and report success.
                            var sql = siClear
                                ? $"DELETE FROM weenie_properties_d_i_d WHERE object_Id = {siWcid} AND type = {siType};"
                                : $"INSERT INTO weenie_properties_d_i_d (object_Id, type, value) VALUES ({siWcid}, {siType}, {siIcon}) "
                                  + $"ON DUPLICATE KEY UPDATE value = {siIcon};";

                            using (var ctx = new ACE.Database.Models.World.WorldDbContext())
                            {
                                ctx.Database.SetCommandTimeout(0);
                                ctx.Database.ExecuteSqlRaw(sql);
                            }

                            var siWhat = siClear ? "cleared" : siIcon.ToString(CultureInfo.InvariantCulture);
                            var siLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                $"zc_seticon_{DateTime.Now:yyyy-MM-dd}.sql");
                            System.IO.File.AppendAllText(siLog,
                                $"-- {DateTime.Now:HH:mm:ss} {siName} ({siWcid}) {siLayerArg}({siType}) {(siOld?.ToString() ?? "none")} -> {siWhat}{System.Environment.NewLine}{sql}{System.Environment.NewLine}");

                            ACE.Database.DatabaseManager.World.ClearCachedWeenie(siWcid);

                            Msg($"seticon: {siName} ({siWcid}) {siLayerArg} layer (type {siType}) {(siOld?.ToString() ?? "none")} -> {siWhat}. " +
                                "Weenie cache cleared; items ALREADY in a pack keep their old biota snapshot - trash and /ci a fresh one to see it. " +
                                $"Logged to zc_seticon_{DateTime.Now:yyyy-MM-dd}.sql");
                        }
                        catch (Exception ex)
                        {
                            Msg($"seticon failed: {ex.Message}");
                        }
                        return;
                    }

                    case "listparts":
                    {
                        // Inspect a mob's (or a raw Setup's) body-part layout: part index -> GfxObj model piece,
                        // plus any anim_part overrides baked on the weenie. Writes a full dump to zc_partsdump.txt
                        // (next to the server dll) + a chat summary. Step 1 of the per-part editor: reveals which
                        // index is the head (humanoid setups use index 16) and whether parts are isolable.
                        if (args.Count < 2) { Msg("Usage: listparts <wcid | 0xSetupId>"); return; }
                        uint setupId;
                        string label;
                        IList<PropertiesAnimPart> ovrList = null;
                        if (args[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                            uint.TryParse(args[1].Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sid) && (sid >> 24) == 0x02)
                        {
                            setupId = sid; label = $"setup 0x{sid:X8}";
                        }
                        else if (uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
                        {
                            var wpn = ACE.Database.DatabaseManager.World.GetCachedWeenie(w);
                            if (wpn == null) { Msg($"No weenie {w}."); return; }
                            setupId = wpn.GetProperty(PropertyDataId.Setup) ?? 0;
                            ovrList = wpn.PropertiesAnimPart;
                            label = $"{wpn.GetProperty(PropertyString.Name) ?? w.ToString()} (wcid {w})";
                        }
                        else { Msg("Give a wcid (number) or a 0x02...... Setup id."); return; }
                        if ((setupId >> 24) != 0x02) { Msg("That target has no valid Setup (0x02......)."); return; }

                        SetupModel setup;
                        try { setup = DatManager.PortalDat.ReadFromDat<SetupModel>(setupId); }
                        catch (Exception ex) { Msg($"Could not read Setup 0x{setupId:X8}: {ex.Message}"); return; }

                        var ovr = new Dictionary<int, uint>();
                        if (ovrList != null) foreach (var o in ovrList) ovr[o.Index] = o.AnimationId;

                        // Per-part placement positions (first usable placement frame) so we can spot the head =
                        // the highest (max-Z) real part, instead of assuming index 16 (which varies per setup).
                        var pos = (setup.PlacementFrames.Count > 0)
                            ? setup.PlacementFrames.Values
                                .FirstOrDefault(p => p.AnimFrame?.Frames != null && p.AnimFrame.Frames.Count >= setup.Parts.Count)
                                ?.AnimFrame.Frames.Select(f => f.Origin).ToList()
                            : null;
                        int headIdx = -1; float maxZ = float.NegativeInfinity;
                        if (pos != null)
                            for (int i = 0; i < setup.Parts.Count; i++)
                                if (setup.Parts[i] != 0x010001EC && pos[i].Z > maxZ) { maxZ = pos[i].Z; headIdx = i; }

                        var sbp = new StringBuilder();
                        sbp.Append("=== listparts ").Append(label).Append(" | setup 0x").Append(setupId.ToString("X8"))
                           .Append(" | ").Append(setup.Parts.Count).Append(" parts | ").Append(ovr.Count).Append(" overrides")
                           .Append(" | highest/likely-head = idx ").Append(headIdx).Append(" ===\n");
                        for (int i = 0; i < setup.Parts.Count; i++)
                        {
                            sbp.Append("  [").Append(i.ToString().PadLeft(2)).Append("] 0x").Append(setup.Parts[i].ToString("X8"));
                            if (ovr.TryGetValue(i, out var g)) sbp.Append("  OVR->0x").Append(g.ToString("X8"));
                            if (pos != null) sbp.Append("  pos(x").Append(pos[i].X.ToString("0.00")).Append(" y").Append(pos[i].Y.ToString("0.00")).Append(" z").Append(pos[i].Z.ToString("0.00")).Append(")");
                            if (setup.Parts[i] == 0x010001EC) sbp.Append("  [null]");
                            if (i == headIdx) sbp.Append("   <== HIGHEST (likely HEAD)");
                            sbp.Append('\n');
                        }
                        foreach (var kv in ovr)
                            if (kv.Key >= setup.Parts.Count)
                                sbp.Append("  [").Append(kv.Key.ToString().PadLeft(2)).Append("] (added by override) -> 0x").Append(kv.Value.ToString("X8")).Append('\n');

                        try
                        {
                            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zc_partsdump.txt");
                            System.IO.File.AppendAllText(path, sbp.ToString() + "\n");
                        }
                        catch { }

                        Msg($"{label}: setup 0x{setupId:X8}, {setup.Parts.Count} parts, {ovr.Count} overrides. " +
                            (headIdx >= 0 ? $"Highest/likely-head = idx {headIdx} (0x{setup.Parts[headIdx]:X8})." : "(no placement data - can't locate head.)") +
                            " Full dump -> zc_partsdump.txt");
                        return;
                    }

                    case "partsof":
                    {
                        // Plugin-facing: reply a mob's body-part layout for the per-part editor's slot list +
                        // "copy from mob" picker. [[ZCPARTS]]w=<wcid>|n=<name>|s=<setupHex>|h=<headIdx>|<i>=<gfxHex>|...
                        // "partsof <wcid>" (typed) = left/editing side; "partsof sel" = the in-game SELECTED mob,
                        // tagged role=src for the right/steal-from side.
                        if (args.Count < 2) { Msg("Usage: partsof <wcid | sel>"); return; }
                        uint pw; uint pSetup; string pName; string roleTag = "";
                        if (args[1].Equals("sel", StringComparison.OrdinalIgnoreCase))
                        {
                            var selp = session.Player?.SelectedTarget;
                            if (selp == null) { Msg("Nothing selected in-game (left-click a mob first)."); return; }
                            pw = selp.WeenieClassId;
                            pSetup = selp.GetProperty(PropertyDataId.Setup) ?? 0;
                            pName = selp.Name ?? pw.ToString();
                            // "partsof sel tgt" = the mob we're EDITING (left, zone-checked plugin-side); else = source (right).
                            roleTag = (args.Count >= 3 && args[2].Equals("tgt", StringComparison.OrdinalIgnoreCase)) ? "role=tgt|" : "role=src|";
                        }
                        else
                        {
                            if (!uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out pw)) { Msg("wcid must be a number or 'sel'."); return; }
                            var pwn = ACE.Database.DatabaseManager.World.GetCachedWeenie(pw);
                            if (pwn == null) { Msg($"No weenie {pw}."); return; }
                            pSetup = pwn.GetProperty(PropertyDataId.Setup) ?? 0;
                            pName = pwn.GetProperty(PropertyString.Name) ?? pw.ToString();
                        }
                        if ((pSetup >> 24) != 0x02) { Msg("That mob has no Setup (0x02......)."); return; }
                        SetupModel pModel;
                        try { pModel = DatManager.PortalDat.ReadFromDat<SetupModel>(pSetup); }
                        catch (Exception ex) { Msg($"Could not read Setup 0x{pSetup:X8}: {ex.Message}"); return; }
                        var pHead = SetupHeadIndex(pModel);
                        var pLabels = SetupPartLabels(pModel, pHead);
                        var psb = new StringBuilder("[[ZCPARTS]]").Append(roleTag).Append("w=").Append(pw)
                            .Append("|n=").Append(CleanWire(pName))
                            .Append("|s=").Append(pSetup.ToString("X8"))
                            .Append("|h=").Append(pHead);
                        for (int i = 0; i < pModel.Parts.Count; i++)
                            psb.Append('|').Append(i).Append('=').Append(pModel.Parts[i].ToString("X8")).Append('~').Append(pLabels[i]);
                        Msg(psb.ToString());
                        return;
                    }

                    case "findmob":
                    {
                        // Name/wcid search over creature weenies for the plugin's Copy-look mob picker.
                        // Replies [[ZCFIND]]q=<query>|<wcid>~<name>|... (creatures only, capped).
                        if (args.Count < 2) { Msg("Usage: findmob <text or wcid>"); return; }
                        var q = string.Join(" ", args.Skip(1)).Trim();
                        var sb = new StringBuilder("[[ZCFIND]]q=").Append(CleanWire(q));
                        var seen = new HashSet<uint>();

                        if (uint.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qWcid))
                        {
                            var byId = ACE.Database.DatabaseManager.World.GetCachedWeenie(qWcid);
                            if (byId != null && byId.WeenieType == WeenieType.Creature && seen.Add(qWcid))
                                sb.Append('|').Append(qWcid).Append('~').Append(CleanWire(byId.GetProperty(PropertyString.Name)));
                        }

                        if (q.Length > 0)
                        {
                            try
                            {
                                using var context = new WorldDbContext();
                                var rows = context.Weenie
                                    .Where(w => w.Type == (int)WeenieType.Creature)
                                    .Join(context.WeeniePropertiesString.Where(s => s.Type == (ushort)PropertyString.Name && s.Value.Contains(q)),
                                          w => w.ClassId, s => s.ObjectId, (w, s) => new { w.ClassId, s.Value })
                                    .Take(30).AsNoTracking().ToList();
                                foreach (var row in rows)
                                {
                                    if (!seen.Add(row.ClassId)) continue;
                                    sb.Append('|').Append(row.ClassId).Append('~').Append(CleanWire(row.Value));
                                }
                            }
                            catch (Exception) { /* search failed - reply with whatever already matched */ }
                        }
                        Msg(sb.ToString());
                        return;
                    }

                    case "selinfo":
                    {
                        // Report the admin's in-game SELECTED object for the plugin's "Copy selected" confirm popup:
                        // [[ZCSEL]]found=<0|1>|wcid=<n>|creature=<0|1>|name=<name>. Lets the plugin verify it's a real
                        // creature (not an item) before copying its look.
                        var sel = session.Player?.SelectedTarget;
                        var sbSel = new StringBuilder("[[ZCSEL]]found=").Append(sel != null ? 1 : 0);
                        if (sel != null)
                            sbSel.Append("|wcid=").Append(sel.WeenieClassId)
                                 .Append("|creature=").Append(sel is ACE.Server.WorldObjects.Creature ? 1 : 0)
                                 .Append("|name=").Append(CleanWire(sel.Name));
                        Msg(sbSel.ToString());
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
                                $"({area.Landblocks.Count} landblock(s), {(area.Enabled ? "ENABLED" : "disabled — stats off; the boundary enforces regardless")}).");
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
                              "Active now - the boundary enforces whether the zone is enabled or not."
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

                        // BUILD EVERY LINE FIRST, THEN SEND (2026-07-30). BuildQuestPayload reads the
                        // player's QuestManager (GetQuest / GetNextSolveTime) for `st=live` rows only, so
                        // the old build-and-send loop interleaved player-state lookups BETWEEN EnqueueSends.
                        // Exactly the live rows then escaped the plugin's chat interception and rendered in
                        // the player's chat box - deterministically, the same 9 of 24 every pull, while the
                        // survey payload (90 messages, no per-line lookups) and the single-message mob
                        // roster were always clean. Wire format is unchanged; the plugin needs no update.
                        var qi = 0;
                        var questLines = new List<string>(quests.Count);
                        foreach (var q in quests)
                            questLines.Add(BuildQuestPayload(name, q, ++qi, session));
                        foreach (var line in questLines)
                            Msg(line);
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
                            $"{(wcid.HasValue ? " [wcid " + wcid.Value + "]" : " [zone]")}:");
                        if (eval == null || eval.Values.Count == 0) { Msg("  (no stats set)"); return; }
                        // Layered values with PROVENANCE, so it is obvious which layer each number came from.
                        foreach (var kv in eval.Values.OrderBy(k => k.Key))
                        {
                            var src = ZoneControlManager.ResolveStatSource(name, wcid, kv.Key);
                            Msg($"    {kv.Key,-22} = {kv.Value,-12:0.####} ({src ?? "?"})");
                        }
                        return;
                    }

                    case "defaultget":
                    {
                        // Machine payload for the plugin's Defaults editor (owner 2026-08-11). READ ONLY -
                        // every write still goes through the 'default set|clearstat|copyfrom|clear'
                        // verbs below. Wire shape matches [[ZC]] ("<stat>=<defined>,<value>") so the plugin
                        // reuses its zone-grid parser.
                        if (args.Count < 2 || !int.TryParse(args[1].TrimStart('v', 'V'), out var getVar) || getVar < 0)
                        { Msg("Usage: defaultget <variation>"); return; }
                        Msg(BuildVariationDefaultPayload(getVar));
                        return;
                    }

                    case "default":
                    {
                        // Per-variation Default layer (2026-07-30). Every zone at <var> inherits these, per
                        // stat; a zone or per-WCID value overrides just that stat. Progression across v11-v25
                        // is 15 of these, all explicitly authored — the server never derives one.
                        if (args.Count < 2)
                        {
                            Msg("Usage: default <variation> <show|set|clearstat|copyfrom|clear|list>");
                            Msg("  default list                              variations that have a Default");
                            Msg("  default <var> show                        the Default's stats");
                            Msg("  default <var> set <stat> <value>");
                            Msg("  default <var> clearstat <stat>");
                            Msg("  default <var> copyfrom <var>              seed from another variation");
                            Msg("  default <var> clear                       drop the whole Default");
                            return;
                        }

                        if (args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                        {
                            var vars = ZoneControlManager.ListVariationDefaults();
                            if (vars.Count == 0) { Msg("(no variation Defaults authored)"); return; }
                            foreach (var v in vars)
                            {
                                var d = ZoneControlManager.GetVariationDefault(v);
                                Msg($"  v{v,-4} stats:{d?.Profile?.Stats?.Count ?? 0}  " +
                                    $"effects:{(d?.Effects != null && !d.Effects.IsEmpty ? "yes" : "no")}  " +
                                    $"appearance:{(d?.Appearance != null && !d.Appearance.IsEmpty ? "yes" : "no")}");
                            }
                            return;
                        }

                        if (!int.TryParse(args[1].TrimStart('v', 'V'), out var dvar))
                        { Msg("variation must be a number, e.g. 'default 11 show'."); return; }
                        if (dvar < 0)
                        { Msg("variation must be >= 0 (negative variations are rift instances and never inherit a Default)."); return; }

                        var dop = args.Count >= 3 ? args[2].ToLowerInvariant() : "show";

                        if (dop == "show")
                        {
                            var d = ZoneControlManager.GetVariationDefault(dvar);
                            if (d == null) { Msg($"v{dvar} has no Default."); return; }
                            var stats = d.Profile?.Stats;
                            Msg($"Default v{dvar}:");
                            if (stats == null || stats.Count == 0) Msg("  (no stats set)");
                            else
                                foreach (var kv in stats.OrderBy(k => k.Key))
                                    Msg($"    {kv.Key,-22} = {kv.Value.Base:0.####}");
                            if (d.Effects != null && !d.Effects.IsEmpty) Msg($"  effects: {DescribeDot(d.Effects)}");
                            return;
                        }

                        if (dop == "clear")
                        {
                            Msg(ZoneControlManager.ClearVariationDefault(dvar)
                                ? $"Default v{dvar} cleared. Zones at v{dvar} fall back to their own stats only."
                                : $"v{dvar} had no Default.");
                            return;
                        }

                        if (dop == "copyfrom")
                        {
                            if (args.Count < 4 || !int.TryParse(args[3].TrimStart('v', 'V'), out var srcVar))
                            { Msg("Usage: default <var> copyfrom <var>"); return; }
                            Msg(ZoneControlManager.CopyVariationDefault(srcVar, dvar)
                                ? $"Default v{dvar} seeded from v{srcVar} (deep copy - edit either independently)."
                                : $"v{srcVar} has no Default to copy.");
                            return;
                        }

                        if (dop == "set")
                        {
                            if (args.Count < 5) { Msg("Usage: default <var> set <stat> <value>"); return; }
                            var dstat = NormalizeStat(args[3]);
                            if (dstat == null) { Msg("Unknown stat. Stats: " + string.Join(", ", ZoneStat.All)); return; }
                            if (!TryDouble(args[4], out var dval)) { Msg("value must be a number."); return; }

                            ZoneControlManager.MutateVariationDefault(dvar, d =>
                                d.Profile.Stats[dstat] = new StatCurve { Base = dval, Growth = 1.0, Additive = false });
                            Msg($"Default v{dvar} {dstat} = {FmtStatEcho(dval)}. Every zone at v{dvar} that doesn't set it inherits this.");
                            if (dstat == ZoneStat.CoreAnchorDr || dstat == ZoneStat.CoreAnchorCdr)
                                AutoApplyForDefault(session, true, dvar, Msg);
                            return;
                        }

                        if (dop == "clearstat")
                        {
                            if (args.Count < 4) { Msg("Usage: default <var> clearstat <stat>"); return; }
                            var dstat = NormalizeStat(args[3]);
                            if (dstat == null) { Msg("Unknown stat."); return; }
                            var dremoved = false;
                            ZoneControlManager.MutateVariationDefault(dvar, d => dremoved = d.Profile.Stats.Remove(dstat));
                            Msg(dremoved ? $"Default v{dvar} {dstat} cleared." : "That stat wasn't set on the Default.");
                            if (dremoved && (dstat == ZoneStat.CoreAnchorDr || dstat == ZoneStat.CoreAnchorCdr))
                                AutoApplyForDefault(session, true, dvar, Msg);
                            return;
                        }

                        Msg("op must be show | set | clearstat | copyfrom | clear | list");
                        return;
                    }

                    case "effect":
                    {
                        if (args.Count < 2) { Msg("Usage: effect <name> [show | dot on|off | dmg <amount> | type <fire|cold|acid|electric|nether|stamina|mana|health|percent> | interval <seconds> | suppress on|off | suppress prodigal on|off | suppress regen <pct 0-100>]"); return; }
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
                            case "suppress":
                            {
                                if (args.Count < 4) { Msg("Usage: effect <name> suppress on|off | suppress prodigal on|off | suppress regen <pct 0-100>"); return; }
                                var supField = args[3].ToLowerInvariant();
                                if (supField == "on" || supField == "off" || supField == "1" || supField == "0" || supField == "true" || supField == "false")
                                {
                                    var supOn = supField == "on" || supField == "1" || supField == "true";
                                    apply = e => e.SuppressEnabled = supOn;
                                }
                                else if (supField == "prodigal")
                                {
                                    if (args.Count < 5) { Msg("Usage: effect <name> suppress prodigal on|off"); return; }
                                    var prodOn = args[4].Equals("on", StringComparison.OrdinalIgnoreCase) || args[4] == "1"
                                                 || args[4].Equals("true", StringComparison.OrdinalIgnoreCase);
                                    apply = e => e.SuppressProdigal = prodOn;
                                }
                                else if (supField == "regen")
                                {
                                    if (args.Count < 5 || !TryDouble(args[4], out var pct) || pct < 0 || pct > 100)
                                    { Msg("regen must be a percent 0-100 (100 = normal regen, 0 = no regen)."); return; }
                                    var mult = pct / 100.0;
                                    apply = e => e.SuppressRegenMult = mult;
                                }
                                else { Msg("Usage: effect <name> suppress on|off | suppress prodigal on|off | suppress regen <pct 0-100>"); return; }
                                break;
                            }
                            default:
                                Msg("Unknown effect field. Use: dot on|off | dmg <amount> | type <name|percent> | interval <seconds> | suppress ... | show");
                                return;
                        }

                        ZoneControlManager.MutateArea(name, a => { a.Effects ??= new ZoneEffects(); apply(a.Effects); });
                        var updated = ZoneControlManager.GetArea(name);
                        Msg($"'{name}' effects: {DescribeDot(updated?.Effects ?? new ZoneEffects())}." +
                            $"{(area.Enabled ? "" : " Zone still DISABLED - /zonecontrol enable " + name)}");
                        return;
                    }

                    case "ladder":
                    {
                        // Live stat resolution (2026-08-22): status | apply [tier|all] | migrate [here|<player>] [--dry] | show
                        HandleLadder(session, args, Msg);
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
        // Reference "Test Dummy" weenie used to fill the effective-look table when no specific mob is targeted
        // (so the plugin shows a real reference look instead of a column of N/A). 99999099 = "Target Dummy".
        private const uint ZoneControlDummyWcid = 99999099;

        /// <summary>"[[ZCD]]var=..|found=..|&lt;stat&gt;=defined,value|.." - one variation Default's stats,
        /// in the same wire shape as [[ZC]] so the plugin's grid parser is shared. Stats only: a Default
        /// also carries Effects/Appearance, which the editor does not author (owner 2026-08-11).</summary>
        /// <summary>"|sess=..." — session-state flags for the GM Tools On/Off highlight, server truth.
        /// Fixed order: adminvision, attackable, unkillable, cloak (any cloaked state), portal bypass.
        /// Shared by the zone payload and the session-only payload so the two can never drift.</summary>
        private static void AppendSessionState(StringBuilder sb, Session session)
        {
            var sp = session?.Player;
            if (sp != null)
                sb.Append("|sess=").Append(sp.Adminvision ? 1 : 0).Append(',')
                  .Append(sp.Attackable ? 1 : 0).Append(',')
                  .Append(sp.IsUnkillable ? 1 : 0).Append(',')
                  .Append(sp.CloakStatus != CloakStatus.Off ? 1 : 0).Append(',')
                  .Append(sp.IgnorePortalRestrictions ? 1 : 0);
        }

        /// <summary>"|combatdefs=..." — live combat-rule bool states so the plugin's GM Tools toggles
        /// show truth. Fixed order: missile_power_bar, zonecontrol_enabled. APPEND-ONLY: the plugin
        /// indexes positionally.</summary>
        private static void AppendCombatDefs(StringBuilder sb)
        {
            sb.Append("|combatdefs=")
              .Append(ServerConfig.missile_power_bar.Value ? '1' : '0').Append(',')
              .Append(ServerConfig.zonecontrol_enabled.Value ? '1' : '0');
        }

        /// <summary>"[[ZCSESS]]|sess=..|combatdefs=.." — the GM Tools state alone, for a bare
        /// "/zonecontrol get" with no zone name. Session flags and shard-combat rules are not zone
        /// state, so the plugin must be able to fetch them with no Zone loaded (owner 2026-08-17:
        /// the GM Tools toggles sat on "--" until a zone sync happened to run).</summary>
        private static string BuildSessionPayload(Session session)
        {
            var sb = new StringBuilder("[[ZCSESS]]");
            AppendSessionState(sb, session);
            AppendCombatDefs(sb);
            AppendLadder(sb);   // APPEND-ONLY (2026-08-22): ladder apply versions, last so older plugins ignore it
            return sb.ToString();
        }

        /// <summary>"|ladder=tier:version:allowNerf:yyyy-MM-dd;..." - the per-tier ladder apply state
        /// (live stat resolution, 2026-08-22). Sparse: only tiers with version > 0; the plugin shows tiers
        /// 11-25 and treats a missing tier as v0 / never applied. Rebuilt on every sync. APPEND-ONLY tag,
        /// emitted LAST in both [[ZC]] and [[ZCSESS]] so it works zoneless and old plugins ignore it.</summary>
        /// <summary>
        /// `ladder apply` proper: bump the per-tier version (null = 11..25), then re-stamp every ONLINE
        /// player's worn pieces now (bounded 18 per player, on each player's own action chain so it never
        /// races combat). Packed items, mules and offline characters stay lazy (equip / login). Also called
        /// by the Default-layer band / core-anchor editors (owner 2026-08-23: "just ladder apply on save").
        /// Always follows the ladder BOTH ways - the raise-only guard was dropped the same day.
        /// </summary>
        private static void LadderApplyNow(Session session, int? tier, Action<string> Msg)
        {
            var by = session?.Player?.Name ?? "console";
            var bumped = ZoneControlManager.BumpLadder(tier, true, by);
            if (bumped.Count == 0) { Msg("Nothing bumped."); return; }

            var parts = bumped.Select(t => $"T{t}->v{ZoneControlManager.GetLadderVersion(t).Version}");
            Msg($"Ladder applied by {by}: {string.Join(", ", parts)}");

            var online = PlayerManager.GetAllOnline();
            foreach (var op in online)
            {
                var target = op;
                var chain = new ACE.Server.Entity.Actions.ActionChain();
                chain.AddAction(target, ACE.Server.Entity.Actions.ActionType.ZoneControl_LadderReresolve, () =>
                {
                    var n = target.ReresolveWornZoneGear();
                    if (n > 0)
                        target.Session?.Network?.EnqueueSend(new ACE.Server.Network.GameMessages.Messages.GameMessageSystemChat(
                            $"Zone Control: {n} worn piece(s) re-resolved against the live ladder.", ChatMessageType.Broadcast));
                });
                chain.EnqueueChain();
            }
            Msg($"Online players re-resolving worn gear: {online.Count}; everyone else catches up at login / equip.");
        }

        /// <summary>Default-layer edits auto-apply for their tier (owner 2026-08-23). Zone-scoped band edits do
        /// NOT: the resolver reads the tier Default, so a zone band only shapes NEW drops in that zone.</summary>
        private static void AutoApplyForDefault(Session session, bool isDefaultScope, int defaultVar, Action<string> Msg)
        {
            if (!isDefaultScope) { Msg("  (zone band: new drops in this zone only - existing gear follows the tier Default)"); return; }
            if (defaultVar < 11 || defaultVar > 25) return;
            LadderApplyNow(session, defaultVar, Msg);
        }

        /// <summary>
        /// `ladder bench [n]` (owner 2026-08-23: "nothing has ever been tested at scale"): in-process timing of
        /// the hot paths. Mints ONE T11 armor piece and ONE T11 melee weapon through the real producers (so they
        /// carry a record / a quality stamp), then times n iterations of: armor Compute (the appraisal cost),
        /// armor Compute+Apply (the equip / ladder-apply cost, no DB), weapon TryResolve x3 (one swing's worth of
        /// WeaponScalingCombat lookups). Single thread, so it measures CPU per op - not lock contention.
        /// Both objects are destroyed afterwards; nothing is saved.
        /// </summary>
        private static void LadderBench(Session session, List<string> args, Action<string> Msg)
        {
            var n = 10000;
            if (args.Count >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nn) && nn > 0)
                n = Math.Min(nn, 1_000_000);

            var player = session?.Player;
            var p = player != null ? ZoneControlManager.ResolveZoneDefaultForPlayer(player) : null;

            // armor: Iron Celdon Breastplate; weapon: Iron Spada (same wcids the plugin Preview uses)
            var armor = ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject("breastplatecelodoniron")
                     ?? ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject(37);
            var weapon = ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject("swordspada")
                      ?? ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject(30571);
            if (armor == null || weapon == null) { Msg("bench: could not create the sample items."); return; }
            if (!(weapon is ACE.Server.WorldObjects.MeleeWeapon)) { Msg($"bench: '{weapon.Name}' is not a melee weapon - weapon timings would be the bail-out path."); armor.Destroy(); weapon.Destroy(); return; }

            try
            {
                ACE.Server.Factories.LootGenerationFactory.ApplyT11GearStats(armor, 11, forceMax: false, p: p);
                if (p != null) ZoneLootMutator.MutateLootItem(armor, p, null, 11);
                // guarantee at least one graded line on the piece regardless of the zone's roll
                if (ZoneCantrips.TryGet(28, out var dr))
                    ZoneCantrips.StampGraded(armor, dr, 500, ZoneStatResolver.EffectiveBand(28, 11));
                ACE.Server.Factories.LootGenerationFactory.ApplyWeaponAugScaleStamp(weapon, 11);

                var rec = ZoneStatResolver.Read(armor).Count;
                Msg($"bench: armor '{armor.Name}' record {rec} entries (\"{armor.GetProperty(PropertyString.ZcLines)}\"), weapon '{weapon.Name}' quality {weapon.GetProperty(PropertyInt.WeaponAugScaleQuality)}; n = {n:N0}");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < n; i++) ZoneStatResolver.Compute(armor);
                sw.Stop();
                Msg($"  armor Compute (appraisal):        {sw.Elapsed.TotalMilliseconds * 1000.0 / n,8:0.00} us/op  ({sw.ElapsedMilliseconds} ms total)");

                sw.Restart();
                for (var i = 0; i < n; i++) ZoneStatResolver.Apply(armor, ZoneStatResolver.Compute(armor));
                sw.Stop();
                var equipUs = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
                Msg($"  armor Compute+Apply (equip):      {equipUs,8:0.00} us/op  ({sw.ElapsedMilliseconds} ms total, no DB)");

                sw.Restart();
                for (var i = 0; i < n; i++) ZoneStatResolver.ApplyIfStale(armor);
                sw.Stop();
                Msg($"  armor ApplyIfStale (login, current): {sw.Elapsed.TotalMilliseconds * 1000.0 / n,5:0.00} us/op  (the no-op path every login takes)");

                var sink = 0f;
                sw.Restart();
                for (var i = 0; i < n; i++)
                {
                    sink += ACE.Server.Managers.WeaponScaling.WeaponScalingCombat.GetFlatBonus(weapon, player);
                    sink += ACE.Server.Managers.WeaponScaling.WeaponScalingCombat.GetCritDamageBonus(weapon, player);
                    ACE.Server.Managers.WeaponScaling.WeaponScalingCombat.TryGetEffectiveVariance(weapon, out var v); sink += (float)v;
                }
                sw.Stop();
                Msg($"  weapon swing (3 resolves):        {sw.Elapsed.TotalMilliseconds * 1000.0 / n,8:0.00} us/swing  ({sw.ElapsedMilliseconds} ms total, sink {sink:0})");

                var hits = 400 * 2;
                Msg($"  at 400 players x 2 hits/s: weapons ~{sw.Elapsed.TotalMilliseconds / n * hits:0.0} ms CPU per second; one ladder apply = {400 * 18:N0} armor resolves ~{equipUs * 400 * 18 / 1000.0:0.0} ms CPU total.");
            }
            finally
            {
                armor.Destroy();
                weapon.Destroy();
            }
        }

        /// <summary>ctspoff=key,key,... - the specials turned OFF at this (evaluated) scope. Sparse; absent = all on.
        /// APPEND-ONLY tag (2026-08-23).</summary>
        private static void AppendSpecialsOff(StringBuilder sb, Dictionary<int, bool> toggles)
        {
            if (toggles == null || toggles.Count == 0) return;
            var off = toggles.Where(kv => !kv.Value).Select(kv => kv.Key).OrderBy(k => k).ToList();
            if (off.Count == 0) return;
            sb.Append("|ctspoff=").Append(string.Join(",", off));
        }

        private static void AppendLadder(StringBuilder sb)
        {
            sb.Append("|ladder=");
            bool first = true;
            foreach (var kv in ZoneControlManager.ListLadderVersions())
            {
                var la = kv.Value;
                if (la == null || la.Version <= 0) continue;
                if (!first) sb.Append(';');
                first = false;
                sb.Append(kv.Key).Append(':').Append(la.Version).Append(':').Append(la.AllowNerf ? 1 : 0)
                  .Append(':').Append(la.AppliedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }

        private static string BuildVariationDefaultPayload(int variation)
        {
            var d = ZoneControlManager.GetVariationDefault(variation);
            var stats = d?.Profile?.Stats;

            var sb = new StringBuilder();
            sb.Append("[[ZCD]]var=").Append(variation)
              .Append("|found=").Append(d != null ? 1 : 0);

            // SPARSE since 2026-08-23: only DEFINED stats ride the wire. The full 192-row form was ~4 KB per
            // reply and each such chat line stalls the client ~0.5 s (tier stepper lock-up). The plugin
            // marks every row of the variation undefined before applying, so an absent key = cleared.
            if (stats != null)
                foreach (var stat in ZoneStat.All)
                    if (stats.TryGetValue(stat, out var curve) && curve != null)
                        sb.Append('|').Append(stat).Append("=1,").Append(curve.Base.ToString(CultureInfo.InvariantCulture));

            // APPEND-ONLY (2026-08-23): this Default's OWN authored bands + slot rules, same shapes as the
            // [[ZC]] sync, so the plugin Catalog at "Default v[N]" scope shows N's bands instead of the last
            // zone's. Sparse: absent keys fall back to the tier-scaled hardcoded band.
            var vp = d?.Profile;
            if (vp?.CustomCantripBands is { Count: > 0 })
            {
                sb.Append("|cantrips=");
                bool firstCb = true;
                foreach (var b in vp.CustomCantripBands)
                {
                    if (b.Value == null) continue;
                    if (!firstCb) sb.Append(';');
                    firstCb = false;
                    sb.Append(b.Key).Append(':').Append(b.Value.Min).Append(':').Append(b.Value.Max)
                      .Append(':').Append(b.Value.ProcMin).Append(':').Append(b.Value.ProcMax);
                }
            }
            if (vp?.CustomCantripSlots is { Count: > 0 })
            {
                sb.Append("|ctslots=");
                bool firstSl = true;
                foreach (var kv in vp.CustomCantripSlots)
                {
                    if (!firstSl) sb.Append(';');
                    firstSl = false;
                    sb.Append(kv.Key).Append(':').Append(kv.Value);
                }
            }
            AppendSpecialsOff(sb, vp?.CustomSpecials);
            return sb.ToString();
        }

        /// <summary>Stat echo in SHORT form (owner 2026-08-23): big numbers read as 100M / 5B, everything
        /// else as the plain value. Display only - the store keeps the exact double.</summary>
        private static string FmtStatEcho(double v)
        {
            var a = Math.Abs(v);
            if (a >= 1_000_000_000 && v % 1_000_000 == 0) return (v / 1_000_000_000).ToString("0.###", CultureInfo.InvariantCulture) + "B";
            if (a >= 1_000_000 && v % 1_000 == 0) return (v / 1_000_000).ToString("0.###", CultureInfo.InvariantCulture) + "M";
            if (a >= 10_000 && v % 1_000 == 0) return (v / 1_000).ToString("0.###", CultureInfo.InvariantCulture) + "K";
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string BuildZonePayload(string name, uint? wcid, Session session)
        {
            var area = ZoneControlManager.GetArea(name);
            // LAYERED view (2026-07-30): VariationDefault -> zone -> wcid, so the plugin shows the value combat
            // actually uses rather than only what this bucket authors. Wire shape is unchanged (still
            // "<stat>=<defined>,<value>") — provenance is a Phase 3 change made together with the plugin.
            var vp = ZoneControlManager.ResolveProfileForDisplay(name, wcid);

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

            // Live server-wide relief-curve defaults (v11_relief_* config, /modify-tunable) so the
            // plugin's Curves tab hints/graphs/simulator never drift from what combat actually uses
            // when a zone doesn't author its own anchors. Fixed order: aug s,m,c,b | dr | critdr.
            sb.Append("|reliefdefs=")
              .Append(ServerConfig.v11_relief_aug_start.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_aug_max.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_aug_cap.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_aug_bend.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_dr_start.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_dr_max.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_dr_cap.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_dr_bend.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_critdr_start.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_critdr_max.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_critdr_cap.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_relief_critdr_bend.Value.ToString(CultureInfo.InvariantCulture));

            // Live shard-wide tuning defaults for the plugin's Curves Server-defaults view
            // (owner-approved 2026-07-28). Fixed order: pcthp variance, pcthp crit mult,
            // vuln effectiveness, vuln cap, vuln enabled (1/0).
            sb.Append("|tunedefs=")
              .Append(ServerConfig.v11_pcthp_variance.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_pcthp_crit_mult.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_vuln_effectiveness.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_vuln_cap.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(ServerConfig.v11_vuln_enabled.Value ? '1' : '0');

            // Live diagnostics-bool states so the plugin's Log-section toggles show truth.
            // Fixed order: damage_event_debug_server_log, damage_event_debug_only_nonplayer_attackers,
            // spawn_diag_verbose, generator_diag_verbose, visibility_create_object_diag_verbose,
            // regen_diag_verbose. APPEND-ONLY: the plugin indexes this list positionally.
            sb.Append("|diagdefs=")
              .Append(ServerConfig.damage_event_debug_server_log.Value ? '1' : '0').Append(',')
              .Append(ServerConfig.damage_event_debug_only_nonplayer_attackers.Value ? '1' : '0').Append(',')
              .Append(ServerConfig.spawn_diag_verbose.Value ? '1' : '0').Append(',')
              .Append(ServerConfig.generator_diag_verbose.Value ? '1' : '0').Append(',')
              .Append(ServerConfig.visibility_create_object_diag_verbose.Value ? '1' : '0').Append(',')
              .Append(ServerConfig.regen_diag_verbose.Value ? '1' : '0');

            AppendCombatDefs(sb);

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

            // Appearance overrides (cosmetic layer, separate from props/stats): ap_<field>=<value>. Sparse — the
            // selected bucket's OWN fields (the zone default with no --wcid, else that WCID's overlay), so the
            // plugin's Set/Clear act on exactly what it shows.
            var apVp = area == null ? null : (wcid.HasValue
                ? (area.AppearanceByWcid != null && area.AppearanceByWcid.TryGetValue(wcid.Value, out var apw) ? apw : null)
                : area.AppearanceDefault);
            if (apVp != null)
            {
                if (!string.IsNullOrEmpty(apVp.Name)) sb.Append("|ap_name=").Append(CleanWire(apVp.Name));
                if (apVp.PaletteTemplate.HasValue) sb.Append("|ap_palette=").Append(apVp.PaletteTemplate.Value.ToString(CultureInfo.InvariantCulture));
                if (apVp.Shade.HasValue) sb.Append("|ap_shade=").Append(apVp.Shade.Value.ToString(CultureInfo.InvariantCulture));
                if (apVp.Scale.HasValue) sb.Append("|ap_scale=").Append(apVp.Scale.Value.ToString(CultureInfo.InvariantCulture));
                if (apVp.Translucency.HasValue) sb.Append("|ap_trans=").Append(apVp.Translucency.Value.ToString(CultureInfo.InvariantCulture));
                if (apVp.Shiny.HasValue) sb.Append("|ap_shiny=").Append(apVp.Shiny.Value ? 1 : 0);
                if (apVp.SetupTableId.HasValue) sb.Append("|ap_setup=").Append(apVp.SetupTableId.Value.ToString("X8"));
                if (apVp.MotionTable.HasValue) sb.Append("|ap_motion=").Append(apVp.MotionTable.Value.ToString("X8"));
                if (apVp.SoundTable.HasValue) sb.Append("|ap_sound=").Append(apVp.SoundTable.Value.ToString("X8"));
                if (apVp.PaletteBase.HasValue) sb.Append("|ap_palbase=").Append(apVp.PaletteBase.Value.ToString("X8"));
                if (apVp.ClothingBase.HasValue) sb.Append("|ap_clothing=").Append(apVp.ClothingBase.Value.ToString("X8"));
                if (apVp.Icon.HasValue) sb.Append("|ap_icon=").Append(apVp.Icon.Value.ToString("X8"));
                if (apVp.PartCount > 0) sb.Append("|ap_parts=").Append(apVp.PartCount.ToString(CultureInfo.InvariantCulture));
                if (apVp.AnimParts != null && apVp.AnimParts.Count > 0)
                {
                    sb.Append("|ap_partlist=");
                    for (int i = 0; i < apVp.AnimParts.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(apVp.AnimParts[i].Index).Append(':').Append(apVp.AnimParts[i].GfxObj.ToString("X8"));
                    }
                }
            }

            // Effective look: the target's ACTUAL resolved value + source for every lever, so the plugin shows what
            // it looks like even with nothing overridden. WITH a specific mob: self = this WCID's override, zone =
            // the zone default, stock = the mob's own weenie. With NO target: self = the zone default (the bucket
            // being edited) and a reference "Test Dummy" weenie stands in for stock (tagged 'dummy'), so the table
            // is never a wall of N/A. eff_<field>=<value>~<self|zone|stock|dummy>.
            if (area != null)
            {
                bool hasMob = wcid.HasValue;
                uint refWcid = wcid ?? ZoneControlDummyWcid;
                var apDef = area.AppearanceDefault;
                var apW = (hasMob && area.AppearanceByWcid != null && area.AppearanceByWcid.TryGetValue(wcid.Value, out var apw2)) ? apw2 : null;
                var selfAp = hasMob ? apW : apDef;
                var zoneAp = hasMob ? apDef : null;
                var wpn = ACE.Database.DatabaseManager.World.GetCachedWeenie(refWcid);
                string stockSrc = hasMob ? "stock" : "dummy";

                void Eff(string key, string self, string zone, string stock)
                {
                    string val, src;
                    if (self != null) { val = self; src = "self"; }
                    else if (zone != null) { val = zone; src = "zone"; }
                    else if (stock != null) { val = stock; src = stockSrc; }
                    else return;
                    sb.Append("|eff_").Append(key).Append('=').Append(val).Append('~').Append(src);
                }
                string I(int? v) => v?.ToString(CultureInfo.InvariantCulture);
                string F(double? v) => v?.ToString(CultureInfo.InvariantCulture);
                string D(uint? v) => v?.ToString("X8");
                string B(bool? v) => v.HasValue ? (v.Value ? "1" : "0") : null;
                string Bi(int? v) => v.HasValue ? (v.Value != 0 ? "1" : "0") : null;

                string N(string v) => string.IsNullOrEmpty(v) ? null : CleanWire(v);
                Eff("name",     N(selfAp?.Name),            N(zoneAp?.Name),            N(wpn?.GetProperty(PropertyString.Name)));
                Eff("palette",  I(selfAp?.PaletteTemplate), I(zoneAp?.PaletteTemplate), I(wpn?.GetProperty(PropertyInt.PaletteTemplate)));
                Eff("shade",    F(selfAp?.Shade),           F(zoneAp?.Shade),           F(wpn?.GetProperty(PropertyFloat.Shade)));
                Eff("scale",    F(selfAp?.Scale),           F(zoneAp?.Scale),           F(wpn?.GetProperty(PropertyFloat.DefaultScale)) ?? "1");
                Eff("trans",    F(selfAp?.Translucency),    F(zoneAp?.Translucency),    F(wpn?.GetProperty(PropertyFloat.Translucency)) ?? "0");
                Eff("shiny",    B(selfAp?.Shiny),           B(zoneAp?.Shiny),           Bi(wpn?.GetProperty(PropertyInt.CreatureVariant)) ?? "0");
                Eff("setup",    D(selfAp?.SetupTableId),    D(zoneAp?.SetupTableId),    D(wpn?.GetProperty(PropertyDataId.Setup)));
                Eff("clothing", D(selfAp?.ClothingBase),    D(zoneAp?.ClothingBase),    D(wpn?.GetProperty(PropertyDataId.ClothingBase)));
                Eff("palbase",  D(selfAp?.PaletteBase),     D(zoneAp?.PaletteBase),     D(wpn?.GetProperty(PropertyDataId.PaletteBase)));
                Eff("motion",   D(selfAp?.MotionTable),     D(zoneAp?.MotionTable),     D(wpn?.GetProperty(PropertyDataId.MotionTable)));
                Eff("sound",    D(selfAp?.SoundTable),      D(zoneAp?.SoundTable),      D(wpn?.GetProperty(PropertyDataId.SoundTable)));
                Eff("icon",     D(selfAp?.Icon),            D(zoneAp?.Icon),            D(wpn?.GetProperty(PropertyDataId.Icon)));

                // Body parts: count of per-part overrides (anim + texture). self/zone come from the appearance
                // layer; stock = the reference weenie's own baked parts (Tusgian-style mobs). Always emit a stock
                // count (even 0) so the row always shows.
                string PC(ZoneAppearance a) => a != null && a.PartCount > 0 ? a.PartCount.ToString(CultureInfo.InvariantCulture) : null;
                int stockParts = (wpn?.PropertiesAnimPart?.Count ?? 0) + (wpn?.PropertiesTextureMap?.Count ?? 0);
                Eff("parts", PC(selfAp), PC(zoneAp), stockParts.ToString(CultureInfo.InvariantCulture));
            }

            // Capability hint: does the TARGET mob have a usable ClothingBase (so PaletteTemplate/Shade can recolor
            // it)? Only meaningful for a specific --wcid; omitted for "all monsters" (capability varies per mob).
            if (wcid.HasValue)
            {
                bool hasClothing;
                if (apVp?.ClothingBase.HasValue == true)
                    hasClothing = true;                                   // an override supplies one
                else if (apVp?.SetupTableId.HasValue == true)
                    hasClothing = false;                                  // model swapped -> base clothing won't match
                else
                    hasClothing = ACE.Database.DatabaseManager.World.GetCachedWeenie(wcid.Value)?.GetProperty(PropertyDataId.ClothingBase) != null;
                sb.Append("|cap_clothing=").Append(hasClothing ? 1 : 0);
            }

            // Custom cantrip pool (for the plugin's Loot cards).
            if (vp?.CustomCantrips is { Count: > 0 })
                sb.Append("|cantrips=").Append(string.Join(",", vp.CustomCantrips));

            // Banded cantrips (sparse, rebuilt each sync like the pool): the EVALUATED/merged view —
            // vp is ResolveProfileForDisplay, so Default-layer bands sync too; a key absent here
            // rolls the catalog band. cantrips=<key>:<min>:<max>:<procMin>:<procMax>;...
            // Entries carry ':' / ';' so the parser can tell them from the legacy comma pool list above.
            if (vp?.CustomCantripBands is { Count: > 0 })
            {
                sb.Append("|cantrips=");
                bool firstCb = true;
                foreach (var b in vp.CustomCantripBands)
                {
                    if (b.Value == null) continue;
                    if (!firstCb) sb.Append(';');
                    firstCb = false;
                    sb.Append(b.Key).Append(':').Append(b.Value.Min).Append(':').Append(b.Value.Max)
                      .Append(':').Append(b.Value.ProcMin).Append(':').Append(b.Value.ProcMax);
                }
            }

            // Slot rules (sparse, EVALUATED view like the bands): ctslots=<key>:<mask>;... - only authored keys.
            // APPEND-ONLY tag (2026-08-22); old plugins ignore it.
            if (vp?.CustomCantripSlots is { Count: > 0 })
            {
                sb.Append("|ctslots=");
                bool firstSl = true;
                foreach (var kv in vp.CustomCantripSlots)
                {
                    if (!firstSl) sb.Append(';');
                    firstSl = false;
                    sb.Append(kv.Key).Append(':').Append(kv.Value);
                }
            }

            AppendSpecialsOff(sb, vp?.CustomSpecials);

            // Spell-book rules (sparse, rebuilt each sync): sprules=id~disabled~chancePct,...
            // chance blank = book default (or 2 for added spells).
            if (vp?.SpellRules is { Count: > 0 })
            {
                sb.Append("|sprules=");
                bool firstSr = true;
                foreach (var r in vp.SpellRules)
                {
                    if (r == null || r.SpellId == 0) continue;
                    if (!firstSr) sb.Append(',');
                    firstSr = false;
                    sb.Append(r.SpellId).Append('~').Append(r.Disabled ? 1 : 0).Append('~')
                      .Append(r.Chance.HasValue ? r.Chance.Value.ToString(CultureInfo.InvariantCulture) : "");
                }
            }

            // Currency drop table: curr=wcid~amount~chance~direct~name~own,... (sparse; rebuilt each sync
            // like cantrips=). Name is display-only for the plugin; sanitized of the wire's separator chars.
            // OWN (field 6, added 2026-08-24) = 1 when this drop is authored in the CURRENTLY TARGETED
            // bucket, 0 when it is inherited from a broader layer. vp is the MERGED view, so without this
            // the plugin cannot tell an inherited row from one this monster owns - and `currency remove`
            // only ever touches the targeted bucket, so deleting an inherited row is a silent no-op.
            // Deliberately the same bucket the remove verb resolves, so the badge cannot disagree with it.
            // Append-only field: an older plugin ignores it, an older server omits it and the plugin
            // falls back to treating the row as owned (the pre-2026-08-24 behaviour).
            if (vp?.CurrencyDrops is { Count: > 0 })
            {
                var ownScope = wcid.HasValue
                    ? area?.Profile?.VariantForWcid(wcid.Value)
                    : area?.Profile?.Minion;
                sb.Append("|curr=");
                bool firstCd = true;
                foreach (var d in vp.CurrencyDrops)
                {
                    if (d == null || d.Wcid == 0) continue;
                    if (!firstCd) sb.Append(',');
                    firstCd = false;
                    var cdName = (ACE.Database.DatabaseManager.World.GetCachedWeenie(d.Wcid)?.GetName() ?? "")
                        .Replace('|', ' ').Replace(',', ' ').Replace('~', ' ').Replace('=', ' ');
                    var own = ownScope?.CurrencyDrops?.Any(x => x != null && x.Wcid == d.Wcid) == true;
                    sb.Append(d.Wcid).Append('~').Append(d.Amount).Append('~').Append(d.Chance.ToString(CultureInfo.InvariantCulture))
                      .Append('~').Append(d.Direct ? 1 : 0).Append('~').Append(cdName).Append('~').Append(own ? 1 : 0);
                }
            }

            // Zone player-effects (for the plugin's Effects tab).
            var effects = area?.Effects ?? new ZoneEffects();
            sb.Append("|fx_dot=").Append(effects.EffectiveDotEnabled ? 1 : 0)
              .Append("|fx_dotdmg=").Append(effects.EffectiveDotDamage.ToString(CultureInfo.InvariantCulture))
              .Append("|fx_dottype=").Append(effects.EffectiveDotDamageType)
              .Append("|fx_dotpercent=").Append(effects.EffectiveDotPercent ? 1 : 0)
              .Append("|fx_dotinterval=").Append(effects.EffectiveDotIntervalSeconds.ToString(CultureInfo.InvariantCulture))
              .Append("|fx_sup=").Append(effects.EffectiveSuppressEnabled ? 1 : 0)
              .Append("|fx_supprod=").Append(effects.EffectiveSuppressProdigal ? 1 : 0)
              .Append("|fx_supregen=").Append(effects.EffectiveSuppressRegenMult.ToString(CultureInfo.InvariantCulture));

            AppendSessionState(sb, session);

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

            AppendLadder(sb);   // APPEND-ONLY (2026-08-22): ladder apply versions, last so older plugins ignore it
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

        /// <summary>Strip the wire's separator chars from a value so it can't break payload parsing.</summary>
        // ── live stat resolution: /zonecontrol ladder ... (2026-08-22) ─────────────────────────────

        private const int LadderTierMin = 11, LadderTierMax = 25;

        /// <summary>`/zonecontrol ladder status | apply [tier|all] | migrate [here|&lt;player&gt;] [--dry] | show`.
        /// Contract: LiveStat_Contract_2026-08-22.md (Commands). Replies use cantrip NAMES, never keys.</summary>
        private static void HandleLadder(Session session, List<string> args, Action<string> Msg)
        {
            if (args.Count < 2)
            {
                Msg("Usage: ladder status | apply [tier|all] | migrate [here|<player>] [--dry] | show");
                Msg("  status  = per-tier ladder apply version (v0 = never applied)");
                Msg("  apply   = bump a tier's ladder version so its gear re-resolves on next equip (online players now, others at login)");
                Msg("  migrate = dev: grade a player's pre-grade Zone Cantrip pieces (equipped + packs); writes a dated SQL file; --dry = preview");
                Msg("  show    = the item you last appraised: tier, version, each graded line");
                return;
            }

            var verb = args[1].ToLowerInvariant();
            switch (verb)
            {
                case "status":
                {
                    Msg("Ladder apply state (live stat resolution):");
                    for (var t = LadderTierMin; t <= LadderTierMax; t++)
                    {
                        var la = ZoneControlManager.GetLadderVersion(t);
                        if (la == null || la.Version <= 0)
                            Msg($"  Tier {t,2}: v0 (never applied)");
                        else
                            Msg($"  Tier {t,2}: v{la.Version} by {la.AppliedBy ?? "?"} on {la.AppliedUtc:yyyy-MM-dd HH:mm} UTC");
                    }
                    return;
                }

                case "apply":
                {
                    int? tier = null;
                    for (var i = 2; i < args.Count; i++)
                    {
                        var a = args[i];
                        if (a.Equals("--nerf", StringComparison.OrdinalIgnoreCase)) continue;   // accepted and ignored: apply always follows the ladder both ways (owner 2026-08-23)
                        if (a.Equals("all", StringComparison.OrdinalIgnoreCase)) { tier = null; continue; }
                        if (int.TryParse(a.TrimStart('t', 'T', 'v', 'V'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tv))
                        {
                            if (tv < LadderTierMin || tv > LadderTierMax) { Msg($"Tier must be {LadderTierMin}-{LadderTierMax} (or 'all')."); return; }
                            tier = tv;
                            continue;
                        }
                        Msg($"Unknown apply argument '{a}'. Usage: ladder apply [tier|all]");
                        return;
                    }

                    LadderApplyNow(session, tier, Msg);
                    return;
                }

                case "migrate":
                    LadderMigrate(session, args, Msg);
                    return;

                case "bench":
                    LadderBench(session, args, Msg);
                    return;

                case "show":
                case "inspect":
                    LadderShow(session, Msg);
                    return;

                default:
                    Msg($"Unknown ladder verb '{args[1]}'. Usage: ladder status | apply [tier|all] | migrate [here|<player>] [--dry] | show");
                    return;
            }
        }

        /// <summary>`ladder show`: read-only view of the last APPRAISED item's record, resolved through
        /// ZoneStatResolver.Compute (never writes).</summary>
        private static void LadderShow(Session session, Action<string> Msg)
        {
            var player = session?.Player;
            if (player == null) { Msg("In-game only."); return; }
            ACE.Server.WorldObjects.WorldObject wo = null;
            if (player.CurrentAppraisalTarget.HasValue)
                wo = player.FindObject(player.CurrentAppraisalTarget.Value, ACE.Server.WorldObjects.Player.SearchLocations.Everywhere, out _, out _, out _);
            if (wo == null) { Msg("No item selected - appraise (examine) the piece first, then run ladder show."); return; }

            Msg($"{wo.Name} (0x{wo.Guid.Full:X8}, wcid {wo.WeenieClassId}):");
            if (!ZoneStatResolver.HasRecord(wo))
            {
                var hasLines = (wo.LongDesc ?? "").Contains("Zone Cantrip:", StringComparison.Ordinal);
                Msg(hasLines
                    ? "  no grade record (pre-grade piece: Zone Cantrip lines but no ZcLines) - 'ladder migrate' grades it"
                    : "  no grade record (not a Zone Control piece)");
                return;
            }

            var r = ZoneStatResolver.Compute(wo);
            if (r == null) { Msg("  record present but nothing resolved."); return; }
            var ladder = ZoneControlManager.GetLadderVersion(r.Tier);
            var seen = wo.GetProperty(PropertyInt.ZcResolvedVersion) ?? 0;
            var want = ZoneStatResolver.ResolveStamp(r.Tier);
            Msg($"  Tier {r.Tier}  stamp {seen} / current {want} (ladder v{ladder.Version}, Zone Control "
                + (ACE.Server.Managers.ServerConfig.zonecontrol_enabled.Value ? "on" : "OFF")
                + $"){(seen != want ? "  STALE - re-resolves on next equip" : "  current")}");
            foreach (var line in r.Lines)
                Msg($"  {line.Name} {line.Record.Grade}/{ZoneStatResolver.GradeMax} -> {line.Value} [{line.Min}-{line.Max}]");
            if (r.ArmorLevel.HasValue)
                Msg($"  Armor Level resolves to {r.ArmorLevel.Value} (base {ZoneStatResolver.BaseArmorLevel(r.Tier)}; stamped {wo.ArmorLevel ?? 0})");
        }

        private static readonly System.Text.RegularExpressions.Regex ZcTierLine =
            new(@"^\s*Tier:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex ZcBand =
            new(@"\[\s*(-?\d+)\s*-\s*(-?\d+)\s*\]", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex ZcInt =
            new(@"[+-]?\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Parse one "Zone Cantrip: ..." LongDesc line: catalog def by Name (longest match,
        /// case-insensitive), the integer right after the name (any ValFmt shape: "+41", "x3", "+5% vitals",
        /// "50", "2 pct HP per hit"), and the [min-max] band when present. Proc-shaped lines ("N% to ...")
        /// return false with proc=true.</summary>
        private static bool TryParseZcLine(string line, out ZoneCantrips.Def def, out int? value, out (int Min, int Max)? band, out bool proc)
        {
            def = null; value = null; band = null; proc = false;
            var idx = line.IndexOf("Zone Cantrip:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var rest = line.Substring(idx + "Zone Cantrip:".Length).Trim();
            if (rest.Length == 0) return false;

            foreach (var d in ZoneCantrips.Catalog.Values.OrderByDescending(d => d.Name?.Length ?? 0))
            {
                if (string.IsNullOrEmpty(d.Name)) continue;
                if (!rest.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (rest.Length > d.Name.Length && !char.IsWhiteSpace(rest[d.Name.Length])) continue;
                def = d;
                break;
            }
            if (def == null) return false;

            var tail = rest.Substring(def.Name.Length).Trim();
            if (tail.Contains("% to", StringComparison.OrdinalIgnoreCase) || tail.Contains(" to +", StringComparison.OrdinalIgnoreCase))
            { proc = true; return false; }

            var bm = ZcBand.Match(tail);
            if (bm.Success)
            {
                band = (int.Parse(bm.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(bm.Groups[2].Value, CultureInfo.InvariantCulture));
                tail = tail.Substring(0, bm.Index);
            }
            var vm = ZcInt.Match(tail);
            if (vm.Success && int.TryParse(vm.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                value = v;
            return true;
        }

        /// <summary>Equipped + inventory, packs walked recursively, no duplicates.</summary>
        private static List<ACE.Server.WorldObjects.WorldObject> WalkPossessions(ACE.Server.WorldObjects.Player player)
        {
            var seen = new HashSet<uint>();
            var list = new List<ACE.Server.WorldObjects.WorldObject>();
            void Visit(ACE.Server.WorldObjects.WorldObject wo)
            {
                if (wo == null || !seen.Add(wo.Guid.Full)) return;
                list.Add(wo);
                if (wo is ACE.Server.WorldObjects.Container c)
                    foreach (var inner in c.Inventory.Values.ToList())
                        Visit(inner);
            }
            foreach (var e in player.EquippedObjects.Values.ToList()) Visit(e);
            foreach (var i in player.Inventory.Values.ToList()) Visit(i);
            return list;
        }

        private static string SqlStr(string s) => "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "''") + "'";

        /// <summary>`ladder migrate [here|&lt;player&gt;] [--dry]` - grade a player's pre-grade Zone Cantrip pieces
        /// (plan §5): the stamped numbers become grades against the tier's live bands, the record is written,
        /// identity stamped, biota saved, and the same rows land in a dated SQL file.</summary>
        private static void LadderMigrate(Session session, List<string> args, Action<string> Msg)
        {
            var dry = false;
            var nameTokens = new List<string>();
            for (var i = 2; i < args.Count; i++)
            {
                if (args[i].Equals("--dry", StringComparison.OrdinalIgnoreCase) || args[i].Equals("dry", StringComparison.OrdinalIgnoreCase)) dry = true;
                else nameTokens.Add(args[i]);
            }
            var who = string.Join(" ", nameTokens);

            ACE.Server.WorldObjects.Player target;
            if (who.Length == 0 || who.Equals("here", StringComparison.OrdinalIgnoreCase))
                target = session?.Player;
            else
                target = PlayerManager.GetOnlinePlayer(who);
            if (target == null) { Msg(who.Length == 0 ? "In-game only (or name an ONLINE player)." : $"Player '{who}' is not online - migrate walks live objects only."); return; }

            var items = WalkPossessions(target);
            int migrated = 0, skippedHasRecord = 0, skippedNoLines = 0, skippedTier = 0;
            var unparsable = new List<string>();
            var sql = new StringBuilder();

            foreach (var wo in items)
            {
                var desc = wo.LongDesc ?? "";
                if (!desc.Contains("Zone Cantrip:", StringComparison.Ordinal)) { skippedNoLines++; continue; }
                if (ZoneStatResolver.HasRecord(wo)) { skippedHasRecord++; continue; }

                // tier: "Tier: N" provenance line, else WeaponAugScaleTier, else 11
                var tier = 0;
                var tm = ZcTierLine.Match(desc);
                if (tm.Success) int.TryParse(tm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tier);
                if (tier <= 0) tier = wo.GetProperty(PropertyInt.WeaponAugScaleTier) ?? 0;
                if (tier <= 0) tier = LadderTierMin;
                if (tier < LadderTierMin || tier > LadderTierMax)
                {
                    skippedTier++;
                    unparsable.Add($"{wo.Name} (0x{wo.Guid.Full:X8}): tier {tier} outside {LadderTierMin}-{LadderTierMax}");
                    continue;
                }

                var records = new List<ZoneStatResolver.LineRecord>();
                void Put(int key, int grade)
                {
                    records.RemoveAll(r => r.Key == key);
                    records.Add(new ZoneStatResolver.LineRecord { Key = key, Grade = Math.Clamp(grade, 0, ZoneStatResolver.GradeMax) });
                }
                var hasArmorLevelLine = false;
                var detail = new List<string>();

                foreach (var raw in desc.Split('\n'))
                {
                    var line = raw.Trim();
                    if (!line.Contains("Zone Cantrip:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TryParseZcLine(line, out var def, out var lineValue, out var lineBand, out var proc))
                    {
                        if (proc) { detail.Add($"skip proc line: {line}"); continue; }
                        unparsable.Add($"{wo.Name} (0x{wo.Guid.Full:X8}): {line}");
                        continue;
                    }
                    if (def.SetsProtection) { detail.Add($"skip {def.Name} (earned, frozen)"); continue; }
                    if (def.ArmorOnly && def.Ints == null) { hasArmorLevelLine = true; continue; }   // key 25: graded from AL below

                    var band = lineBand ?? ZoneStatResolver.EffectiveBand(def.Key, tier);
                    int? v = lineValue;
                    if (def.SlotSpecial)
                    {
                        // specials: the prop value is the truth (the line may carry no number at all)
                        band = ZoneStatResolver.EffectiveBand(def.Key, tier);
                        if (def.Ints != null && def.Ints.Length > 0)
                        {
                            var pv = wo.GetProperty((PropertyInt)def.Ints[0].PropId);
                            if (pv.HasValue) v = pv.Value;
                        }
                    }
                    else if (!v.HasValue && def.Ints != null && def.Ints.Length > 0)
                        v = wo.GetProperty((PropertyInt)def.Ints[0].PropId);
                    if (!v.HasValue)
                    {
                        unparsable.Add($"{wo.Name} (0x{wo.Guid.Full:X8}): {def.Name} has no value on the line or the item");
                        continue;
                    }
                    var grade = ZoneStatResolver.GradeFor(band.Min, band.Max, v.Value);
                    Put(def.Key, grade);
                    detail.Add($"{def.Name} {v.Value} in [{band.Min}-{band.Max}] -> {grade}");
                }

                // core four from the stamped Gear* props against the tier's window
                foreach (var coreKey in ZoneStatResolver.CoreKeys)
                {
                    var pv = wo.GetProperty(ZoneStatResolver.CoreProp(coreKey));
                    if (!pv.HasValue) continue;
                    var (cmin, cmax) = ZoneStatResolver.CoreWindow(coreKey, tier);
                    var grade = ZoneStatResolver.GradeFor(cmin, cmax, pv.Value);
                    Put(coreKey, grade);
                    detail.Add($"{ZoneStatResolver.CoreName(coreKey)} {pv.Value} in [{cmin}-{cmax}] -> {grade}");
                }

                // key 25 Armor Level: only an Armor piece above the tier base, and only when the line exists
                if (hasArmorLevelLine && wo.ItemType == ItemType.Armor && wo.ArmorLevel.HasValue
                    && wo.ArmorLevel.Value > ZoneStatResolver.BaseArmorLevel(tier))
                {
                    var (amin, amax) = ZoneStatResolver.EffectiveBand(25, tier);
                    var bonus = wo.ArmorLevel.Value - ZoneStatResolver.BaseArmorLevel(tier);
                    var grade = ZoneStatResolver.GradeFor(amin, amax, bonus);
                    Put(25, grade);
                    detail.Add($"Armor Level +{bonus} in [{amin}-{amax}] -> {grade}");
                }

                if (records.Count == 0)
                {
                    unparsable.Add($"{wo.Name} (0x{wo.Guid.Full:X8}): no gradable line");
                    continue;
                }

                var version = ZoneStatResolver.ResolveStamp(tier);   // must match what StampIdentity just wrote
                var recordText = ZoneStatResolver.Format(records);
                Msg($"{(dry ? "[dry] " : "")}{wo.Name} (0x{wo.Guid.Full:X8}) T{tier}: {string.Join("; ", detail)}");
                if (dry) { migrated++; continue; }

                ZoneStatResolver.Write(wo, records);
                ZoneStatResolver.StampIdentity(wo, tier);
                wo.ChangesDetected = true;
                wo.SaveBiotaToDatabase();
                migrated++;

                var oid = wo.Guid.Full;
                sql.AppendLine($"-- {wo.Name.Replace('\n', ' ')} T{tier}")
                   .AppendLine($"INSERT INTO biota_properties_string (object_Id, type, value) VALUES ({oid}, {(int)PropertyString.ZcLines}, {SqlStr(recordText)}) ON DUPLICATE KEY UPDATE value=VALUES(value);")
                   .AppendLine($"INSERT INTO biota_properties_int (object_Id, type, value) VALUES ({oid}, {(int)PropertyInt.ZcTier}, {tier}) ON DUPLICATE KEY UPDATE value=VALUES(value);")
                   .AppendLine($"INSERT INTO biota_properties_int (object_Id, type, value) VALUES ({oid}, {(int)PropertyInt.ZcResolvedVersion}, {version}) ON DUPLICATE KEY UPDATE value=VALUES(value);");
            }

            string sqlNote = "";
            if (!dry && sql.Length > 0)
            {
                try
                {
                    var path = $@"C:\AI\ZoneControl\T11_LiveStat_Migration_{DateTime.UtcNow:yyyy-MM-dd}.sql";
                    var isNew = !System.IO.File.Exists(path);
                    var block = new StringBuilder();
                    if (isNew)
                        block.AppendLine("-- T11 Live Stat Resolution migration (pre-grade pieces -> ZcLines grade records)")
                             .AppendLine("-- Written by /zonecontrol ladder migrate; rows mirror what SaveBiotaToDatabase already wrote live.")
                             .AppendLine("-- biota_properties_string type 50100 = ZcLines; biota_properties_int 50109 = ZcTier, 50110 = ZcResolvedVersion.")
                             .AppendLine();
                    block.AppendLine($"-- run {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC by {session?.Player?.Name ?? "console"} on player {target.Name} ({migrated} pieces)");
                    block.Append(sql).AppendLine();
                    System.IO.File.AppendAllText(path, block.ToString());
                    sqlNote = $" SQL appended to {path}.";
                }
                catch (Exception ex)
                {
                    sqlNote = $" SQL file NOT written: {ex.Message}";
                }
            }

            Msg($"{(dry ? "[dry] " : "")}Migrate {target.Name}: {migrated} pieces {(dry ? "would be " : "")}migrated, {skippedHasRecord} skipped (already graded), " +
                $"{skippedTier} skipped (tier out of range), {items.Count - skippedNoLines} Zone Control pieces of {items.Count} objects walked.{sqlNote}");
            if (unparsable.Count > 0)
            {
                Msg($"Unparsable ({unparsable.Count}):");
                foreach (var u in unparsable.Take(40)) Msg("  " + u);
                if (unparsable.Count > 40) Msg($"  ... and {unparsable.Count - 40} more");
            }
        }

        private static string CleanWire(string s) => (s ?? "").Replace('|', ' ').Replace(',', ' ').Replace('~', ' ').Replace('=', ' ');

        /// <summary>The raw-SQL statements that write an appearance set into a weenie — shared by
        /// bakemob (bake onto the SAME wcid) and clonemob (bake onto a fresh clone). Numeric-only
        /// interpolation, except Name which is ASCII-sanitized at set time and quote-escaped here.
        /// A Setup swap clears the weenie's own overlay first (parity with the runtime applier).</summary>
        private static List<string> BuildAppearanceBakeSql(uint w, ZoneAppearance merged)
        {
            var bake = new List<string>();
            void Did(int type, uint? v) { if (v.HasValue) bake.Add($"INSERT INTO `weenie_properties_d_i_d` (`object_Id`,`type`,`value`) VALUES ({w},{type},{v.Value}) ON DUPLICATE KEY UPDATE `value` = {v.Value}"); }
            void Flt(int type, double? v) { if (v.HasValue) { var s = v.Value.ToString("0.####", CultureInfo.InvariantCulture); bake.Add($"INSERT INTO `weenie_properties_float` (`object_Id`,`type`,`value`) VALUES ({w},{type},{s}) ON DUPLICATE KEY UPDATE `value` = {s}"); } }

            if (merged.SetupTableId.HasValue)
            {
                bake.Add($"DELETE FROM `weenie_properties_d_i_d` WHERE `object_Id` = {w} AND `type` IN (6,7)");
                bake.Add($"DELETE FROM `weenie_properties_int` WHERE `object_Id` = {w} AND `type` = 3");
                bake.Add($"DELETE FROM `weenie_properties_float` WHERE `object_Id` = {w} AND `type` = 12");
                bake.Add($"DELETE FROM `weenie_properties_anim_part` WHERE `object_Id` = {w}");
                bake.Add($"DELETE FROM `weenie_properties_palette` WHERE `object_Id` = {w}");
                bake.Add($"DELETE FROM `weenie_properties_texture_map` WHERE `object_Id` = {w}");
            }
            Did(1, merged.SetupTableId); Did(2, merged.MotionTable); Did(3, merged.SoundTable);
            Did(6, merged.PaletteBase); Did(7, merged.ClothingBase); Did(8, merged.Icon);
            if (merged.PaletteTemplate.HasValue) bake.Add($"INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES ({w},3,{merged.PaletteTemplate.Value}) ON DUPLICATE KEY UPDATE `value` = {merged.PaletteTemplate.Value}");
            if (merged.Shiny.HasValue) bake.Add(merged.Shiny.Value
                ? $"INSERT INTO `weenie_properties_int` (`object_Id`,`type`,`value`) VALUES ({w},9038,1) ON DUPLICATE KEY UPDATE `value` = 1"
                : $"DELETE FROM `weenie_properties_int` WHERE `object_Id` = {w} AND `type` = 9038");
            Flt(12, merged.Shade); Flt(39, merged.Scale); Flt(76, merged.Translucency);
            if (!string.IsNullOrEmpty(merged.Name))
            {
                var esc = merged.Name.Replace("\\", "\\\\").Replace("'", "''");
                bake.Add($"INSERT INTO `weenie_properties_string` (`object_Id`,`type`,`value`) VALUES ({w},1,'{esc}') ON DUPLICATE KEY UPDATE `value` = '{esc}'");
            }
            if (merged.AnimParts != null && merged.AnimParts.Count > 0)
            {
                bake.Add($"DELETE FROM `weenie_properties_anim_part` WHERE `object_Id` = {w}");
                foreach (var p in merged.AnimParts)
                    bake.Add($"INSERT INTO `weenie_properties_anim_part` (`object_Id`,`index`,`animation_Id`) VALUES ({w},{p.Index},{p.GfxObj})");
            }
            if (merged.TextureMaps != null && merged.TextureMaps.Count > 0)
            {
                bake.Add($"DELETE FROM `weenie_properties_texture_map` WHERE `object_Id` = {w}");
                foreach (var t in merged.TextureMaps)
                    bake.Add($"INSERT INTO `weenie_properties_texture_map` (`object_Id`,`index`,`old_Id`,`new_Id`) VALUES ({w},{t.Index},{t.OldTex},{t.NewTex})");
            }
            return bake;
        }

        /// <summary>The head part index of a Setup = the highest (max-Z) non-null part, using the first placement
        /// frame that has one entry per part. Part index->bone mapping varies per setup, so the head is NOT a fixed
        /// index (e.g. 16 on the void-lord but 14 on the Mosswart). Returns -1 if no placement data.</summary>
        private static int SetupHeadIndex(SetupModel setup)
        {
            var pf = setup.PlacementFrames.Values
                .FirstOrDefault(p => p.AnimFrame?.Frames != null && p.AnimFrame.Frames.Count >= setup.Parts.Count);
            if (pf == null) return -1;
            int headIdx = -1; float maxZ = float.NegativeInfinity;
            for (int i = 0; i < setup.Parts.Count; i++)
                if (setup.Parts[i] != 0x010001EC && pf.AnimFrame.Frames[i].Origin.Z > maxZ) { maxZ = pf.AnimFrame.Frames[i].Origin.Z; headIdx = i; }
            return headIdx;
        }

        /// <summary>Rough anatomical label per Setup part from placement position (part index->bone varies per setup,
        /// so labels are GEOMETRIC, not authoritative): the max-Z real part = "head", others = [L|R ] upper/mid/
        /// lower/foot by height + X sign, null parts (0x010001EC) = "empty".</summary>
        private static string[] SetupPartLabels(SetupModel setup, int headIdx)
        {
            var labels = new string[setup.Parts.Count];
            var pf = setup.PlacementFrames.Values
                .FirstOrDefault(p => p.AnimFrame?.Frames != null && p.AnimFrame.Frames.Count >= setup.Parts.Count);
            float zmin = float.MaxValue, zmax = float.MinValue, ymin = float.MaxValue, ymax = float.MinValue;
            if (pf != null)
                for (int i = 0; i < setup.Parts.Count; i++)
                {
                    var o = pf.AnimFrame.Frames[i].Origin;
                    if (o.Z < zmin) zmin = o.Z; if (o.Z > zmax) zmax = o.Z;
                    if (o.Y < ymin) ymin = o.Y; if (o.Y > ymax) ymax = o.Y;
                }
            // Only add a front/back axis when the body has real front-back spread relative to its height - i.e.
            // long/flat creatures (armoredillo). A tall humanoid (height >> depth) stays without it, so its labels
            // don't gain noise. +Y = forward (the facing direction); may read inverted on oddly-authored models.
            bool useDepth = pf != null && (ymax - ymin) > 0.6f * (zmax - zmin);
            for (int i = 0; i < setup.Parts.Count; i++)
            {
                if (i == headIdx) { labels[i] = "head"; continue; }
                if (setup.Parts[i] == 0x010001EC) { labels[i] = "empty"; continue; }
                if (pf == null) { labels[i] = "part " + i; continue; }
                var o = pf.AnimFrame.Frames[i].Origin;
                float zn = zmax > zmin ? (o.Z - zmin) / (zmax - zmin) : 0.5f;
                string band = zn >= 0.78f ? "upper" : zn >= 0.5f ? "mid" : zn >= 0.22f ? "lower" : "foot";
                string side = o.X < -0.06f ? "L " : o.X > 0.06f ? "R " : "";
                string depth = "";
                if (useDepth)
                {
                    float yn = ymax > ymin ? (o.Y - ymin) / (ymax - ymin) : 0.5f;
                    depth = yn >= 0.70f ? " fwd" : yn <= 0.30f ? " back" : "";
                }
                labels[i] = side + band + depth;
            }
            return labels;
        }

        /// <summary>Parse a DataId: "0x02001234" (hex) or a plain decimal uint. Used by the appearance DataId levers.</summary>
        private static bool TryParseDid(string s, out uint value)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Human-readable one-liner for a zone's DoT config (command echoes + show).</summary>
        private static string DescribeDot(ZoneEffects e)
        {
            string dot;
            if (!e.EffectiveDotEnabled) dot = "DoT off";
            else
            {
                var amount = e.EffectiveDotPercent
                    ? $"{e.EffectiveDotDamage:0.##} pct max health"
                    : $"{e.EffectiveDotDamage:0.##} {(DamageType)e.EffectiveDotDamageType}";
                dot = $"DoT ON: {amount} every {Math.Max(1.0, e.EffectiveDotIntervalSeconds):0.##}s";
            }

            string sup;
            if (!e.EffectiveSuppressEnabled) sup = "Suppression off";
            else sup = $"Suppression ON: Prodigal regen {(e.EffectiveSuppressProdigal ? "blocked" : "allowed")}, " +
                       $"regen {e.EffectiveSuppressRegenMult * 100.0:0.##} pct";

            return dot + "; " + sup;
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

        /// <summary>NPC coords go on the wire as SIGNED DECIMALS - "30.3S, 94.8E" becomes
        /// "|cy=-30.3|cx=94.8" (N/E positive, S/W negative) - never as coordinate-SHAPED text.
        ///
        /// PROVEN 2026-07-30: something in the client stack reacts to a coordinate-shaped substring in a
        /// chat line and re-renders that line, bypassing the plugin's chat interception entirely. Evidence:
        /// of 24 [[ZCQ]] lines, the 9 with resolved NPC coords ALWAYS appeared in the player's chat box and
        /// the 15 with an empty co= never did - including C1-C6, which are st=live but have no coords, so
        /// it tracks the coordinate text and nothing else. The plugin's log proved all 24 were offered to
        /// its handler and eaten every time. Every other payload ([[ZC]], [[ZCM]], [[ZCA]], [[ZCS]] at 90
        /// messages) carries no coordinates and has never leaked.
        ///
        /// The plugin reassembles "30.3S, 94.8E" from cy/cx for display. Empty or unparseable -> empty
        /// fields, which is what an unplaced NPC already produced.</summary>
        private static string QuestCoordFields(string coords)
        {
            var s = (coords ?? "").Trim();
            var comma = s.IndexOf(',');
            if (comma > 0 &&
                TryCoordPart(s.Substring(0, comma), 'N', 'S', out var cy) &&
                TryCoordPart(s.Substring(comma + 1), 'E', 'W', out var cx))
            {
                return "|cy=" + cy.ToString("0.###", CultureInfo.InvariantCulture) +
                       "|cx=" + cx.ToString("0.###", CultureInfo.InvariantCulture);
            }
            return "|cy=|cx=";
        }

        /// <summary>"30.3S" -> -30.3 (given pos 'N', neg 'S'). False if it isn't that shape.</summary>
        private static bool TryCoordPart(string part, char pos, char neg, out double value)
        {
            value = 0;
            part = (part ?? "").Trim();
            if (part.Length < 2) return false;
            var hemi = char.ToUpperInvariant(part[part.Length - 1]);
            if (hemi != pos && hemi != neg) return false;
            if (!double.TryParse(part.Substring(0, part.Length - 1).Trim(),
                                 NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            if (hemi == neg) value = -value;
            return true;
        }

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
              .Append(QuestCoordFields(q.Coords))
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

        /// <summary>Builds the "[[ZCG]]" generator-knob payload for the plugin's Generator Settings rows:
        /// w=wcid~name~delay~radius~stagger~init~max (delay = first generator entry's; -1 = no entries).</summary>
        private static string BuildGenInfoPayload(uint wcid, ACE.Entity.Models.Weenie weenie)
        {
            var name = (weenie.GetName() ?? ("wcid " + wcid)).Replace('|', ' ').Replace('~', ' ').Replace('=', ' ');
            var delay = weenie.PropertiesGenerator != null && weenie.PropertiesGenerator.Count > 0
                ? (weenie.PropertiesGenerator[0].Delay ?? 0f) : -1f;
            float F(int id) => (float)(weenie.GetProperty((PropertyFloat)id) ?? 0);
            int I(int id) => weenie.GetProperty((PropertyInt)id) ?? 0;
            return $"[[ZCG]]w={wcid}~found=1~{name}~{delay:0.####}~{F(43):0.####}~{F(9034):0.####}~{I(82)}~{I(81)}";
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

            // ratings: rt=damage_rating,damage_resist_rating,crit_chance,crit_damage,crit_resist,crit_damage_resist
            // (plugin accepts 2 or 6 fields - back/forward compatible)
            // crit_chance/crit_damage are EFFECTIVE bases (chance in percent / final Nx multiplier):
            // the best wielded weapon's props when present, engine defaults 10 / 2 otherwise - matching
            // the REPLACE semantics of the crit_rating/crit_damage_rating zone stats.
            int I(PropertyInt p) => weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(p, out var iv) ? iv : 0;
            double critChanceBase = 10.0, critDamageBase = 2.0;
            if (weenie.PropertiesCreateList != null)
            {
                foreach (var cl in weenie.PropertiesCreateList)
                {
                    if ((cl.DestinationType & DestinationType.Wield) == 0)
                        continue;
                    var wieldItem = ACE.Database.DatabaseManager.World.GetCachedWeenie(cl.WeenieClassId);
                    if (wieldItem?.PropertiesFloat == null)
                        continue;
                    if (wieldItem.PropertiesFloat.TryGetValue(PropertyFloat.CriticalFrequency, out var cfb))
                        critChanceBase = Math.Max(critChanceBase, cfb * 100.0);
                    if (wieldItem.PropertiesFloat.TryGetValue(PropertyFloat.CriticalMultiplier, out var cmb))
                        critDamageBase = Math.Max(critDamageBase, 1.0 + cmb);
                }
            }
            sb.Append("|rt=").Append(I(PropertyInt.DamageRating)).Append(',').Append(I(PropertyInt.DamageResistRating))
              .Append(',').Append(critChanceBase.ToString(CultureInfo.InvariantCulture))
              .Append(',').Append(critDamageBase.ToString(CultureInfo.InvariantCulture))
              .Append(',').Append(I(PropertyInt.CritResistRating)).Append(',').Append(I(PropertyInt.CritDamageResistRating));

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

            // wielded weapons from the create list:
            // wield=<wcid>,<name>,<damage>,<variance>,<dtypeMask>,<speed>[,<critFreq>,<critMult>]
            // critFreq/critMult are the weapon's own props; -1 = unset (engine defaults 0.1 / 1.0 apply).
            // Plugin accepts 6 or 8 fields - back/forward compatible.
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
                    var critFreq = item.PropertiesFloat != null && item.PropertiesFloat.TryGetValue(PropertyFloat.CriticalFrequency, out var cf) ? cf : -1.0;
                    var critMult = item.PropertiesFloat != null && item.PropertiesFloat.TryGetValue(PropertyFloat.CriticalMultiplier, out var cm) ? cm : -1.0;
                    sb.Append("|wield=").Append(cl.WeenieClassId).Append(',')
                      .Append((item.GetName() ?? "?").Replace('|', ' ').Replace(',', ' ').Replace('=', ' ')).Append(',')
                      .Append(dmg).Append(',')
                      .Append(dvar.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(dtype).Append(',')
                      .Append(speed).Append(',')
                      .Append(critFreq.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(critMult.ToString(CultureInfo.InvariantCulture));
                }
            }

            sb.Append("|spells=").Append(weenie.PropertiesSpellBook?.Count ?? 0);

            // per-spell book rows for the Spells tab: spell=<id>,<chancePct>,<school>,<name>
            // (chance decoded from the 2.0-base encoding: 2.029 -> 2.9)
            if (weenie.PropertiesSpellBook != null)
            {
                foreach (var s in weenie.PropertiesSpellBook.OrderBy(s => s.Key))
                {
                    var sp = new ACE.Server.Entity.Spell(s.Key);
                    var chancePct = (s.Value > 2.0f ? s.Value - 2.0f : s.Value / 100.0f) * 100.0;
                    var spName = (sp.NotFound ? "unknown" : sp.Name ?? "unknown")
                        .Replace('|', ' ').Replace(',', ' ').Replace('=', ' ');
                    sb.Append("|spell=").Append(s.Key).Append(',')
                      .Append(chancePct.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                      .Append(sp.NotFound ? "" : sp.School.ToString()).Append(',')
                      .Append(spName);
                }
            }
            return sb.ToString();
        }
    }
}
