using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Turret Charm (2026-09-13). While the charm is active, a basic War/Void projectile spell places a Turret at the
    /// caster's feet instead of firing; the turret then casts that spell on the owner's behalf. The charm's tier sets
    /// how many turrets may stand at once; dropping one past the cap replaces the oldest.
    /// </summary>
    partial class Player
    {
        public bool HasTurretCharm
        {
            get => GetProperty(PropertyBool.HasTurretCharm) ?? false;
            set { if (!value) RemoveProperty(PropertyBool.HasTurretCharm); else SetProperty(PropertyBool.HasTurretCharm, value); }
        }

        /// <summary>Oldest first. Touched only on this player's landblock thread (the turret is always within OwnerRange).</summary>
        public readonly List<Turret> ActiveTurrets = new();

        public int GetTurretMax()
        {
            ActiveCharmLevels.TryGetValue(CharmAbilityRegistry.TurretAbilityId, out var level);
            return CharmSettingsManager.Turret.MaxForLevel(level < 1 ? 1 : level);
        }

        /// <summary>Seconds taken off the turret cast interval by the owner's gear, consumables etc. Hook only - nothing feeds it yet.</summary>
        public double GetTurretIntervalReduction()
        {
            return 0.0;
        }

        /// <summary>True when a cast of this spell should drop a turret: charm on, feature enabled, spell qualifies.</summary>
        public bool TurretRedirectsCast(Spell spell)
        {
            return HasTurretCharm && CharmSettingsManager.Turret.Enabled && TurretQualifies(spell);
        }

        /// <summary>The basic War and Void damage spells: bolts, streaks, volleys, arcs, rings (and blasts, walls, strikes of the same family).</summary>
        public static bool TurretQualifies(Spell spell)
        {
            if (spell == null || !spell.IsHarmful || spell.NumProjectiles <= 0)
                return false;

            if (spell.School != MagicSchool.WarMagic && spell.School != MagicSchool.VoidMagic)
                return false;

            var type = SpellProjectile.GetProjectileSpellType(spell.Id);
            return type != ProjectileSpellType.Undef;
        }

        /// <summary>
        /// Instant placement from the cast handlers (owner ruling 2026-09-13: no windup, no cast animation). The spell
        /// must be known and castable: components and mana are checked and paid exactly as a cast would, the skill roll
        /// is skipped (a turret never fizzles), then the turret is placed and the client's use is acknowledged.
        /// </summary>
        public void PlaceTurretInstant(Spell spell, uint selectionGuid = 0)
        {
            log.Info($"[TURRET] {Name}: {spell.Name} ({spell.Id}) with selection 0x{selectionGuid:X8}{(selectionGuid == Guid.Full ? " (self)" : "")} -> placing");

            if (!IsValidSpell(spell, false))          // unknown spell / missing components: sends its own UseDone
                return;

            if (!VerifySpellSchoolSuppression(spell))
                return;

            var magicSkill = GetCreatureSkill(spell.School).Current;
            if (!CalculateManaUsage(CastingPreCheckStatus.Success, spell, this, null, out var manaUsed))
                return;                                // not enough mana: CalculateManaUsage reports it

            UpdateVitalDelta(Mana, -(int)manaUsed);
            TryBurnComponents(spell);

            if (spell.IsHarmful)
                LastCombatActionTime = System.DateTime.UtcNow;

            TryPlaceTurret(spell, GetEquippedWand());
            SendUseDoneEvent();
        }

        /// <summary>
        /// A deployer item was used: drop a turret of the item's element, powered by the best War/Void spell of that
        /// element the player knows (the turret chooses the shape per cast anyway; this spell fixes the element and the
        /// mana cost). Owner 2026-09-13: one item per element, no selection needed.
        /// </summary>
        public void UseTurretDeployer(WorldObject item, DamageType element)
        {
            if (!HasTurretCharm || !CharmSettingsManager.Turret.Enabled)
            {
                SendTransientError("A Turret Charm must be active to use that.");
                SendUseDoneEvent();
                return;
            }

            // Owner 2026-09-13: the deployed turret matches the WAND in hand. An item with no element of its own reads the
            // equipped caster's damage type (Flaming Orb -> Fire); an item that names an element overrides that.
            if (element == DamageType.Undef)
            {
                var wand = GetEquippedWand();
                var wandElement = (DamageType)(wand?.GetProperty(PropertyInt.DamageType) ?? 0);

                if (wand == null || wandElement == DamageType.Undef || (wandElement & (wandElement - 1)) != 0)
                {
                    SendTransientError(wand == null
                        ? "Equip an elemental wand first: the turret takes its element from your wand."
                        : $"{wand.Name} has no element: the turret takes its element from your wand.");
                    SendUseDoneEvent();
                    return;
                }

                element = wandElement;
            }

            var spell = FindBestKnownTurretSpell(element);
            if (spell == null)
            {
                SendTransientError($"You know no {Turret.ElementName(element)} War or Void spell to power a turret.");
                SendUseDoneEvent();
                return;
            }

            PlaceTurretInstant(spell, item?.Guid.Full ?? 0);
        }

        private static readonly ProjectileSpellType[] DeployerShapePreference =
        {
            ProjectileSpellType.Streak, ProjectileSpellType.Bolt, ProjectileSpellType.Arc, ProjectileSpellType.Ring,
            ProjectileSpellType.Volley, ProjectileSpellType.Blast, ProjectileSpellType.Wall, ProjectileSpellType.Strike,
        };

        /// <summary>The highest-level qualifying spell of this element in the spellbook, preferring single-target shapes.</summary>
        public Spell FindBestKnownTurretSpell(DamageType element)
        {
            Spell best = null;
            var bestRank = int.MaxValue;

            foreach (var spellId in Biota.GetKnownSpellsIds(BiotaDatabaseLock))
            {
                if (spellId <= 0)
                    continue;

                var spell = new Spell((uint)spellId);
                if (spell.NotFound || !TurretQualifies(spell) || spell.DamageType != element)
                    continue;

                var rank = System.Array.IndexOf(DeployerShapePreference, SpellProjectile.GetProjectileSpellType(spell.Id));
                if (rank < 0)
                    rank = DeployerShapePreference.Length;

                // highest level first, then the preferred shape, then power
                if (best == null || spell.Level > best.Level
                    || (spell.Level == best.Level && (rank < bestRank || (rank == bestRank && spell.Power > best.Power))))
                {
                    best = spell;
                    bestRank = rank;
                }
            }

            return best;
        }

        /// <summary>
        /// Places the turret. Returns true when one was placed.
        /// </summary>
        public bool TryPlaceTurret(Spell spell, WorldObject wand)
        {
            if (!HasTurretCharm || !CharmSettingsManager.Turret.Enabled || !TurretQualifies(spell))
                return false;

            ActiveTurrets.RemoveAll(t => t == null || t.IsDestroyed);

            var max = GetTurretMax();
            while (ActiveTurrets.Count >= max && ActiveTurrets.Count > 0)
            {
                // ruling 2026-09-13: past the cap the OLDEST turret goes, the cast is never refused
                var oldest = ActiveTurrets[0];
                ActiveTurrets.RemoveAt(0);
                oldest.Despawn("replaced by a newer turret");
            }

            var wo = WorldObjectFactory.CreateNewWorldObject(Turret.TurretWcid);
            if (wo is not Turret turret)
            {
                log.Warn($"[TURRET] {Name}: weenie {Turret.TurretWcid} is missing or is not WeenieType.Turret - no turret placed");
                SendTransientError("The turret could not be created.");
                wo?.Destroy();
                return false;
            }

            if (!turret.Init(this, spell, wand))
                return false;

            ActiveTurrets.Add(turret);
            Session.Network.EnqueueSend(new GameMessageSystemChat($"You place a {Turret.ElementName(spell.DamageType)} Turret ({spell.Name}). Turrets: {ActiveTurrets.Count}/{max}.", ChatMessageType.Magic));
            return true;
        }

        public void OnTurretDestroyed(Turret turret)
        {
            ActiveTurrets.Remove(turret);
        }

        /// <summary>Logout, charm deactivation: every turret this player owns goes away.</summary>
        public void DestroyAllTurrets()
        {
            if (ActiveTurrets.Count == 0)
                return;

            var turrets = ActiveTurrets.ToList();
            ActiveTurrets.Clear();
            foreach (var turret in turrets)
                if (turret != null && !turret.IsDestroyed)
                    turret.Despawn("owner logged out or deactivated the charm");
        }
    }
}
