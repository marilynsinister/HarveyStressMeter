using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>Дневная шкала социальной усталости: начисление, восстановление, пороги HUD и debuff Social.</summary>
    public sealed class SocialExposureService
    {
        private readonly SaveData _data;
        private readonly StateService _stateService;
        private readonly TreatmentService _treatmentService;
        private readonly IMonitor _monitor;

        private int _harveyRecoverySeconds;
        private int _homeRecoverySeconds;

        /// <summary>NPC, с которыми разговор уже учтён сегодня (сейв только в конце дня — хранить не нужно).</summary>
        private readonly HashSet<string> _npcsCountedToday = new(System.StringComparer.Ordinal);

        public SocialExposureService(
            SaveData data,
            StateService stateService,
            TreatmentService treatmentService,
            IMonitor monitor)
        {
            _data = data;
            _stateService = stateService;
            _treatmentService = treatmentService;
            _monitor = monitor;
        }

        public SocialExposureState State => _data.SocialExposure;

        public int ExposureToday => State.SocialExposureToday;

        public void ResetDaily()
        {
            State.SocialExposureToday = 0;
            State.ThresholdsShownToday = 0;
            _npcsCountedToday.Clear();
            _harveyRecoverySeconds = 0;
            _homeRecoverySeconds = 0;
        }

        /// <summary>Миграция старого поля SocialStressExposure (0–20) в новую шкалу 0–100.</summary>
        public void MigrateLegacyExposure()
        {
#pragma warning disable CS0618
            if (State.SocialExposureToday > 0 || _data.SocialStressExposure <= 0)
                return;

            State.SocialExposureToday = System.Math.Min(
                SocialStressHelper.MaxExposure,
                _data.SocialStressExposure * 5);
            _data.SocialStressExposure = 0;
#pragma warning restore CS0618
            _monitor.Log(
                $"[SocialExposure] Migrated legacy SocialStressExposure → {State.SocialExposureToday}/100",
                LogLevel.Info);
        }

        public void OnNpcConversationStarted(NPC npc)
        {
            if (Game1.CurrentEvent != null)
                return;

            if (!SocialStressHelper.IsQualifyingNpc(npc))
                return;

            // Подарок, повторный клик или второй DialogueBox того же NPC — не новый разговор.
            if (!_npcsCountedToday.Add(npc.Name))
                return;

            if (Game1.stats.DaysPlayed < 5)
            {
                LogSkip(npc, $"первые дни игры (день {Game1.stats.DaysPlayed}/5)");
                return;
            }

            if (IsSocialDebuffBlockingAccumulation())
            {
                LogSkip(npc, "debuff Social уже активен");
                return;
            }

            var blockReason = _stateService.GetIssueBlockReason(
                BuffIds.Social,
                TreatmentService.GetIssueCooldownDays(BuffIds.Social));
            if (blockReason != null)
            {
                // Шкала не копится, если срыв всё равно нельзя выдать: иначе игрок видит «на грани» без последствий.
                LogSkip(npc, blockReason);
                return;
            }

            var baseGain = SocialStressHelper.GetBaseExposureGain(npc.Name);
            if (baseGain <= 0)
            {
                LogSkip(npc, "близкий человек (8+ сердец)");
                return;
            }

            var multiplier = SocialStressHelper.GetAccumulationMultiplier(HasOtherActiveStressDebuff());
            var gain = SocialStressHelper.ApplyAccumulationMultiplier(baseGain, multiplier);
            if (gain <= 0)
                return;

            AddExposure(gain, $"разговор с {npc.Name} (+{gain}, base {baseGain}, mult {multiplier:0.##})");
        }

        private void LogSkip(NPC npc, string reason)
            => _monitor.Log($"[SocialExposure] Разговор с {npc.Name}: +0 — {reason}", LogLevel.Info);

        public void UpdateRecovery(bool harveyNearby)
        {
            if (State.SocialExposureToday <= 0)
            {
                _harveyRecoverySeconds = 0;
                _homeRecoverySeconds = 0;
                return;
            }

            if (harveyNearby)
            {
                _harveyRecoverySeconds++;
                if (_harveyRecoverySeconds >= SocialStressHelper.HarveyRecoveryIntervalSeconds)
                {
                    _harveyRecoverySeconds = 0;
                    ApplyRecovery(-1, "рядом с Харви");
                }
            }
            else
            {
                _harveyRecoverySeconds = 0;
            }

            if (SocialStressHelper.IsHomeRecoveryContext())
            {
                _homeRecoverySeconds++;
                if (_homeRecoverySeconds >= SocialStressHelper.HomeRecoveryIntervalSeconds)
                {
                    _homeRecoverySeconds = 0;
                    ApplyRecovery(-1, "дома после 18:00");
                }
            }
            else
            {
                _homeRecoverySeconds = 0;
            }
        }

        /// <summary>Каждые 10 игровых минут усталость понемногу спадает (кроме времени разговора).</summary>
        public void ApplyPassiveRecovery()
        {
            if (Game1.activeClickableMenu is StardewValley.Menus.DialogueBox || Game1.CurrentEvent != null)
                return;

            ApplyRecovery(-SocialStressHelper.PassiveRecoveryPer10Minutes, "время без разговоров");
        }

        public void SetExposure(int value)
        {
            var previous = State.SocialExposureToday;
            State.SocialExposureToday = ClampExposure(value);
            TryApplyThresholdMessages(previous);
            TryApplyDebuffAtMax();
        }

        public void AddExposure(int amount, string? reason = null)
        {
            if (amount == 0)
                return;

            var previous = State.SocialExposureToday;
            State.SocialExposureToday = ClampExposure(previous + amount);

            _monitor.Log(
                $"[SocialExposure] {(amount > 0 ? "+" : "")}{amount} → {State.SocialExposureToday}/{SocialStressHelper.MaxExposure}"
                + (string.IsNullOrEmpty(reason) ? "" : $" ({reason})"),
                LogLevel.Debug);

            TryApplyThresholdMessages(previous);
            TryApplyDebuffAtMax();
        }

        public string GetCompactStatusLabel()
            => SocialStressHelper.GetCompactStatusLabel(
                State.SocialExposureToday,
                _stateService.HasActiveTreatmentState(BuffIds.Social) || _stateService.HasBuffInGame(BuffIds.Social));

        public string BuildDebugSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"exposureToday: {State.SocialExposureToday}/{SocialStressHelper.MaxExposure}");
            sb.AppendLine($"status: {GetCompactStatusLabel()}");
            sb.AppendLine($"thresholdsShownToday: {State.ThresholdsShownToday}");
            sb.AppendLine($"socialDebuffActive: {IsSocialDebuffBlockingAccumulation()}");
            sb.AppendLine($"hasOtherStressDebuff: {HasOtherActiveStressDebuff()}");
            sb.AppendLine($"issueBlockReason: {_stateService.GetIssueBlockReason(BuffIds.Social, TreatmentService.GetIssueCooldownDays(BuffIds.Social)) ?? "(none)"}");
            sb.AppendLine($"npcsCountedToday: {string.Join(", ", _npcsCountedToday)}");
            sb.AppendLine($"harveyDatingOrMarried: {HarveyFriendshipHelper.IsDatingHarvey() || HarveyFriendshipHelper.IsMarriedToHarvey()}");
            sb.AppendLine($"harveyRecoverySeconds: {_harveyRecoverySeconds}/{SocialStressHelper.HarveyRecoveryIntervalSeconds}");
            sb.AppendLine($"homeRecoverySeconds: {_homeRecoverySeconds}/{SocialStressHelper.HomeRecoveryIntervalSeconds}");
            sb.AppendLine($"homeRecoveryActive: {SocialStressHelper.IsHomeRecoveryContext()}");
            return sb.ToString().TrimEnd();
        }

        private void ApplyRecovery(int amount, string reason)
        {
            if (State.SocialExposureToday <= 0)
                return;

            var previous = State.SocialExposureToday;
            State.SocialExposureToday = System.Math.Max(0, previous + amount);
            _monitor.Log(
                $"[SocialExposure] {amount} → {State.SocialExposureToday}/{SocialStressHelper.MaxExposure} ({reason})",
                LogLevel.Debug);
        }

        private void TryApplyThresholdMessages(int previousExposure)
        {
            foreach (var threshold in new[] { SocialStressHelper.ThresholdWarning, SocialStressHelper.ThresholdPause, SocialStressHelper.ThresholdOverload })
            {
                if (State.SocialExposureToday < threshold || previousExposure >= threshold)
                    continue;

                TryShowThresholdHud(threshold);
            }
        }

        private void TryShowThresholdHud(int threshold)
        {
            var flag = SocialStressHelper.ThresholdFlagForExposure(threshold);
            if (flag == 0 || (State.ThresholdsShownToday & flag) != 0)
                return;

            var message = SocialStressHelper.GetThresholdHudMessage(threshold);
            if (string.IsNullOrEmpty(message))
                return;

            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
            State.ThresholdsShownToday |= flag;
            _monitor.Log($"[SocialExposure] Threshold HUD {threshold}: {message}", LogLevel.Info);
        }

        private void TryApplyDebuffAtMax()
        {
            if (State.SocialExposureToday < SocialStressHelper.ThresholdDebuff)
                return;

            if (IsSocialDebuffBlockingAccumulation())
                return;

            var blockReason = _stateService.GetIssueBlockReason(
                BuffIds.Social,
                TreatmentService.GetIssueCooldownDays(BuffIds.Social));
            if (blockReason != null)
            {
                _monitor.Log($"[SocialExposure] Порог 100, но debuff Social не выдан: {blockReason}", LogLevel.Info);
                Game1.addHUDMessage(new HUDMessage(SocialStressHelper.MaxReachedButProtectedHud, HUDMessage.newQuest_type));
                return;
            }

            _monitor.Log("[SocialExposure] Порог 100 — выдан debuff Social", LogLevel.Info);
            _treatmentService.ApplyStressBuff(BuffIds.Social, "Социальный дискомфорт");
        }

        private bool IsSocialDebuffBlockingAccumulation()
            => _stateService.HasActiveTreatmentState(BuffIds.Social)
               || _stateService.HasBuffInGame(BuffIds.Social);

        private bool HasOtherActiveStressDebuff()
        {
            foreach (var buffId in StressDebuffSelector.PriorityOrder)
            {
                if (string.Equals(buffId, BuffIds.Social, System.StringComparison.Ordinal))
                    continue;

                if (_stateService.HasBuffInGame(buffId))
                    return true;
            }

            return DarknessLegacyHelper.HasAnyDarknessStressBuffInGame(_stateService);
        }

        private static int ClampExposure(int value)
            => System.Math.Clamp(value, 0, SocialStressHelper.MaxExposure);
    }

}
