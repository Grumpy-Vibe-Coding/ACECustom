using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
        /// Spawns four ethereal clones at the diagonal corners around the player.
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

            // Offset angles in degrees from the owner's heading.
            //  +45° = front-left   |  -45° = front-right
            // +135° = rear-left    | -135° = rear-right
            float[] cornerAngles = { 45f, -45f, 135f, -135f };

            foreach (var angleDeg in cornerAngles)
            {
                var guid  = GuidManager.NewDynamicGuid();
                var clone = new PlayerClone(weenie, guid);
                clone.Initialize(this, angleDeg);

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
        /// player's own projectile is launched.
        ///
        /// <para><b>Target selection (Penta Cast style):</b> each clone picks a
        /// <em>different</em> living enemy within 25 m of the player, sorted by distance.
        /// If fewer unique targets exist than clones, remaining clones fall back to the
        /// player's primary target.</para>
        ///
        /// <para><b>Physics position fix:</b> <see cref="WorldObject.Location"/> (the
        /// visual/DB position) and <c>PhysicsObj.Position</c> (used for origin + velocity
        /// math inside <see cref="WorldObject.CreateSpellProjectiles"/>) are not
        /// automatically kept in sync for ethereal WorldObjects.  We sync them
        /// immediately before each clone fires so the bolt spawns at the correct world
        /// position and aims correctly at the chosen target.</para>
        ///
        /// <para><see cref="SpellProjectile.IsCloneProjectile"/> prevents the
        /// <see cref="TryApplyCloneDamage"/> hook from triggering again on clone hits,
        /// which would create an exponential damage chain.</para>
        /// </summary>
        public void TryFireProjectilesFromClones(Spell spell, WorldObject target, WorldObject weapon, bool isWeaponSpell, bool fromProc, uint lifeProjectileDamage)
        {
            if (!HasShadowCloneCharm || _activeClones.Count == 0) return;
            if (target == null || target.Location == null) return;

            // Build a list of alternative targets — one per clone (Penta Cast approach).
            // Excludes the player's primary target since the player already hits it.
            var altTargets = GetNearbyCloneTargets(target, _activeClones.Count, spell);

            for (int i = 0; i < _activeClones.Count; i++)
            {
                var clone = _activeClones[i];
                if (clone.CurrentLandblock == null) continue;

                // Pick a unique target for this clone; fallback to player's target.
                var cloneTarget = (i < altTargets.Count) ? (WorldObject)altTargets[i] : target;
                if (cloneTarget.Location == null) continue;

                // ── Physics position fix ──────────────────────────────────────────
                // Setting WorldObject.Location only updates the DB cache + client packet.
                // PhysicsObj.Position (used by CreateSpellProjectiles for spawn origin
                // and velocity direction) is NOT updated automatically for ethereal
                // WorldObjects. Sync it now so the bolt flies from the right place.
                SyncClonePhysicsPosition(clone);

                var projs = clone.CreateSpellProjectiles(spell, cloneTarget, weapon, isWeaponSpell, fromProc, lifeProjectileDamage);
                if (projs == null) continue;

                foreach (var proj in projs)
                {
                    proj.ProjectileSource = this;  // attribute damage/XP to the player
                    proj.IsCloneProjectile = true; // prevent recursive clone chain
                }
            }
        }

        /// <summary>
        /// Gathers up to <paramref name="count"/> living enemies within 25 m of the
        /// player, excluding <paramref name="primaryTarget"/> (which the player already
        /// hits).  Sorted by distance from the player (closest first).
        /// Mirrors the Penta Cast Charm's candidate-gathering logic.
        /// </summary>
        private List<Creature> GetNearbyCloneTargets(WorldObject primaryTarget, int count, Spell spell)
        {
            var results = new List<Creature>();
            var landblock = CurrentLandblock;
            if (landblock == null || Location == null) return results;

            var allObjects = new List<WorldObject>();
            allObjects.AddRange(landblock.GetWorldObjectsForPhysicsHandling());
            foreach (var adj in landblock.Adjacents)
                if (adj != null) allObjects.AddRange(adj.GetWorldObjectsForPhysicsHandling());

            var uniqueObjects = allObjects.GroupBy(o => o.Guid).Select(g => g.First());
            var playerGlobal  = Location.ToGlobal(false);

            var candidates = new List<(Creature creature, float dist)>();

            foreach (var obj in uniqueObjects)
            {
                if (obj is not Creature creature) continue;
                if (creature == (Creature)(WorldObject)this) continue;       // skip self
                if (creature.Guid == primaryTarget.Guid) continue;           // player hits this one
                if (!creature.IsAlive) continue;
                if (creature.Location == null) continue;
                if (!CanDamage(creature)) continue;
                if (CheckPKStatusVsTarget(creature, spell) != null) continue;

                var dist = Vector3.Distance(playerGlobal, creature.Location.ToGlobal(false));
                if (dist > 25.0f) continue;

                candidates.Add((creature, dist));
            }

            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
            return candidates.Take(count).Select(c => c.creature).ToList();
        }

        /// <summary>
        /// Synchronises a clone's <c>PhysicsObj.Position</c> with its current visual
        /// <see cref="WorldObject.Location"/> so that <see cref="WorldObject.CreateSpellProjectiles"/>
        /// uses the correct origin and velocity direction when fired from the clone.
        /// </summary>
        private static void SyncClonePhysicsPosition(PlayerClone clone)
        {
            if (clone.PhysicsObj?.Position == null || clone.Location == null) return;

            clone.PhysicsObj.Position.ObjCellID        = clone.Location.Cell;
            clone.PhysicsObj.Position.Frame.Origin      = clone.Location.Pos;
            clone.PhysicsObj.Position.Frame.Orientation = clone.Location.Rotation;
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
