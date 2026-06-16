using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Entity.Enum;
using ACE.Common;

namespace ACE.Server.WorldObjects
{
    public partial class Player
    {
        public bool IsTrackingDamageTaken { get; set; }
        public double DamageTakenRollingWindowSeconds { get; set; } = 300.0;
        public double DamageTakenTrackingStartTimestamp { get; set; }
        public double DamageTakenLastPrintTimestamp { get; set; }
        public List<(double Timestamp, uint Amount)> DamageTakenLog { get; } = new();

        public long SessionTotalDamage { get; set; }
        public long SessionTotalHits { get; set; }
        public uint SessionMinDamageTaken { get; set; }
        public uint SessionMaxDamageTaken { get; set; }

        public bool IsTrackingDamageDealt { get; set; }
        public double DamageDealtRollingWindowSeconds { get; set; } = 300.0;
        public double DamageDealtTrackingStartTimestamp { get; set; }
        public double DamageDealtLastPrintTimestamp { get; set; }
        public List<(double Timestamp, uint Amount)> DamageDealtLog { get; } = new();

        public long SessionTotalDamageDealt { get; set; }
        public long SessionTotalHitsDealt { get; set; }
        public uint SessionMinDamageDealt { get; set; }
        public uint SessionMaxDamageDealt { get; set; }

