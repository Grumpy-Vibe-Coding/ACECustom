DELETE FROM `weenie` WHERE `class_Id` = 777700030;
INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (777700030, '777700030PrestigeMirrorShadow', 10, '2026-06-08 22:00:00') /* Creature */;

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (777700030,   1,        16) /* ItemType - Creature */
     , (777700030,   2,        22) /* CreatureType - Shadow */
     , (777700030,   3,        39) /* PaletteTemplate */
     , (777700030,   6,        -1) /* ItemsCapacity */
     , (777700030,   7,        -1) /* ContainersCapacity */
     , (777700030,  16,         1) /* ItemUseable */
     , (777700030,  25,      1100) /* Level */
     , (777700030,  27,         0) /* ArmorType */
     , (777700030,  40,         2) /* CombatMode */
     , (777700030,  68,        13) /* TargetingTactic */
     , (777700030,  93,      1032) /* PhysicsState */
      , (777700030, 101,       512) /* AiAllowedCombatStyle */
     , (777700030, 133,         2) /* ShowableOnRadar */
     , (777700030, 140,         1) /* AiOptions */
     , (777700030, 146,  10000000) /* XpOverride - 10M */
     , (777700030, 307,     11000) /* DamageRating */
     , (777700030, 308,     11000) /* DamageResistRating */
     , (777700030, 313,         20) /* CritRating */
     , (777700030, 314,         80) /* CritDamageRating - Boosted to T10/T11 scale */
     , (777700030, 331,       1600) /* NetherResistRating */
     , (777700030, 332,       5000) /* LuminanceAward - 5K */
     , (777700030, 350,        999) /* DotResistRating */
     , (777700030, 351,        999) /* LifeResistRating */
     , (777700030, 386,        800) /* Overpower - Boosted to T10/T11 scale */;

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (777700030,   1, True ) /* Stuck */
     , (777700030,   6, False) /* AiUsesMana */
     , (777700030,  11, False) /* IgnoreCollisions */
     , (777700030,  12, True ) /* ReportCollisions */
     , (777700030,  13, False) /* Ethereal */
     , (777700030,  14, True ) /* GravityStatus */
     , (777700030,  19, True ) /* Attackable */
     , (777700030,  42, True ) /* AllowEdgeSlide */
     , (777700030, 50, True ) /* NeverFailCasting */
     , (777700030, 103, False) /* NonProjectileMagicImmune */;

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (777700030,   1,         5) /* HeartbeatInterval */
     , (777700030,   2,         0) /* HeartbeatTimestamp */
     , (777700030,   3,        50) /* HealthRate */
     , (777700030,   4,        60) /* StaminaRate */
     , (777700030,   5,        20) /* ManaRate */
     , (777700030,  12,       0.5) /* Shade */
     , (777700030,  13,         1) /* ArmorModVsSlash */
     , (777700030,  14,       1) /* ArmorModVsPierce */
     , (777700030,  15,       1) /* ArmorModVsBludgeon */
     , (777700030,  16,      1) /* ArmorModVsCold */
     , (777700030,  17,      1) /* ArmorModVsFire */
     , (777700030,  18,       1) /* ArmorModVsAcid */
     , (777700030,  19,         1) /* ArmorModVsElectric */
     , (777700030,  31,        26) /* VisualAwarenessRange */
     , (777700030,  34,         1) /* PowerupTime */
     , (777700030,  36,         1) /* ChargeSpeed */
     , (777700030,  39,       1.2) /* DefaultScale */
     , (777700030,  44,       180) /* TimeToRot */
     , (777700030,  55,        95) /* HomeRadius */
     , (777700030,  64,       1.0) /* ResistSlash */
     , (777700030,  65,       1.0) /* ResistPierce */
     , (777700030,  66,       1.0) /* ResistBludgeon */
     , (777700030,  67,       1.0) /* ResistFire */
     , (777700030,  68,       1.0) /* ResistCold */
     , (777700030,  69,       1.0) /* ResistAcid */
     , (777700030,  70,       1.0) /* ResistElectric */
     , (777700030,  71,         1) /* ResistHealthBoost */
     , (777700030,  72,         1) /* ResistStaminaDrain */
     , (777700030,  73,         1) /* ResistStaminaBoost */
     , (777700030,  74,         1) /* ResistManaDrain */
     , (777700030,  75,         1) /* ResistManaBoost */
     , (777700030, 104,         1) /* ObviousRadarRange */
     , (777700030, 125,         1) /* ResistHealthDrain */
     , (777700030, 151,       0.9) /* IgnoreShield */
     , (777700030, 165,         1) /* ArmorModVsNether */
     , (777700030, 166,       1.0) /* ResistNether */
     , (777700030, 50000,     7.0) /* MaxCritDamageMultiplier */
     , (777700030, 50001,     2.0) /* MaxVulnMultiplier */;

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (777700030,   1, 'Prestige Mirror Shadow') /* Name */
     , (777700030,  45, 'KillTaskPrestigeMirrorShadow') /* KillQuest */;

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (777700030,   1, 0x0200071B) /* Setup - Nexus Shadow (Female human shadow) */
     , (777700030,   2, 0x09000001) /* MotionTable - Human (wields weapon animations) */
     , (777700030,   3, 0x20000002) /* SoundTable */
     , (777700030,   4, 0x30000000) /* CombatTable */
     , (777700030,   6, 0x0400007E) /* PaletteBase */
     , (777700030,   7, 0x1000019F) /* ClothingBase */
     , (777700030,   8, 0x06001BBE) /* Icon */
     , (777700030,  22, 0x34000063) /* PhysicsEffectTable */
     , (777700030,  35, 0x00000BBF) /* DeathTreasureType - T10 Loot Table */;

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`)
VALUES (777700030,   1,     1100, 0, 0) /* Strength */
     , (777700030,   2,     1100, 0, 0) /* Endurance */
     , (777700030,   3,     1100, 0, 0) /* Quickness */
     , (777700030,   4,     1100, 0, 0) /* Coordination */
     , (777700030,   5,     1100, 0, 0) /* Focus */
     , (777700030,   6,     1100, 0, 0) /* Self */;

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`)
VALUES (777700030,   1, 11000000, 0, 0, 11000000) /* MaxHealth */
     , (777700030,   3,     1100, 0, 0,     11000) /* MaxStamina */
     , (777700030,   5,     11000, 0, 0,     11000) /* MaxMana */;

