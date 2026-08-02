using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;

using ACE.Common.Performance;
using ACE.Entity.Enum;
using ACE.Server.Managers.WeaponScaling;
using ACE.Server.Network;

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
                if (payload == watch.LastPayload && (now - watch.LastSentUtc).TotalSeconds < SyncKeepaliveSeconds)
                    continue;

                watch.LastPayload = payload;
                watch.LastSentUtc = now;
                ChatPacket.SendServerMessage(session, payload, ChatMessageType.Broadcast);
            }
        }

        /// <summary>Pipe/tilde wire format (house style; the plugin has no JSON parser):
        /// [[ZCW]]|enabled=1|kc=0.6~0.8|tiers=t~cap~minwield,...|scripts=name~kmin~kmax,...</summary>
        private static string BuildPayload()
        {
            var cfg = WeaponScalingManager.Current;
            var sb = new StringBuilder("[[ZCW]]");
            sb.Append("|enabled=").Append(cfg.Enabled ? '1' : '0');
            sb.Append("|kc=").Append(F(cfg.KcMin)).Append('~').Append(F(cfg.KcMax));
            sb.Append("|tiers=").Append(string.Join(",",
                cfg.Tiers.Select(t => $"{t.Tier}~{t.Cap}~{t.MinWieldAugs}")));
            sb.Append("|scripts=").Append(string.Join(",",
                cfg.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(s => $"{s.Key}~{F(s.Value.KMin)}~{F(s.Value.KMax)}")));
            return sb.ToString();
        }

        private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        [CommandHandler("weaponscale", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Weapon aug-scaling config (plugin fallback).",
            "show | enable on|off | tier <t> cap|minwield <n> | tier add <t> [cap] [minwield] | tier remove <t> | "
            + "script <name> kmin|kmax <v> | script add <name> [kmin] [kmax] | script remove <name> | "
            + "kc min|max <v> | sync on|off | reset | reload")]
        public static void HandleWeaponScale(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            if (parameters.Length == 0 || parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Msg("Weapon aug-scaling config (items store quality; these knobs give it meaning at swing time):");
                Msg("  /weaponscale show");
                Msg("  /weaponscale enable on|off       master switch; off = static-base-only combat (current behavior)");
                Msg("  /weaponscale tier <t> cap <n>    scaling stops growing at n item augs for tier-t weapons");
                Msg("  /weaponscale tier <t> minwield <n>   item augs required to WIELD tier-t weapons (economy gate)");
                Msg("  /weaponscale tier add <t> [cap] [minwield] | tier remove <t>");
                Msg("  /weaponscale script <name> kmin <v> | kmax <v>   per-loot-script k roll range");
                Msg("  /weaponscale script add <name> [kmin] [kmax] | script remove <name>");
                Msg("  /weaponscale kc min <v> | kc max <v>   crit-channel coefficient range");
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
                    sb.AppendLine($"Weapon aug-scaling: {(cfg.Enabled ? "ENABLED" : "DISABLED")} (kc {cfg.KcMin:0.###}-{cfg.KcMax:0.###})");
                    sb.AppendLine("  tier | cap | minwield");
                    foreach (var t in cfg.Tiers)
                        sb.AppendLine($"  T{t.Tier} | {t.Cap:N0} | {t.MinWieldAugs:N0}");
                    sb.AppendLine("  script | kmin | kmax");
                    foreach (var s in cfg.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine($"  {s.Key} | {s.Value.KMin:0.###} | {s.Value.KMax:0.###}");
                    Msg(sb.ToString().TrimEnd());
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
                    Msg($"Weapon aug-scaling {(on ? "ENABLED" : "DISABLED")}.");
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
                    if (args.Length < 4 || !TryParseDouble(args[3], out var v))
                    {
                        Msg("Usage: /weaponscale script <name> kmin|kmax <v>  |  script add <name> [kmin] [kmax]  |  script remove <name>");
                        return;
                    }
                    var scriptName = args[1];
                    var kField = args[2].ToLowerInvariant();
                    if (kField != "kmin" && kField != "kmax")
                    {
                        Msg("script: field must be kmin or kmax.");
                        return;
                    }
                    var scriptFound = false;
                    WeaponScalingManager.Mutate(cfg =>
                    {
                        if (!cfg.Scripts.TryGetValue(scriptName, out var s)) return;
                        scriptFound = true;
                        if (kField == "kmin") s.KMin = v;
                        else s.KMax = v;
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
                    });
                    Msg("Weapon aug-scaling config reset to locked launch defaults (system DISABLED).");
                    return;
                }

                case "reload":
                {
                    WeaponScalingManager.Reload();
                    Msg("Weapon aug-scaling config reloaded from store.");
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
            ("ua",        30612, "Knuckles"),   // knuckleselectric — W_WeaponType Unarmed
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
        private static string ForgeWeapon(ACE.Server.WorldObjects.Player player, uint wcid, string cleanName,
            int quality, int tier, DamageType? element)
        {
            var wo = ACE.Server.Factories.WorldObjectFactory.CreateNewWorldObject(wcid);
            if (wo == null)
                return $"forge: could not create wcid {wcid}";

            ACE.Server.Factories.LootGenerationFactory.StripWieldRequirements(wo);
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

            if (!player.TryCreateInInventoryWithNetworking(wo))
            {
                wo.Destroy();
                return $"forge: could not place {wo.Name} in inventory (full?)";
            }

            return $"forged: {wo.Name} -> family {WeaponScalingCombat.GetFamilyKey(wo) ?? "none"}, " +
                   $"grade {WeaponScalingManager.GetQualityGrade(quality)} ({quality}/1000), tier {tier}";
        }

        [CommandHandler("wstestkit", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 0,
            "Grants weapon aug-scaling test weapons (UA, bow, sword, wand) stamped at a fixed quality.",
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
            "Forges one weapon aug-scaling test weapon of the given class (or a full set with 'all').",
            "<class|all> [quality 0-1000, default 500] [element] [tier, default 11]\n" +
            "Classes: sword sword_ms dagger dagger_ms axe mace spear staff ua cleaver spear2h bow crossbow atlatl wand")]
        public static void HandleWsForge(Session session, params string[] parameters)
        {
            void Msg(string s) => ChatPacket.SendServerMessage(session, s, ChatMessageType.Broadcast);

            var player = session.Player;
            if (player == null)
                return;

            var classKey = parameters[0].ToLowerInvariant();
            var all = classKey == "all";
            var cls = ForgeClasses.FirstOrDefault(c => c.Key == classKey);
            if (!all && cls.Wcid == 0)
            {
                Msg("wsforge: unknown class. Classes: all " + string.Join(" ", ForgeClasses.Select(c => c.Key)));
                return;
            }

            var quality = 500;
            if (parameters.Length > 1 && int.TryParse(parameters[1], out var q))
                quality = Math.Clamp(q, 0, 1000);

            DamageType? element = null;
            if (parameters.Length > 2 && !parameters[2].Equals("base", StringComparison.OrdinalIgnoreCase))
            {
                element = ParseElement(parameters[2]);
                if (element == null)
                {
                    Msg("wsforge: unknown element. Use base|slash|pierce|bludge|acid|fire|cold|electric|nether.");
                    return;
                }
            }

            var tier = 11;
            if (parameters.Length > 3 && int.TryParse(parameters[3], out var t))
                tier = Math.Clamp(t, 11, 25);

            if (all)
            {
                // one of every class at the same quality/element/tier (owner 2026-08-01)
                foreach (var c in ForgeClasses)
                    Msg(ForgeWeapon(player, c.Wcid, c.CleanName, quality, tier, element));
                return;
            }

            Msg(ForgeWeapon(player, cls.Wcid, cls.CleanName, quality, tier, element));
        }
    }
}
