using System;
using System.Reflection;
using log4net;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers.ZoneControl
{
    /// <summary>
    /// Applies per-zone PLAYER effects (currently damage-over-time; slow/charm reserved). Direct-applier model:
    /// the server acts on the player each due tick with no spell/enchantment object, so effects are fully under
    /// our control and stop the instant a player leaves the zone. Called EACH Player_Tick (per-frame) and
    /// self-timed via <see cref="Player.ZoneEffectNextTick"/> so a zone can tick as fast as 1s regardless of the
    /// 5s heartbeat, while players not in any zone cost only a cheap "not due yet" / landblock-set check.
    /// </summary>
    public static class ZoneEffectManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>How often to re-check a player who isn't currently taking a zone tick (entering-zone latency cap).</summary>
        private const double IdleRecheckSeconds = 1.0;
        /// <summary>Floor on the authored interval so a misconfigured 0 can't hammer every frame.</summary>
        private const double MinIntervalSeconds = 1.0;

        public static void Tick(Player player, double currentUnixTime)
        {
            if (player == null)
                return;

            // Cheap gate: not due yet.
            if (currentUnixTime < player.ZoneEffectNextTick)
                return;

            // Default: look again in 1s. Overwritten below to the effect interval when a tick actually lands.
            player.ZoneEffectNextTick = currentUnixTime + IdleRecheckSeconds;

            if (player.IsDead || player.Teleporting)
                return;

            ZoneEffects fx;
            try
            {
                fx = ZoneControlManager.ResolveEffectsForPlayer(player);
            }
            catch (Exception ex)
            {
                log.Error($"ZoneEffectManager.Tick resolve failed for {player.Name}: {ex}");
                return;
            }

            if (fx == null || !fx.DotEnabled || fx.DotDamage <= 0)
                return;

            ApplyDot(player, fx);

            var interval = Math.Max(MinIntervalSeconds, fx.DotIntervalSeconds);
            player.ZoneEffectNextTick = currentUnixTime + interval;
        }

        /// <summary>One DoT hit: flat points, or a percent of the player's max health when DotPercent is set
        /// (which forces Health damage). Vital selection + messaging live in Player.TakeZoneEffectDamage.</summary>
        private static void ApplyDot(Player player, ZoneEffects fx)
        {
            double raw = fx.DotPercent
                ? player.Health.MaxValue * (fx.DotDamage / 100.0)
                : fx.DotDamage;

            var amount = (uint)Math.Round(raw);
            if (amount == 0)
                return;

            var damageType = fx.DotPercent ? DamageType.Health : (DamageType)fx.DotDamageType;
            if (damageType == DamageType.Undef)
                damageType = DamageType.Fire;

            try
            {
                player.TakeZoneEffectDamage(amount, damageType);
            }
            catch (Exception ex)
            {
                log.Error($"ZoneEffectManager.ApplyDot failed for {player.Name}: {ex}");
            }
        }
    }
}
