DELETE FROM `weenie` WHERE `class_Id` = 72000001;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (72000001, 'TyrantDarkspireGolemLifeVoid', 10, '2026-06-20 00:00:00') /* Creature */;

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (72000001,   1,         16) /* ItemType - Creature */
     , (72000001,   2,         13) /* CreatureType - Golem */
     , (72000001,   6,         -1) /* ItemsCapacity */
     , (72000001,   7,         -1) /* ContainersCapacity */
     , (72000001,  16,          1) /* ItemUseable - No */
     , (72000001,  25,       1100) /* Level */
     , (72000001,  27,          0) /* ArmorType - None */
     , (72000001,  40,          2) /* CombatMode - Melee */
     , (72000001,  68,          9) /* TargetingTactic - Random, TopDamager */
     , (72000001,  93,    4195336) /* PhysicsState - ReportCollisions, Gravity, EdgeSlide */
     , (72000001, 101,        131) /* AiAllowedCombatStyle - Unarmed, OneHanded, ThrownWeapon */
     , (72000001, 133,          2) /* ShowableOnRadar - ShowMovement */
     , (72000001, 140,          1) /* AiOptions - CanOpenDoors */
     , (72000001, 146,  480000000) /* XpOverride */
     , (72000001, 307,        975) /* DamageRating */
     , (72000001, 308,        800) /* DamageResistRating */
     , (72000001, 313,         10) /* CritRating */
     , (72000001, 314,         10) /* CritDamageRating */
     , (72000001, 331,        999) /* NetherResistRating */
     , (72000001, 332,      95000) /* LuminanceAward */
     , (72000001, 350,        999) /* DotResistRating */
     , (72000001, 351,        999) /* LifeResistRating */
     , (72000001, 386,        250) /* Overpower */;

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (72000001,   1, True ) /* Stuck */
     , (72000001,   6, True ) /* AiUsesMana */
     , (72000001,  11, False) /* IgnoreCollisions */
     , (72000001,  12, True ) /* ReportCollisions */
     , (72000001,  13, False) /* Ethereal */
     , (72000001,  14, True ) /* GravityStatus */
     , (72000001,  19, True ) /* Attackable */
     , (72000001,  50, True ) /* NeverFailCasting */
     , (72000001,  65, True ) /* IgnoreMagicResist */
     , (72000001, 103, True ) /* NonProjectileMagicImmune */;

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (72000001,   1,       5) /* HeartbeatInterval */
     , (72000001,   2,       0) /* HeartbeatTimestamp */
     , (72000001,   3,       5) /* HealthRate */
     , (72000001,   4,       6) /* StaminaRate */
     , (72000001,   5,       2) /* ManaRate */
     , (72000001,  12,     0.5) /* Shade */
     , (72000001,  13,     0.5) /* ArmorModVsSlash */
     , (72000001,  14,    0.65) /* ArmorModVsPierce */
     , (72000001,  15,     0.3) /* ArmorModVsBludgeon */
     , (72000001,  16,     0.9) /* ArmorModVsCold */
     , (72000001,  17,     0.9) /* ArmorModVsFire */
     , (72000001,  18,     0.9) /* ArmorModVsAcid */
     , (72000001,  19,     0.9) /* ArmorModVsElectric */
     , (72000001,  31,      45) /* VisualAwarenessRange */
     , (72000001,  34,       1) /* PowerupTime */
     , (72000001,  36,       1) /* ChargeSpeed */
     , (72000001,  39,     1.5) /* DefaultScale */
     , (72000001,  64,    0.18) /* ResistSlash */
     , (72000001,  65,    0.15) /* ResistPierce */
     , (72000001,  66,    0.22) /* ResistBludgeon */
     , (72000001,  67,    0.13) /* ResistFire */
     , (72000001,  68,    0.08) /* ResistCold */
     , (72000001,  69,    0.08) /* ResistAcid */
     , (72000001,  70,    0.08) /* ResistElectric */
     , (72000001,  71,       1) /* ResistHealthBoost */
     , (72000001,  72,       1) /* ResistStaminaDrain */
     , (72000001,  73,       1) /* ResistStaminaBoost */
     , (72000001,  74,       1) /* ResistManaDrain */
     , (72000001,  75,       1) /* ResistManaBoost */
     , (72000001, 104,       1) /* ObviousRadarRange */
     , (72000001, 125,       1) /* ResistHealthDrain */
     , (72000001, 151,    0.95) /* IgnoreShield */
     , (72000001, 165,       1) /* ArmorModVsNether */
     , (72000001, 166,    0.31) /* ResistNether */;

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (72000001, 1, 'Tyrant Darkspire Golem - Life Void') /* Name */;

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (72000001,   1, 0x020013F7) /* Setup */
     , (72000001,   2, 0x09000081) /* MotionTable */
     , (72000001,   3, 0x20000015) /* SoundTable */
     , (72000001,   4, 0x30000008) /* CombatTable */
     , (72000001,   7, 0x10000964) /* ClothingBase */
     , (72000001,   8, 0x06001224) /* Icon */
     , (72000001,  22, 0x3400005D) /* PhysicsEffectTable */;

