using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Command.Handlers;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers.ZoneControl;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Combat Bench (owner 2026-09-28: "a full server side combat test bot"). An admin's own character is the test
    /// subject: /bench gear builds and wears a premade suit + weapon, /bench run spawns each target monster beside them,
    /// drives the player's real melee loop (the same call the client's attack sends - no keystrokes) and records every
    /// hit both ways where damage is applied. Two modes, owner-ruled: Live = real fights (time-to-kill, time-to-die),
    /// Math = the player's damage calculation sampled N times with no fight (exact averages + crit rate). Bench
    /// monsters give no XP, loot, corpse, kill task or Bounty. Gated by combat_bench_enabled (default off).
    /// Results go to the Combat Bench plugin over [[CBS]] (state) / [[CBR]] (a row) / [[CBL]] (log) / [[CBD]] (done) /
    /// [[CBT]] (the monster to select, once per fight).
    /// </summary>
    public static partial class CombatBench
    {
        private const double TickSeconds = 0.25;
        private const double TravelTimeoutSeconds = 45;   // a matrix step's teleport to its variation
        private const double TravelSettleSeconds = 3;     // after arriving, before the step's gear + spawns
        // an Average-suit boss kill runs ~270 s (2026-09-28); 240 cut it off. 900 (owner 2026-09-29): a T10-gear boss
        // needs ~390 strikes = ~613 s, and 600 timed it out just short of the kill.
        private const double FightTimeoutSeconds = 900;
        private const double BetweenFightsSeconds = 1.5;
        private const double CombatModeWaitSeconds = 5;
        private const int MaxMathSamples = 50_000;

        private enum Phase { Start, Gear, NextTarget, Math, SpawnLive, Engage, Fighting, AfterFight, Finish }   // Gear: a weapon sweep's Equip (owner 2026-10-01)

        private class Fight
        {
            public DateTime? FirstStrike, MobFirst, End;
            public bool Killed, TimedOut;
            public int Strikes, Hits, Crits;
            public double NormalSum, CritSum, ProcSum;
            public int MobAttacks, MobHits, MobCrits, MobSpells;
            public double MobMeleeSum, MobSpellSum;
            public double MobTrueSum;                 // the True Damage part of both (owner 2026-10-01)
            // owner 2026-10-01 ("I need to SEE the true damage vs the hit damage"): each monster hit split into its
            // normal part (what armor / protections / Damage Resist cut) and its True Damage part, per rank
            public readonly Dictionary<string, RankHits> ByRank = new Dictionary<string, RankHits>();
            // Pack mode (owner 2026-10-01): kit heals, the time spent healing, the low point, and 1-HP "deaths"
            public int MobsKilled, Heals, HealFails, Deaths;
            public double HealSum, HealSeconds, LowHpPct = 100;
            // Timed pack (owner 2026-10-01): the monsters never die - the fight lasts a fixed time and the target rotates
            public bool Timed;
            public int PackSize;
            public double PackHp;
            public double Seconds => FirstStrike.HasValue && End.HasValue ? (End.Value - FirstStrike.Value).TotalSeconds : 0;
            public double MobSeconds => MobFirst.HasValue && End.HasValue ? (End.Value - MobFirst.Value).TotalSeconds : 0;
        }

        private class RankHits
        {
            public int Hits, Crits, Spells;
            public double Normal, True, SpellNormal, SpellTrue;
        }

        private class Run
        {
            public Player Player;
            public string Mode;                       // live / math / both
            public List<uint> Wcids;
            public int Fights, Samples;
            public float Power;

            public Phase Phase = Phase.Start;
            public DateTime PhaseAt = DateTime.UtcNow;
            public DateTime LastAttackOrder = DateTime.MinValue;
            public DateTime LastStatus = DateTime.MinValue;
            public bool StopRequested;
            public bool ModeAsked;                    // melee combat mode requested for this Engage
            public string StopReason;

            public int TargetIdx = -1, FightIdx;
            public Creature Mob;
            public Fight Cur;
            public List<Fight> Done = new List<Fight>();
            public string TargetName = "", TargetRank = "";
            public Dictionary<string, string> MathRow;   // re-sent with the live swing time when mode = both

            // Pack mode (owner 2026-10-01): Wcids are generators; their spawn list is fought at once with real health
            public bool Pack;
            public int HealPct;                       // heal below this share of max HP; 0 = never
            public readonly List<Creature> PackMobs = new List<Creature>();
            public DateTime? HealStarted;             // the kit use in progress
            public bool HealCancelAsked;              // the swing was told to stop so the kit can be used
            public string PackName = "";              // the row name: "<generator>: Leader x1 + Regular x3"
            public int TimedSeconds, SwitchSeconds;   // timed pack: fight length and target rotation; 0 = fight to the kill
            public DateTime LastSwitch;

            // Weapon sweep (owner 2026-10-01): Equip + this same run, once per weapon
            public readonly Dictionary<uint, string> MobRank = new Dictionary<uint, string>();   // rank by monster guid (a per-hit cache)

            // Matrix (owner 2026-10-02): the step's monster overrides, and the character state the steps change
            public MobOverlay Overlay;
            public uint[] SavedAugs;
            public long[] SavedCharms;   // Triune + the four attack growth charms, put back at the end
            public int LastGearTier;
            public string LastGearKey;
            public bool AugsDirty, AugsChanged, BuffsOff;

            public SweepSpec Sweep;
            public int SweepIdx = -1;                 // the weapon of the current step
            public int SweepStep = -1, SweepCycle = 1;   // step within the cycle (weapon-major inside each power bar)
            public DateTime? GearAskedAt;
            // a matrix step's variation (2026-10-02): the bench teleports the player to the same spot at that layer
            // and waits for the arrival before the step's gear, so one plan can walk Tou Tou 16 .. 25
            public int? TravelTo;
            public DateTime TravelAskedAt;
            public DateTime? ArrivedAt;
            // the weapon each sweep key was forged as on its first Equip - later steps wear that same one again
            // (2026-10-01: every Equip forged a new copy, which fills the pack on a looping sweep)
            public readonly Dictionary<string, uint> SweepWeapons = new Dictionary<string, uint>();
            // a looping sweep's fights per weapon + power bar + target: each lap adds to the same row
            public readonly Dictionary<string, List<Fight>> SweepFights = new Dictionary<string, List<Fight>>();

            // player state restored at the end
            public bool SavedUnkillable;
            public bool? SavedAttackable;
        }

        private static readonly ConcurrentDictionary<uint, Run> Runs = new ConcurrentDictionary<uint, Run>();        // by player guid
        private static readonly ConcurrentDictionary<uint, Run> MobRuns = new ConcurrentDictionary<uint, Run>();     // by bench monster guid
        private static readonly ConcurrentDictionary<uint, DateTime> DeadBench = new ConcurrentDictionary<uint, DateTime>(); // killed bench monsters until their corpse step (outlives the run)
        private static readonly ConcurrentDictionary<uint, string> GearText = new ConcurrentDictionary<uint, string>(); // what /bench gear last built
        private static readonly ConcurrentDictionary<uint, DateTime> GearDoneAt = new ConcurrentDictionary<uint, DateTime>(); // when /bench gear last finished wearing (a sweep waits on it)

        /// <summary>/bench sweep (owner 2026-10-01): the loadout every weapon of the sweep is Equipped with.</summary>
        public sealed class SweepSpec
        {
            public List<string> Weapons;
            public int Tier, Quality;
            public bool Bis;
            public string Element;
            // owner 2026-10-01: every weapon at the first bar, then every weapon at the next bar ... (null = the run's
            // own power); Loop = start over after the last one, until /bench stop
            public List<float> Powers;
            public bool Loop;
            // Matrix (owner 2026-10-02, CombatBenchMatrix.cs): the plan's combinations replace the weapon x power grid
            public List<MatrixStep> Matrix;
            public string PlanName, ResultsPath;
            public int TakenSamples, DealtSamples, SpellSamples;   // SpellSamples: the monster's SPELL on the player (2026-10-02)
            public uint? SpellId;
            public double SwingRate = 1.0;
            public int StepCount => Matrix?.Count ?? Weapons.Count * (Powers?.Count ?? 1);
        }

        // =========================================================================================================
        // Hooks (called from the damage + death paths; a cheap no-op while no bench run exists)
        // =========================================================================================================

        /// <summary>Player.DamageTarget, right after the damage is calculated: one melee strike by the bench player.</summary>
        public static void OnPlayerStrike(Player player, Creature target, DamageEvent de)
        {
            if (Runs.IsEmpty || player == null || target == null || de == null) return;
            if (!Runs.TryGetValue(player.Guid.Full, out var run)) return;
            lock (run)
            {
                if (run.Cur == null || !IsRunTarget(run, target) || run.Cur.End.HasValue) return;
                var f = run.Cur;
                f.FirstStrike ??= DateTime.UtcNow;
                f.Strikes++;
                if (!de.HasDamage) return;
                f.Hits++;
                if (de.IsCritical) { f.Crits++; f.CritSum += de.Damage; }
                else f.NormalSum += de.Damage;
                KeepAlive(f, target, de.Damage);
            }
        }

        /// <summary>Timed pack: a strike or proc that would kill a pack monster refills it first (this runs before the
        /// damage is applied), so the monster never dies; the damage still counts in full.</summary>
        private static void KeepAlive(Fight f, Creature target, float damage)
        {
            if (f.Timed && target != null && !target.IsDead && damage >= target.Health.Current)
                target.UpdateVital(target.Health, target.Health.MaxValue);
        }

        /// <summary>Monster melee, right after the damage is calculated, when the target is a player.</summary>
        public static void OnMonsterStrike(Creature mob, Player target, DamageEvent de)
        {
            if (MobRuns.IsEmpty || mob == null || de == null) return;
            if (!MobRuns.TryGetValue(mob.Guid.Full, out var run) || run.Player != target) return;
            lock (run)
            {
                if (run.Cur == null || run.Cur.End.HasValue) return;
                var f = run.Cur;
                f.MobFirst ??= DateTime.UtcNow;
                f.MobAttacks++;
                if (!de.HasDamage) return;
                f.MobHits++;
                if (de.IsCritical) f.MobCrits++;
                f.MobMeleeSum += de.Damage;
                f.MobTrueSum += de.TrueDamage;
                var rh = RankHitsOf(run, f, mob);
                rh.Hits++;
                if (de.IsCritical) rh.Crits++;
                rh.Normal += de.Damage - de.TrueDamage;
                rh.True += de.TrueDamage;
            }
        }

        /// <summary>The fight's per-rank split for this monster (its rank looked up once per monster).</summary>
        private static RankHits RankHitsOf(Run run, Fight f, Creature mob)
        {
            if (!run.MobRank.TryGetValue(mob.Guid.Full, out var rank))
                run.MobRank[mob.Guid.Full] = rank = RankOf(run.Player, mob.WeenieClassId);
            if (!f.ByRank.TryGetValue(rank, out var rh))
                f.ByRank[rank] = rh = new RankHits();
            return rh;
        }

        /// <summary>Spell health damage (SpellProjectile.DamageTarget, before Cheat Death / Battle Mending can change it):
        /// the monster's spells on the bench player, and the bench player's procs on the monster.</summary>
        public static void OnSpellDamage(WorldObject source, Creature target, float damage, float trueDamage = 0f)
        {
            if (Runs.IsEmpty || source == null || target == null) return;
            if (target is Player p && MobRuns.TryGetValue(source.Guid.Full, out var run) && run.Player == p)
            {
                lock (run)
                {
                    if (run.Cur == null || run.Cur.End.HasValue) return;
                    run.Cur.MobFirst ??= DateTime.UtcNow;
                    run.Cur.MobSpells++;
                    run.Cur.MobSpellSum += damage;
                    run.Cur.MobTrueSum += trueDamage;
                    if (source is Creature caster)
                    {
                        var rh = RankHitsOf(run, run.Cur, caster);
                        rh.Spells++;
                        rh.SpellNormal += damage - trueDamage;
                        rh.SpellTrue += trueDamage;
                    }
                }
            }
            else if (source is Player sp && Runs.TryGetValue(sp.Guid.Full, out var run2) && IsRunTarget(run2, target))
            {
                lock (run2)
                {
                    if (run2.Cur != null && !run2.Cur.End.HasValue)
                    {
                        run2.Cur.ProcSum += damage;
                        KeepAlive(run2.Cur, target, damage);
                    }
                }
            }
        }

        /// <summary>Creature.OnDeath: true for a bench monster - the fight is closed and the caller skips kill tasks,
        /// XP and Bounty.</summary>
        public static bool OnBenchDeath(Creature creature)
        {
            if (MobRuns.IsEmpty || creature == null) return false;
            if (!MobRuns.TryGetValue(creature.Guid.Full, out var run)) return false;
            DeadBench[creature.Guid.Full] = DateTime.UtcNow;
            foreach (var old in DeadBench.Where(kv => (DateTime.UtcNow - kv.Value).TotalMinutes > 5).ToList())
                DeadBench.TryRemove(old.Key, out _);
            lock (run)
            {
                if (run.Pack)
                {
                    // the fight ends when the whole pack is down; the step loop moves to the next monster before that
                    if (run.Cur != null && !run.Cur.End.HasValue && run.PackMobs.Contains(creature)
                        && ++run.Cur.MobsKilled >= run.PackMobs.Count)
                    {
                        run.Cur.End = DateTime.UtcNow;
                        run.Cur.Killed = true;
                    }
                }
                else if (run.Mob == creature && run.Cur != null && !run.Cur.End.HasValue)
                {
                    run.Cur.End = DateTime.UtcNow;
                    run.Cur.Killed = true;
                }
            }
            return true;
        }

        /// <summary>Healer.DoHealing: a kit use by the bench player on themself, landed or failed (Pack mode).</summary>
        public static void OnHeal(Player healer, Creature target, bool landed, uint amount)
        {
            if (Runs.IsEmpty || healer == null || healer != target) return;
            if (!Runs.TryGetValue(healer.Guid.Full, out var run)) return;
            lock (run)
            {
                if (!run.Pack || run.Cur == null || run.Cur.End.HasValue) return;
                if (landed) { run.Cur.Heals++; run.Cur.HealSum += amount; }
                else run.Cur.HealFails++;
            }
        }

        /// <summary>The monster being fought - or, in Pack mode, any monster of the pack (cleave and procs hit the others).</summary>
        private static bool IsRunTarget(Run run, Creature target) =>
            target != null && (run.Mob == target || (run.Pack && run.PackMobs.Contains(target)));

        /// <summary>Creature.Die: is this a bench monster (death emote + siphon lens skipped)? Does not forget it.</summary>
        public static bool IsBench(Creature creature) =>
            creature != null && (DeadBench.ContainsKey(creature.Guid.Full) || MobRuns.ContainsKey(creature.Guid.Full));

        /// <summary>Creature.CreateCorpse: a bench monster leaves no corpse and no loot (and is forgotten here).</summary>
        public static bool IsBenchMob(Creature creature)
        {
            if (creature == null) return false;
            var dead = DeadBench.TryRemove(creature.Guid.Full, out _);
            if (MobRuns.TryRemove(creature.Guid.Full, out var run))
            {
                lock (run)
                {
                    if (run.Mob == creature) run.Mob = null;
                }
                return true;
            }
            return dead;
        }

        // =========================================================================================================
        // Commands
        // =========================================================================================================

        public static bool IsRunning(Player player) => player != null && Runs.ContainsKey(player.Guid.Full);

        public static void SendStatus(Player player)
        {
            if (player?.Session == null) return;
            Runs.TryGetValue(player.Guid.Full, out var run);
            var loc = player.Location;
            var variation = ZoneControlManager.GetEffectiveVariation(player);
            var zone = loc != null ? ZoneControlManager.ResolveWinnerForLocation(loc.LandblockId.Landblock, variation)?.Name ?? "" : "";
            var sb = new StringBuilder("[[CBS]]");
            sb.Append("enabled=").Append(ServerConfig.combat_bench_enabled.Value ? 1 : 0);
            sb.Append("|busy=").Append(run != null ? 1 : 0);
            sb.Append("|char=").Append(Wire(player.Name));
            sb.Append("|var=").Append(variation);
            sb.Append("|zone=").Append(Wire(zone));
            sb.Append("|gear=").Append(Wire(GearText.TryGetValue(player.Guid.Full, out var g) ? g : ""));
            if (run != null)
            {
                lock (run)
                {
                    var sweepPart = run.Sweep != null && run.SweepIdx >= 0 && run.SweepIdx < run.Sweep.Weapons.Count
                        ? (run.Sweep.Loop ? $"lap {run.SweepCycle}, " : "")
                          + (run.Sweep.Matrix != null && run.SweepStep >= 0 && run.SweepStep < run.Sweep.Matrix.Count
                              ? $"matrix {run.Sweep.PlanName} {run.SweepStep + 1}/{run.Sweep.StepCount} {run.Sweep.Matrix[run.SweepStep].Label}"
                              : $"sweep {run.SweepStep + 1}/{run.Sweep.StepCount} {run.Sweep.Weapons[run.SweepIdx]}")
                          + (run.Sweep.Powers != null ? $" power {run.Power.ToString("0.00", CultureInfo.InvariantCulture)}" : "") + ", "
                        : "";
                    sb.Append("|job=").Append(Wire($"{sweepPart}{(run.Pack ? "pack" : run.Mode)} {run.TargetIdx + 1}/{run.Wcids.Count} {run.TargetName}"));
                    var prog = run.Phase switch
                    {
                        Phase.Math => "math sampling",
                        Phase.Fighting => $"fight {run.FightIdx + 1}/{run.Fights} {(run.Cur?.FirstStrike != null ? (DateTime.UtcNow - run.Cur.FirstStrike.Value).TotalSeconds : 0):0} s",
                        _ => run.Phase.ToString().ToLowerInvariant(),
                    };
                    sb.Append("|progress=").Append(Wire(prog));
                }
            }
            Send(player, sb.ToString());
        }

        /// <summary>/bench gear: builds the set (the premade suit - minted, or the held copy re-used - the premade weapon and
        /// the aetheria), takes off only what is not part of it, wears it a second later, then /buff (which also casts
        /// the worn aetheria's self surges). Character stats are NOT touched here (the tab sends the preset first).
        /// <paramref name="destroyOld"/> (owner 2026-09-28, an explicit exception to "never delete player items", test
        /// gear on the caller's own character only): every bench-made piece the character holds that is NOT part of the
        /// new set - worn or in a pack - is destroyed instead of packed. See <see cref="IsBenchGear"/>.
        /// <paramref name="weaponQuality"/> (owner 2026-09-29): the weapon's quality AND the fixed grade of its four premade
        /// cards (Rend, Slayer, Biting, Crushing) - repeatable forges; the tab's default is grade B = 850.</summary>
        public static void Gear(Session session, int tier, bool bis, string weaponKey, string elementKey, bool destroyOld = false, int weaponQuality = 1000,
            bool peaceRetried = false, uint reuseWeaponGuid = 0, long[] charmsAfterForge = null)
        {
            var player = session.Player;
            var mode = bis ? "bis" : "avg";
            var cls = WeaponScalingCommands.ForgeClasses.FirstOrDefault(c => c.Key == weaponKey);
            var element = WeaponScalingCommands.ParseElement(elementKey);
            if (cls.Key == null || element == null)
            {
                Log(player, $"gear: unknown weapon '{weaponKey}' or element '{elementKey}'.");
                return;
            }
            // owner 2026-10-01: the bow joins the melee weapons (Infinite Deadly Prismatic Arrow); crossbow, atlatl and
            // wand are in the forge list but have no bench ammo / attack path yet
            if (cls.Key == "crossbow" || cls.Key == "atlatl" || cls.Key == "wand")
            {
                Log(player, $"gear: the bench fights with melee weapons and the bow only - not '{cls.Key}' yet.");
                return;
            }
            var isBow = cls.Key == "bow";

            // 0. weapon away first: a drawn weapon only comes off after the stance animation, too late for this pass.
            //    2026-09-29 (owner): the bench puts you in peace mode itself (a fight leaves you in combat mode, and the
            //    refusal silently left the old tier's gear on), then runs this again once the stance animation is done.
            if (player.CombatMode != CombatMode.NonCombat)
            {
                if (peaceRetried)
                {
                    Log(player, "gear: could not get you into peace mode - nothing was changed. Put your weapon away and Equip again.");
                    return;
                }
                Log(player, "gear: switching to peace mode first...");
                player.HandleActionChangeCombatMode(CombatMode.NonCombat, false, () =>
                {
                    var retry = new ActionChain();
                    retry.AddDelaySeconds(0.5);
                    retry.AddAction(player, ActionType.CombatBench_Tick, () =>
                    {
                        try { Gear(session, tier, bis, weaponKey, elementKey, destroyOld, weaponQuality, peaceRetried: true, reuseWeaponGuid: reuseWeaponGuid, charmsAfterForge: charmsAfterForge); }
                        catch (Exception ex) { log.Error($"[CombatBench] gear retry {player.Name}: {ex}"); Log(player, "gear: failed - " + ex.Message); }
                    });
                    retry.EnqueueChain();
                });
                return;
            }

            // 1. room first (2026-09-28: a full main pack made an unequip fail half-way and strand a wielded item in
            //    memory). Worst case: everything worn comes off, and 18 suit pieces + the weapon + 3 aetheria go in
            //    (+ the arrows for a bow).
            var worn = player.EquippedObjects.Values.ToList();
            var needed = worn.Count + 18 + 1 + 3 + (isBow ? 1 : 0);
            var free = player.GetFreeInventorySlots(true);
            if (free < needed)
            {
                Log(player, $"gear: needs {needed} free pack slots (main pack + side packs), you have {free}. Clear some space - nothing was changed.");
                return;
            }

            // 2. build the set BEFORE undressing: the suit (minted, or the held copy re-used), the weapon (forged, or the
            //    held one with the same name at this tier), aetheria (mint-if-missing)
            var suit = new List<WorldObject>();
            TestCharacterCommands.HandleAsForgePremade(session, player, new[] { "premade", tier.ToString(CultureInfo.InvariantCulture), mode }, suit, raiseItemAugsOnly: true);
            // a matrix step's Triune + attack charms (2026-10-02): the forge just pinned them to the tier's wield minimum -
            // put the step's own values back BEFORE the wear + rebuff, so the buffs are cast at the step's aug level
            if (charmsAfterForge != null)
                SetCharms(player, charmsAfterForge);

            var tag = bis ? "BiS" : "Avg";   // the SUIT's label (the weapon's grade is its own, weaponQuality)
            // T10 (owner 2026-09-28): the T10 loot-table weapon with NO cards - the four premade cards are T11+ Zone Control
            // lines, which a real T10 weapon never carries. The forge names it "(Test T10)".
            var q = Math.Clamp(weaponQuality, 0, 1000).ToString(CultureInfo.InvariantCulture);
            var weaponName = tier == 10 ? $"{element.Value} {cls.CleanName} (Test T10)" : $"{element.Value} {cls.CleanName} (Test q{q} Bench)";
            bool IsThisWeapon(WorldObject i) => i.Name == weaponName
                && (tier == 10 || (i.GetProperty(ACE.Entity.Enum.Properties.PropertyInt.WeaponAugScaleTier) ?? 0) == tier);   // a T10 forge stamps no scale tier

            // a looping sweep (2026-10-01) wears the copy it forged on its first lap again instead of forging another
            var weapon = reuseWeaponGuid != 0
                ? player.GetAllPossessionsDeep().FirstOrDefault(i => i.Guid.Full == reuseWeaponGuid && IsThisWeapon(i))
                : null;
            if (weapon == null)
            {
                var held = new HashSet<uint>(player.GetAllPossessionsDeep().Where(IsThisWeapon).Select(i => i.Guid.Full));
                if (tier == 10)
                    WeaponScalingCommands.HandleWsForge(session, cls.Key, "1000", elementKey, "10");
                else
                    WeaponScalingCommands.HandleWsForge(session, cls.Key, q, elementKey, tier.ToString(CultureInfo.InvariantCulture), $"cards:premade={mode},premadegrade={q}");
                // the copy just forged - not an older one with the same name (2026-10-01: four identical claws were held)
                var matches = player.GetAllPossessionsDeep().Where(IsThisWeapon).ToList();
                weapon = matches.FirstOrDefault(i => !held.Contains(i.Guid.Full)) ?? matches.FirstOrDefault();
            }

            // owner 2026-10-01: a T11+ bench weapon carries ZcTier like a real drop (LootGenerationFactory_ZoneSet
            // stamps it on every T11 weapon). Without it EndgameRulesApply read the bench weapon as RETAIL gear, so
            // crits used the retail base (no flats) - the claw's crits read ~3x and the bow's ~9x, neither what a drop
            // does. A held copy from before this is stamped on its next Equip - taken OFF first if worn, because the
            // worn-ZC-gear count and rating cache are kept by the equip/dequip pair (Creature_Equipment.cs): stamping a
            // worn item would make its later dequip subtract what its equip never added. The wear step puts it back on.
            if (weapon != null && tier >= 11 && (weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyInt.ZcTier) ?? 0) != tier)
            {
                if (weapon.CurrentWieldedLocation != null)
                {
                    var pack = FreePack(player);
                    if (pack != null)
                        player.HandleActionPutItemInContainer(weapon.Guid.Full, pack.Guid.Full, 0);
                }
                if (weapon.CurrentWieldedLocation == null)
                    weapon.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.ZcTier, tier);
                else
                    Log(player, $"gear: {weapon.Name} would not come off to take its T{tier} stamp - it fights as retail gear.");
            }

            TestCharacterCommands.HandleTestChar(session, "extra", "aetheria");
            // one aetheria per sigil slot: the one already worn there stays, an empty slot takes one from the pack
            var sigilPick = new Dictionary<EquipMask, WorldObject>();
            foreach (var a in player.GetAllPossessionsDeep().Where(i => Aetheria.IsAetheria(i.WeenieClassId))
                                    .OrderByDescending(i => i.CurrentWieldedLocation != null))
            {
                var slot = a.ValidLocations ?? EquipMask.None;
                if (slot != EquipMask.None && !sigilPick.ContainsKey(slot)) sigilPick[slot] = a;
            }

            var set = new HashSet<WorldObject>(suit);
            if (weapon != null) set.Add(weapon);
            foreach (var a in sigilPick.Values) set.Add(a);

            // a bow's arrows (owner 2026-10-01): the Infinite Deadly Prismatic Arrow - never used up, takes the bow's
            // element. The held one is re-used; otherwise one is made.
            if (isBow)
            {
                var arrows = player.GetAllPossessionsDeep().FirstOrDefault(i => i.WeenieClassId == BowArrowWcid);
                if (arrows == null)
                {
                    arrows = WorldObjectFactory.CreateNewWorldObject(BowArrowWcid);
                    if (arrows == null || !player.TryCreateInInventoryWithNetworking(arrows))
                    {
                        arrows?.Destroy();
                        arrows = null;
                        Log(player, "gear: could not make the Infinite Deadly Prismatic Arrows - the bow has no ammo.");
                    }
                }
                if (arrows != null) set.Add(arrows);
            }

            // 3. old bench gear: destroyed (destroyOld) - worn or in a pack - when it is not part of the new set
            var destroyed = 0;
            if (destroyOld)
            {
                foreach (var item in player.GetAllPossessionsDeep().Where(i => !set.Contains(i) && IsBenchGear(player, i)).ToList())
                {
                    var ok = item.CurrentWieldedLocation != null
                        ? player.TryDequipObjectWithNetworking(item.Guid, out _, Player.DequipObjectAction.ConsumeItem)
                        : player.TryConsumeFromInventoryWithNetworking(item);
                    if (ok) destroyed++;
                }
            }

            // 4. undress ONLY what is not part of the new set (2026-09-28: taking a piece off and wearing it again in
            //    the same instant left the client showing worn gear in the backpack) - into a pack with a free slot
            foreach (var item in player.EquippedObjects.Values.Where(w => !set.Contains(w)).ToList())
            {
                var target = FreePack(player);
                if (target == null)
                {
                    Log(player, "gear: ran out of pack space while undressing - stopped.");
                    return;
                }
                player.HandleActionPutItemInContainer(item.Guid.Full, target.Guid.Full, 0);
                if (item.CurrentWieldedLocation != null)
                {
                    Log(player, $"gear: {item.Name} would not come off - stopped.");
                    return;
                }
            }
            if (destroyed > 0)
                Log(player, $"gear: destroyed {destroyed} old bench piece(s).");

            // 5. wear a second later (2026-09-28): a just-minted item's create message travels in the Smartbox queue and
            //    the wield event in the UI queue, and the two are not ordered - a wield that overtakes the create is
            //    dropped by the client, which then shows the worn piece in the backpack. One second lets it arrive.
            var chain = new ActionChain();
            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, ActionType.CombatBench_Tick, () =>
            {
                try { WearSet(session, tier, tag, suit, weapon, set, sigilPick); }
                catch (Exception ex) { log.Error($"[CombatBench] gear wear {player.Name}: {ex}"); Log(player, "gear: failed while wearing - " + ex.Message); }
            });
            chain.EnqueueChain();
        }

        private static void WearSet(Session session, int tier, string tag, List<WorldObject> suit, WorldObject weapon,
            HashSet<WorldObject> set, Dictionary<EquipMask, WorldObject> sigilPick)
        {
            var player = session.Player;
            if (player?.Session == null) return;

            // wear what is not worn yet (a piece already on stays exactly where it is); aetheria last
            var failed = new List<string>();
            foreach (var item in set.Where(i => i.CurrentWieldedLocation == null && !i.IsDestroyed).OrderBy(i => Aetheria.IsAetheria(i.WeenieClassId)))
                if (!Wear(player, item)) failed.Add(item.Name);
            var sigils = sigilPick.Values.Count(a => a.CurrentWieldedLocation != null);

            // dispel, THEN buff (owner 2026-09-29): a recast never replaces a stronger buff already on you (one cast at
            // higher augs, or another tier's), so old buffs skew every number. DispelAllEnchantments removes the timed
            // enchantments only - gear spells, set bonuses and vitae are indefinite and stay. Item buffs (Blood Drinker,
            // Impen, banes ...) live on each worn piece, so those are dispelled piece by piece.
            player.EnchantmentManager.DispelAllEnchantments();
            foreach (var worn in player.EquippedObjects.Values.ToList())
                worn.EnchantmentManager.DispelAllEnchantments();

            // buffs (+ worn aetheria self surges)
            SentinelCommands.HandleBuff(session);
            Log(player, "gear: dispelled you and your worn gear, then buffed fresh.");

            var text = $"T{tier} {tag} suit ({suit.Count(s => s.CurrentWieldedLocation != null)}/{suit.Count} worn), "
                + (weapon != null ? weapon.Name : "NO WEAPON") + $", {sigils} aetheria, level {player.Level}, item augs {player.LuminanceAugmentItemCount ?? 0}";
            GearText[player.Guid.Full] = text;
            GearDoneAt[player.Guid.Full] = DateTime.UtcNow;
            Log(player, "gear: " + text + (failed.Count > 0 ? " - NOT worn: " + string.Join(", ", failed) : ""));
            if (suit.Count != 18)
                Log(player, $"gear: expected 18 suit pieces, got {suit.Count} - see the forge lines above.");
            SendStatus(player);
        }

        private static readonly System.Text.RegularExpressions.Regex PremadePieceName =
            new System.Text.RegularExpressions.Regex(@"^T\d{1,2} .+ \((BiS|Avg)\)$");
        private static readonly System.Text.RegularExpressions.Regex ForgeWeaponName =
            new System.Text.RegularExpressions.Regex(@"\(Test (q\d+|T10)( BiS| Avg| Bench)?\)$");

        /// <summary>A piece the bench's forges made for THIS character: a premade suit piece ("T11 Helm (BiS)", provenance
        /// "Premade: BiS|Avg") or a forge test weapon ("Fire Claw (Test q1000 BiS)"), both stamped "Created by: {name}".
        /// Aetheria, charges and anything else never match.</summary>
        private static bool IsBenchGear(Player player, WorldObject item)
        {
            if (item == null || item is Container || Aetheria.IsAetheria(item.WeenieClassId)) return false;
            var desc = item.LongDesc ?? "";
            if (!desc.Contains("Created by: " + player.Name)) return false;
            var name = item.Name ?? "";
            if (PremadePieceName.IsMatch(name) && desc.Contains("Premade: ")) return true;
            return ForgeWeaponName.IsMatch(name);
        }

        /// <summary>The main pack if it has a free slot, else the first side pack that does; null = everything is full.</summary>
        private static Container FreePack(Player player)
        {
            if (player.GetFreeInventorySlots(false) > 0) return player;
            return player.Inventory.Values.OfType<Container>().FirstOrDefault(c => c.GetFreeInventorySlots(false) > 0);
        }

        private static bool Wear(Player player, WorldObject item)
        {
            if (item == null) return false;
            var valid = item.ValidLocations ?? EquipMask.None;
            var slot = valid;
            foreach (var pair in new[] { EquipMask.FingerWear, EquipMask.WristWear })
            {
                if ((valid & pair) != pair) continue;
                var left = pair == EquipMask.FingerWear ? EquipMask.FingerWearLeft : EquipMask.WristWearLeft;
                var right = pair == EquipMask.FingerWear ? EquipMask.FingerWearRight : EquipMask.WristWearRight;
                slot = !Occupied(player, left) ? left : right;
            }
            player.HandleActionGetAndWieldItem(item.Guid.Full, slot);
            return item.CurrentWieldedLocation != null;
        }

        private static bool Occupied(Player player, EquipMask slot) =>
            player.EquippedObjects.Values.Any(e => ((e.CurrentWieldedLocation ?? EquipMask.None) & slot) != 0);

        /// <summary>/bench run. <paramref name="sweep"/> (owner 2026-10-01): Equip + this run once per weapon.</summary>
        public static void Start(Player player, string mode, List<uint> wcids, int fights, int samples, float power, SweepSpec sweep = null)
        {
            var run = new Run
            {
                Player = player,
                Mode = mode,
                Wcids = wcids,
                Fights = Math.Clamp(fights, 1, 10),
                Samples = Math.Clamp(samples, 100, MaxMathSamples),
                Power = Math.Clamp(power, 0f, 1f),
                Sweep = sweep,
            };
            if (!Runs.TryAdd(player.Guid.Full, run))
            {
                Log(player, "run: a run is already going - /bench stop first.");
                return;
            }
            Log(player, $"run: {mode}, {wcids.Count} target(s), {run.Fights} fight(s), {run.Samples} math samples, power {run.Power:0.00}."
                + SweepText(sweep));
            Schedule(run, 0.05);
        }

        private static string SweepText(SweepSpec sweep) =>
            sweep == null ? "" : $" Sweep: {string.Join(", ", sweep.Weapons)} (T{sweep.Tier} {(sweep.Bis ? "BiS" : "Avg")}, {sweep.Element}, q{sweep.Quality})"
                + (sweep.Powers != null ? $", each at power {string.Join(" then ", sweep.Powers.Select(p => p.ToString("0.00", CultureInfo.InvariantCulture)))}" : "")
                + (sweep.Loop ? ", repeating until Stop" : "") + ".";

        /// <summary>/bench pack (owner 2026-10-01): each generator's spawn list is spawned at once and fought one monster
        /// after another, with REAL health (stamina + mana stay full) and the Eternal Health Kit used below
        /// <paramref name="healPct"/> of max HP. Unkillable stays on as the net: reaching 1 HP counts as a death.
        /// <paramref name="timedSeconds"/> &gt; 0 (owner 2026-10-01): the pack never dies - each fight lasts that long and
        /// the target moves to the next monster every <paramref name="switchSeconds"/>.</summary>
        public static void StartPack(Player player, List<uint> generators, int fights, int healPct, float power,
            int timedSeconds = 0, int switchSeconds = 10, SweepSpec sweep = null, int mathSamples = 0)
        {
            var run = new Run
            {
                Player = player,
                // owner 2026-10-01: "math and normal (both)" - mathSamples > 0 samples the pack's first monster before
                // each live fight (the Both flow), so damage dealt comes out exact while the fight measures damage taken
                Mode = mathSamples > 0 ? "both" : "live",
                Pack = true,
                Wcids = generators,
                Fights = Math.Clamp(fights, 1, 10),
                Samples = mathSamples > 0 ? Math.Clamp(mathSamples, 100, MaxMathSamples) : 0,
                Power = Math.Clamp(power, 0f, 1f),
                HealPct = Math.Clamp(healPct, 0, 95),
                TimedSeconds = timedSeconds > 0 ? Math.Clamp(timedSeconds, 20, 300) : 0,
                SwitchSeconds = Math.Clamp(switchSeconds, 3, 30),
                Sweep = sweep,
            };
            if (run.HealPct > 0)
            {
                if (player.GetCreatureSkill(Skill.Healing).AdvancementClass < SkillAdvancementClass.Trained)
                {
                    Log(player, "pack: Healing is not trained - train it, or use heal 0.");
                    return;
                }
                if (FindKit(player) == null)
                {
                    var kit = WorldObjectFactory.CreateNewWorldObject(HealthKitWcid);
                    if (kit == null || !player.TryCreateInInventoryWithNetworking(kit))
                    {
                        kit?.Destroy();
                        Log(player, "pack: could not give you an Eternal Health Kit (pack full?).");
                        return;
                    }
                    Log(player, "pack: an Eternal Health Kit was put in your pack.");
                }
            }
            if (!Runs.TryAdd(player.Guid.Full, run))
            {
                Log(player, "run: a run is already going - /bench stop first.");
                return;
            }
            Log(player, $"pack: {generators.Count} pack(s), {run.Fights} fight(s) each, heal below {(run.HealPct > 0 ? run.HealPct + " pct HP" : "never")}, power {run.Power:0.00}"
                + (run.TimedSeconds > 0 ? $", monsters never die: {run.TimedSeconds} s fights, switch every {run.SwitchSeconds} s." : ", fights to the kill.")
                + SweepText(sweep));
            Schedule(run, 0.05);
        }

        private const uint HealthKitWcid = 30247;   // Eternal Health Kit (retail rare: unlimited uses)
        private const uint BowArrowWcid = 4395100;  // Infinite Deadly Prismatic Arrow (owner 2026-10-01): damage 40, never used up

        /// <summary>The worn weapon's name without the forge's "(Test q850 Bench)" tail: "Fire Bow".</summary>
        private static string WeaponLabel(Player player)
        {
            var name = BenchWeapon(player)?.Name ?? "";
            var cut = name.IndexOf(" (Test", StringComparison.Ordinal);
            return cut > 0 ? name.Substring(0, cut) : name;
        }

        /// <summary>The worn melee weapon, else the worn bow (owner 2026-10-01).</summary>
        private static WorldObject BenchWeapon(Player player) => player.GetEquippedMeleeWeapon() ?? player.GetEquippedMissileWeapon();

        /// <summary>True when the bench fights with a bow: no melee weapon worn, a missile launcher is.</summary>
        private static bool UsesBow(Player player) => player.GetEquippedMeleeWeapon() == null && player.GetEquippedMissileWeapon() != null;

        /// <summary>What the player's attack loop is aimed at (melee or missile).</summary>
        private static Creature AttackTargetOf(Player player) => (UsesBow(player) ? player.MissileTarget : player.MeleeTarget) as Creature;

        private static Healer FindKit(Player player) =>
            player.GetInventoryItemsOfWCID(HealthKitWcid).OfType<Healer>().FirstOrDefault();

        public static void Stop(Player player)
        {
            if (player == null || !Runs.TryGetValue(player.Guid.Full, out var run))
            {
                Log(player, "stop: nothing is running.");
                return;
            }
            lock (run) { run.StopRequested = true; run.StopReason = "stopped"; }
        }

        /// <summary>Logout / teleport safety: end the run and remove its monster.</summary>
        public static void Abort(Player player, string reason)
        {
            if (player == null || !Runs.TryGetValue(player.Guid.Full, out var run)) return;
            lock (run) { run.StopRequested = true; run.StopReason = reason; }
        }

        // =========================================================================================================
        // The run's step machine (one tick every 0.25 s on the player's own thread)
        // =========================================================================================================

        private static void Schedule(Run run, double delay)
        {
            var chain = new ActionChain();
            chain.AddDelaySeconds(delay);
            chain.AddAction(run.Player, ActionType.CombatBench_Tick, () => Tick(run));
            chain.EnqueueChain();
        }

        private static void Tick(Run run)
        {
            var again = true;
            try
            {
                lock (run) { again = Step(run); }
            }
            catch (Exception ex)
            {
                log.Error($"[CombatBench] {run.Player?.Name}: {ex}");
                lock (run) { Finish(run, "error", ex.Message); }
                again = false;
            }
            if (again) Schedule(run, TickSeconds);
        }

        /// <summary>Advances the run one step. False = finished.</summary>
        private static bool Step(Run run)
        {
            var player = run.Player;
            var now = DateTime.UtcNow;

            // the bench's own teleport to a matrix step's variation: wait it out instead of ending the run
            if (run.TravelTo is int travelTo && player.Session != null && !run.StopRequested)
            {
                var there = !player.Teleporting && player.CurrentLandblock != null && (player.Location?.Variation ?? 0) == travelTo;
                if (!there)
                {
                    if ((now - run.TravelAskedAt).TotalSeconds > TravelTimeoutSeconds)
                    {
                        run.TravelTo = null;
                        Finish(run, "error", $"the teleport to variation {travelTo} did not finish in {TravelTimeoutSeconds:0} s");
                        return false;
                    }
                    return true;
                }
                run.ArrivedAt ??= now;
                if ((now - run.ArrivedAt.Value).TotalSeconds < TravelSettleSeconds)
                    return true;   // the landblock's objects and the zone snapshot settle before anything spawns
                var zone = ZoneControlManager.ResolveWinnerForLocation(player.Location.LandblockId.Landblock, ZoneControlManager.GetEffectiveVariation(player))?.Name;
                Log(player, $"matrix: arrived at variation {travelTo} - zone {(zone ?? "NONE (the tier settings do not reach monsters here)")}.");
                run.TravelTo = null;
                run.ArrivedAt = null;
            }

            if (player.Session == null || player.CurrentLandblock == null || player.Teleporting)
            {
                Finish(run, "stopped", "you left the world or teleported");
                return false;
            }
            if (run.StopRequested)
            {
                Finish(run, run.StopReason ?? "stopped", "by request");
                return false;
            }
            if (!ServerConfig.combat_bench_enabled.Value)
            {
                Finish(run, "stopped", "combat_bench_enabled was turned off");
                return false;
            }

            if ((now - run.LastStatus).TotalSeconds >= 2)
            {
                run.LastStatus = now;
                SendStatus(player);
            }

            switch (run.Phase)
            {
                case Phase.Start:
                    // nothing spawns without a melee weapon in hand (2026-09-28: Math ran weaponless and logged two
                    // errors per sample). A sweep Equips each weapon itself, so it checks after each Equip instead.
                    if (run.Sweep == null && WeaponProblem(player) is string problem)
                    {
                        Finish(run, "error", problem);
                        return false;
                    }
                    run.SavedUnkillable = player.IsUnkillable;
                    run.SavedAttackable = player.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.Attackable);
                    player.IsUnkillable = true;
                    player.UpdateProperty(player, ACE.Entity.Enum.Properties.PropertyBool.Attackable, true, true);
                    if (player.CloakStatus != CloakStatus.Off && player.CloakStatus != CloakStatus.Undef)
                    {
                        SentinelCommands.HandleCloak(player.Session, "off");
                        Log(player, "run: cloak turned off (monsters do not attack a cloaked admin).");
                    }
                    if (run.Sweep?.Matrix != null)
                    {
                        run.SavedAugs = CurrentAugs(player);   // put back at the end (MatrixRestore)
                        run.SavedCharms = CurrentCharms(player);
                    }
                    if (run.Sweep != null)
                        return NextSweepWeapon(run);
                    SetPhase(run, Phase.NextTarget);
                    return true;

                case Phase.Gear:
                    {
                        // a weapon sweep (owner 2026-10-01): Equip this weapon, wait for the wear step, then run the targets
                        var key = run.Sweep.Weapons[run.SweepIdx];
                        var mstep = run.Sweep.Matrix?[run.SweepStep];
                        var gearTier = mstep?.GearTier ?? run.Sweep.Tier;   // a matrix step may wear another tier's gear (2026-10-02)
                        var reuseKey = key + "@" + gearTier.ToString(CultureInfo.InvariantCulture);
                        // a step at another variation (2026-10-02): the same spot at that layer first - the monsters
                        // spawn in the player's layer, so they take that zone's tier settings
                        if (mstep?.Variation is int stepVar && run.GearAskedAt == null && (player.Location.Variation ?? 0) != stepVar)
                        {
                            Despawn(run);
                            Log(player, $"matrix: teleporting to variation {stepVar} (same spot) for step {run.SweepStep + 1}/{run.Sweep.StepCount}...");
                            run.TravelTo = stepVar;
                            run.TravelAskedAt = now;
                            run.ArrivedAt = null;
                            var dest = new ACE.Entity.Position(player.Location) { Variation = stepVar == 0 ? null : stepVar };
                            WorldManager.ThreadSafeTeleport(player, dest);
                            return true;
                        }
                        if (mstep != null && run.GearAskedAt == null && !MatrixPrepare(run, mstep))
                        {
                            // nothing to re-equip for this matrix step: straight to its targets
                            MatrixAfterGear(run, mstep, geared: false);
                            run.TargetIdx = -1;
                            SetPhase(run, Phase.NextTarget);
                            return true;
                        }
                        if (run.GearAskedAt == null)
                        {
                            Log(player, $"sweep {run.SweepStep + 1}/{run.Sweep.StepCount}{(run.Sweep.Loop ? $" (lap {run.SweepCycle})" : "")}: equipping {key}"
                                + (run.Sweep.Powers != null ? $", power {run.Power.ToString("0.00", CultureInfo.InvariantCulture)}" : "") + "...");
                            run.GearAskedAt = now;
                            run.SweepWeapons.TryGetValue(reuseKey, out var reuse);
                            // a matrix that changes gear tier destroys the old bench-made set (only items the forges stamped with this
                            // character) - 15 tiers of 18-piece suits would fill the pack (2026-10-02)
                            Gear(player.Session, gearTier, run.Sweep.Bis, key, run.Sweep.Element, mstep?.GearTier != null, run.Sweep.Quality, reuseWeaponGuid: reuse,
                                charmsAfterForge: mstep?.Triune is long tri ? Enumerable.Repeat(tri, CharmProps.Length).ToArray() : null);
                            return true;
                        }
                        if (GearDoneAt.TryGetValue(player.Guid.Full, out var doneAt) && doneAt >= run.GearAskedAt.Value)
                        {
                            if (WeaponProblem(player) is string wp)
                            {
                                Log(player, $"sweep: {key} skipped - {wp}.");
                                return NextSweepWeapon(run);
                            }
                            if (BenchWeapon(player) is WorldObject worn)
                                run.SweepWeapons[reuseKey] = worn.Guid.Full;
                            if (mstep != null)
                                MatrixAfterGear(run, mstep, geared: true);
                            run.TargetIdx = -1;
                            SetPhase(run, Phase.NextTarget);
                            return true;
                        }
                        if ((now - run.GearAskedAt.Value).TotalSeconds > 30)
                        {
                            Log(player, $"sweep: {key} did not finish equipping in 30 s - skipped (see the gear lines above).");
                            return NextSweepWeapon(run);
                        }
                        return true;
                    }

                case Phase.NextTarget:
                    run.TargetIdx++;
                    if (run.TargetIdx >= run.Wcids.Count)
                    {
                        if (run.Sweep != null)
                            return NextSweepWeapon(run);
                        Finish(run, "done", "");
                        return false;
                    }
                    run.FightIdx = 0;
                    if (run.Sweep?.Loop == true)
                    {
                        // each lap adds its fights to the same weapon + power bar + target row
                        var comboKey = $"{run.Sweep.Weapons[run.SweepIdx]}|{run.Power.ToString("0.00", CultureInfo.InvariantCulture)}|{run.Wcids[run.TargetIdx]}";
                        if (!run.SweepFights.TryGetValue(comboKey, out var combo))
                            run.SweepFights[comboKey] = combo = new List<Fight>();
                        run.Done = combo;
                    }
                    else
                        run.Done = new List<Fight>();
                    run.MathRow = null;
                    if (!(run.Pack ? SpawnPack(run, run.Wcids[run.TargetIdx]) : Spawn(run, run.Wcids[run.TargetIdx])))
                    {
                        Log(player, $"run: wcid {run.Wcids[run.TargetIdx]} could not be spawned - skipped.");
                        return true;   // next tick moves on
                    }
                    SetPhase(run, run.Mode == "live" ? Phase.Engage : Phase.Math);
                    return true;

                case Phase.Math:
                    if (run.Mob?.CurrentLandblock == null) return Waited(run, 5, "the monster never entered the world");
                    if (run.Pack)
                    {
                        // a pack (owner 2026-10-01): one math row per kind of monster in it (each rank has its own defenses)
                        var fightFirst = run.Mob;
                        foreach (var kind in run.PackMobs.Where(m => m != null && !m.IsDead && m.CurrentLandblock != null)
                                                         .GroupBy(m => m.WeenieClassId).Select(g => g.First()).ToList())
                        {
                            run.Mob = kind;
                            MathFor(run);
                        }
                        run.Mob = fightFirst;
                    }
                    else
                        MathFor(run);
                    if (run.Mode == "math")
                    {
                        Despawn(run);
                        SetPhase(run, Phase.NextTarget);
                    }
                    else
                        SetPhase(run, Phase.Engage);   // fight 1 re-uses the untouched monster
                    return true;

                case Phase.SpawnLive:
                    if (!(run.Pack ? SpawnPack(run, run.Wcids[run.TargetIdx]) : Spawn(run, run.Wcids[run.TargetIdx])))
                    {
                        Log(player, "run: re-spawn failed - target ended.");
                        SendLiveRow(run);
                        SetPhase(run, Phase.NextTarget);
                        return true;
                    }
                    SetPhase(run, Phase.Engage);
                    return true;

                case Phase.Engage:
                    if (run.Mob?.CurrentLandblock == null) return Waited(run, 5, "the monster never entered the world");
                    if (run.Pack) player.SetMaxVitals();   // every pack fight starts at full health
                    TopUp(run);
                    var wantMode = UsesBow(player) ? CombatMode.Missile : CombatMode.Melee;   // the bow: owner 2026-10-01
                    if (player.CombatMode != wantMode)
                    {
                        if (BenchWeapon(player) == null && player.EquippedObjects.Values.All(e => e.WeenieType != WeenieType.MeleeWeapon))
                        {
                            Finish(run, "error", "no melee weapon or bow worn - /bench gear first");
                            return false;
                        }
                        if (!run.ModeAsked)
                        {
                            run.ModeAsked = true;
                            player.HandleActionChangeCombatMode(wantMode);
                        }
                        return Waited(run, CombatModeWaitSeconds, $"could not enter {wantMode.ToString().ToLowerInvariant()} combat mode");
                    }
                    run.Cur = new Fight
                    {
                        Timed = run.Pack && run.TimedSeconds > 0,
                        PackSize = run.Pack ? run.PackMobs.Count : 1,
                        PackHp = run.Pack ? run.PackMobs.Sum(m => (double)m.Health.MaxValue) : run.Mob.Health.MaxValue,
                    };
                    run.LastSwitch = now;
                    // owner 2026-09-28: the plugin selects the bench monster once per fight, so the client's target
                    // is the monster being fought, not whatever was last clicked
                    Send(player, "[[CBT]]guid=" + run.Mob.Guid.Full.ToString(CultureInfo.InvariantCulture));
                    Attack(run);
                    SetPhase(run, Phase.Fighting);
                    return true;

                case Phase.Fighting:
                    TopUp(run);
                    if (run.Cur.Killed)
                    {
                        SetPhase(run, Phase.AfterFight);
                        return true;
                    }
                    if (run.Cur.Timed && TimedStep(run, now))
                        return true;   // the timed fight ended, or the target was just switched
                    if (run.Pack && (run.Mob == null || run.Mob.IsDead || run.Mob.CurrentLandblock == null))
                    {
                        // this monster is down: the next live one of the pack (the client selects it too)
                        run.Mob = run.PackMobs.FirstOrDefault(m => m != null && !m.IsDead && !m.IsDestroyed && m.CurrentLandblock != null);
                        if (run.Mob != null)
                        {
                            run.TargetName = run.Mob.Name;
                            Send(player, "[[CBT]]guid=" + run.Mob.Guid.Full.ToString(CultureInfo.InvariantCulture));
                            if (run.HealStarted == null && !run.HealCancelAsked && !player.Attacking)
                                Attack(run);
                            return true;
                        }
                    }
                    if (run.Mob == null || run.Mob.IsDead || run.Mob.CurrentLandblock == null)
                    {
                        // died without the death hook (should not happen) or despawned
                        run.Cur.End ??= now;
                        run.Cur.Killed = run.Pack ? run.PackMobs.All(m => m.IsDead) : run.Mob != null && run.Mob.IsDead;
                        SetPhase(run, Phase.AfterFight);
                        return true;
                    }
                    if ((now - run.PhaseAt).TotalSeconds > FightTimeoutSeconds)
                    {
                        run.Cur.End = now;
                        run.Cur.TimedOut = true;
                        Log(player, $"run: fight timed out after {FightTimeoutSeconds:0} s - counted as not killed.");
                        Despawn(run);
                        SetPhase(run, Phase.AfterFight);
                        return true;
                    }
                    if (run.Pack && HealStep(run, now))
                        return true;   // healing (or waiting for the swing to end so it can) - no new swing
                    // the client's Auto-repeat Attacks keeps the loop going on its own; with it off the swing ends
                    // with no target, and the next tick starts the next one (no extra wait padded into the kill time)
                    var aimedAt = AttackTargetOf(player);
                    if (!player.Attacking && (aimedAt == null || !aimedAt.IsAlive || aimedAt != run.Mob) && (now - run.LastAttackOrder).TotalSeconds >= TickSeconds)
                        Attack(run);
                    return true;

                case Phase.AfterFight:
                    if (run.Cur != null)
                    {
                        var f = run.Cur;
                        Log(player, (f.Timed
                                ? $"fight {run.FightIdx + 1}: {f.Seconds:0.0} s timed (monsters never die), {f.Strikes} strikes, {f.Crits} crits"
                                : $"fight {run.FightIdx + 1}: {(f.Killed ? "killed" : "NOT killed")} in {f.Seconds:0.0} s, {f.Strikes} strikes, {f.Crits} crits")
                            + $"; took {f.MobHits} melee hits + {f.MobSpells} spells."
                            + (run.Pack ? $" Pack {(f.Timed ? "" : f.MobsKilled + "/")}{f.PackSize}; {f.Heals} heals (+{f.HealFails} failed), low {f.LowHpPct:0} pct HP, {f.Deaths} death(s)." : "")
                            + HitSplitText(new[] { f }));
                        run.Done.Add(f);
                        run.Cur = null;
                        run.FightIdx++;
                        run.PhaseAt = now;
                        return true;
                    }
                    if ((now - run.PhaseAt).TotalSeconds < BetweenFightsSeconds) return true;
                    if (run.FightIdx < run.Fights)
                    {
                        SetPhase(run, Phase.SpawnLive);
                        return true;
                    }
                    SendLiveRow(run);
                    SetPhase(run, Phase.NextTarget);
                    return true;

                case Phase.Finish:
                    return false;
            }
            return true;
        }

        /// <summary>" Hits (normal + true): Regular 1 + 37 (351), Leader 2 + 99 (117)." - melee average per rank.</summary>
        private static string HitSplitText(IEnumerable<Fight> fights)
        {
            var parts = new List<string>();
            foreach (var rank in new[] { "Regular", "Leader", "Boss", "None" })
            {
                int hits = 0; double n = 0, t = 0;
                foreach (var f in fights)
                    if (f.ByRank.TryGetValue(rank, out var rh)) { hits += rh.Hits; n += rh.Normal; t += rh.True; }
                if (hits > 0)
                    parts.Add($"{(rank == "None" ? "no rank" : rank)} {n / hits:0} + {t / hits:0} ({hits})");
            }
            return parts.Count > 0 ? " Hits (normal + true): " + string.Join(", ", parts) + "." : "";
        }

        private static void SetPhase(Run run, Phase p) { run.Phase = p; run.PhaseAt = DateTime.UtcNow; run.ModeAsked = false; }

        /// <summary>Timed pack (owner 2026-10-01: "we don't have to kill these mobs, just cycle between them"): the pack is
        /// kept at full health, the fight ends after TimedSeconds, and the attack moves to the next monster every
        /// SwitchSeconds (never during a heal). True = this tick is used up.</summary>
        private static bool TimedStep(Run run, DateTime now)
        {
            var player = run.Player;
            foreach (var m in run.PackMobs)
                if (m != null && !m.IsDead && m.Health.Current < m.Health.MaxValue)
                    m.UpdateVital(m.Health, m.Health.MaxValue);

            if ((now - run.PhaseAt).TotalSeconds >= run.TimedSeconds)
            {
                run.Cur.End = now;
                Despawn(run);
                if (player.Attacking || player.MeleeTarget != null || player.MissileTarget != null)
                    player.HandleActionCancelAttack();
                SetPhase(run, Phase.AfterFight);
                return true;
            }

            if (run.HealStarted != null || run.HealCancelAsked || (now - run.LastSwitch).TotalSeconds < run.SwitchSeconds)
                return false;
            var alive = run.PackMobs.Where(m => m != null && !m.IsDead && !m.IsDestroyed && m.CurrentLandblock != null).ToList();
            if (alive.Count < 2)
                return false;
            var next = alive[(alive.IndexOf(run.Mob) + 1) % alive.Count];
            run.Mob = next;
            run.TargetName = next.Name;
            run.LastSwitch = now;
            Send(player, "[[CBT]]guid=" + next.Guid.Full.ToString(CultureInfo.InvariantCulture));
            // the swing in progress finishes; the attack loop then stops and the next tick aims at the new monster
            if (player.Attacking || player.MeleeTarget != null || player.MissileTarget != null)
                player.HandleActionCancelAttack();
            return true;
        }

        /// <summary>A weapon sweep moves on to its next weapon's Equip, or finishes. False = finished.</summary>
        private static bool NextSweepWeapon(Run run)
        {
            Despawn(run);
            run.SweepStep++;
            run.GearAskedAt = null;
            if (run.SweepStep >= run.Sweep.StepCount)
            {
                if (!run.Sweep.Loop)
                {
                    Finish(run, "done", "");
                    return false;
                }
                // owner 2026-10-01: keep rotating until /bench stop
                Log(run.Player, $"sweep: lap {run.SweepCycle} done - starting lap {run.SweepCycle + 1} (Stop ends it).");
                log.Info($"[CombatBench] {run.Player.Name}: sweep lap {run.SweepCycle} done ({run.Sweep.StepCount} steps: {string.Join(",", run.Sweep.Weapons)}"
                    + (run.Sweep.Powers != null ? $" x power {string.Join(",", run.Sweep.Powers.Select(p => p.ToString("0.00", CultureInfo.InvariantCulture)))}" : "") + ")");
                run.SweepCycle++;
                run.SweepStep = 0;
            }
            if (run.Sweep.Matrix != null)
            {
                // a matrix step names its weapon and power bar itself (CombatBenchMatrix.cs)
                var mst = run.Sweep.Matrix[run.SweepStep];
                run.SweepIdx = Math.Max(0, run.Sweep.Weapons.IndexOf(mst.Weapon));
                run.Power = mst.Power;
            }
            else
            {
                // weapon-major inside each power bar: every weapon at the fast bar, then every weapon at the next bar ...
                run.SweepIdx = run.SweepStep % run.Sweep.Weapons.Count;
                if (run.Sweep.Powers != null)
                    run.Power = run.Sweep.Powers[run.SweepStep / run.Sweep.Weapons.Count];
            }
            SetPhase(run, Phase.Gear);
            return true;
        }

        /// <summary>Why the worn weapon cannot fight, or null when it can (a melee weapon, or a bow with arrows).</summary>
        private static string WeaponProblem(Player player)
        {
            if (BenchWeapon(player) == null) return "no melee weapon or bow worn - Equip first";
            if (UsesBow(player) && player.GetEquippedAmmo() == null) return "a bow but no arrows worn - Equip first";
            return null;
        }

        /// <summary>A wait that gives up after <paramref name="seconds"/>: the target is dropped with a log line.</summary>
        private static bool Waited(Run run, double seconds, string why)
        {
            if ((DateTime.UtcNow - run.PhaseAt).TotalSeconds < seconds) return true;
            Log(run.Player, $"run: {why} - target skipped.");
            Despawn(run);
            SetPhase(run, Phase.NextTarget);
            return true;
        }

        private static void Attack(Run run)
        {
            run.LastAttackOrder = DateTime.UtcNow;
            // the power bar doubles as the bow's accuracy bar (owner 2026-10-01)
            if (UsesBow(run.Player))
                run.Player.HandleActionTargetedMissileAttack(run.Mob.Guid.Full, (uint)AttackHeight.Medium, run.Power);
            else
                run.Player.HandleActionTargetedMeleeAttack(run.Mob.Guid.Full, (uint)AttackHeight.Medium, run.Power);
        }

        /// <summary>Full health / stamina / mana every tick: the fight measures damage, not survival - and a player near
        /// death would trip Cheat Death / Battle Mending lines, whose immunity windows would hide spell hits.
        /// Pack mode (owner 2026-10-01): health is REAL - only stamina and mana are kept full; the low point is recorded,
        /// and reaching 1 HP (the unkillable floor) counts as a death and refills health.</summary>
        private static void TopUp(Run run)
        {
            var player = run.Player;
            // owner 2026-10-01: a pack with heal 0 = no kit - the server keeps you full, so the attack never stops
            if (!run.Pack || run.HealPct <= 0)
            {
                if (player.Health.Current < player.Health.MaxValue || player.Stamina.Current < player.Stamina.MaxValue)
                    player.SetMaxVitals();
                return;
            }
            if (player.Stamina.Current < player.Stamina.MaxValue)
                player.UpdateVital(player.Stamina, (int)player.Stamina.MaxValue);
            if (player.Mana.Current < player.Mana.MaxValue)
                player.UpdateVital(player.Mana, (int)player.Mana.MaxValue);
            var f = run.Cur;
            if (f == null || f.End.HasValue || player.Health.MaxValue == 0) return;
            f.LowHpPct = Math.Min(f.LowHpPct, 100.0 * player.Health.Current / player.Health.MaxValue);
            if (player.Health.Current <= 1)
            {
                f.Deaths++;
                player.UpdateVital(player.Health, (int)player.Health.MaxValue);
                Log(player, $"fight {run.FightIdx + 1}: DIED (1 HP) at {(f.FirstStrike.HasValue ? (DateTime.UtcNow - f.FirstStrike.Value).TotalSeconds : 0):0} s - refilled, fighting on.");
            }
        }

        /// <summary>Pack mode: below the heal line, let the swing in progress end, then use the kit on yourself (the real
        /// Healer path: animation, skill check, busy time). True = no new swing this tick.</summary>
        private static bool HealStep(Run run, DateTime now)
        {
            var player = run.Player;
            if (run.HealStarted.HasValue)
            {
                if (player.IsBusy && (now - run.HealStarted.Value).TotalSeconds < 10) return true;   // the heal motion
                run.Cur.HealSeconds += (now - run.HealStarted.Value).TotalSeconds;
                run.HealStarted = null;
                if (run.Mob != null && !run.Mob.IsDead)
                    Attack(run);   // the cancel ended the attack loop - start it again
                return true;
            }
            if (run.HealPct <= 0 || player.Health.MaxValue == 0
                || 100.0 * player.Health.Current / player.Health.MaxValue >= run.HealPct)
            {
                run.HealCancelAsked = false;
                return false;
            }
            if (player.Attacking || player.IsBusy)
            {
                if (player.Attacking && !run.HealCancelAsked)
                {
                    run.HealCancelAsked = true;
                    player.HandleActionCancelAttack();
                }
                return true;
            }
            var kit = FindKit(player);
            if (kit == null)
            {
                Log(player, "pack: no Eternal Health Kit any more - healing off for this run.");
                run.HealPct = 0;
                return false;
            }
            run.HealCancelAsked = false;
            run.HealStarted = now;
            kit.HandleActionUseOnTarget(player, player);
            return true;
        }

        private static bool Spawn(Run run, uint wcid, double distance = 1.5)
        {
            var player = run.Player;
            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie == null) return false;
            if (WorldObjectFactory.CreateNewWorldObject(weenie) is not Creature mob) return false;

            // 1.5 m in front (a pack: a line further out, they close in), turned to face the player (no sneak-attack
            // angle), in the player's own variation
            mob.Location = player.Location.InFrontOf(distance, true);
            mob.Location.LandblockId = new LandblockId(mob.Location.GetCell());
            if (run.Overlay != null)
                Overlays[mob.Guid.Full] = run.Overlay;   // matrix (2026-10-02): before the spawn scaler reads the profile
            ZoneSpawnScaler.ApplyToSpawn(mob);
            mob.Lifespan = (int)FightTimeoutSeconds + 120;   // a leaked bench monster still goes away on its own
            MobRuns[mob.Guid.Full] = run;
            if (!mob.EnterWorld())
            {
                MobRuns.TryRemove(mob.Guid.Full, out _);
                DropOverlay(mob.Guid.Full);
                return false;
            }
            run.Mob = mob;
            run.TargetName = mob.Name;
            run.TargetRank = RankOf(player, wcid);
            return true;
        }

        private const int MaxPackSize = 12;

        /// <summary>Pack mode: spawns a generator's whole spawn list at once, in a fan in front of the player. Rows with
        /// probability -1 always spawn (InitCreate each); a pool generator's rows (cumulative odds) pick ONE by a roll,
        /// as the live generator does; a row that is itself a generator is opened the same way. A creature wcid is a
        /// pack of one.</summary>
        private static bool SpawnPack(Run run, uint wcid)
        {
            var player = run.Player;
            run.PackMobs.Clear();
            var wcids = new List<uint>();
            PackOf(wcid, wcids, 0);
            if (wcids.Count == 0) return false;

            var genWeenie = DatabaseManager.World.GetCachedWeenie(wcid);
            var genName = (genWeenie != null ? ACE.Entity.Models.WeenieExtensions.GetName(genWeenie) : null) ?? wcid.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < wcids.Count; i++)
            {
                if (!Spawn(run, wcids[i], 2.5 + 1.0 * i)) continue;
                run.PackMobs.Add(run.Mob);
            }
            if (run.PackMobs.Count == 0) return false;

            var ranks = wcids.GroupBy(w => RankOf(player, w)).Select(g => $"{g.Key} x{g.Count()}");
            run.PackName = $"{genName}: {string.Join(" + ", ranks)}";
            run.TargetName = run.PackMobs[0].Name;
            run.TargetRank = "pack";
            run.Mob = run.PackMobs[0];
            return true;
        }

        private static void PackOf(uint wcid, List<uint> into, int depth)
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie == null || depth > 3 || into.Count >= MaxPackSize) return;
            var rows = weenie.PropertiesGenerator;
            if (rows == null || rows.Count == 0)
            {
                if (weenie.WeenieType == WeenieType.Creature) into.Add(wcid);
                return;
            }
            var always = rows.Where(r => r.Probability < 0).ToList();
            var pool = rows.Where(r => r.Probability >= 0).ToList();
            var picked = new List<ACE.Entity.Models.PropertiesGenerator>(always);
            if (pool.Count > 0)
            {
                var roll = ThreadSafeRandom.Next(0.0f, 1.0f);
                var pick = pool.FirstOrDefault(r => roll < r.Probability) ?? pool[^1];
                picked.Add(pick);
            }
            foreach (var r in picked)
            {
                var count = r.InitCreate > 0 ? r.InitCreate : Math.Max(1, r.MaxCreate);
                for (var i = 0; i < count && into.Count < MaxPackSize; i++)
                    PackOf(r.WeenieClassId, into, depth + 1);
            }
        }

        private static string RankOf(Player player, uint wcid)
        {
            var loc = player.Location;
            var area = loc != null ? ZoneControlManager.ResolveWinnerForLocation(loc.LandblockId.Landblock, ZoneControlManager.GetEffectiveVariation(player)) : null;
            if (area == null) return "no zone";
            return ZoneControlManager.ResolveRankForWcid(area, wcid).Rank.ToString();
        }

        private static void Despawn(Run run)
        {
            var mobs = run.PackMobs.ToList();
            if (run.Mob != null && !mobs.Contains(run.Mob)) mobs.Add(run.Mob);
            run.Mob = null;
            run.PackMobs.Clear();
            run.HealStarted = null;
            run.HealCancelAsked = false;
            foreach (var mob in mobs)
            {
                if (mob == null) continue;
                MobRuns.TryRemove(mob.Guid.Full, out _);   // a killed one stays known through DeadBench (no corpse, no loot)
                DropOverlay(mob.Guid.Full);
                if (!mob.IsDestroyed && !mob.IsDead)
                    mob.Destroy();
            }
        }

        private static void Finish(Run run, string reason, string msg)
        {
            run.Phase = Phase.Finish;
            var player = run.Player;
            Despawn(run);
            foreach (var kv in MobRuns.Where(kv => kv.Value == run).ToList())
                MobRuns.TryRemove(kv.Key, out _);

            try
            {
                player.IsUnkillable = run.SavedUnkillable;
                if (run.SavedAttackable.HasValue)
                    player.UpdateProperty(player, ACE.Entity.Enum.Properties.PropertyBool.Attackable, run.SavedAttackable.Value, true);
                if (player.Attacking || player.MeleeTarget != null || player.MissileTarget != null)
                    player.HandleActionCancelAttack();
            }
            catch (Exception ex) { log.Warn($"[CombatBench] restore {player?.Name}: {ex.Message}"); }

            if (run.Sweep?.Matrix != null)
            {
                try { MatrixRestore(run); }
                catch (Exception ex) { log.Warn($"[CombatBench] matrix restore {player?.Name}: {ex.Message}"); }
            }

            Runs.TryRemove(player.Guid.Full, out _);
            Send(player, $"[[CBD]]reason={Wire(reason)}|msg={Wire(msg)}");
            SendStatus(player);
        }

        // =========================================================================================================
        // Math mode + rows
        // =========================================================================================================

        private static void DoMath(Run run)
        {
            var player = run.Player;
            var mob = run.Mob;
            // the damage source: the melee weapon - or, for the bow (owner 2026-10-01), a stand-in arrow built the way
            // LaunchProjectile builds one (source, launcher, ammo) but never put in the world, so the missile damage
            // path runs exactly as for a real arrow hit
            WorldObject source = player.GetEquippedMeleeWeapon();
            WorldObject arrow = null;
            if (source == null && UsesBow(player) && player.GetEquippedAmmo() is WorldObject ammo)
            {
                arrow = WorldObjectFactory.CreateNewWorldObject(ammo.WeenieClassId);
                if (arrow != null)
                {
                    arrow.ProjectileSource = player;
                    arrow.ProjectileTarget = mob;
                    arrow.ProjectileLauncher = player.GetEquippedMissileWeapon();
                    arrow.ProjectileAmmo = ammo;
                    source = arrow;
                }
            }
            if (source == null)
            {
                Log(player, "math: no melee weapon, or bow with arrows, worn - skipped.");
                return;
            }
            var savedPower = player.PowerLevel;
            var savedAccuracy = player.AccuracyLevel;
            player.PowerLevel = run.Power;
            player.AccuracyLevel = run.Power;   // the bow's power bar is its accuracy bar
            int hits = 0, crits = 0;
            double normalSum = 0, critSum = 0;
            try
            {
                for (var i = 0; i < run.Samples; i++)
                {
                    var de = DamageEvent.CalculateDamage(player, mob, source);
                    if (!de.HasDamage) continue;
                    hits++;
                    if (de.IsCritical) { crits++; critSum += de.Damage; }
                    else normalSum += de.Damage;
                }
            }
            finally
            {
                player.PowerLevel = savedPower;
                player.AccuracyLevel = savedAccuracy;
                arrow?.Destroy();
            }

            var perStrike = (normalSum + critSum) / run.Samples;
            var hp = (double)mob.Health.MaxValue;
            var row = BaseRow(run, "math");
            row["n"] = run.Samples + " strikes";
            row["swings"] = perStrike > 0 ? Num(hp / perStrike) : "";
            row["hit"] = Num(100.0 * hits / run.Samples);
            row["crit"] = hits > 0 ? Num(100.0 * crits / hits) : "";
            row["avg"] = hits - crits > 0 ? Num(normalSum / (hits - crits)) : "";
            row["avgcrit"] = crits > 0 ? Num(critSum / crits) : "";
            row["critx"] = crits > 0 && hits - crits > 0 ? Num(critSum / crits / (normalSum / (hits - crits))) : "";
            row["note"] = "strikes to kill " + (perStrike > 0 ? (hp / perStrike).ToString("0.0", CultureInfo.InvariantCulture) : "-") + "; Kill s needs a live swing time";
            run.MathRow = row;
            SendRow(player, row);
            Log(player, $"math {run.TargetName} ({row["weapon"]}): {run.Samples} strikes, hit {row["hit"]} pct, crit {row["crit"]} pct, avg {row["avg"]}, crit avg {row["avgcrit"]} ({row["critx"]}x).");
        }

        private static void SendLiveRow(Run run)
        {
            var player = run.Player;
            var fights = run.Done;
            if (fights.Count == 0) return;
            var killed = fights.Where(f => f.Killed).ToList();
            var strikes = fights.Sum(f => f.Strikes);
            var hits = fights.Sum(f => f.Hits);
            var crits = fights.Sum(f => f.Crits);
            var normal = fights.Sum(f => f.NormalSum);
            var crit = fights.Sum(f => f.CritSum);
            var procs = fights.Sum(f => f.ProcSum);
            var seconds = fights.Sum(f => f.Seconds);
            var mobSeconds = fights.Sum(f => f.MobSeconds);
            var mobHits = fights.Sum(f => f.MobHits);
            var maxHp = (double)player.Health.MaxValue;
            var mobDmg = fights.Sum(f => f.MobMeleeSum + f.MobSpellSum);

            var row = BaseRow(run, "live");
            var timed = fights.Any(f => f.Timed);
            var dps = seconds > 0 ? (normal + crit + procs) / seconds : 0;
            row["n"] = timed ? $"{fights.Count} timed fight(s), monsters never die" : $"{fights.Count} fight(s), {killed.Count} killed";
            if (timed)
            {
                // owner 2026-10-01: nothing dies, so Kill s / Strikes are the pack's health over your damage
                var packHp = fights.Average(f => f.PackHp);
                row["ttk"] = dps > 0 ? Num(packHp / dps) : "";
                row["swings"] = hits > 0 && normal + crit > 0 ? Num(packHp / ((normal + crit) / strikes)) : "";
            }
            else
            {
                row["ttk"] = killed.Count > 0 ? Num(killed.Average(f => f.Seconds)) : "";
                row["swings"] = killed.Count > 0 ? Num(killed.Average(f => (double)f.Strikes)) : "";
            }
            row["hit"] = strikes > 0 ? Num(100.0 * hits / strikes) : "";
            row["crit"] = hits > 0 ? Num(100.0 * crits / hits) : "";
            row["avg"] = hits - crits > 0 ? Num(normal / (hits - crits)) : "";
            row["avgcrit"] = crits > 0 ? Num(crit / crits) : "";
            row["critx"] = crits > 0 && hits - crits > 0 ? Num(crit / crits / (normal / (hits - crits))) : "";
            row["dps"] = seconds > 0 ? Num(dps) : "";
            row["mavg"] = mobHits > 0 ? Num(fights.Sum(f => f.MobMeleeSum) / mobHits) : "";
            row["mpct"] = mobHits > 0 && maxHp > 0 ? Num(100.0 * fights.Sum(f => f.MobMeleeSum) / mobHits / maxHp) : "";
            row["mcrit"] = mobHits > 0 ? Num(100.0 * fights.Sum(f => f.MobCrits) / mobHits) : "";
            // True Damage (owner 2026-10-01): its share of everything the monsters dealt you, melee + spells
            row["mtrue"] = mobDmg > 0 ? Num(100.0 * fights.Sum(f => f.MobTrueSum) / mobDmg) : "";
            // the hit split per rank (owner 2026-10-01): "<normal> + <true>" per melee hit, and the raw numbers for the CSV
            foreach (var (rank, key) in new[] { ("Regular", "reg"), ("Leader", "ldr"), ("Boss", "boss") })
            {
                int rHits = 0, rSpells = 0; double rn = 0, rt = 0, sn = 0, st = 0;
                foreach (var f in fights)
                    if (f.ByRank.TryGetValue(rank, out var rh)) { rHits += rh.Hits; rn += rh.Normal; rt += rh.True; rSpells += rh.Spells; sn += rh.SpellNormal; st += rh.SpellTrue; }
                if (rHits == 0 && rSpells == 0) continue;
                row["h" + key] = rHits > 0 ? $"{rn / rHits:0} + {rt / rHits:0}" : "-";
                row["n" + key] = rHits > 0 ? Num(rn / rHits) : "";
                row["t" + key] = rHits > 0 ? Num(rt / rHits) : "";
                row["s" + key] = rSpells > 0 ? $"{sn / rSpells:0} + {st / rSpells:0}" : "";
            }
            row["ttd"] = mobDmg > 0 && mobSeconds > 0 ? Num(maxHp / (mobDmg / mobSeconds)) : "";
            row["note"] = $"your max HP {maxHp:0}; monster {fights.Sum(f => f.MobAttacks)} melee swings, {mobHits} hit, {fights.Sum(f => f.MobSpells)} spells ({fights.Sum(f => f.MobSpellSum):0} dmg); procs {procs:0} dmg"
                + (fights.Any(f => f.TimedOut) ? "; TIMEOUTS" : "")
                + (timed ? $"; timed {run.TimedSeconds} s fights, target switched every {run.SwitchSeconds} s - Kill s and Strikes are the pack's health over your damage" : "");
            if (run.Pack)
            {
                // owner 2026-10-01: how often healing happens. Per fight = per pack cleared (or timed out).
                var heals = fights.Sum(f => f.Heals);
                var fails = fights.Sum(f => f.HealFails);
                var fightSeconds = fights.Sum(f => f.Seconds);
                var healSeconds = fights.Sum(f => f.HealSeconds);
                row["heals"] = Num((double)(heals + fails) / fights.Count);
                row["hpm"] = fightSeconds > 0 ? Num((heals + fails) / (fightSeconds / 60.0)) : "";
                row["havg"] = heals > 0 ? Num(fights.Sum(f => f.HealSum) / heals) : "";
                row["hfail"] = fails.ToString(CultureInfo.InvariantCulture);
                row["htime"] = fightSeconds > 0 ? Num(100.0 * healSeconds / fightSeconds) : "";
                row["lowhp"] = Num(fights.Min(f => f.LowHpPct));
                row["deaths"] = Num((double)fights.Sum(f => f.Deaths) / fights.Count);
                row["note"] += $"; heal below {(run.HealPct > 0 ? run.HealPct + " pct" : "never")}: {heals} landed, {fails} failed, {healSeconds:0} s of {fightSeconds:0} s healing ({row["htime"]} pct); {fights.Sum(f => f.Deaths)} death(s)";
            }
            SendRow(player, row);

            // Both: the math row gets its time-to-kill from this live swing time
            if (run.MathRow != null && killed.Count > 0 && strikes > 0 && double.TryParse(run.MathRow["swings"], NumberStyles.Float, CultureInfo.InvariantCulture, out var mathStrikes))
            {
                var secPerStrike = killed.Sum(f => f.Seconds) / killed.Sum(f => f.Strikes);
                run.MathRow["ttk"] = Num(mathStrikes * secPerStrike);
                run.MathRow["note"] = $"strikes to kill {mathStrikes:0.0}; Kill s uses the live {secPerStrike:0.000} s per strike";
                SendRow(player, run.MathRow);
            }
        }

        private static Dictionary<string, string> BaseRow(Run run, string mode)
        {
            var row = BaseRowCore(run, mode);
            AddMatrixFields(run, row);   // matrix (2026-10-02): the step and the player it ran on
            return row;
        }

        private static Dictionary<string, string> BaseRowCore(Run run, string mode) => new Dictionary<string, string>
        {
            // a pack's MATH rows are per monster (owner 2026-10-01): its wcid, name and rank, not the generator's
            ["target"] = (run.Pack && mode != "live" && run.Mob != null ? run.Mob.WeenieClassId : run.Wcids[run.TargetIdx]).ToString(CultureInfo.InvariantCulture),
            ["name"] = run.Pack ? (mode != "live" && run.Mob != null ? run.Mob.Name : run.PackName) : run.TargetName,
            ["rank"] = run.Pack ? (mode != "live" && run.Mob != null ? RankOf(run.Player, run.Mob.WeenieClassId) : "pack") : run.TargetRank,
            ["weapon"] = WeaponLabel(run.Player),   // owner 2026-10-01: sweeps put several weapons in one table
            ["mode"] = mode,
            ["power"] = run.Power.ToString("0.00", CultureInfo.InvariantCulture),   // owner 2026-09-29: every row says its power bar
        };

        private static void SendRow(Player player, Dictionary<string, string> row)
        {
            var sb = new StringBuilder("[[CBR]]");
            var first = true;
            foreach (var kv in row)
            {
                if (!first) sb.Append('|');
                first = false;
                sb.Append(kv.Key).Append('=').Append(Wire(kv.Value));
            }
            Send(player, sb.ToString());
            // owner 2026-10-01: every result row also goes to the server log, so a run can be read without the client
            log.Info($"[CombatBench] {player?.Name} row: {sb.ToString(7, sb.Length - 7)}");
            // matrix (2026-10-02): and to the run's results file
            if (player != null && Runs.TryGetValue(player.Guid.Full, out var run) && run.Sweep?.ResultsPath != null)
                AppendResult(run.Sweep.ResultsPath, row);
        }

        // =========================================================================================================
        // Helpers
        // =========================================================================================================

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static void Log(Player player, string text)
        {
            if (player == null) return;
            Send(player, "[[CBL]]" + (text ?? "").Replace("\n", " "));
        }

        private static void Send(Player player, string line)
        {
            player?.Session?.Network.EnqueueSend(new GameMessageSystemChat(line, ChatMessageType.Broadcast));
        }

        private static string Wire(string s) => (s ?? "").Replace("|", "/").Replace("=", ":").Replace("\n", " ");

        private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
