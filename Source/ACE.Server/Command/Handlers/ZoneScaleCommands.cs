using System;
using System.Globalization;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers.ZoneScaling;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// /zonescale — author + inspect Zone Scaler profiles (Developer). Subcommands mutate the shard-persisted
    /// profile store live (no respawn for live stats; HP/loot apply on spawn/kill). Scope tokens use the canonical
    /// form: global | zone:&lt;name&gt; | lb:&lt;hex&gt; | lbvar:&lt;hex&gt;:v&lt;n&gt;.
    /// </summary>
    public static class ZoneScaleCommands
    {
        [CommandHandler("zonescale", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Author/inspect Zone Scaler profiles.",
            "help | list | show <scope> [tier] [--boss] | enable <scope> | disable <scope> | create <scope> | "
            + "set <scope> <stat> <base> [growth] [--boss] [--additive] | pin <scope> <stat> <tier> <value> [--boss] | "
            + "unpin <scope> <stat> <tier> [--boss] | zone add|remove <name> <hex> | here | delete <scope> | reload")]
        public static void HandleZoneScale(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            if (parameters.Length == 0 || parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Msg("Zone Scaler commands:");
                Msg("  /zonescale list");
                Msg("  /zonescale show <scope> [tier] [--boss]");
                Msg("  /zonescale enable|disable <scope>");
                Msg("  /zonescale create <scope>");
                Msg("  /zonescale set <scope> <stat> <base> [growth] [--boss] [--additive]");
                Msg("  /zonescale pin <scope> <stat> <tier> <value> [--boss]");
                Msg("  /zonescale unpin <scope> <stat> <tier> [--boss]");
                Msg("  /zonescale zone add|remove <name> <hex>");
                Msg("  /zonescale here | delete <scope> | reload");
                Msg("  scope = global | zone:<name> | lb:<hex> | lbvar:<hex>:v<n>");
                Msg("  stats = " + string.Join(", ", ZoneStat.All));
                return;
            }

            // extract flags
            var args = parameters.ToList();
            bool boss = args.RemoveAll(a => a.Equals("--boss", StringComparison.OrdinalIgnoreCase)) > 0;
            bool additive = args.RemoveAll(a => a.Equals("--additive", StringComparison.OrdinalIgnoreCase)) > 0;
            var variant = boss ? ZoneVariant.Boss : ZoneVariant.Minion;

            var sub = args[0].ToLowerInvariant();

            try
            {
                switch (sub)
                {
                    case "list":
                    {
                        var profiles = ZoneScalingManager.ListProfiles();
                        if (profiles.Count == 0) { Msg("(no profiles)"); }
                        foreach (var p in profiles.OrderBy(p => p.ScopeType))
                            Msg($"  {p.ScopeKey(),-22} {(p.Enabled ? "ENABLED " : "disabled")}  minion:{p.Minion.Stats.Count} boss:{p.Boss.Stats.Count}  {p.Notes}");
                        foreach (var z in ZoneScalingManager.GetZoneMap())
                            Msg($"  zone '{z.Key}' = {z.Value.Count} landblocks");
                        return;
                    }

                    case "reload":
                        ZoneScalingManager.Reload();
                        Msg("Zone Scaler store reloaded from shard.");
                        return;

                    case "zone":
                    {
                        // zone add|remove <name> <hex>
                        if (args.Count < 4) { Msg("Usage: /zonescale zone add|remove <name> <hex>"); return; }
                        var op = args[1].ToLowerInvariant();
                        var name = args[2].ToLowerInvariant();
                        if (!TryHex(args[3], out var zlb)) { Msg("hex landblock required, e.g. F559"); return; }
                        if (op == "add") { ZoneScalingManager.AddZoneLandblock(name, (ushort)zlb); Msg($"zone '{name}' += lb {zlb:X4}"); }
                        else if (op == "remove") { ZoneScalingManager.RemoveZoneLandblock(name, (ushort)zlb); Msg($"zone '{name}' -= lb {zlb:X4}"); }
                        else Msg("op must be add or remove");
                        return;
                    }

                    case "here":
                    {
                        var loc = session.Player?.Location;
                        if (loc == null) { Msg("No location."); return; }
                        var lb = loc.LandblockId.Landblock;
                        var v = loc.Variation ?? 0;
                        Msg($"Here: lb:{lb:X4} variation v{v}");
                        Msg($"  candidate scopes (most-specific first): lbvar:{lb:X4}:v{v}  lb:{lb:X4}  " +
                            string.Join(" ", ZoneScalingManager.GetZoneMap().Where(z => z.Value.Contains(lb)).Select(z => "zone:" + z.Key)) + "  global");
                        return;
                    }
                }

                // remaining subcommands need a scope token at args[1]
                if (args.Count < 2) { Msg($"'{sub}' needs a scope. See /zonescale help."); return; }

                var scope = ParseScope(args[1], out var scopeErr);
                if (scope == null) { Msg("Bad scope: " + scopeErr); return; }
                var key = scope.ScopeKey();

                switch (sub)
                {
                    case "create":
                        if (ZoneScalingManager.GetProfileByScope(key) != null) { Msg($"Profile {key} already exists."); return; }
                        ZoneScalingManager.UpsertProfile(scope);
                        Msg($"Created profile {key} (disabled, empty). Use 'set'/'pin' then 'enable'.");
                        return;

                    case "delete":
                        Msg(ZoneScalingManager.RemoveProfile(key) ? $"Deleted {key}." : $"No profile {key}.");
                        return;

                    case "enable":
                        Msg(ZoneScalingManager.SetProfileEnabled(key, true) ? $"{key} ENABLED." : $"No profile {key} (create it first).");
                        return;

                    case "disable":
                        Msg(ZoneScalingManager.SetProfileEnabled(key, false) ? $"{key} disabled." : $"No profile {key}.");
                        return;

                    case "show":
                    {
                        int tier = 1;
                        if (args.Count >= 3) int.TryParse(args[2], out tier);
                        if (tier < 1) tier = 1;
                        var eval = ZoneScalingManager.EvaluateScope(key, tier, variant);
                        if (eval == null) { Msg($"No profile {key}."); return; }
                        Msg($"{key} @ tier {tier} ({variant}):");
                        if (eval.Values.Count == 0) Msg("  (no stats defined for this variant)");
                        foreach (var kv in eval.Values.OrderBy(k => k.Key))
                            Msg($"    {kv.Key,-22} = {kv.Value:0.####}");
                        return;
                    }

                    case "set":
                    {
                        // set <scope> <stat> <base> [growth]
                        if (args.Count < 4) { Msg("Usage: set <scope> <stat> <base> [growth] [--boss] [--additive]"); return; }
                        var stat = NormalizeStat(args[2]); if (stat == null) { Msg("Unknown stat. Stats: " + string.Join(", ", ZoneStat.All)); return; }
                        if (!TryDouble(args[3], out var baseVal)) { Msg("base must be a number."); return; }
                        double growth = 1.0;
                        if (args.Count >= 5) TryDouble(args[4], out growth);

                        var profile = ZoneScalingManager.GetProfileByScope(key) ?? scope;
                        profile.Variant(variant).Stats[stat] = new StatCurve { Base = baseVal, Growth = growth, Additive = additive };
                        ZoneScalingManager.UpsertProfile(profile);
                        Msg($"{key} [{variant}] {stat} = base {baseVal:0.####} growth {growth:0.####}{(additive ? " (additive)" : "")}. " +
                            $"{(profile.Enabled ? "" : "Profile still DISABLED — /zonescale enable " + key)}");
                        return;
                    }

                    case "pin":
                    {
                        // pin <scope> <stat> <tier> <value>
                        if (args.Count < 5) { Msg("Usage: pin <scope> <stat> <tier> <value> [--boss]"); return; }
                        var stat = NormalizeStat(args[2]); if (stat == null) { Msg("Unknown stat."); return; }
                        if (!int.TryParse(args[3], out var tier) || tier < 1) { Msg("tier must be a positive integer."); return; }
                        if (!TryDouble(args[4], out var value)) { Msg("value must be a number."); return; }

                        var profile = ZoneScalingManager.GetProfileByScope(key) ?? scope;
                        var stats = profile.Variant(variant).Stats;
                        if (!stats.TryGetValue(stat, out var curve))
                            stats[stat] = curve = new StatCurve { Base = value, Growth = 1.0 };
                        curve.Overrides ??= new System.Collections.Generic.Dictionary<int, double>();
                        curve.Overrides[tier] = value;
                        ZoneScalingManager.UpsertProfile(profile);
                        Msg($"{key} [{variant}] {stat} tier {tier} pinned = {value:0.####}.");
                        return;
                    }

                    case "unpin":
                    {
                        if (args.Count < 4) { Msg("Usage: unpin <scope> <stat> <tier> [--boss]"); return; }
                        var stat = NormalizeStat(args[2]); if (stat == null) { Msg("Unknown stat."); return; }
                        if (!int.TryParse(args[3], out var tier)) { Msg("tier must be an integer."); return; }
                        var profile = ZoneScalingManager.GetProfileByScope(key);
                        if (profile != null && profile.Variant(variant).Stats.TryGetValue(stat, out var curve) && curve.Overrides != null && curve.Overrides.Remove(tier))
                        {
                            ZoneScalingManager.UpsertProfile(profile);
                            Msg($"{key} [{variant}] {stat} tier {tier} unpinned.");
                        }
                        else Msg("No such pin.");
                        return;
                    }

                    default:
                        Msg($"Unknown subcommand '{sub}'. See /zonescale help.");
                        return;
                }
            }
            catch (Exception ex)
            {
                Msg("Error: " + ex.Message);
            }
        }

        #region parsing helpers

        private static ZoneScalingProfile ParseScope(string token, out string error)
        {
            error = null;
            token = token.Trim();
            if (token.Equals("global", StringComparison.OrdinalIgnoreCase))
                return new ZoneScalingProfile { ScopeType = ZoneScopeType.Global };

            var parts = token.Split(':');
            switch (parts[0].ToLowerInvariant())
            {
                case "zone":
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1])) { error = "zone:<name> (e.g. zone:tou_tou)"; return null; }
                    return new ZoneScalingProfile { ScopeType = ZoneScopeType.Zone, ZoneName = parts[1].ToLowerInvariant() };

                case "lb":
                    if (parts.Length < 2 || !TryHex(parts[1], out var lb)) { error = "lb:<hex> (e.g. lb:F559)"; return null; }
                    return new ZoneScalingProfile { ScopeType = ZoneScopeType.Landblock, Landblock = lb };

                case "lbvar":
                    if (parts.Length < 3 || !TryHex(parts[1], out var lb2)) { error = "lbvar:<hex>:v<n> (e.g. lbvar:F559:v11)"; return null; }
                    var vstr = parts[2].TrimStart('v', 'V');
                    if (!int.TryParse(vstr, out var v)) { error = "bad variation in lbvar:<hex>:v<n>"; return null; }
                    return new ZoneScalingProfile { ScopeType = ZoneScopeType.LandblockVariation, Landblock = lb2, Variation = v };

                default:
                    error = "global | zone:<name> | lb:<hex> | lbvar:<hex>:v<n>";
                    return null;
            }
        }

        private static bool TryHex(string s, out int value)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryDouble(string s, out double value)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        private static string NormalizeStat(string s)
        {
            s = s.Trim().ToLowerInvariant();
            return ZoneStat.All.FirstOrDefault(k => k.Equals(s, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
