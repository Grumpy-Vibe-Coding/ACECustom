using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Player Traders (owner 2026-10-28, plan C:\AI\Trader_Plan_2026-10-28.md): a player's shop NPC. It stands where its owner put it
    /// and stays there (a saved dynamic object, reloaded with its landblock) until the owner moves it or puts it away. Its inventory
    /// is the owner's stock - real item rows, never copies; an item with a TraderPrice is for sale. It never rots, never wears its
    /// stock, and is never destroyed (Destroy puts it away instead). All buying / stocking goes through TraderManager.
    /// </summary>
    public class PlayerTrader : Vendor
    {
        /// <summary>A copy of a trader that is already live elsewhere in memory (a landblock spawned it from a stale row): it sells
        /// nothing, saves nothing, and leaves the world - two live copies of one stock would double its items.</summary>
        public bool IsGhost;

        public PlayerTrader(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
            TraderManager.Track(this);
            ScheduleIdleCheck(3600);
        }

        public PlayerTrader(Biota biota) : base(biota)
        {
            SetEphemeralValues();
            if (!TraderManager.Track(this))
                BecomeGhost();

            // a trader a landblock loads: the owner's account may have been away too long (owner 10-28: put away after 14 days)
            ScheduleIdleCheck(15);
        }

        /// <summary>TraderManager.CheckIdle after the delay (it runs only while the trader stands in a landblock), then hourly.</summary>
        private void ScheduleIdleCheck(double seconds)
        {
            var chain = new ActionChain();
            chain.AddDelaySeconds(seconds);
            chain.AddAction(this, ActionType.Vendor_CheckClose, () => TraderManager.CheckIdle(this));
            chain.EnqueueChain();
        }

        private void SetEphemeralValues()
        {
            TimeToRot = -1;          // never rots (a placed dynamic object rots otherwise - and Destroy takes its inventory with it)
            ItemCapacity = TraderManager.MaxItems;   // the most a container holds; trader_listings_per_category caps each category below it
            Ethereal = true;         // never blocks a door (owner 10-28)

            // the clothes it is dressed in are made fresh every time it is built - never saved, or they pile up row after row
            foreach (var worn in EquippedObjects.Values)
                worn.SuppressShardPersistence = true;
        }

        private void BecomeGhost()
        {
            IsGhost = true;
            SuppressShardPersistence = true;
            var chain = new ActionChain();
            chain.AddDelaySeconds(2);
            chain.AddAction(this, ActionType.Vendor_CheckClose, () => CurrentLandblock?.RemoveWorldObject(Guid, false));
            chain.EnqueueChain();
        }

        protected override void OnInitialInventoryLoadCompleted()
        {
            base.OnInitialInventoryLoadCompleted();
            if (IsGhost)
                foreach (var item in Inventory.Values)
                    item.SuppressShardPersistence = true;   // the live copy's items are the real ones
        }

        public uint OwnerCharacter
        {
            get => GetProperty(PropertyInstanceId.TraderOwner) ?? 0;
            set => SetProperty(PropertyInstanceId.TraderOwner, value);
        }

        public long OwnerAccount
        {
            get => GetProperty(PropertyInt64.TraderAccount) ?? 0;
            set => SetProperty(PropertyInt64.TraderAccount, value);
        }

        /// <summary>1 = Enlightened Coins, 2 = Luminance (units of trader_luminance_unit).</summary>
        public int Currency
        {
            get => GetProperty(PropertyInt.TraderCurrency) ?? TraderManager.CurrencyCoins;
            set => SetProperty(PropertyInt.TraderCurrency, value);
        }

        public bool IsOwnedBy(Player player) => player.Account != null && OwnerAccount == player.Account.AccountId;

        public override void ActOnUse(WorldObject wo)
        {
            if (wo is Player player)
                TraderManager.OpenWindow(player, this);
        }

        /// <summary>Never destroyed: Destroy() deletes the row, every item it holds and recycles the guid. Refused and logged; the
        /// trader is put away instead (stock kept).</summary>
        public override void Destroy(bool raiseNotifyOfDestructionEvent = true, bool fromLandblockUnload = false, bool enqueueDatabaseRemove = true)
        {
            if (IsGhost)
            {
                // a ghost holds only copies: take it out of memory without touching the database
                CurrentLandblock?.RemoveWorldObject(Guid, false);
                return;
            }
            TraderManager.RefuseDestroy(this, fromLandblockUnload);
        }
    }
}