INSERT INTO `weenie_properties_int64` (`object_Id`, `type`, `value`)
VALUES (777700030, 9011, 300) /* LumAugWarCount */;

INSERT INTO `weenie_properties_skill` (`object_Id`, `type`, `level_From_P_P`, `s_a_c`, `p_p`, `init_Level`, `resistance_At_Last_Check`, `last_Used_Time`)
VALUES (777700030,  6, 0, 3, 0,  1100, 0, 0) /* MeleeDefense Specialized */
     , (777700030,  7, 0, 3, 0,  1100, 0, 0) /* MissileDefense Specialized */
     , (777700030, 13, 0, 3, 0,  30000, 0, 0) /* UnarmedCombat Specialized (30500 final) */
     , (777700030, 15, 0, 3, 0,  1100, 0, 0) /* MagicDefense Specialized */
     , (777700030, 20, 0, 2, 0,  5000, 0, 0) /* Deception Trained */
     , (777700030, 22, 0, 2, 0,  1000, 0, 0) /* Jump Trained */
     , (777700030, 24, 0, 2, 0,  500, 0, 0) /* Run Trained */
     , (777700030, 33, 0, 2, 0,  1100, 0, 0) /* LifeMagic Trained (10000 final) */
     , (777700030, 34, 0, 3, 0,  7100, 0, 0) /* WarMagic Specialized (7650 final) */;

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`) VALUES
(777700030,  0,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 1, 0.33,    0,    0, 0.33,    0,    0, 0.33,    0,    0, 0.33,    0,    0), /* Head */
(777700030,  1,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 2, 0.44, 0.17,    0, 0.44, 0.17,    0, 0.44, 0.17,    0, 0.44, 0.17,    0), /* Chest */
(777700030,  2,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 3,    0, 0.17,    0,    0, 0.17,    0,    0, 0.17,    0,    0, 0.17,    0), /* Abdomen */
(777700030,  3,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 1, 0.23, 0.03,    0, 0.23, 0.03,    0, 0.23, 0.03,    0, 0.23, 0.03,    0), /* UpperArm */
(777700030,  4,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 2,    0,  0.3,    0,    0,  0.3,    0,    0,  0.3,    0,    0,  0.3,    0), /* LowerArm */
(777700030,  5,  4, 15, 0.75, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 2,    0,  0.2,    0,    0,  0.2,    0,    0,  0.2,    0,    0,  0.2,    0), /* Hand */
(777700030,  6,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 3,    0, 0.13, 0.18,    0, 0.13, 0.18,    0, 0.13, 0.18,    0, 0.13, 0.18), /* UpperLeg */
(777700030,  7,  4,  0, 0.0, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 3,    0,    0,  0.6,    0,    0,  0.6,    0,    0,  0.6,    0,    0,  0.6), /* LowerLeg */
(777700030,  8,  4, 20, 0.75, 1100, 1100, 1100, 1100, 1100, 1100, 1100, 1100,   0, 3,    0,    0, 0.22,    0,    0, 0.22,    0,    0, 0.22,    0,    0, 0.22);


INSERT INTO `weenie_properties_spell_book` (`object_Id`, `spell`, `probability`)
VALUES (777700030,  4421, 2.0476) /* Incantation of Acid Arc (Acid Arc VIII) */
     , (777700030,  4422, 2.0500) /* Incantation of Blade Arc (Blade Arc VIII) */
     , (777700030,  4423, 2.0526) /* Incantation of Flame Arc (Flame Arc VIII) */
     , (777700030,  4424, 2.0556) /* Incantation of Force Arc (Force Arc VIII) */
     , (777700030,  4425, 2.0588) /* Incantation of Frost Arc (Frost Arc VIII) */
     , (777700030,  4426, 2.0625) /* Incantation of Lightning Arc (Lightning Arc VIII) */
     , (777700030,  4427, 2.0667) /* Incantation of Shock Arc (Shock Arc VIII) */
     , (777700030,  4432, 2.0714) /* Incantation of Acid Streak (Acid Streak VIII) */
     , (777700030,  4433, 2.0769) /* Incantation of Acid Stream (Acid Stream VIII) */
     , (777700030,  4439, 2.0833) /* Incantation of Flame Bolt (Flame Bolt VIII) */
     , (777700030,  4440, 2.0909) /* Incantation of Flame Streak (Flame Streak VIII) */
     , (777700030,  4443, 2.1000) /* Incantation of Force Bolt (Force Bolt VIII) */
     , (777700030,  4444, 2.1111) /* Incantation of Force Streak (Force Streak VIII) */
     , (777700030,  4447, 2.1250) /* Incantation of Frost Bolt (Frost Bolt VIII) */
     , (777700030,  4448, 2.1429) /* Incantation of Frost Streak (Frost Streak VIII) */
     , (777700030,  4451, 2.1667) /* Incantation of Lightning Bolt (Lightning Bolt VIII) */
     , (777700030,  4452, 2.2000) /* Incantation of Lightning Streak (Lightning Streak VIII) */
     , (777700030,  4455, 2.2500) /* Incantation of Shock Wave (Shock Wave VIII) */
     , (777700030,  4456, 2.3333) /* Incantation of Shock Wave Streak (Shock Wave Streak VIII) */
     , (777700030,  4457, 2.5000) /* Incantation of Whirling Blade (Whirling Blade VIII) */
     , (777700030,  4458, 3.0000) /* Incantation of Whirling Blade Streak (Whirling Blade Streak VIII) */;

-- Clean up old static spawn if it exists in the database
DELETE FROM `landblock_instance` WHERE `weenie_Class_Id` = 777700030 AND `variation_Id` = 11;