        public void StartDamageTracking(double windowMinutes)
        {
            IsTrackingDamageTaken = true;
            DamageTakenRollingWindowSeconds = windowMinutes * 60.0;
            DamageTakenTrackingStartTimestamp = Time.GetUnixTime();
            DamageTakenLastPrintTimestamp = Time.GetUnixTime();
            DamageTakenLog.Clear();
            SessionTotalDamage = 0;
            SessionTotalHits = 0;
            SessionMinDamageTaken = uint.MaxValue;
            SessionMaxDamageTaken = 0;

            Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"[Damage Taken] Starting ({windowMinutes:F1} minute test)", 
                ChatMessageType.System));
        }

        public void StopDamageTracking()
        {
            if (!IsTrackingDamageTaken) return;

            // Print final overall summary before stopping
            PrintDamageTakenStats(Time.GetUnixTime(), isFinalSummary: true);

            var windowMinutes = DamageTakenRollingWindowSeconds / 60.0;
            IsTrackingDamageTaken = false;
            DamageTakenLog.Clear();

            Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"[Damage Taken] Ended ({windowMinutes:F1} minute test)", 
                ChatMessageType.System));
        }

        public void ResetDamageTracking()
        {
            if (!IsTrackingDamageTaken) return;

            DamageTakenTrackingStartTimestamp = Time.GetUnixTime();
            DamageTakenLastPrintTimestamp = Time.GetUnixTime();
            DamageTakenLog.Clear();
            SessionTotalDamage = 0;
            SessionTotalHits = 0;
            SessionMinDamageTaken = uint.MaxValue;
            SessionMaxDamageTaken = 0;

            var windowMinutes = DamageTakenRollingWindowSeconds / 60.0;
            Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"[Damage Taken] Resetting ({windowMinutes:F1} minute test)", 
                ChatMessageType.System));
        }

        public void RecordDamageTaken(uint amount)
        {
            if (!IsTrackingDamageTaken || amount == 0) return;

            var now = Time.GetUnixTime();
            DamageTakenLog.Add((now, amount));
            SessionTotalDamage += amount;
            SessionTotalHits++;

            if (amount < SessionMinDamageTaken) SessionMinDamageTaken = amount;
            if (amount > SessionMaxDamageTaken) SessionMaxDamageTaken = amount;
        }

        public void PruneDamageTakenLog(double currentUnixTime)
        {
            var cutoff = currentUnixTime - DamageTakenRollingWindowSeconds;
            DamageTakenLog.RemoveAll(e => e.Timestamp < cutoff);
        }

        public void PrintDamageTakenStats(double currentUnixTime, bool isFinalSummary = false)
        {
            if (isFinalSummary)
            {
                var elapsed = currentUnixTime - DamageTakenTrackingStartTimestamp;
                if (elapsed < 1.0) elapsed = 1.0;

                var totalDamage = SessionTotalDamage;
                var totalHits = SessionTotalHits;
                var avgHit = totalHits > 0 ? (double)totalDamage / totalHits : 0.0;
                var dps = (double)totalDamage / elapsed;
                var minHit = totalHits > 0 ? SessionMinDamageTaken : 0;
                var maxHit = totalHits > 0 ? SessionMaxDamageTaken : 0;

                var elapsedMins = elapsed / 60.0;
                var message = $"[Damage Taken] FINAL SUMMARY (Duration: {elapsedMins:F2}m) - Total: {totalDamage:N0} | Avg DPS: {dps:F2} | Hits: {totalHits:N0} | Avg/Hit: {avgHit:F2} | Min Hit: {minHit:N0} | Max Hit: {maxHit:N0}";

                Session?.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
            else
            {
                PruneDamageTakenLog(currentUnixTime);

                var elapsed = currentUnixTime - DamageTakenTrackingStartTimestamp;
                var actualWindow = Math.Min(elapsed, DamageTakenRollingWindowSeconds);
                if (actualWindow < 1.0) actualWindow = 1.0;

                var totalDamage = DamageTakenLog.Sum(e => (long)e.Amount);
                var totalHits = DamageTakenLog.Count;
                var avgHit = totalHits > 0 ? (double)totalDamage / totalHits : 0.0;
                var dps = (double)totalDamage / actualWindow;

                var message = $"[Damage Taken] Total: {totalDamage:N0} | DPS: {dps:F2} | Hits: {totalHits:N0} | Avg/Hit: {avgHit:F2}";

                Session?.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
        }

        public void StartDamageDealtTracking(double windowMinutes)
        {
            IsTrackingDamageDealt = true;
            DamageDealtRollingWindowSeconds = windowMinutes * 60.0;
            DamageDealtTrackingStartTimestamp = Time.GetUnixTime();
            DamageDealtLastPrintTimestamp = Time.GetUnixTime();
            DamageDealtLog.Clear();
            SessionTotalDamageDealt = 0;
            SessionTotalHitsDealt = 0;
            SessionMinDamageDealt = uint.MaxValue;
            SessionMaxDamageDealt = 0;

            Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"[Damage Dealt] Starting ({windowMinutes:F1} minute test)", 
                ChatMessageType.System));
        }

        public void StopDamageDealtTracking()
        {
            if (!IsTrackingDamageDealt) return;

            // Print final overall summary before stopping
            PrintDamageDealtStats(Time.GetUnixTime(), isFinalSummary: true);

            var windowMinutes = DamageDealtRollingWindowSeconds / 60.0;
            IsTrackingDamageDealt = false;
            DamageDealtLog.Clear();

            Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"[Damage Dealt] Ended ({windowMinutes:F1} minute test)", 
                ChatMessageType.System));
        }

        public void ResetDamageDealtTracking()
        {
            if (!IsTrackingDamageDealt) return;

            DamageDealtTrackingStartTimestamp = Time.GetUnixTime();
            DamageDealtLastPrintTimestamp = Time.GetUnixTime();
            DamageDealtLog.Clear();
            SessionTotalDamageDealt = 0;
            SessionTotalHitsDealt = 0;
            SessionMinDamageDealt = uint.MaxValue;
            SessionMaxDamageDealt = 0;

            var windowMinutes = DamageDealtRollingWindowSeconds / 60.0;
            Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"[Damage Dealt] Resetting ({windowMinutes:F1} minute test)", 
                ChatMessageType.System));
        }

        public void RecordDamageDealt(uint amount)
        {
            if (!IsTrackingDamageDealt || amount == 0) return;

            var now = Time.GetUnixTime();
            DamageDealtLog.Add((now, amount));
            SessionTotalDamageDealt += amount;
            SessionTotalHitsDealt++;

            if (amount < SessionMinDamageDealt) SessionMinDamageDealt = amount;
            if (amount > SessionMaxDamageDealt) SessionMaxDamageDealt = amount;
        }

        public void PruneDamageDealtLog(double currentUnixTime)
        {
            var cutoff = currentUnixTime - DamageDealtRollingWindowSeconds;
            DamageDealtLog.RemoveAll(e => e.Timestamp < cutoff);
        }

        public void PrintDamageDealtStats(double currentUnixTime, bool isFinalSummary = false)
        {
            if (isFinalSummary)
            {
                var elapsed = currentUnixTime - DamageDealtTrackingStartTimestamp;
                if (elapsed < 1.0) elapsed = 1.0;

                var totalDamage = SessionTotalDamageDealt;
                var totalHits = SessionTotalHitsDealt;
                var avgHit = totalHits > 0 ? (double)totalDamage / totalHits : 0.0;
                var dps = (double)totalDamage / elapsed;
                var minHit = totalHits > 0 ? SessionMinDamageDealt : 0;
                var maxHit = totalHits > 0 ? SessionMaxDamageDealt : 0;

                var elapsedMins = elapsed / 60.0;
                var message = $"[Damage Dealt] FINAL SUMMARY (Duration: {elapsedMins:F2}m) - Total: {totalDamage:N0} | Avg DPS: {dps:F2} | Hits: {totalHits:N0} | Avg/Hit: {avgHit:F2} | Min Hit: {minHit:N0} | Max Hit: {maxHit:N0}";

                Session?.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
            else
            {
                PruneDamageDealtLog(currentUnixTime);

                var elapsed = currentUnixTime - DamageDealtTrackingStartTimestamp;
                var actualWindow = Math.Min(elapsed, DamageDealtRollingWindowSeconds);
                if (actualWindow < 1.0) actualWindow = 1.0;

                var totalDamage = DamageDealtLog.Sum(e => (long)e.Amount);
                var totalHits = DamageDealtLog.Count;
                var avgHit = totalHits > 0 ? (double)totalDamage / totalHits : 0.0;
                var dps = (double)totalDamage / actualWindow;

                var message = $"[Damage Dealt] Total: {totalDamage:N0} | DPS: {dps:F2} | Hits: {totalHits:N0} | Avg/Hit: {avgHit:F2}";

                Session?.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
        }
    }
}
