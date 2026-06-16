using System;

namespace ACE.Entity.Enum
{
    [Flags]
    public enum RegenLocationType : uint
    {
        Undef               = 0x00,
        OnTop               = 0x01,
        Scatter             = 0x02,
        Specific            = 0x04,
        Contain             = 0x08,
        Wield               = 0x10,
        Shop                = 0x20,
        Treasure            = 0x40,
        LandblockGrid       = 0x80,
        PoissonScatter      = 0x100,
        Checkpoint          = Contain | Wield | Shop, // 56
        OnTopTreasure       = OnTop | Treasure, // 65
        ScatterTreasure     = Scatter | Treasure, // 66
        SpecificTreasure    = Specific | Treasure, // 68
        ContainTreasure     = Contain | Treasure, // 72
        WieldTreasure       = Wield | Treasure, // 80
        ShopTreasure        = Shop | Treasure, // 96
        LandblockGridTreasure = LandblockGrid | Treasure,
        PoissonScatterTreasure = PoissonScatter | Treasure
    }
}
