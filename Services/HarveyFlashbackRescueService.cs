using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Харви может найти игрока в лесу во время Gotoro thunder flashback.
    /// Не телепортирует игрока, не завершает treatment напрямую, не выдаёт лечебные баффы.
    /// </summary>
    public sealed class HarveyFlashbackRescueService
    {
        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly ThunderFlashbackService _thunderFlashbackService;
        private readonly TreatmentService _treatmentService;
        private readonly ModConfig _config;
        private readonly IMonitor _monitor;
        private readonly HarveyCareTrustService? _trustService;
        private HarveyCareTrustDialogueService? _trustDialogueService;
        private StressSystemsCoordinator? _coordinator;

        private bool _rescueEventWasActive;

        public HarveyFlashbackRescueService(
            SaveData data,
            StressLoadService stressLoadService,
            ThunderFlashbackService thunderFlashbackService,
            TreatmentService treatmentService,
            ModConfig config,
            IMonitor monitor,
            HarveyCareTrustService? trustService = null)
        {
            _data = data;
            _stressLoadService = stressLoadService;
            _thunderFlashbackService = thunderFlashbackService;
            _treatmentService = treatmentService;
            _config = config;
            _monitor = monitor;
            _trustService = trustService;
        }

        public HarveyFlashbackRescueState State => _data.HarveyFlashbackRescue;

        public void SetCoordinator(StressSystemsCoordinator coordinator)
            => _coordinator = coordinator;

        public void SetTrustDialogueService(HarveyCareTrustDialogueService trustDialogueService)
            => _trustDialogueService = trustDialogueService;

        public void ResetDailyState()
        {
            State.HarveyRescueTriggeredToday = false;
            State.HarveyHelpedStabilizeToday = false;
            State.ForestSecondsBeforeRescue = 0;
            State.LastRescueCheckTime = 0;
            State.RescueTier = null;
            _rescueEventWasActive = false;
        }

        public void ResetRescueState()
        {
            ResetDailyState();
            State.LastRescueDay = 0;
            State.LastRescueEventId = null;
            State.PendingPostRescueTier = null;
            State.PendingPostRescueEventId = null;
            _rescueEventWasActive = false;
            ClearRescuePendingTopic();
        }

        private static void ClearRescuePendingTopic()
        {
            if (ConversationHelper.HasTopic(TopicIds.GotoroForestRescuePending))
                ConversationHelper.RemoveTopic(TopicIds.GotoroForestRescuePending);
        }

        public void OnLocationChanged(string? previousLocation, string? newLocation)
        {
            if (!GameStateHelper.IsForestShelterLocation())
                State.ForestSecondsBeforeRescue = 0;
        }

        public void Update(int elapsedSeconds = 1)
        {
            if (!Context.IsWorldReady || !_config.EnableHarveyFlashbackRescue)
                return;

            TryCompletePendingPostRescue();

            if (elapsedSeconds <= 0)
                return;

            if (!IsGotoroFlashbackContextActive())
            {
                State.ForestSecondsBeforeRescue = 0;
                return;
            }

            if (GameStateHelper.IsForestShelterLocation())
                State.ForestSecondsBeforeRescue += elapsedSeconds;
            else
                State.ForestSecondsBeforeRescue = 0;

            State.LastRescueCheckTime = Game1.timeOfDay;

            if (State.HarveyRescueTriggeredToday || State.PendingPostRescueTier != null)
                return;

            if (!CanAttemptRescue(out var blockReason))
            {
                if (State.ForestSecondsBeforeRescue >= _config.MinForestSecondsBeforeRescue)
                    LogRescueSkipped(blockReason);
                return;
            }

            var tier = HarveyFriendshipHelper.ResolveRescueTier(_config.MinHeartsForForestRescue);
            if (tier == null)
            {
                LogRescueSkipped($"Harvey hearts < {_config.MinHeartsForForestRescue}");
                return;
            }

            var chance = ComputeRescueChance(tier, out var baseChance, out var trustBonus);
            var roll = Game1.random.NextDouble();
            LogRescueRoll(tier, baseChance, trustBonus, chance, roll, passed: roll <= chance);

            if (chance <= 0 || roll > chance)
                return;

            TryTriggerRescue(tier, chanceAlreadyPassed: true);
        }

        public RescueEvaluation EvaluateRescue(bool ignoreChance = false)
        {
            var eval = new RescueEvaluation
            {
                Enabled = _config.EnableHarveyFlashbackRescue,
                GotoroFlashbackActive = _data.StressLoad.GotoroFlashbackActive,
                FlashbackIsActive = _thunderFlashbackService.State.IsActive,
                IsGotoroFlashback = _thunderFlashbackService.State.IsGotoroFlashback,
                StormWeather = GameStateHelper.IsStormWeather(),
                InForest = GameStateHelper.IsForestShelterLocation(),
                ForestSeconds = State.ForestSecondsBeforeRescue,
                MinForestSeconds = _config.MinForestSecondsBeforeRescue,
                HarveyHearts = HarveyFriendshipHelper.GetHarveyHearts(),
                RescueTriggeredToday = State.HarveyRescueTriggeredToday,
                HarveyHelpedToday = State.HarveyHelpedStabilizeToday,
                LastRescueDay = State.LastRescueDay,
                AwaitingHarveyReview = IsAwaitingHarveyReview(),
            };

            eval.Tier = HarveyFriendshipHelper.ResolveRescueTier(_config.MinHeartsForForestRescue);
            eval.TrustLevel = _trustService?.GetTrustLevel() ?? 0;
            eval.TrustRescueBonus = _trustService?.GetRescueChanceBonus() ?? 0;
            eval.BaseRescueChance = eval.Tier != null
                ? HarveyFriendshipHelper.GetRescueChance(eval.Tier, _config)
                : 0;
            eval.RescueChance = eval.Tier != null
                ? Math.Min(1.0, eval.BaseRescueChance + eval.TrustRescueBonus)
                : 0;

            eval.CanAttempt = CanAttemptRescue(out var blockReason, ignoreChance);
            eval.BlockReason = blockReason;

            return eval;
        }

        public bool TryTriggerRescue(string? tierOverride = null, bool force = false, bool chanceAlreadyPassed = false)
        {
            if (!Context.IsWorldReady)
                return false;

            if (!force && !_config.EnableHarveyFlashbackRescue)
                return false;

            if (!CanAttemptRescue(out var reason, ignoreChance: true))
            {
                if (!force)
                    LogRescueSkipped(reason);
                return false;
            }

            var tier = tierOverride;
            if (tier == null || !FlashbackRescueTiers.TryParse(tier, out tier!))
                tier = HarveyFriendshipHelper.ResolveRescueTier(_config.MinHeartsForForestRescue);

            if (tier == null)
            {
                LogRescueSkipped($"Harvey hearts < {_config.MinHeartsForForestRescue}");
                return false;
            }

            if (!force && !chanceAlreadyPassed)
            {
                var chance = ComputeRescueChance(tier, out var baseChance, out var trustBonus);
                var roll = Game1.random.NextDouble();
                LogRescueRoll(tier, baseChance, trustBonus, chance, roll, passed: roll <= chance);

                if (chance <= 0 || roll > chance)
                    return false;
            }
            else if (force)
            {
                _monitor.Log(
                    $"[FlashbackRescue] Forced trigger: tier={tier}, hearts={HarveyFriendshipHelper.GetHarveyHearts()}, " +
                    $"trust={_trustService?.GetTrustLevel() ?? 0}",
                    LogLevel.Debug);
            }

            var eventId = FlashbackRescueEventIds.ForTier(tier);
            var location = Game1.currentLocation;
            if (location == null)
                return false;

            if (GameStateHelper.IsBlockingFlashbackContext() && !force)
                return false;

            if (!ConversationHelper.HasTopic(TopicIds.GotoroForestRescuePending))
                ConversationHelper.AddTopic(TopicIds.GotoroForestRescuePending, 0);

            var started = EventStartHelper.TryStartRescueEvent(
                location,
                eventId,
                tier,
                _monitor,
                out var usedCp);

            if (!started)
            {
                ShowFallbackMessage(tier);
                MarkRescueConsumedToday(tier, "fallback");
                ApplyPostRescueEffects(tier, "fallback", usedFallback: true);
                _monitor.Log(
                    $"[FlashbackRescue] Fallback rescue applied: tier={tier}, hearts={HarveyFriendshipHelper.GetHarveyHearts()}, " +
                    $"trust={_trustService?.GetTrustLevel() ?? 0}, eventId=fallback",
                    LogLevel.Info);
                return true;
            }

            if (!usedCp)
            {
                _monitor.Log(
                    $"[FlashbackRescue] Event '{eventId}' not in CP — used dynamic script at player tile.",
                    LogLevel.Warn);
            }

            MarkRescueConsumedToday(tier, eventId);
            State.PendingPostRescueTier = tier;
            State.PendingPostRescueEventId = eventId;
            _rescueEventWasActive = false;

            _monitor.Log(
                $"[FlashbackRescue] Started event tier={tier}, id={eventId}, cp={usedCp}, " +
                $"hearts={HarveyFriendshipHelper.GetHarveyHearts()}, trust={_trustService?.GetTrustLevel() ?? 0}, " +
                $"forestSeconds={State.ForestSecondsBeforeRescue}",
                LogLevel.Info);

            return true;
        }

        private void TryCompletePendingPostRescue()
        {
            if (State.PendingPostRescueTier == null)
                return;

            if (GameStateHelper.IsEventActive())
            {
                _rescueEventWasActive = true;
                return;
            }

            if (!_rescueEventWasActive)
                return;

            var tier = State.PendingPostRescueTier;
            var eventId = State.PendingPostRescueEventId ?? FlashbackRescueEventIds.ForTier(tier!);
            ApplyPostRescueEffects(tier!, eventId, usedFallback: false);
            State.PendingPostRescueTier = null;
            State.PendingPostRescueEventId = null;
            _rescueEventWasActive = false;
            ClearRescuePendingTopic();
        }

        private void ApplyPostRescueEffects(string tier, string eventId, bool usedFallback)
        {
            var stressReduced = HarveyFriendshipHelper.GetStressReductionForTier(tier);
            var shelterBonus = HarveyFriendshipHelper.GetForestShelterBonusForTier(tier);
            var friendshipBonus = HarveyFriendshipHelper.GetFriendshipBonusForTier(tier);

            _stressLoadService.DecayStress(stressReduced);
            _thunderFlashbackService.MarkHarveyHelped(shelterBonus);

            var flashbackStabilized = _thunderFlashbackService.State.ForestShelterSeconds
                >= _thunderFlashbackService.State.RequiredForestShelterSeconds;

            if (flashbackStabilized)
                _thunderFlashbackService.StabilizeFlashback(applyStressDecay: false);

            var rescueOutcome = _coordinator?.OnRescueCompleted(
                tier,
                stressReduced,
                shelterBonus,
                flashbackStabilized);

            if (rescueOutcome?.MarkedReadyForReview == true
                && !string.IsNullOrEmpty(rescueOutcome.ReadyForReviewEpisodeId)
                && !flashbackStabilized)
            {
                _treatmentService.MarkTreatmentReadyForReviewByEpisode(
                    rescueOutcome.ReadyForReviewEpisodeId,
                    "Харви помог тебе вернуться. Когда будешь готова — поговорим.");
            }

            ApplyFriendshipBonus(friendshipBonus);
            _trustService?.OnForestRescueCompleted();
            _trustDialogueService?.QueueRescueFollowUpTopic(tier);

            MarkRescueConsumedToday(tier, eventId);
            State.HarveyHelpedStabilizeToday = true;
            ClearRescuePendingTopic();

            _monitor.Log(
                $"[FlashbackRescue] Post-rescue applied: tier={tier}, stressReduced={stressReduced}, " +
                $"forestSeconds={State.ForestSecondsBeforeRescue}, shelter={_thunderFlashbackService.State.ForestShelterSeconds}/" +
                $"{_thunderFlashbackService.State.RequiredForestShelterSeconds}, fallback={usedFallback}, eventId={eventId}",
                LogLevel.Info);
        }

        private void MarkRescueConsumedToday(string tier, string eventId)
        {
            State.HarveyRescueTriggeredToday = true;
            State.LastRescueDay = (int)Game1.Date.TotalDays;
            State.RescueTier = tier;
            State.LastRescueEventId = eventId;
        }

        private double ComputeRescueChance(string tier, out double baseChance, out double trustBonus)
        {
            baseChance = HarveyFriendshipHelper.GetRescueChance(tier, _config);
            trustBonus = _trustService?.GetRescueChanceBonus() ?? 0;
            return Math.Min(1.0, baseChance + trustBonus);
        }

        private void LogRescueSkipped(string? reason)
        {
            _monitor.Log(
                $"[FlashbackRescue] Skipped: {reason ?? "(unknown)"}; hearts={HarveyFriendshipHelper.GetHarveyHearts()}, " +
                $"trust={_trustService?.GetTrustLevel() ?? 0}, forestSeconds={State.ForestSecondsBeforeRescue}, " +
                $"triggeredToday={State.HarveyRescueTriggeredToday}, awaitingReview={IsAwaitingHarveyReview()}",
                LogLevel.Debug);
        }

        private void LogRescueRoll(
            string tier,
            double baseChance,
            double trustBonus,
            double totalChance,
            double roll,
            bool passed)
        {
            _monitor.Log(
                $"[FlashbackRescue] Roll {(passed ? "passed" : "failed")}: tier={tier}, hearts={HarveyFriendshipHelper.GetHarveyHearts()}, " +
                $"trust={_trustService?.GetTrustLevel() ?? 0}, base={baseChance:P0}, trustBonus=+{trustBonus:P0}, " +
                $"total={totalChance:P0}, roll={roll:F3}",
                LogLevel.Debug);
        }

        private static void ApplyFriendshipBonus(int points)
        {
            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey == null || points <= 0)
                return;

            Game1.player.changeFriendship(points, harvey);
        }

        private static void ShowFallbackMessage(string tier)
        {
            var text = tier switch
            {
                FlashbackRescueTiers.Married =>
                    "Харви: Я здесь. Дыши со мной — вдох, выдох. Ты в безопасности.",
                FlashbackRescueTiers.Dating =>
                    "Харви: Я нашёл тебя. Это гроза, не взрывы. Я рядом.",
                FlashbackRescueTiers.HighTrust =>
                    "Харви: Это я. Ты в лесу, со мной. Дыши медленно.",
                _ => "Харви: Это доктор Харви. Сфокусируйся на дыхании — ты не одна.",
            };

            Game1.showGlobalMessage(text);
        }

        private bool CanAttemptRescue(out string? blockReason, bool ignoreChance = false)
        {
            blockReason = null;

            if (!_config.EnableHarveyFlashbackRescue)
            {
                blockReason = "disabled in config";
                return false;
            }

            if (!IsGotoroFlashbackContextActive())
            {
                blockReason = "no active Gotoro flashback";
                return false;
            }

            if (!GameStateHelper.IsStormWeather())
            {
                blockReason = "not storm weather";
                return false;
            }

            if (!GameStateHelper.IsForestShelterLocation())
            {
                blockReason = "player not in forest shelter";
                return false;
            }

            if (State.ForestSecondsBeforeRescue < _config.MinForestSecondsBeforeRescue)
            {
                blockReason = $"forest time {State.ForestSecondsBeforeRescue} < {_config.MinForestSecondsBeforeRescue}s";
                return false;
            }

            if (GameStateHelper.IsBlockingFlashbackContext())
            {
                blockReason = "blocking context (event/dialogue/menu/festival)";
                return false;
            }

            if (State.HarveyRescueTriggeredToday)
            {
                blockReason = "rescue already triggered today";
                return false;
            }

            if (!IsCooldownElapsed())
            {
                blockReason = $"cooldown ({_config.RescueCooldownDays} days since last rescue)";
                return false;
            }

            if (IsAwaitingHarveyReview())
            {
                blockReason = "awaiting Harvey review";
                return false;
            }

            if (HarveyFriendshipHelper.ResolveRescueTier(_config.MinHeartsForForestRescue) == null)
            {
                blockReason = $"Harvey hearts < {_config.MinHeartsForForestRescue}";
                return false;
            }

            if (!IsHarveyAvailable())
            {
                blockReason = "Harvey unavailable";
                return false;
            }

            if (State.PendingPostRescueTier != null)
            {
                blockReason = "post-rescue pending";
                return false;
            }

            return true;
        }

        private bool IsGotoroFlashbackContextActive()
            => _data.StressLoad.GotoroFlashbackActive
               && _thunderFlashbackService.State.IsActive
               && _thunderFlashbackService.State.IsGotoroFlashback;

        private bool IsCooldownElapsed()
        {
            if (State.LastRescueDay <= 0)
                return true;

            if (_config.MarriedIgnoresCooldown && HarveyFriendshipHelper.IsMarriedToHarvey())
                return true;

            var daysSince = (int)Game1.Date.TotalDays - State.LastRescueDay;
            return daysSince >= _config.RescueCooldownDays;
        }

        private bool IsAwaitingHarveyReview()
            => _stressLoadService.IsAwaitingHarveyReview()
               || _data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true;

        private static bool IsHarveyAvailable()
        {
            if (Game1.getCharacterFromName("Harvey") == null)
                return false;

            try
            {
                if (Game1.isFestival())
                    return false;
            }
            catch
            {
                // ignored
            }

            return true;
        }

        public string BuildDebugSnapshot()
        {
            var eval = EvaluateRescue(ignoreChance: true);
            var sb = new StringBuilder();
            sb.AppendLine("=== Harvey Flashback Rescue ===");
            sb.AppendLine($"Enabled: {_config.EnableHarveyFlashbackRescue}");
            sb.AppendLine($"HarveyRescueTriggeredToday: {State.HarveyRescueTriggeredToday}");
            sb.AppendLine($"HarveyHelpedStabilizeToday: {State.HarveyHelpedStabilizeToday}");
            sb.AppendLine($"LastRescueDay: {State.LastRescueDay} (today={(int)Game1.Date.TotalDays})");
            sb.AppendLine($"LastRescueEventId: {State.LastRescueEventId ?? "(none)"}");
            sb.AppendLine($"ForestSecondsBeforeRescue: {State.ForestSecondsBeforeRescue} (min {_config.MinForestSecondsBeforeRescue})");
            sb.AppendLine($"RescueTier: {State.RescueTier ?? "(none)"}");
            sb.AppendLine($"PendingPostRescue: {State.PendingPostRescueTier ?? "(none)"}");
            sb.AppendLine($"CanAttempt: {eval.CanAttempt}");
            sb.AppendLine($"BlockReason: {eval.BlockReason ?? "(none)"}");
            sb.AppendLine($"EvalTier: {eval.Tier ?? "(none)"}, base={eval.BaseRescueChance:P0}, trustBonus=+{eval.TrustRescueBonus:P0}, total={eval.RescueChance:P0}");
            sb.AppendLine($"TrustLevel: {eval.TrustLevel}");
            sb.AppendLine($"CooldownDays: {_config.RescueCooldownDays}, marriedBypass={_config.MarriedIgnoresCooldown}");
            sb.AppendLine($"GotoroActive: {eval.GotoroFlashbackActive}, flashback={eval.FlashbackIsActive}, gotoro={eval.IsGotoroFlashback}");
            sb.AppendLine($"Storm: {eval.StormWeather}, forest: {eval.InForest}, hearts: {eval.HarveyHearts}");
            return sb.ToString().TrimEnd();
        }
    }

    public sealed class RescueEvaluation
    {
        public bool Enabled { get; init; }
        public bool GotoroFlashbackActive { get; init; }
        public bool FlashbackIsActive { get; init; }
        public bool IsGotoroFlashback { get; init; }
        public bool StormWeather { get; init; }
        public bool InForest { get; init; }
        public int ForestSeconds { get; init; }
        public int MinForestSeconds { get; init; }
        public int HarveyHearts { get; init; }
        public bool RescueTriggeredToday { get; init; }
        public bool HarveyHelpedToday { get; init; }
        public int LastRescueDay { get; init; }
        public bool AwaitingHarveyReview { get; init; }
        public string? Tier { get; set; }
        public double BaseRescueChance { get; set; }
        public double TrustRescueBonus { get; set; }
        public int TrustLevel { get; set; }
        public double RescueChance { get; set; }
        public bool CanAttempt { get; set; }
        public string? BlockReason { get; set; }
    }
}
