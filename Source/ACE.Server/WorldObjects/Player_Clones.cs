using System.Collections.Generic;

using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Manages the lifecycle and combat integration of shadow clones
    /// spawned by the Shadow Clone Charm (ability ID 24).
    ///
    /// Clones are spawned when the charm activates, despawned on
    /// death / teleport, and re-spawned after the player materialises
    /// at their destination.  Every server tick the clone positions are
    /// updated to trail the owner.  Each time the player lands a hit,
    /// <see cref="TryApplyCloneDamage"/> fires identical damage from
    /// each clone, attributed to the player for XP / DamageHistory.
    /// </summary>
    partial class Player
    {
        // ── Constants ─────────────────────────────────────────────────────────
        /// <summary>WCID of the minimal placeholder weenie used to construct clones.</summary>
        private const uint ShadowCloneWcid = 777700031u;

        // ── Runtime state ─────────────────────────────────────────────────────
        /// <summary>Live clones currently in the world for this player.</summary>
        private readonly List<PlayerClone> _activeClones = new();

        /// <summary>True if at least one clone is currently active in the world.</summary>
        public bool HasActiveClones => _activeClones.Count > 0;

        // ── Spawn / Despawn ───────────────────────────────────────────────────
        /// <summary>
        /// Spawns two ethereal clones (left and right) flanking the player.
        /// Any previously active clones are removed first.
        /// Called when the Shadow Clone Charm is activated.
        /// </summary>
        public void SpawnClones()
        {
            DespawnClones(); // clean up stale clones if any

            if (Location == null) return;

            var weenie = DatabaseManager.World.GetCachedWeenie(ShadowCloneWcid);
            if (weenie == null)
            {
                log.Warn($"[ShadowClone] Placeholder weenie {ShadowCloneWcid} not found — " +
                         $"cannot spawn clones for {Name}. Run the SQL file first.");
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                bool isLeft = (i == 0);

                var guid  = GuidManager.NewDynamicGuid();
                var clone = new PlayerClone(weenie, guid);
                clone.Initialize(this, isLeft);

                // Add to the landblock — this makes the clone visible to nearby players.
                LandblockManager.AddObject(clone);
                _activeClones.Add(clone);
            }
        }

        /// <summary>
        /// Destroys all active clones and clears the list.
        /// Safe to call when no clones exist (no-op).
        /// </summary>
        public void DespawnClones()
        {
            foreach (var clone in _activeClones)
                clone.Destroy();

            _activeClones.Clear();
        }

        // ── Position tracking ─────────────────────────────────────────────────
        /// <summary>
        /// Broadcasts updated positions for every active clone.
        /// Called from <see cref="Player_Tick.Player_Tick"/> on each server heartbeat.
        /// </summary>
        public void UpdateClonePositions()
        {
            if (_activeClones.Count == 0) return;

            foreach (var clone in _activeClones)
                clone.UpdatePosition();
        }

        // ── Damage mirroring ──────────────────────────────────────────────────
        /// <summary>
        /// Mirrors a melee, missile, or magic hit to every active clone.
        /// Each clone applies the same <paramref name="damage"/> and
        /// <paramref name="damageType"/> to <paramref name="target"/>,
        /// attributed to this player so XP and kill credit flow correctly.
        ///
        /// Guards:
        /// • Charm must be active (<see cref="HasShadowCloneCharm"/>).
        /// • At least one clone must be alive in the world.
        /// • Target must be a living non-player Creature (no PvP mirroring).
        /// </summary>
        public void TryApplyCloneDamage(Creature target, float damage, DamageType damageType)
        {
            if (!HasShadowCloneCharm)   return;
            if (_activeClones.Count == 0) return;
            if (target == null)         return;
            if (target is Player)       return;  // no PvP clone damage
            if (!target.IsAlive)        return;

            foreach (var clone in _activeClones)
                clone.DealCloneDamage(target, damage, damageType);
        }

        // ── War spell (projectile) mirroring ─────────────────────────────────
        /// <summary>
        /// Called from <see cref="WorldObject_Magic.HandleCastSpell_Projectile"/> after the
        /// player's own projectile is launched.  Fires an identical spell projectile from
        /// each active clone's position so the bolts visually originate from the clones.
        ///
        /// <para>After each projectile is created by the clone (so physics origin and aim
        /// direction are computed from the clone's position), <see cref="SpellProjectile.ProjectileSource"/>
        /// is overridden to the owner player so damage calculations, kill XP, and DamageHistory
        /// attribution all use the player's full stats.</para>
        ///
        /// <para><see cref="SpellProjectile.IsCloneProjectile"/> is set to <c>true</c> so
        /// <see cref="TryApplyCloneDamage"/> is NOT triggered a second time when these
        /// projectiles hit, which would otherwise create an exponential damage chain.</para>
        /// </summary>
        public void TryFireProjectilesFromClones(Spell spell, WorldObject target, WorldObject weapon, bool isWeaponSpell, bool fromProc, uint lifeProjectileDamage)
        {
            if (!HasShadowCloneCharm || _activeClones.Count == 0) return;
            if (target == null || target.Location == null) return;

            foreach (var clone in _activeClones)
            {
                if (clone.CurrentLandblock == null) continue;

                // CreateSpellProjectiles called on the clone so that:
                //   • casterLoc / PhysicsObj.Position  = clone's actual world position
                //   • velocity direction               = clone → target (correct aim)
                //   • ProjectileSource                 = clone (overridden below)
                var projs = clone.CreateSpellProjectiles(spell, target, weapon, isWeaponSpell, fromProc, lifeProjectileDamage);
                if (projs == null) continue;

                foreach (var proj in projs)
                {
                    // Re-attribute to the owner so kill XP / DamageHistory go to the right player.
                    proj.ProjectileSource = this;
                    // Mark so the SpellProjectile.DamageTarget hook doesn't spawn more clones.
                    proj.IsCloneProjectile = true;
                }
            }
        }

        // ── Self-buff mirroring ───────────────────────────────────────────────
        /// <summary>
        /// Called when the player casts a beneficial non-projectile spell on
        /// themselves while the Shadow Clone Charm is active.
        ///
        /// For <see cref="SpellType.Enchantment"/> spells (attribute/skill buffs,
        /// protection spells, etc.) the enchantment is applied directly to each
        /// clone so it receives the buff aura visuals and is formally buffed.
        ///
        /// For all other spell types (e.g. <see cref="SpellType.Boost"/> heals)
        /// only the spell particle effect is replayed on the clone — clones have
        /// no vitals so there is nothing to restore.
        /// </summary>
        public void TryMirrorSelfSpellToClones(Spell spell)
        {
            if (!HasShadowCloneCharm || _activeClones.Count == 0) return;

            var weapon = GetEquippedWand();

            foreach (var clone in _activeClones)
            {
                // Play the landing particle effect on the clone (the "buff lands" visual).
                DoSpellEffects(spell, this, clone);

                // For Enchantment-type buffs, actually apply the enchantment to the clone.
                // This makes the clone glow with buff particles and have the enchantment on record.
                if (spell.MetaSpellType == SpellType.Enchantment ||
                    spell.MetaSpellType == SpellType.FellowEnchantment)
                {
                    CreateEnchantment(clone, this, weapon, spell);
                }
            }
        }

        // ── Animation mirroring ───────────────────────────────────────────────
        /// <summary>
        /// Overrides <see cref="WorldObject.EnqueueBroadcastMotion"/> so that
        /// every motion the player broadcasts (walk, cast, attack, emote, …)
        /// is immediately forwarded to all active clones.
        ///
        /// This is the correct hook for capturing spell-cast animations, which
        /// fire as one-shot motion packets and are gone before the next tick.
        /// </summary>
        public override void EnqueueBroadcastMotion(Motion motion, float? maxRange = null, bool? applyPhysics = null)
        {
            // Always run the normal player broadcast first.
            base.EnqueueBroadcastMotion(motion, maxRange, applyPhysics);

            // Forward to clones if any are active.
            if (_activeClones.Count == 0) return;

            foreach (var clone in _activeClones)
                clone.EnqueueBroadcastMotion(motion, maxRange);
        }
    }
}
