using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Account Vault (owner 2026-10-28): one page of a player's vault - a Storage chest of 100 items that lives in the shard and
    /// is placed in the world only while its owner has it open (VaultManager). An account page (VaultAccount) holds any
    /// character's items of that account except attuned ones; an attuned page (VaultCharacter) holds one character's items,
    /// attuned included. A page is never destroyed: closing it saves it and takes it out of the world.
    /// </summary>
    public class VaultPage : Storage
    {
        public const int PageItems = 100;

        /// <summary>A new page, taking its values from the Vault Page weenie.</summary>
        public VaultPage(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
            VaultManager.Track(this);
        }

        /// <summary>A page restored from the database (by VaultManager, or by a landblock after a crash left it placed).</summary>
        public VaultPage(Biota biota) : base(biota)
        {
            SetEphemeralValues();
            VaultManager.Track(this);

            // A page is only ever placed while open; one saved with a Location was open when the server stopped. The landblock that
            // spawned it does not know it - VaultManager takes it back out of the world (it is reused if its owner opens it first).
            if (Location != null)
            {
                var chain = new ActionChain();
                chain.AddDelaySeconds(10);
                chain.AddAction(this, ActionType.Vault_Page, () => VaultManager.RetireStray(this));
                chain.EnqueueChain();
            }
        }

        private void SetEphemeralValues()
        {
            // owner 10-28 ("Wrong Vault"): a strongbox always wears the Strongbox weenie's current look - a saved strongbox keeps the
            // model it was made with otherwise (a new look in the weenie reaches only new strongboxes)
            // (Weenie is set only on a new object - one loaded from its row reads the cached weenie)
            var weenie = Weenie ?? Database.DatabaseManager.World.GetCachedWeenie(WeenieClassId);
            foreach (var look in new[] { PropertyDataId.Setup, PropertyDataId.MotionTable, PropertyDataId.SoundTable, PropertyDataId.Icon })
                if (weenie?.PropertiesDID != null && weenie.PropertiesDID.TryGetValue(look, out var id) && GetProperty(look) != id)
                    SetProperty(look, id);

            ItemCapacity = PageItems;
            ContainerCapacity = 0;   // no packs in a page: opening a chest sends every item of its packs too

            // 2026-10-28 TEST: a placed page is a dynamic object, and those rot (5 min, counted while open and saved with the page);
            // a rotted page was destroyed WITH its items the next time it stood in the world unopened. A page never rots.
            TimeToRot = -1;
        }

        public long? VaultAccount
        {
            get => GetProperty(PropertyInt64.VaultAccount);
            set { if (!value.HasValue) RemoveProperty(PropertyInt64.VaultAccount); else SetProperty(PropertyInt64.VaultAccount, value.Value); }
        }

        public uint? VaultCharacter
        {
            get => GetProperty(PropertyInstanceId.VaultCharacter);
            set { if (!value.HasValue) RemoveProperty(PropertyInstanceId.VaultCharacter); else SetProperty(PropertyInstanceId.VaultCharacter, value.Value); }
        }

        public int VaultPageNumber
        {
            get => GetProperty(PropertyInt.VaultPageNumber) ?? 0;
            set => SetProperty(PropertyInt.VaultPageNumber, value);
        }

        public bool IsAttunedPage => (VaultCharacter ?? 0) != 0;

        /// <summary>The player who asked for this page while its items were still loading.</summary>
        public Player PendingViewer;

        /// <summary>Last item moved in or out (or the open) - VaultManager closes a page left idle.</summary>
        public double LastActivity;

        /// <summary>Whether this player may use this page at all (its account / its character).</summary>
        public bool BelongsTo(Player player)
        {
            if (IsAttunedPage)
                return VaultCharacter == player.Guid.Full;

            return VaultAccount == player.Account?.AccountId;
        }

        /// <summary>Attuned items may go only into the depositing character's own attuned page.</summary>
        public bool AcceptsAttunedFrom(Player player) => IsAttunedPage && VaultCharacter == player.Guid.Full && Viewer == player.Guid.Full;

        public override ActivationResult CheckUseRequirements(WorldObject activator)
        {
            // Pages are opened by VaultManager (/storage, the Quartermaster). A click on a placed page only closes it, for its viewer.
            if (activator is Player player && IsOpen && Viewer == player.Guid.Full)
                return new ActivationResult(true);

            return new ActivationResult(false);
        }

        public override void ActOnUse(WorldObject wo)
        {
            if (wo is Player player && IsOpen && Viewer == player.Guid.Full)
                Close(player);
        }

        protected override void OnInitialInventoryLoadCompleted()
        {
            base.OnInitialInventoryLoadCompleted();

            VaultManager.OnPageLoaded(this);
        }

        /// <summary>/storage sort moves every item at once: no save and no log line per item - the sort saves each strongbox once.</summary>
        public bool BulkMove;

        protected override void OnAddItem(WorldObject addedItem)
        {
            if (BulkMove)
                return;
            base.OnAddItem(addedItem);
            LastActivity = Common.Time.GetUnixTime();
            VaultManager.LogMove(this, addedItem, true);
        }

        protected override void OnRemoveItem(WorldObject removedItem)
        {
            if (BulkMove)
                return;
            base.OnRemoveItem(removedItem);
            LastActivity = Common.Time.GetUnixTime();
            VaultManager.LogMove(this, removedItem, false);
        }

        /// <summary>A page is never destroyed: Destroy() deletes its row and every item on it and recycles the guid. Anything that tries
        /// (rot, a landblock clean-up, a GM tool) is refused and logged, and the page is put away instead.</summary>
        public override void Destroy(bool raiseNotifyOfDestructionEvent = true, bool fromLandblockUnload = false, bool enqueueDatabaseRemove = true)
        {
            VaultManager.RefuseDestroy(this, fromLandblockUnload);
        }

        public override void FinishClose(Player player)
        {
            base.FinishClose(player);

            VaultManager.OnPageClosed(this, player);
        }
    }
}
