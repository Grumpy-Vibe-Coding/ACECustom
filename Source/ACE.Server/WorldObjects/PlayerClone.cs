using System;
using System.Numerics;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// An ethereal, untargetable shadow clone of a player.
    /// Flanks the player (left or right), mirrors the owner's full appearance
    /// (including equipped gear), and applies identical damage to all targets
    /// the owner hits — covering melee, missile, and magic attacks.
    ///
    /// Created when the player activates the Shadow Clone Charm (WCID 777700030).
    /// The clone uses a minimal placeholder weenie (WCID 777700031) that is
    /// overridden at spawn time via <see cref="Initialize"/>.
    /// </summary>
    public class PlayerClone : WorldObject
    {
        // ── Public state ──────────────────────────────────────────────────────
        public Player Owner       { get; private set; }
        public bool   IsLeftClone { get; private set; }

        // Perpendicular offset distance from the owner (in metres).
        private const float OffsetDistance = 2.0f;

        // ── Constructors ──────────────────────────────────────────────────────
        public PlayerClone(Weenie weenie, ObjectGuid guid) : base(weenie, guid) { }

        public PlayerClone(Biota biota) : base(biota) { }

        // ── Initialization ────────────────────────────────────────────────────
        /// <summary>
        /// Call immediately after creating the clone, before adding it to a landblock.
        /// Copies all appearance and physics identifiers from the owner, sets ethereal
        /// physics flags, and places the clone at the correct flanking position.
        /// </summary>
        public void Initialize(Player owner, bool isLeft)
        {
            Owner       = owner;
            IsLeftClone = isLeft;

            // Name matches owner so observers see a recognisable label.
            Name = owner.Name;

            // ── Visual identifiers ──────────────────────────────────────────
            // These drive the GameMessageCreateObject packet fields that tell
            // the client which skeleton (SetupTableId) and animation table
            // (MotionTableId) to use.  Without them the clone is invisible.
            SetupTableId   = owner.SetupTableId;
            MotionTableId  = owner.MotionTableId;
            SoundTableId   = owner.SoundTableId;
            PhysicsTableId = owner.PhysicsTableId;

            // Character race/gender used by AddBaseModelData in the base
            // CalculateObjDesc path, kept in sync even though we delegate to
            // Owner.CalculateObjDesc() below.
            Heritage = owner.Heritage;
            Gender   = owner.Gender;

            // ── Physics flags ───────────────────────────────────────────────
            // Ethereal  → passes through walls and creatures.
            // IgnoreCollisions → no collision response.
            // Attackable=false → client targeting reticule ignores the clone.
            // GravityStatus=false → floats freely (does not fall to floor).
            Ethereal         = true;
            IgnoreCollisions = true;
            ReportCollisions = false;
            GravityStatus    = false;
            Attackable       = false;

            // Do not appear as a radar blip.
            RadarBehavior = ACE.Entity.Enum.RadarBehavior.ShowNever;

            // ── Starting position ───────────────────────────────────────────
            Location           = CalculateOffsetPosition() ?? new Position(owner.Location);
            CurrentMotionState = new Motion(MotionStance.NonCombat, MotionCommand.Ready);
        }

        // ── Appearance ────────────────────────────────────────────────────────
        /// <summary>
        /// Delegates directly to the owner's ObjDesc so the clone renders with
        /// the exact same appearance — including every clothing/armour layer —
        /// as the owner.
        /// </summary>
        public override ACE.Entity.ObjDesc CalculateObjDesc()
        {
            if (Owner != null)
                return Owner.CalculateObjDesc();

            return base.CalculateObjDesc();
        }

        // ── Position ──────────────────────────────────────────────────────────
        /// <summary>
        /// Recalculates the clone's position relative to the owner's current
        /// location and broadcasts it to nearby players.
        /// Called every server tick from <see cref="Player_Clones.UpdateClonePositions"/>.
        /// </summary>
        public void UpdatePosition()
        {
            if (Owner == null || Owner.Location == null || CurrentLandblock == null)
                return;

            // Mirror the owner's motion state so the clone plays walk/run/combat
            // animations instead of sliding between position updates.
            if (Owner.CurrentMotionState != null)
                EnqueueBroadcastMotion(Owner.CurrentMotionState);

            var newPos = CalculateOffsetPosition();
            if (newPos == null) return;

            Location = newPos;
            SendUpdatePosition();
        }

        /// <summary>
        /// Computes a position 2 m to the left or right of the owner,
        /// perpendicular to the direction the owner is facing.
        /// </summary>
        private Position CalculateOffsetPosition()
        {
            if (Owner?.Location == null) return null;

            // Extract yaw (heading around Z-axis) from the owner's rotation quaternion.
            var q = Owner.Location.Rotation;
            double headingRad = Math.Atan2(
                2.0 * (q.W * q.Z + q.X * q.Y),
                1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z));

            // Perpendicular angle: left clone = +90°, right clone = −90°.
            double perpRad = headingRad + (IsLeftClone ? Math.PI / 2.0 : -Math.PI / 2.0);

            var dx = (float)(Math.Sin(perpRad) * OffsetDistance);
            var dy = (float)(Math.Cos(perpRad) * OffsetDistance);

            var newPos  = new Position(Owner.Location);
            newPos.Pos  = new Vector3(Owner.Location.Pos.X + dx,
                                      Owner.Location.Pos.Y + dy,
                                      Owner.Location.Pos.Z);
            newPos.Rotation = Owner.Location.Rotation;
            return newPos;
        }

        // ── Damage mirroring ──────────────────────────────────────────────────
        /// <summary>
        /// Applies <paramref name="damage"/> of <paramref name="damageType"/> to
        /// <paramref name="target"/>, attributed to the owner so kill XP and
        /// DamageHistory credit flows to the correct player.
        ///
        /// Should only be called from <see cref="Player_Clones.TryApplyCloneDamage"/>.
        /// </summary>
        public void DealCloneDamage(Creature target, float damage, DamageType damageType)
        {
            if (target == null || !target.IsAlive || Owner == null)
                return;

            // Round to a minimum of 1 to match normal attack behaviour.
            var finalAmount = (float)Math.Max(1.0, Math.Round(damage));

            // TakeDamage(attacker, …) attributes the damage to Owner in
            // the creature's DamageHistory, so XP on kill goes to the owner.
            target.TakeDamage(Owner, damageType, finalAmount);
        }
    }
}
