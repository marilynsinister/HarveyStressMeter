using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Services
{
    /// <summary>Ремиссия и риск рецидива после завершения терапии темноты.</summary>
    public class DarknessRemissionService
    {
        private readonly SaveData _data;
        private readonly DarknessService _darknessService;
        private readonly QuestService _questService;
        private readonly IMonitor _monitor;

        private int _lastTimeOfDay = -1;
        private int _outsideHomeAccumulatorMinutes;
        private int _lastHealth = -1;
        private bool _wasExhaustedLastTick;
        private float _prophylaxisElapsedMs;

        private const int RiskCap = 100;
        private const int OutsideHomeBucketMinutes = 30;

        public DarknessRemissionService(
            SaveData data,
            DarknessService darknessService,
            QuestService questService,
            IMonitor monitor)
        {
            _data = data;
            _darknessService = darknessService;
            _questService = questService;
            _monitor = monitor;
        }

        public bool IsRemissionActive => _data.Darkness.DarknessRemissionActive;

        public bool IsProphylaxisActive => _data.Darkness.DarknessProphylaxisActive;

        public void StartRemission(bool showHud = true)
        {
            var d = _data.Darkness;
            int today = SDate.Now().DaysSinceStart;

            d.DarknessRemissionActive = true;
            d.DarknessRemissionStartDay = today;
            d.DarknessRelapseRisk = 0;
            d.DarknessRelapseWarning50ShownToday = false;
            d.DarknessRelapseWarning75ShownToday = false;
            ClearRemissionSeverityFlags();
            d.DarknessProphylaxisActive = false;
            d.DarknessProphylaxisTodaySeconds = 0;
            d.DarknessRelapseTreatmentActive = false;
            d.DarknessTherapyEveningsRequired = DarknessLegacyHelper.Step1EveningsRequired;

            _outsideHomeAccumulatorMinutes = 0;
            _lastTimeOfDay = Game1.timeOfDay;
            _lastHealth = Game1.player?.health ?? -1;

            _monitor.Log(
                $"[DarknessRemission] Ремиссия начата day={today}, safe={d.DarknessRemissionMinSafeDays}, base={d.DarknessRemissionBaseDays}",
                LogLevel.Info);

            if (showHud && Context.IsWorldReady)
                Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.RemissionStartedHud, HUDMessage.newQuest_type));
        }

        public void ClearRemission()
        {
            var d = _data.Darkness;
            d.DarknessRemissionActive = false;
            d.DarknessRemissionStartDay = -1;
            d.DarknessRelapseRisk = 0;
            d.DarknessRelapseWarning50ShownToday = false;
            d.DarknessRelapseWarning75ShownToday = false;
            d.DarknessProphylaxisActive = false;
            ClearRemissionSeverityFlags();
            RemoveProphylaxisQuest();
        }

        public void OnDayStarted()
        {
            var d = _data.Darkness;
            d.DarknessRelapseWarning50ShownToday = false;
            d.DarknessRelapseWarning75ShownToday = false;
            d.DarknessRemissionSeriousEventToday = false;
            d.DarknessRemissionCalmHomeMinutesToday = 0;
            d.DarknessRemissionCalmCreditToday = false;
            _outsideHomeAccumulatorMinutes = 0;
            _lastTimeOfDay = Game1.timeOfDay;

            if (d.DarknessProphylaxisActive)
            {
                d.DarknessProphylaxisTodaySeconds = 0;
                RefreshProphylaxisQuest();
            }
        }

        public void OnDayEnding()
        {
            var d = _data.Darkness;
            if (!d.DarknessRemissionActive && !d.DarknessProphylaxisActive)
                return;

            if (Game1.timeOfDay < 2300 && Game1.player?.isInBed?.Value == true)
                AdjustRelapseRisk(-3, "sleep_before_23");
        }

        public void UpdatePerSecond(TimeSpan elapsed)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            var d = _data.Darkness;
            if (d.DarknessRemissionActive)
                UpdateRemissionRiskTick();

            if (d.DarknessProphylaxisActive)
                UpdateProphylaxisTimer(elapsed);

            TrackHealthAndPassOut();
        }

        public void OnDarknessEnvironmentalTrigger(GameLocation location)
        {
            if (!_data.Darkness.DarknessRemissionActive)
                return;

            var name = location.NameOrUniqueName;
            if (!DarknessRemissionHelper.IsDarkOutdoorLocation(name))
                return;

            AdjustRelapseRisk(+3, $"dark_outdoor:{name}");
            _data.Darkness.DarknessRemissionSeriousEventToday = true;
            _monitor.Log($"[DarknessRemission] Триггер темной локации {name} → +3 риск", LogLevel.Debug);
        }

        public void OnHarveyTalkEnded()
        {
            var d = _data.Darkness;
            if (!d.DarknessRemissionActive)
                return;

            if (d.DarknessRelapseRisk >= 50)
                AdjustRelapseRisk(-5, "harvey_talk");

            if (d.DarknessRelapseRisk >= 75 && !d.DarknessProphylaxisActive)
                TryStartProphylaxis();
        }

        public void TryStartProphylaxis()
        {
            var d = _data.Darkness;
            if (d.DarknessProphylaxisActive || d.DarknessRelapseRisk < 75)
                return;

            d.DarknessProphylaxisActive = true;
            d.DarknessProphylaxisTodaySeconds = 0;
            _prophylaxisElapsedMs = 0;

            if (!_questService.HasQuest(QuestIds.DarknessProphylaxis))
                _questService.AddQuest(QuestIds.DarknessProphylaxis);

            RefreshProphylaxisQuest();
            Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.ProphylaxisOfferHud, HUDMessage.newQuest_type));
            _monitor.Log("[DarknessRemission] Профилактический вечер назначен (риск >= 75)", LogLevel.Info);
        }

        public bool TryForceRelapse(string reason = "debug")
        {
            if (!_data.Darkness.DarknessRemissionActive)
                return false;

            _data.Darkness.DarknessRelapseRisk = RiskCap;
            TriggerRelapse(reason);
            return true;
        }

        public string BuildStatusLine()
        {
            var d = _data.Darkness;
            int today = SDate.Now().DaysSinceStart;
            int daysIn = DarknessRemissionHelper.GetDaysInRemission(d, today);

            return
                $"RemissionActive={d.DarknessRemissionActive} StartDay={d.DarknessRemissionStartDay} " +
                $"DaysInRemission={daysIn} MinSafe={d.DarknessRemissionMinSafeDays} Base={d.DarknessRemissionBaseDays} Max={d.DarknessRemissionMaxDays} " +
                $"RelapseRisk={d.DarknessRelapseRisk} RiskStage={DarknessRemissionCopy.RiskStageLabel(d.DarknessRelapseRisk)} " +
                $"RelapseCount={d.DarknessRelapseCount} warn50={d.DarknessRelapseWarning50ShownToday} warn75={d.DarknessRelapseWarning75ShownToday} " +
                $"prophylaxis={d.DarknessProphylaxisActive} relapseTreatment={d.DarknessRelapseTreatmentActive} " +
                $"therapyEveningsRequired={DarknessRemissionHelper.GetStep1EveningsRequired(d)}";
        }

        public void AdjustRelapseRisk(int delta, string reason)
        {
            if (!_data.Darkness.DarknessRemissionActive || delta == 0)
                return;

            int before = _data.Darkness.DarknessRelapseRisk;
            _data.Darkness.DarknessRelapseRisk = Math.Clamp(before + delta, 0, RiskCap);
            int after = _data.Darkness.DarknessRelapseRisk;

            if (after != before)
            {
                _monitor.Log(
                    $"[DarknessRemission] Risk {before}→{after} ({reason})",
                    LogLevel.Debug);
                TryShowRiskWarnings();
                TryEvaluateRelapse();
            }
        }

        public void SetRelapseRisk(int value, string reason)
        {
            int before = _data.Darkness.DarknessRelapseRisk;
            _data.Darkness.DarknessRelapseRisk = Math.Clamp(value, 0, RiskCap);
            _monitor.Log($"[DarknessRemission] Risk set {before}→{_data.Darkness.DarknessRelapseRisk} ({reason})", LogLevel.Info);
            TryShowRiskWarnings();
            TryEvaluateRelapse();
        }

        private void UpdateRemissionRiskTick()
        {
            int time = Game1.timeOfDay;
            int deltaMinutes = DarknessRemissionHelper.GetGameMinutesDelta(_lastTimeOfDay, time);
            _lastTimeOfDay = time;

            if (deltaMinutes <= 0)
                return;

            if (!DarknessRemissionHelper.IsNightGameTime(time))
                return;

            if (GameStateHelper.IsPlayerHomeLocation())
            {
                _outsideHomeAccumulatorMinutes = 0;
                AccumulateCalmHomeCredit(deltaMinutes);
                return;
            }

            _outsideHomeAccumulatorMinutes += deltaMinutes;
            while (_outsideHomeAccumulatorMinutes >= OutsideHomeBucketMinutes)
            {
                _outsideHomeAccumulatorMinutes -= OutsideHomeBucketMinutes;
                int add = DarknessRemissionHelper.IsAfterMidnight(time) ? 2 : 1;
                if (DarknessRemissionHelper.IsAfterTenPm(time) && !DarknessRemissionHelper.IsAfterMidnight(time))
                    add = 1;

                AdjustRelapseRisk(add, "outside_home_night");

                var locName = Game1.player.currentLocation?.NameOrUniqueName;
                if (DarknessRemissionHelper.IsDarkOutdoorLocation(locName))
                    AdjustRelapseRisk(+3, "dark_outdoor_bucket");

                if (DarknessRemissionHelper.IsDangerousNightLocation(locName))
                {
                    AdjustRelapseRisk(+5, "danger_location");
                    _data.Darkness.DarknessRemissionHadDangerLocation = true;
                    _data.Darkness.DarknessRemissionSeriousEventToday = true;
                }
            }
        }

        private void AccumulateCalmHomeCredit(int deltaMinutes)
        {
            if (Game1.timeOfDay < 2000)
                return;

            var d = _data.Darkness;
            d.DarknessRemissionCalmHomeMinutesToday += deltaMinutes;
            if (d.DarknessRemissionCalmHomeMinutesToday >= 60 && !d.DarknessRemissionCalmCreditToday)
            {
                d.DarknessRemissionCalmCreditToday = true;
                AdjustRelapseRisk(-2, "calm_home_evening");
            }
        }

        private void TrackHealthAndPassOut()
        {
            var player = Game1.player;
            if (player == null)
                return;

            int health = player.health;
            int time = Game1.timeOfDay;

            if (_lastHealth >= 0 && health < _lastHealth && DarknessRemissionHelper.IsNightGameTime(time))
            {
                AdjustRelapseRisk(+8, "night_damage");
                _data.Darkness.DarknessRemissionHadNightDamage = true;
                _data.Darkness.DarknessRemissionSeriousEventToday = true;

                float maxHp = player.maxHealth;
                if (maxHp > 0 && health / maxHp < 0.4f)
                    AdjustRelapseRisk(+25, "night_low_hp");
            }

            if (health <= 0 && DarknessRemissionHelper.IsNightGameTime(time))
            {
                AdjustRelapseRisk(+20, "pass_out");
                _data.Darkness.DarknessRemissionHadPassOut = true;
                _data.Darkness.DarknessRemissionSeriousEventToday = true;
            }

            bool lowStamina = player.stamina <= 1f;
            if (_wasExhaustedLastTick && lowStamina && player.isInBed.Value && DarknessRemissionHelper.IsNightGameTime(time))
            {
                AdjustRelapseRisk(+20, "exhausted_bed");
                _data.Darkness.DarknessRemissionHadPassOut = true;
                _data.Darkness.DarknessRemissionSeriousEventToday = true;
            }

            _lastHealth = health;
            _wasExhaustedLastTick = lowStamina && !GameStateHelper.IsPlayerHomeLocation();
        }

        private void TryShowRiskWarnings()
        {
            var d = _data.Darkness;
            if (d.DarknessRelapseRisk >= 50 && !d.DarknessRelapseWarning50ShownToday)
            {
                d.DarknessRelapseWarning50ShownToday = true;
                Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.WarningRisk50, HUDMessage.newQuest_type));
            }

            if (d.DarknessRelapseRisk >= 75 && !d.DarknessRelapseWarning75ShownToday)
            {
                d.DarknessRelapseWarning75ShownToday = true;
                Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.WarningRisk75, HUDMessage.newQuest_type));
            }
        }

        private void TryEvaluateRelapse()
        {
            var d = _data.Darkness;
            if (!d.DarknessRemissionActive)
                return;

            int today = SDate.Now().DaysSinceStart;
            int daysIn = DarknessRemissionHelper.GetDaysInRemission(d, today);

            if (daysIn < d.DarknessRemissionMinSafeDays)
                return;

            bool canRelapse = false;

            if (daysIn >= d.DarknessRemissionMaxDays && d.DarknessRelapseRisk >= RiskCap)
                canRelapse = true;
            else if (daysIn >= d.DarknessRemissionBaseDays && d.DarknessRelapseRisk >= RiskCap)
                canRelapse = true;
            else if (daysIn >= d.DarknessRemissionMinSafeDays
                     && daysIn < d.DarknessRemissionBaseDays
                     && d.DarknessRemissionSeriousEventToday
                     && (d.DarknessRemissionHadPassOut
                         || d.DarknessRemissionHadNightDamage
                         || d.DarknessRemissionHadDangerLocation))
                canRelapse = true;

            if (canRelapse)
                TriggerRelapse("risk_threshold");
        }

        private void TriggerRelapse(string reason)
        {
            var d = _data.Darkness;
            int evenings = DarknessRemissionHelper.ComputeRelapseEveningsRequired(d);

            _monitor.Log(
                $"[DarknessRemission] Рецидив ({reason}): risk={d.DarknessRelapseRisk}, evenings={evenings}, count={d.DarknessRelapseCount + 1}",
                LogLevel.Warn);

            d.DarknessRelapseCount++;
            ClearRemission();
            d.DarknessProphylaxisActive = false;
            RemoveProphylaxisQuest();

            Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.RelapseHud, HUDMessage.error_type));
            Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.RelapseTonightHud, HUDMessage.newQuest_type));
            Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.RelapseComeEarlyHud, HUDMessage.newQuest_type));

            _darknessService.BeginRelapseTreatment(evenings);
        }

        private void UpdateProphylaxisTimer(TimeSpan elapsed)
        {
            if (Game1.CurrentEvent != null || Game1.activeClickableMenu != null)
                return;

            if (Game1.player?.isInBed?.Value == true)
                return;

            bool evening = Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 2400;
            bool atHome = GameStateHelper.IsPlayerHomeLocation();
            if (!evening || !atHome)
                return;

            var d = _data.Darkness;
            if (d.DarknessProphylaxisTodaySeconds >= DarknessLegacyHelper.Step1SecondsPerEvening)
                return;

            _prophylaxisElapsedMs += (float)elapsed.TotalMilliseconds;
            while (_prophylaxisElapsedMs >= 1000f)
            {
                _prophylaxisElapsedMs -= 1000f;
                d.DarknessProphylaxisTodaySeconds++;

                if (d.DarknessProphylaxisTodaySeconds % 900 == 0)
                    RefreshProphylaxisQuest();

                if (d.DarknessProphylaxisTodaySeconds >= DarknessLegacyHelper.Step1SecondsPerEvening)
                    CompleteProphylaxisEvening();
            }
        }

        private void CompleteProphylaxisEvening()
        {
            var d = _data.Darkness;
            AdjustRelapseRisk(-30, "prophylaxis_evening");
            d.DarknessProphylaxisActive = false;
            RemoveProphylaxisQuest();

            Game1.addHUDMessage(new HUDMessage(DarknessRemissionCopy.ProphylaxisObjectiveDone(), HUDMessage.achievement_type));

            if (d.DarknessRelapseRisk < 50)
                _monitor.Log("[DarknessRemission] Профилактика завершена, риск < 50", LogLevel.Info);
        }

        private void RefreshProphylaxisQuest()
        {
            if (!_questService.HasQuest(QuestIds.DarknessProphylaxis))
                return;

            int min = _data.Darkness.DarknessProphylaxisTodaySeconds / 60;
            _questService.UpdateQuest(
                QuestIds.DarknessProphylaxis,
                objective: DarknessRemissionCopy.ProphylaxisObjective(min));
        }

        private void RemoveProphylaxisQuest()
        {
            if (_questService.HasQuest(QuestIds.DarknessProphylaxis))
                Game1.player.removeQuest(QuestIds.DarknessProphylaxis);
        }

        private void ClearRemissionSeverityFlags()
        {
            var d = _data.Darkness;
            d.DarknessRemissionHadPassOut = false;
            d.DarknessRemissionHadNightDamage = false;
            d.DarknessRemissionHadDangerLocation = false;
            d.DarknessRemissionSeriousEventToday = false;
            d.DarknessRemissionCalmHomeMinutesToday = 0;
            d.DarknessRemissionCalmCreditToday = false;
        }
    }
}