INSERT INTO `weenie_properties_attribute` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`)
VALUES (72000001,   1,1600, 0, 0) /* Strength */
     , (72000001,   2,2600, 0, 0) /* Endurance */
     , (72000001,   3, 100, 0, 0) /* Quickness */
     , (72000001,   4,1600, 0, 0) /* Coordination */
     , (72000001,   5,3220, 0, 0) /* Focus */
     , (72000001,   6,3220, 0, 0) /* Self */;

INSERT INTO `weenie_properties_attribute_2nd` (`object_Id`, `type`, `init_Level`, `level_From_C_P`, `c_P_Spent`, `current_Level`)
VALUES (72000001,   1,3480000, 0, 0,3490000) /* MaxHealth */
     , (72000001,   3, 180000, 0, 0, 200000) /* MaxStamina */
     , (72000001,   5, 900000, 0, 0, 100000) /* MaxMana */;

INSERT INTO `weenie_properties_skill` (`object_Id`, `type`, `level_From_P_P`, `s_a_c`, `p_p`, `init_Level`, `resistance_At_Last_Check`, `last_Used_Time`)
VALUES (72000001,   6, 0, 3, 0,3200, 0, 0) /* MeleeDefense        Specialized */
     , (72000001,   7, 0, 3, 0,3000, 0, 0) /* MissileDefense      Specialized */
     , (72000001,  15, 0, 3, 0,2500, 0, 0) /* MagicDefense        Specialized */
     , (72000001,  20, 0, 2, 0,2500, 0, 0) /* Deception           Trained */
     , (72000001,  22, 0, 2, 0,1000, 0, 0) /* Jump                Trained */
     , (72000001,  24, 0, 2, 0, 100, 0, 0) /* Run                 Trained */
     , (72000001,  33, 0, 3, 0,9000, 0, 0) /* LifeMagic           Specialized */
     , (72000001,  43, 0, 3, 0,9000, 0, 0) /* VoidMagic           Specialized */
     , (72000001,  44, 0, 3, 0,50000, 0, 0) /* HeavyWeapons        Specialized */
     , (72000001,  45, 0, 3, 0,50000, 0, 0) /* LightWeapons        Specialized */
     , (72000001,  47, 0, 3, 0,50000, 0, 0) /* MissileWeapons      Specialized */;

INSERT INTO `weenie_properties_body_part` (`object_Id`, `key`, `d_Type`, `d_Val`, `d_Var`, `base_Armor`, `armor_Vs_Slash`, `armor_Vs_Pierce`, `armor_Vs_Bludgeon`, `armor_Vs_Cold`, `armor_Vs_Fire`, `armor_Vs_Acid`, `armor_Vs_Electric`, `armor_Vs_Nether`, `b_h`, `h_l_f`, `m_l_f`, `l_l_f`, `h_r_f`, `m_r_f`, `l_r_f`, `h_l_b`, `m_l_b`, `l_l_b`, `h_r_b`, `m_r_b`, `l_r_b`)
VALUES (72000001,  0,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 1, 0.33,    0,    0, 0.33,    0,    0, 0.33,    0,    0, 0.33,    0,    0) /* Head */
     , (72000001,  1,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 2, 0.44, 0.17,    0, 0.44, 0.17,    0, 0.44, 0.17,    0, 0.44, 0.17,    0) /* Chest */
     , (72000001,  2,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 3,    0, 0.17,    0,    0, 0.17,    0,    0, 0.17,    0,    0, 0.17,    0) /* Abdomen */
     , (72000001,  3,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 1, 0.23, 0.03,    0, 0.23, 0.03,    0, 0.23, 0.03,    0, 0.23, 0.03,    0) /* UpperArm */
     , (72000001,  4,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 2,    0,  0.3,    0,    0,  0.3,    0,    0,  0.3,    0,    0,  0.3,    0) /* LowerArm */
     , (72000001,  5,  4,2350, 0.75, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 2,    0,  0.2,    0,    0,  0.2,    0,    0,  0.2,    0,    0,  0.2,    0) /* Hand */
     , (72000001,  6,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 3,    0, 0.13, 0.18,    0, 0.13, 0.18,    0, 0.13, 0.18,    0, 0.13, 0.18) /* UpperLeg */
     , (72000001,  7,  4,  0,    0, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 3,    0,    0,  0.6,    0,    0,  0.6,    0,    0,  0.6,    0,    0,  0.6) /* LowerLeg */
     , (72000001,  8,  4,2350, 0.75, 1550,  775,  775,  775,  775,  775,  775,  775,    0, 3,    0,    0, 0.22,    0,    0, 0.22,    0,    0, 0.22,    0,    0, 0.22) /* Foot */;

INSERT INTO `weenie_properties_spell_book` (`object_Id`, `spell`, `probability`)
VALUES (72000001, 4317, 2.11) /* HarmOther8 */
     , (72000001, 4652, 2.11) /* DrainHealth8 */;
