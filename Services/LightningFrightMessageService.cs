using System;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>HUD/ambient сообщения LightningFright / GotoroFlashback из assets/stress_messages.json.</summary>
    public sealed class LightningFrightMessageService
    {
        private readonly IMonitor _monitor;
        private readonly ModConfig _config;
        private StressMessagesRoot _messages = new();

        public LightningFrightMessageService(IMonitor monitor, ModConfig config)
        {
            _monitor = monitor;
            _config = config;
        }

        public void Load(IModHelper helper)
        {
            try
            {
                _messages = helper.Data.ReadJsonFile<StressMessagesRoot>("assets/stress_messages.json")
                    ?? new StressMessagesRoot();

                _monitor.Log(
                    $"[LightningFrightMessages] Loaded pools: triggered={_messages.LightningFright.Triggered.Count}, " +
                    $"forest={_messages.LightningFright.InForest.Count}",
                    LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _monitor.Log($"[LightningFrightMessages] Load failed: {ex.Message}", LogLevel.Warn);
                _messages = new StressMessagesRoot();
            }
        }

        public int CooldownMinMinutes =>
            Math.Max(1, _config.LightningFrightCooldownMinMinutes);

        public int CooldownMaxMinutes =>
            Math.Max(CooldownMinMinutes, _config.LightningFrightCooldownMaxMinutes);

        public string? PickMessage(LightningFrightMessagePhase phase)
        {
            var pool = GetPool(phase);
            if (pool.Count == 0)
                return null;

            return pool[Game1.random.Next(pool.Count)];
        }

        /// <summary>Показывает HUD-сообщение фазы с учётом cooldown (кроме force).</summary>
        public bool TryShowPhaseMessage(
            ThunderFlashbackState state,
            LightningFrightMessagePhase phase,
            bool force = false)
        {
            if (!_config.EnableHudMessages)
                return false;

            if (GameStateHelper.IsBlockingFlashbackContext())
                return false;

            if (!force && !CanShowMessage(state))
                return false;

            var text = PickMessage(phase);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            ShowMessage(state, text, force);
            return true;
        }

        public void ShowMessage(ThunderFlashbackState state, string text, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(text) || !_config.EnableHudMessages)
                return;

            Game1.addHUDMessage(new HUDMessage(text, HUDMessage.achievement_type));
            state.LastHudMessageTime = Game1.timeOfDay;

            if (!force)
                state.HudMessageCooldownMinutes = RollCooldownMinutes();

            _monitor.Log(
                $"[LightningFrightMessages] HUD ({(force ? "force" : "cooldown")}): {text}",
                LogLevel.Trace);
        }

        public bool CanShowMessage(ThunderFlashbackState state)
        {
            if (state.LastHudMessageTime <= 0)
                return true;

            var cooldown = state.HudMessageCooldownMinutes > 0
                ? state.HudMessageCooldownMinutes
                : RollCooldownMinutes();

            return Math.Abs(Game1.timeOfDay - state.LastHudMessageTime) >= cooldown;
        }

        public int RollCooldownMinutes()
        {
            var min = CooldownMinMinutes;
            var max = CooldownMaxMinutes;
            return min == max ? min : min + Game1.random.Next(max - min + 1);
        }

        private System.Collections.Generic.IReadOnlyList<string> GetPool(LightningFrightMessagePhase phase)
        {
            var pools = _messages.LightningFright;
            return phase switch
            {
                LightningFrightMessagePhase.Triggered => pools.Triggered,
                LightningFrightMessagePhase.MovingToForest => pools.MovingToForest,
                LightningFrightMessagePhase.InForest => pools.InForest,
                LightningFrightMessagePhase.Stabilized => pools.Stabilized,
                LightningFrightMessagePhase.ReturnedTooEarly => pools.ReturnedTooEarly,
                _ => Array.Empty<string>(),
            };
        }
    }
}
