using System;
using System.Collections.Generic;
using System.Numerics;

using log4net;

using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>How a turret picks WHICH creature to shoot. Upgradeable: new strategies slot in here (owner's target, threat).</summary>
    public enum TurretTargeting
    {
        Nearest,
        LowestHealth,
        HighestHealth,
    }

    /// <summary>
    /// Turret (2026-09-13): a stationary, untargetable caster a player drops at their feet by casting a basic War or
    /// Void projectile spell while a Turret Charm is active. Its ELEMENT is fixed by what placed it; the SHAPE of every
    /// cast is chosen by the situation from the owner's own spellbook at the highest level they know (ruling 10): a ring
    /// when enough mobs stand inside ring radius, an arc for a far single target, a streak or bolt up close. Every cast
    /// is launched BY THE OWNER from the turret's position (CreateSpellProjectiles with originOverride), so damage, wand
    /// bonuses and rends, kill credit and aggro are exactly the owner's own cast.
    /// No monster AI runs: the weenie is Attackable=false with no TargetingTactic, so Creature.IsMonster is false.
    /// It never moves; it vanishes when the owner logs out, changes layer, drops the charm or walks out of range.
    /// </summary>
    public class Turret : Creature
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const uint TurretWcid = 739999993;

        private const double MinInterval = 0.25;
        private const double SpellbookRefreshSeconds = 60;

        public Player Owner { get; private set; }
        /// <summary>The spell that placed the turret: the element source, and the fallback when the owner knows nothing better.</summary>
        public Spell PlacingSpell { get; private set; }
        public DamageType Element { get; private set; }
        public double PlacedAt { get; private set; }
        public int CastsFired { get; private set; }

        private ObjectGuid placementWandGuid = ObjectGuid.Invalid;
        private int castGeneration;
        private string despawnReason;

        /// <summary>Best known spell per shape for this turret's element, resolved from the owner's spellbook.</summary>
        private readonly Dictionary<ProjectileSpellType, Spell> shapeSpells = new();
        private double shapeSpellsResolvedAt;

        public Turret(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        public Turret(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            Attackable = false;
            NoCorpse = true;
            ItemUseable = Usable.No;
            SuppressGenerateEffect = true;
            Ethereal = true;   // owner 2026-09-13: players walk through it, nobody gets stuck inside a turret

            // Attackable=false and no TargetingTactic -> IsMonster=false: the monster AI never ticks a turret.
            SetMonsterState();
        }

        public bool Init(Player owner, Spell spell, WorldObject wand)
        {
            var settings = CharmSettingsManager.Turret;

            Owner = owner;
            PlacingSpell = spell;
            Element = spell.DamageType;
            placementWandGuid = wand?.Guid ?? ObjectGuid.Invalid;

            // at the owner's feet, just far enough forward that the two physics bodies do not overlap
            var ownerRadius = owner.PhysicsObj?.GetPhysicsRadius() ?? 0.5f;
            Location = owner.Location.InFrontOf(ownerRadius + settings.SpawnOffset, false);
            Location.LandblockId = new LandblockId(Location.GetCell());

            Name = $"{owner.Name}'s {ElementName(Element)} Turret";
            PetOwner = owner.Guid.Full;
            Lifespan = Math.Max(1, (int)Math.Round(settings.Lifetime));
            PlacedAt = Time.GetUnixTime();

            if (!EnterWorld())
            {
                owner.SendTransientError($"Couldn't place {Name}");
                return false;
            }

            // InitPhysicsObj reads CalculatedPhysicsState(); re-apply so PhysicsState.Ethereal is on the wire (same as Pet.Init)
            Ethereal = true;

            ResolveShapeSpells(owner);

            ScheduleCast(++castGeneration, Math.Max(MinInterval, ResolveInterval()));
            return true;
        }

        public static string ElementName(DamageType damageType)
        {
            switch (damageType)
            {
                case DamageType.Slash:    return "Slashing";
                case DamageType.Pierce:   return "Piercing";
                case DamageType.Bludgeon: return "Bludgeoning";
                case DamageType.Cold:     return "Frost";
                case DamageType.Fire:     return "Fire";
                case DamageType.Acid:     return "Acid";
                case DamageType.Electric: return "Lightning";
                case DamageType.Nether:   return "Nether";
                default:                  return "Arcane";
            }
        }

        /// <summary>
        /// The highest-level spell the owner knows for each shape of this turret's element. Refreshed once a minute so a
        /// spell learned mid-fight is picked up; the placing spell is always available as the fallback.
        /// </summary>
        private void ResolveShapeSpells(Player owner)
        {
            shapeSpells.Clear();
            shapeSpellsResolvedAt = Time.GetUnixTime();

            foreach (var spellId in owner.Biota.GetKnownSpellsIds(owner.BiotaDatabaseLock))
            {
                if (spellId <= 0)
                    continue;

                var spell = new Spell((uint)spellId);
                if (spell.NotFound || !Player.TurretQualifies(spell) || spell.DamageType != Element)
                    continue;

                var shape = SpellProjectile.GetProjectileSpellType(spell.Id);
                if (shape == ProjectileSpellType.Undef)
                    continue;

                if (!shapeSpells.TryGetValue(shape, out var best)
                    || spell.Level > best.Level
                    || (spell.Level == best.Level && spell.Power > best.Power))
                    shapeSpells[shape] = spell;
            }

            // the placing spell counts too (it may be an unknown/built-in one the owner cast from an item)
            var placingShape = SpellProjectile.GetProjectileSpellType(PlacingSpell.Id);
            if (placingShape != ProjectileSpellType.Undef
                && (!shapeSpells.TryGetValue(placingShape, out var current) || PlacingSpell.Level > current.Level))
                shapeSpells[placingShape] = PlacingSpell;
        }

        private Spell Known(ProjectileSpellType shape) => shapeSpells.TryGetValue(shape, out var s) ? s : null;

        /// <summary>
        /// Ruling 10, the owner's matrix (2026-09-13), every distance measured from the TURRET:
        ///   close, 2+ mobs inside ring radius  -> Ring      close, one mob -> Bolt
        ///   far, a pack around the target      -> Volley    far, one mob   -> Arc
        /// Each cell falls back to the nearest known shape; the last fallback is the placing spell.
        /// </summary>
        private Spell ChooseSpell(List<(Creature creature, float dist)> candidates, Creature target, float targetDist, CharmSettingsManager.TurretBlock settings, Player owner, out string why)
        {
            var ringRadius = SmartRingSettingsManager.Radius * (float)(owner.GetProperty(PropertyFloat.AoeRangeMultiplier) ?? 1.0f);
            var nearTurret = 0;
            foreach (var (_, dist) in candidates)
                if (dist <= ringRadius)
                    nearTurret++;

            var packAroundTarget = 0;
            if (target?.Location != null)
            {
                var targetGlobal = target.Location.ToGlobal(false);
                foreach (var (creature, _) in candidates)
                    if (creature.Location != null && Vector3.Distance(targetGlobal, creature.Location.ToGlobal(false)) <= settings.PackRadius)
                        packAroundTarget++;
            }

            var bolt   = Known(ProjectileSpellType.Bolt) ?? Known(ProjectileSpellType.Streak);
            var arc    = Known(ProjectileSpellType.Arc);
            var ring   = Known(ProjectileSpellType.Ring);
            var volley = Known(ProjectileSpellType.Volley);
            var close  = targetDist <= settings.ArcDistance;

            Spell pick;
            if (close && nearTurret >= settings.RingMinTargets && ring != null)
            {
                pick = ring; why = $"ring: {nearTurret} within {ringRadius:F1}m of the turret";
            }
            else if (close)
            {
                pick = bolt ?? arc ?? ring; why = $"bolt: lone target {targetDist:F1}m away";
            }
            else if (packAroundTarget >= settings.PackMin && volley != null)
            {
                pick = volley; why = $"volley: {packAroundTarget} within {settings.PackRadius:F0}m of the target, {targetDist:F1}m away";
            }
            else
            {
                pick = arc ?? bolt ?? volley; why = $"arc: lone target {targetDist:F1}m away";
            }

            if (pick == null)
            {
                pick = PlacingSpell; why = "fallback: the placing spell";
            }
            else
            {
                var shape = SpellProjectile.GetProjectileSpellType(pick.Id).ToString().ToLowerInvariant();
                if (!why.StartsWith(shape))
                    why = shape + " (fallback) - " + why;
            }

            return pick;
        }

        /// <summary>Seconds between casts: the charm setting minus whatever the owner's gear/consumables take off (0 today).</summary>
        private double ResolveInterval()
        {
            var baseInterval = CharmSettingsManager.Turret.Interval;
            var reduction = Owner?.GetTurretIntervalReduction() ?? 0.0;
            return Math.Max(MinInterval, baseInterval - reduction);
        }

        private void ScheduleCast(int gen, double delaySeconds)
        {
            var chain = new ActionChain();
            chain.AddDelaySeconds(delaySeconds);
            chain.AddAction(this, ActionType.Turret_Cast, () => CastTick(gen));
            chain.EnqueueChain();
        }

        private void CastTick(int gen)
        {
            if (gen != castGeneration || IsDestroyed || CurrentLandblock == null)
                return;

            if (IsLifespanSpent)
            {
                Despawn("lifespan");
                return;
            }

            var settings = CharmSettingsManager.Turret;
            var owner = Owner;

            if (!settings.Enabled || owner == null || owner.IsDestroyed || owner.Session == null || owner.CurrentLandblock == null
                || owner.Location == null || !owner.HasTurretCharm
                || !VariationManager.SameVariationForVisibility(VariationManager.GetEffectiveVariationForVisibility(owner), VariationManager.GetEffectiveVariationForVisibility(this))
                || owner.Location.DistanceTo(Location) > settings.OwnerRange)
            {
                Despawn("owner gone");
                return;
            }

            if (Time.GetUnixTime() - shapeSpellsResolvedAt > SpellbookRefreshSeconds)
                ResolveShapeSpells(owner);

            var candidates = GatherCandidates(owner, settings);
            var target = PickTarget(candidates, settings, out var targetDist);
            if (target != null)
            {
                var spell = ChooseSpell(candidates, target, targetDist, settings, owner, out var why);
                var wand = ResolveWand(owner);

                // Launched BY THE OWNER from the turret: ProjectileSource = owner (damage, skill, kill credit, aggro),
                // ProjectileLauncher = wand (element bonus, rends), origin and direction = this turret.
                var projectiles = owner.CreateSpellProjectiles(spell, target, wand, false, false, 0, this);
                if (projectiles != null && projectiles.Count > 0)
                    CastsFired++;

                // On this fork a player's RING deals its damage through a radius sweep after the projectiles launch
                // (WorldObject_Magic.HandleCastSpell), not through projectile collision, unless the player opted into
                // Classic ring physics. Run the same sweep centred on the turret; the candidate list is the OWNER's known
                // objects (maintained for players only, and the owner is always within OwnerRange of the turret).
                if (SpellProjectile.GetProjectileSpellType(spell.Id) == ProjectileSpellType.Ring
                    && !(owner.GetProperty(PropertyBool.ClassicRingAoe) ?? false))
                {
                    owner.ApplyRingSpellAreaDamage(spell, centerOverride: Location);
                }

                if (settings.Verbose)
                    log.Info($"[TURRET] {Name}: cast {spell.Name} at {target.Name} ({why}); {candidates.Count} candidate(s)");
            }

            ScheduleCast(gen, ResolveInterval());
        }

        /// <summary>"Full wand stats carry over": the wand in the owner's hand now, else the wand that placed the turret if the owner still has it.</summary>
        private WorldObject ResolveWand(Player owner)
        {
            var current = owner.GetEquippedWand();
            if (current != null)
                return current;

            if (placementWandGuid != ObjectGuid.Invalid)
                return owner.GetInventoryItem(placementWandGuid);

            return null;
        }

        /// <summary>
        /// Hostile creatures (monster AI, attackable) on this layer within range that the owner is allowed to damage,
        /// with their distance from the turret. Line of sight is checked later, only for the few that matter.
        /// </summary>
        private List<(Creature creature, float dist)> GatherCandidates(Player owner, CharmSettingsManager.TurretBlock settings)
        {
            var candidates = new List<(Creature creature, float dist)>();
            var landblock = CurrentLandblock;
            if (landblock == null || Location == null)
                return candidates;

            var objects = new List<WorldObject>(landblock.GetWorldObjectsForPhysicsHandling());
            if (landblock.Adjacents != null)
            {
                foreach (var adjacent in landblock.Adjacents)
                    if (adjacent != null)
                        objects.AddRange(adjacent.GetWorldObjectsForPhysicsHandling());
            }

            var here = Location.ToGlobal(false);
            var myVariation = VariationManager.GetEffectiveVariationForVisibility(this);
            var seen = new HashSet<ObjectGuid>();
            var spellForPk = PlacingSpell;

            foreach (var obj in objects)
            {
                if (!seen.Add(obj.Guid))
                    continue;
                if (obj is not Creature creature || creature is Player || creature is Pet || creature is Turret)
                    continue;
                if (!creature.IsAlive || creature.IsDead || creature.Location == null || creature.PhysicsObj == null)
                    continue;
                if (!creature.IsMonster || !creature.Attackable)
                    continue;
                if (!VariationManager.SameVariationForVisibility(myVariation, VariationManager.GetEffectiveVariationForVisibility(creature)))
                    continue;

                var dist = Vector3.Distance(here, creature.Location.ToGlobal(false));
                if (dist > settings.Range)
                    continue;

                if (!owner.CanDamage(creature))
                    continue;
                if (owner.CheckPKStatusVsTarget(creature, spellForPk) != null)
                    continue;

                candidates.Add((creature, dist));
            }

            return candidates;
        }

        /// <summary>Orders the candidates by the strategy, then takes the first one in line of sight (the ray test is the expensive part).</summary>
        private Creature PickTarget(List<(Creature creature, float dist)> candidates, CharmSettingsManager.TurretBlock settings, out float targetDist)
        {
            targetDist = 0f;
            if (candidates.Count == 0)
                return null;

            switch (settings.TargetingMode)
            {
                case TurretTargeting.LowestHealth:
                    candidates.Sort((a, b) => HealthPercent(a.creature).CompareTo(HealthPercent(b.creature)));
                    break;
                case TurretTargeting.HighestHealth:
                    candidates.Sort((a, b) => HealthPercent(b.creature).CompareTo(HealthPercent(a.creature)));
                    break;
                default:
                    candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                    break;
            }

            var checks = 0;
            foreach (var (creature, dist) in candidates)
            {
                if (checks++ >= settings.MaxLosChecks)
                    break;
                if (IsDirectVisible(creature))
                {
                    targetDist = dist;
                    return creature;
                }
            }

            return null;
        }

        private static float HealthPercent(Creature creature)
        {
            var max = creature.Health?.MaxValue ?? 0;
            return max == 0 ? 1.0f : (float)(creature.Health?.Current ?? 0) / max;
        }

        public void Despawn(string reason)
        {
            despawnReason = reason;
            castGeneration++;
            if (!IsDestroyed)
                Destroy();
        }

        public override void Destroy(bool raiseNotifyOfDestructionEvent = true, bool fromLandblockUnload = false)
        {
            castGeneration++;

            if (!IsDestroyed)
                log.Info($"[TURRET] {Name} (0x{Guid.Full:X8}) removed: {despawnReason ?? (fromLandblockUnload ? "landblock unload" : "destroyed")} after {CastsFired} cast(s), {Time.GetUnixTime() - PlacedAt:F0}s");

            // The owner's list belongs to the owner's thread; a turret can die on its own landblock thread after the owner
            // teleported out of range, so the removal is queued on the owner rather than done here.
            var owner = Owner;
            var me = this;
            if (owner != null && !owner.IsDestroyed)
                owner.EnqueueAction(new ActionEventDelegate(ActionType.Turret_Cast, () => owner.OnTurretDestroyed(me)));

            base.Destroy(raiseNotifyOfDestructionEvent, fromLandblockUnload);
        }
    }
}
