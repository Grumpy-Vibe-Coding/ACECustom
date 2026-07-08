using System;
using System.Collections.Generic;
using System.Numerics;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public partial class Creature
    {
        private double lastAuraCheckTime = 0;

        /// <summary>
        /// Stores the original (non-prefixed) name so we can restore it cleanly.
        /// </summary>
        private string _originalName;

        public void UpdateAuraBuffs(double currentUnixTime)
        {
            if (currentUnixTime - lastAuraCheckTime < 1.0)
                return;

            lastAuraCheckTime = currentUnixTime;

            // Only process creatures flagged with CanBeEmpowered
            if (GetProperty(PropertyBool.CanBeEmpowered) != true || IsDead)
                return;

            // Determine this creature's group identity:
            // EmpowerGroup (explicit override) takes priority, else fall back to CreatureType
            var myGroup = GetProperty(PropertyInt.EmpowerGroup);
            var myCreatureType = CreatureType;

            bool bossNearby = false;

            if (CurrentLandblock != null && Location != null)
            {
                var nearbyObjects = CurrentLandblock.GetWorldObjectsForPhysicsHandling();
                var myGlobalPos = Location.ToGlobal(false);

                foreach (var obj in nearbyObjects)
                {
                    if (obj is Creature creature && creature.GetProperty(PropertyBool.IsEmpowerSource) == true)
                    {
                        if (!creature.IsAlive)
                            continue;

                        // Check group match:
                        // If both have EmpowerGroup set, match on that
                        // Otherwise fall back to CreatureType
                        var bossGroup = creature.GetProperty(PropertyInt.EmpowerGroup);

                        bool groupMatch;
                        if (myGroup != null && bossGroup != null)
                            groupMatch = myGroup == bossGroup;
                        else
                            groupMatch = myCreatureType != null && myCreatureType == creature.CreatureType;

                        if (!groupMatch)
                            continue;

                        var dist = Vector3.Distance(myGlobalPos, creature.Location.ToGlobal(false));
                        if (dist <= 20.0f)
                        {
                            bossNearby = true;
                            break;
                        }
                    }
                }
            }

            bool currentlyEmpowered = GetProperty(PropertyBool.IsEmpowered) ?? false;

            if (bossNearby && !currentlyEmpowered)
            {
                SetProperty(PropertyBool.IsEmpowered, true);

                // Save the original name on first empower
                _originalName = Name;

                var newName = "Empowered " + Name;
                Name = newName;
                SetProperty(PropertyString.Name, newName);

                // Full object refresh - the client does not update its 3D name label from a bare
                // property-string update, only from an object (re)create
                EnqueueBroadcast(new GameMessageUpdateObject(this));

                log.Debug($"[AURA STATE] Crew {Guid} became EMPOWERED -> {newName}");
            }
            else if (!bossNearby && currentlyEmpowered)
            {
                RemoveProperty(PropertyBool.IsEmpowered);

                // Restore original name (fallback to string strip if _originalName is null)
                var restoredName = _originalName;
                if (restoredName == null)
                    restoredName = Name.StartsWith("Empowered ") ? Name.Substring("Empowered ".Length) : Name;
                _originalName = null;

                Name = restoredName;
                SetProperty(PropertyString.Name, restoredName);

                EnqueueBroadcast(new GameMessageUpdateObject(this));

                log.Debug($"[AURA STATE] Crew {Guid} lost EMPOWERED -> {restoredName}");
            }
        }
    }
}
