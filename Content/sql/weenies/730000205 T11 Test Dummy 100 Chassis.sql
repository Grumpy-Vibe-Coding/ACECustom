/* =====================================================================================
   730000205  T11 Test Dummy 100-Everything Chassis
   -------------------------------------------------------------------------------------
   Strips the test boss (730000200) and minion (730000201) down to a neutral "identity
   only" chassis: 100 attributes / 100 skills / 100 base armor / 100 Max Health/Stam/Mana,
   and removes the baked DamageRating(307) + DamageResistRating(308).

   The Zone Scaler now BUILDS these mobs from a zone profile (attack/def skills, ratings,
   ARMOR, HP, mitigation, %HP, loot). With NO enabled profile matching, a chassis mob is a
   trivial 100-stat weakling -- that is expected; enable a profile to make it endgame:
       /zonescale create lb:<hex>
       /zonescale set lb:<hex> damage_resist_rating 10000
       /zonescale set lb:<hex> armor_level 1000
       /zonescale set lb:<hex> max_health 120000000            (respawn to apply HP)
       /zonescale enable lb:<hex>

   Idempotent (UPDATE/DELETE). Test asset -- NOT in apply_t11_camps.ps1. Already-spawned
   instances cache stats; re-spawn to pick up the change. Level(25) + luminance augs are
   left as-is (identity/cosmetic; floor + knobs dominate).
   ===================================================================================== */

/* attributes 1..6 (Str/End/Coord/Quick/Focus/Self) -> 100 */
UPDATE weenie_properties_attribute
   SET init_Level = 100
 WHERE object_Id IN (730000200, 730000201) AND type BETWEEN 1 AND 6;

/* secondary vitals: MaxHealth(1) / MaxStamina(3) / MaxMana(5) -> 100 */
UPDATE weenie_properties_attribute_2nd
   SET init_Level = 100, current_Level = 100
 WHERE object_Id IN (730000200, 730000201) AND type IN (1, 3, 5);

/* all skills -> 100 */
UPDATE weenie_properties_skill
   SET init_Level = 100
 WHERE object_Id IN (730000200, 730000201);

/* all body-part base armor -> 100 (Zone Scaler armor_level provides real mitigation) */
UPDATE weenie_properties_body_part
   SET base_Armor = 100
 WHERE object_Id IN (730000200, 730000201);

/* remove baked DamageRating(307) + DamageResistRating(308) -> scaler provides them */
DELETE FROM weenie_properties_int
 WHERE object_Id IN (730000200, 730000201) AND type IN (307, 308);
