using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;

using log4net;

namespace ACE.Server.Managers
{
    public static class EventManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly object _eventsLock = new object();

        public static Dictionary<string, Event> Events;

        public static bool Debug = false;

        private static System.Threading.Timer _scheduleTimer;
        private static System.Threading.Timer _slotTimer;
        private static int _lastSlotIndex = -1;
        private static DateTime _slotChangedAtUtc = DateTime.MinValue;
        private static int _lastMonthVal = -1;
        private static int _lastWeekVal = -1;
        private static int _lastQuarterVal = -1;

        static EventManager()
        {
            Events = new Dictionary<string, Event>(StringComparer.OrdinalIgnoreCase);
        }

        public static void Initialize()
        {
            var events = Database.DatabaseManager.World.GetAllEvents();

            foreach(var evnt in events)
            {
                Events.Add(evnt.Name, evnt);

                if (evnt.State == (int)GameEventState.On)
                    StartEvent(evnt.Name, null, null);
            }

            log.DebugFormat("EventManager Initalized.");

            try
            {
                CheckCalendarEvents();
                _scheduleTimer = new System.Threading.Timer(OnScheduleTimer, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

                // Slots get their OWN timer rather than sharing the calendar one. The calendar check
                // is hourly and early-outs unless the month/week/quarter actually changed; speeding it
                // up to serve minute-scale slots would drag that logic along for no reason. This one
                // ticks every 5s because the overlap window is measured in seconds, not because slots
                // change that often - CheckSlotEvents is a no-op when nothing is due.
                CheckSlotEvents();
                _slotTimer = new System.Threading.Timer(OnSlotTimer, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                log.Error($"[EventManager] Failed to initialize calendar scheduler: {ex}");
            }
        }

        /// <summary>
        /// Reloads a single event from the database into the Events dictionary
        /// </summary>
        public static bool ReloadEvent(string eventName)
        {
            // Normalize event name (strip @comment if present)
            var normalizedName = GetEventName(eventName);

            // Clear from database cache first so we get fresh data
            Database.DatabaseManager.World.ClearCachedEvent(normalizedName);

            var evnt = Database.DatabaseManager.World.GetCachedEvent(normalizedName);

            lock (_eventsLock)
            {
                if (evnt != null)
                {
                    Events[evnt.Name] = evnt;
                    log.Debug($"[EventManager] Reloaded event '{evnt.Name}' from database (State: {(GameEventState)evnt.State})");
                    return true;
                }

                if (Events.Remove(normalizedName))
                    log.Debug($"[EventManager] Removed event '{normalizedName}' (not found in database)");
                return false;
            }
        }

        /// <summary>
        /// Reloads all events from the database into the Events dictionary
        /// </summary>
        public static void ReloadAllEvents()
        {
            // Clear database cache
            Database.DatabaseManager.World.ClearAllCachedEvents();

            // Build new dictionary
            var events = Database.DatabaseManager.World.GetAllEvents();
            var reloaded = new Dictionary<string, Event>(StringComparer.OrdinalIgnoreCase);
            foreach (var evnt in events)
                reloaded[evnt.Name] = evnt;

            lock (_eventsLock)
                Events = reloaded;

            log.Debug($"[EventManager] Reloaded {reloaded.Count} events from database");
        }

        public static bool StartEvent(string e, WorldObject source, WorldObject target)
        {
            var eventName = GetEventName(e);

            if (eventName.Equals("EventIsPKWorld", StringComparison.OrdinalIgnoreCase)) // special event
                return false;

            lock (_eventsLock)
            {
                if (!Events.TryGetValue(eventName, out Event evnt))
                    return false;

                var state = (GameEventState)evnt.State;

                if (state == GameEventState.Disabled)
                    return false;

                if (state == GameEventState.Enabled || state == GameEventState.Off)
                {
                    evnt.State = (int)GameEventState.On;

                    if (Debug)
                        Console.WriteLine($"Starting event {evnt.Name}");
                }

                log.Debug($"[EVENT] {(source == null ? "SYSTEM" : $"{source.Name} (0x{source.Guid}|{source.WeenieClassId})")}{(target == null ? "" : $", triggered by {target.Name} (0x{target.Guid}|{target.WeenieClassId}),")} started an event: {evnt.Name}{((int)state == evnt.State ? (source == null ? ", which is the default state for this event." : ", which had already been started.") : "")}");

                return true;
            }
        }

        public static bool StopEvent(string e, WorldObject source, WorldObject target)
        {
            var eventName = GetEventName(e);

            if (eventName.Equals("EventIsPKWorld", StringComparison.OrdinalIgnoreCase)) // special event
                return false;

            lock (_eventsLock)
            {
                if (!Events.TryGetValue(eventName, out Event evnt))
                    return false;

                var state = (GameEventState)evnt.State;

                if (state == GameEventState.Disabled)
                    return false;

                if (state == GameEventState.Enabled || state == GameEventState.On)
                {
                    evnt.State = (int)GameEventState.Off;

                    if (Debug)
                        Console.WriteLine($"Stopping event {evnt.Name}");
                }

                log.Debug($"[EVENT] {(source == null ? "SYSTEM" : $"{source.Name} (0x{source.Guid}|{source.WeenieClassId})")}{(target == null ? "" : $", triggered by {target.Name} (0x{target.Guid}|{target.WeenieClassId}),")} stopped an event: {evnt.Name}{((int)state == evnt.State ? (source == null ? ", which is the default state for this event." : ", which had already been stopped.") : "")}");

                return true;
            }
        }

        public static bool IsEventStarted(string e, WorldObject source, WorldObject target)
        {
            var eventName = GetEventName(e);

            if (eventName.Equals("EventIsPKWorld", StringComparison.OrdinalIgnoreCase)) // special event
            {
                var serverPkState = ServerConfig.pk_server.Value;

                return serverPkState;
            }

            lock (_eventsLock)
            {
                if (!Events.TryGetValue(eventName, out Event evnt))
                    return false;

                if (evnt.State != (int)GameEventState.Disabled && (evnt.StartTime != -1 || evnt.EndTime != -1))
                {
                    var prevState = (GameEventState)evnt.State;

                    var now = (int)Time.GetUnixTime();

                    var start = (now > evnt.StartTime) && (evnt.StartTime > -1);
                    var end = (now > evnt.EndTime) && (evnt.EndTime > -1);

                    if (prevState == GameEventState.On && end)
                        return !StopEvent(evnt.Name, source, target);
                    else if ((prevState == GameEventState.Off || prevState == GameEventState.Enabled) && start && !end)
                        return StartEvent(evnt.Name, source, target);
                }

                return evnt.State == (int)GameEventState.On;
            }
        }

        public static bool IsEventEnabled(string e)
        {
            var eventName = GetEventName(e);

            lock (_eventsLock)
            {
                if (!Events.TryGetValue(eventName, out Event evnt))
                    return false;

                return evnt.State != (int)GameEventState.Disabled;
            }
        }

        public static bool IsEventAvailable(string e)
        {
            var eventName = GetEventName(e);

            lock (_eventsLock)
            {
                return Events.ContainsKey(eventName);
            }
        }

        public static GameEventState GetEventStatus(string e)
        {
            var eventName = GetEventName(e);

            if (eventName.Equals("EventIsPKWorld", StringComparison.OrdinalIgnoreCase)) // special event
            {
                if (ServerConfig.pk_server.Value)
                    return GameEventState.On;
                else
                    return GameEventState.Off;
            }

            lock (_eventsLock)
            {
                if (!Events.TryGetValue(eventName, out Event evnt))
                    return GameEventState.Undef;

                return (GameEventState)evnt.State;
            }
        }

        /// <summary>
        /// Thread-safe snapshot of registered events for admin tooling (web portal).
        /// </summary>
        public static List<Event> GetEventSnapshots()
        {
            lock (_eventsLock)
                return Events.Values.ToList();
        }

        /// <summary>
        /// Returns the event name without the @ comment
        /// </summary>
        /// <param name="eventFormat">A event name with an optional @comment on the end</param>
        public static string GetEventName(string eventFormat)
        {
            var idx = eventFormat.IndexOf('@');     // strip comment
            if (idx == -1)
                return eventFormat;

            var eventName = eventFormat.Substring(0, idx);
            return eventName;
        }

        private static void OnScheduleTimer(object state)
        {
            try
            {
                CheckCalendarEvents();
            }
            catch (Exception ex)
            {
                log.Error($"[EventManager] Error in calendar scheduler timer tick: {ex}");
            }
        }

        private static void OnSlotTimer(object state)
        {
            try
            {
                CheckSlotEvents();
            }
            catch (Exception ex)
            {
                log.Error($"[EventManager] Error in slot scheduler timer tick: {ex}");
            }
        }

        /// <summary>
        /// Picks which slot is active for the given period. Deterministic - no stored state - so a
        /// restart lands on the same slot the clock says, and two servers agree without talking.
        ///
        /// Not a plain modulo: that would cycle 1,2,3... predictably. Instead each ROUND of
        /// <paramref name="count"/> periods walks the slots in a shuffled order, using a stride that
        /// is coprime with count so the walk visits every slot exactly once before repeating. That
        /// gives an unpredictable order AND guarantees fair coverage - no slot is skipped or drawn
        /// twice in a round, which plain randomness could not promise.
        /// </summary>
        private static int GetSlotIndex(long period, int count)
        {
            if (count <= 1)
                return 0;

            var round = period / count;
            var offset = (int)(((period % count) + count) % count);

            var h = Avalanche((ulong)round);
            var start = (int)(h % (ulong)count);

            // Stride must be coprime with count or the walk revisits a subset instead of all of it.
            var stride = (int)((Avalanche(h) % (ulong)(count - 1)) + 1);
            while (Gcd(stride, count) != 1)
                stride = stride % (count - 1) + 1;

            return (start + offset * stride) % count;
        }

        private static ulong Avalanche(ulong x)
        {
            // splitmix64 finalizer - cheap, and mixes adjacent inputs to distant outputs so
            // consecutive rounds do not produce near-identical orders.
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                var t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        /// <summary>
        /// Rotating content: keeps exactly one Slot&lt;N&gt; event running, chosen from the clock.
        ///
        /// Mirrors CheckCalendarEvents, but on a minute scale rather than a calendar one. Which slot
        /// is active is a pure function of the current time, so there is no token to lose and nothing
        /// to drift - and because it re-asserts the desired state on every tick rather than firing
        /// once at the boundary, it repairs itself if an event is started or stopped by hand.
        ///
        /// The outgoing slot is held for slot_event_overlap_seconds after the incoming one starts.
        /// Generators take 5-10s to spawn and despawn (a fixed 5s update cadence plus a two-tick
        /// stager), so stopping the old slot at the same instant the new one starts would leave a
        /// window where neither slot's content exists.
        /// </summary>
        public static void CheckSlotEvents()
        {
            List<string> slots;
            lock (_eventsLock)
            {
                slots = Events.Keys
                    .Where(name => System.Text.RegularExpressions.Regex.IsMatch(
                        name, @"^Slot\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .ToList();
            }

            if (slots.Count == 0)
                return;     // nothing registered - the overwhelmingly common case, so bail cheaply

            // Numeric order, so Slot10 sorts after Slot9 rather than after Slot1.
            slots.Sort((a, b) => ParseSlotNumber(a).CompareTo(ParseSlotNumber(b)));

            var intervalMinutes = ServerConfig.slot_event_interval_minutes.Value;
            if (intervalMinutes < 1)
                intervalMinutes = 1;

            var period = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMinutes / intervalMinutes;
            var index = GetSlotIndex(period, slots.Count);

            if (index != _lastSlotIndex)
            {
                _lastSlotIndex = index;
                _slotChangedAtUtc = DateTime.UtcNow;
                log.Info($"[EventManager] Slot rotation -> {slots[index]} (period {period}, {slots.Count} slots registered)");
            }

            var overlapSeconds = ServerConfig.slot_event_overlap_seconds.Value;
            if (overlapSeconds < 0)
                overlapSeconds = 0;

            var overlapElapsed = DateTime.UtcNow >= _slotChangedAtUtc + TimeSpan.FromSeconds(overlapSeconds);
            var chosen = slots[index];

            foreach (var name in slots)
            {
                var status = GetEventStatus(name);

                if (name.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                {
                    // Only act when it is not already running, so a slot is not restarted every tick.
                    if (status != GameEventState.On)
                        StartEvent(name, null, null);
                }
                else if (status == GameEventState.On && overlapElapsed)
                {
                    StopEvent(name, null, null);
                }
            }
        }

        private static int ParseSlotNumber(string name)
        {
            return int.TryParse(name.Substring(4), out var n) ? n : int.MaxValue;
        }

        public static void CheckCalendarEvents()
        {
            var utcNow = DateTime.UtcNow;
            var currentMonthVal = utcNow.Month;
            var currentQuarterVal = (currentMonthVal - 1) / 3 + 1;
            var currentWeekVal = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                utcNow, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
            // Cap at 53: GetWeekOfYear with FirstDay rule can return 53 in years where Jan 1 falls late in the week.
            // Clamping to 52 would incorrectly fire Week52 events during that last week.
            if (currentWeekVal > 53) currentWeekVal = 53;

            if (currentMonthVal == _lastMonthVal && currentWeekVal == _lastWeekVal && currentQuarterVal == _lastQuarterVal)
                return;

            _lastMonthVal = currentMonthVal;
            _lastWeekVal = currentWeekVal;
            _lastQuarterVal = currentQuarterVal;

            var currentMonthEvent = $"Month{currentMonthVal}";
            var currentQuarterEvent = $"Quarter{currentQuarterVal}";
            var currentWeekEvent = $"Week{currentWeekVal}";

            log.Info($"[EventManager] Updating seasonal event states. Month: {currentMonthVal}, Quarter: {currentQuarterVal}, Week: {currentWeekVal}");

            List<string> eventNames;
            lock (_eventsLock)
            {
                eventNames = new List<string>(Events.Keys);
            }

            foreach (var name in eventNames)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^Month\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (name.Equals(currentMonthEvent, StringComparison.OrdinalIgnoreCase))
                        StartEvent(name, null, null);
                    else
                        StopEvent(name, null, null);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^Quarter\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (name.Equals(currentQuarterEvent, StringComparison.OrdinalIgnoreCase))
                        StartEvent(name, null, null);
                    else
                        StopEvent(name, null, null);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^Week\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (name.Equals(currentWeekEvent, StringComparison.OrdinalIgnoreCase))
                        StartEvent(name, null, null);
                    else
                        StopEvent(name, null, null);
                }
            }
        }

        /// <summary>
        /// Disposes the background calendar timer. Should be called during server shutdown
        /// to prevent the timer from firing against stale state after the world is torn down.
        /// </summary>
        public static void Shutdown()
        {
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;

            _slotTimer?.Dispose();
            _slotTimer = null;
        }
    }
}
