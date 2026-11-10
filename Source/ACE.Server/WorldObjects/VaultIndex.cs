using ACE.Entity;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Account Vault index (owner 2026-10-28, "if they have 50 pages ... a pain to keep clicking"): a chest that lists the player's
    /// storage pages as tokens ("Page 3 - 87 items - Bows"). Taking or double-clicking a token opens that page (VaultManager.OnTokenTaken);
    /// nothing can be put in. Built when it opens, never saved, destroyed when it closes - it holds nothing but its own tokens.
    /// </summary>
    public class VaultIndex : Chest
    {
        public VaultIndex(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        public VaultIndex(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            ItemCapacity = 100;
            ContainerCapacity = 0;
            TimeToRot = -1;
            SuppressShardPersistence = true;
        }

        public override ActivationResult CheckUseRequirements(WorldObject activator)
        {
            // only its viewer, and only to close it
            if (activator is Player player && IsOpen && Viewer == player.Guid.Full)
                return new ActivationResult(true);

            return new ActivationResult(false);
        }

        public override void ActOnUse(WorldObject wo)
        {
            if (wo is Player player && IsOpen && Viewer == player.Guid.Full)
                Close(player);
        }

        public override void FinishClose(Player player)
        {
            base.FinishClose(player);

            VaultManager.OnIndexClosed(this);
        }
    }
}
