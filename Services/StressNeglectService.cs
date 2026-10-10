using HarveyOverhaul.Core.Core;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Последствия запущенного стресса. Раньше стрессовый дебафф без лечения висел бесконечно и ни к чему
    /// не вёл (нагрузка сама спадала). Теперь по утрам:
    /// 2 дня без Харви — напоминание; 4 дня — стресс копится (вес причины растёт) и сон хуже (меньше энергии);
    /// 6 дней — Харви встревожен (доверие снижается). Начатое, но заброшенное назначение — напоминание на 3-й день.
    /// </summary>
    public sealed class StressNeglectService
    {
        private const int ReminderDays = 2;
        private const int WorseningDays = 4;
        private const int AlarmDays = 6;
        private const int StartedReminderDays = 3;
        private const int WorseningExtraWeight = 10;
        private const float PoorSleepStaminaShare = 0.15f;
        private const int AlarmTrustPenalty = 3;

        private readonly IMonitor _monitor;
        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly HarveyCareTrustService _trustService;

        public StressNeglectService(
            IMonitor monitor,
            SaveData data,
            StressLoadService stressLoadService,
            HarveyCareTrustService trustService)
        {
            _monitor = monitor;
            _data = data;
            _stressLoadService = stressLoadService;
            _trustService = trustService;
        }

        public void OnDayStarted(object? sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            int today = SDate.Now().DaysSinceStart;
            bool poorSleepApplied = false;

            foreach (var treatment in _data.StressState.ActiveTreatments.Values.ToList())
            {
                if (treatment.IsCured || treatment.IsCompleted || treatment.AwaitingHarveyReview)
                    continue;

                if (string.IsNullOrEmpty(treatment.BuffId) || !Game1.player.hasBuff(treatment.BuffId))
                    continue;

                // У темноты своя система уровней и счётчик «игнорируется».
                if (treatment.BuffId == BuffIds.Darkness || DarknessLegacyHelper.BlocksLegacyTreatmentPipeline(treatment.BuffId))
                    continue;

                if (treatment.TreatmentStarted)
                {
                    ProcessStartedTreatment(treatment, today);
                    continue;
                }

                poorSleepApplied |= ProcessUntreated(treatment, today, poorSleepApplied);
            }
        }

        private bool ProcessUntreated(TreatmentState treatment, int today, bool poorSleepAlreadyApplied)
        {
            int days = today - treatment.IssuedDate.DaysSinceStart;
            if (days < ReminderDays)
                return false;

            if (treatment.NeglectStage < 1)
            {
                treatment.NeglectStage = 1;
                Game1.addHUDMessage(new HUDMessage(
                    $"Харви заметил, что ты {PlayerGrammar.Gendered("сам не свой", "сама не своя")}. Загляни к нему.",
                    HUDMessage.health_type));
                Log(treatment, days, "напоминание");
            }

            if (days < WorseningDays)
                return false;

            if (treatment.NeglectStage < 2)
            {
                treatment.NeglectStage = 2;
                if (StressCauses.TryGetCauseForBuff(treatment.BuffId, out var causeId))
                {
                    _stressLoadService.AddCause(
                        causeId,
                        treatment.BuffId,
                        StressCauses.GetBaseWeight(causeId) + WorseningExtraWeight);
                }

                Game1.addHUDMessage(new HUDMessage(
                    "Стресс копится — без помощи становится только тяжелее.",
                    HUDMessage.error_type));
                Log(treatment, days, "стресс копится");
            }

            // Запущенный стресс мешает высыпаться: утром меньше энергии (один раз за утро, даже если дебаффов несколько).
            bool appliedNow = false;
            if (!poorSleepAlreadyApplied)
            {
                float loss = Game1.player.MaxStamina * PoorSleepStaminaShare;
                Game1.player.Stamina = Math.Max(1f, Game1.player.Stamina - loss);
                Game1.addHUDMessage(new HUDMessage(
                    $"Ты плохо {PlayerGrammar.Gendered("спал", "спала")} — тревога не отпускает.",
                    HUDMessage.health_type));
                appliedNow = true;
            }

            if (days >= AlarmDays && treatment.NeglectStage < 3)
            {
                treatment.NeglectStage = 3;
                _trustService.PenalizeTrust(HarveyCareTrustReasons.LongIgnoredAssignment, AlarmTrustPenalty);
                Game1.addHUDMessage(new HUDMessage(
                    "Харви встревожен: ты избегаешь его уже почти неделю.",
                    HUDMessage.error_type));
                Log(treatment, days, "Харви встревожен, доверие снижено");
            }

            return appliedNow;
        }

        private void ProcessStartedTreatment(TreatmentState treatment, int today)
        {
            if (treatment.StartedReminderShown || treatment.TreatmentStartedDate == null)
                return;

            int days = today - treatment.TreatmentStartedDate.DaysSinceStart;
            if (days < StartedReminderDays)
                return;

            treatment.StartedReminderShown = true;
            Game1.addHUDMessage(new HUDMessage(
                "Харви ждёт, как продвигается его назначение. Загляни в журнал заданий.",
                HUDMessage.health_type));
            Log(treatment, days, "напоминание о начатом назначении");
        }

        private void Log(TreatmentState treatment, int days, string what) =>
            _monitor.Log($"[StressNeglect] {treatment.BuffId}: {days} дн. без лечения — {what}", LogLevel.Info);
    }
}
