using System.Collections.Generic;

using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Network.GameEvent.Events
{
    /// <summary>
    /// Player Traders (2026-10-28): the vendor window for a player's trader - the same message as GameEventApproachVendor, filled from
    /// the trader's stock instead of a shop list. Each item is written with its listing price as its Value (TraderManager.WindowPrice);
    /// the item's real Value is never changed. Owner: the window takes every item type at no pay (its Sell tab stocks the trader).
    /// Buyer: it buys nothing back. The purse ("you have") is the buyer's BANK only, in the trader's currency.
    /// </summary>
    public class GameEventTraderWindow : GameEventMessage
    {
        public GameEventTraderWindow(Session session, PlayerTrader trader, bool owner, IReadOnlyList<WorldObject> items,
            uint currencyWcid, uint have, string currencyPlural)
            : base(GameEventType.ApproachVendor, GameMessageGroup.UIQueue, session)
        {
            Writer.Write(trader.Guid.Full);

            // what the window lets the player "sell" - the owner stocks anything, a buyer sells nothing here
            Writer.Write(owner ? 0xFFFFFFFFu : 0u);   // item types
            Writer.Write(0u);                          // min value
            Writer.Write(owner ? 0xFFFFFFFFu : 0u);    // max value
            Writer.Write(owner ? 1u : 0u);             // magic items

            Writer.Write(0f);   // buy price: stocking pays nothing
            Writer.Write(1f);   // sell price: the cost shown is the item's (price) Value

            Writer.Write(currencyWcid);
            Writer.Write(have);
            Writer.WriteString16L(currencyPlural);

            Writer.Write(items.Count);
            foreach (var item in items)
            {
                var stackSize = item.StackSize ?? 1;
                // packed value: (stackSize & 0xFFFFFF) | (pwdType << 24) - as GameEventApproachVendor
                Writer.Write(stackSize & 0xFFFFFF | -1 << 24);
                item.SerializeGameDataOnly(Writer);
            }

            Writer.Align();
        }
    }
}
