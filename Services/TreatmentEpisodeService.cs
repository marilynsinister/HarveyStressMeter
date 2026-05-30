using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>Выбор TreatmentEpisode и lifecycle назначения (единый квест на несколько causes).</summary>
    public sealed class TreatmentEpisodeService
    {
        private const int DefaultEpisodeImmunityDays = 3;

        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly StateService _stateService;
        private readonly QuestService _questService;
        private readonly BuffService _buffService;
        private readonly IMonitor _monitor;
        private TreatmentService? _treatmentService;
        private HarveyCareTrustService? _trustService;
        private StressSystemsCoordinator? _coordinator;

        public TreatmentEpisodeService(
            SaveData data,
            StressLoadService stressLoadService,
            StateService stateService,
            QuestService questService,
            BuffService buffService,
            IMonitor monitor)
        {
            _data = data;
            _stressLoadService = stressLoadService;
            _stateService = stateService;
            _questService = questService;
            _buffService = buffService;
            _monitor = monitor;
        }

        public void SetTreatmentService(TreatmentService treatmentService)
            => _treatmentService = treatmentService;

        public void SetTrustService(HarveyCareTrustService trustService)
            => _trustService = trustService;

        public void SetCoordinator(StressSystemsCoordinator coordinator)
            => _coordinator = coordinator;

        public bool HasActiveTreatmentEpisode()
            => _data.ActiveTreatmentEpisode?.IsActiveEpisode() == true;

        public TreatmentEpisodeState? GetActiveTreatmentEpisode()
            => HasActiveTreatmentEpisode() ? _data.ActiveTreatmentEpisode : null;

        public TreatmentEpisodeState? GetTreatmentAwaitingReview()
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !episode.AwaitingHarveyReview || episode.IsCompleted)
                return null;

            return episode;
        }

        public bool StartTreatmentEpisode(string episodeId)
        {
            if (_coordinator != null && !_coordinator.CanStartTreatmentEpisode(episodeId))
                return false;

            if (HasActiveTreatmentEpisode())
            {
                _monitor.Log("[StartTreatmentEpisode] Уже есть active treatment episode", LogLevel.Debug);
                return false;
            }

            if (IsEpisodeOnImmunity(episodeId))
            {
                _monitor.Log($"[StartTreatmentEpisode] Episode {episodeId} на кулдауне", LogLevel.Debug);
                return false;
            }

            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
            {
                _monitor.Log($"[StartTreatmentEpisode] Неизвестный episode: {episodeId}", LogLevel.Error);
                return false;
            }

            if (_treatmentService == null)
            {
                _monitor.Log("[StartTreatmentEpisode] TreatmentService не установлен", LogLevel.Error);
                return false;
            }

            var activeCauseIds = _stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .Select(kvp => kvp.Key)
                .ToList();

            var relatedCauseIds = definition.RelatedCauses
                .Where(activeCauseIds.Contains)
                .ToList();

            if (relatedCauseIds.Count == 0 && episodeId == StressEpisodes.GotoroFlashback)
                relatedCauseIds.Add(StressCauses.GotoroFlashback);

            var primaryCauseId = TreatmentEpisodeDefinitions.ResolvePrimaryCauseId(episodeId, activeCauseIds)
                ?? relatedCauseIds.FirstOrDefault();
            var primaryBuffId = TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(episodeId, activeCauseIds);

            _data.ActiveTreatmentEpisode = new TreatmentEpisodeState
            {
                EpisodeId = episodeId,
                RelatedCauseIds = relatedCauseIds,
                QuestId = definition.QuestId,
                PrimaryCauseId = primaryCauseId,
                StartedTime = Game1.timeOfDay,
                TreatmentStarted = true,
            };

            _stressLoadService.SetActiveTreatmentEpisode(episodeId);
            _stressLoadService.Recalculate();

            _treatmentService.StartTreatment(
                primaryBuffId,
                definition.DisplayName,
                questIdOverride: definition.QuestId,
                episodeId: episodeId);

            ShowHudMessage(StressQuestCopy.TreatmentAssignedHud);

            _monitor.Log(
                $"[StartTreatmentEpisode] Episode={episodeId}, quest={definition.QuestId}, " +
                $"primary={primaryCauseId}/{primaryBuffId}, related=[{string.Join(", ", relatedCauseIds)}]",
                LogLevel.Info);

            return true;
        }

        public void MarkTreatmentEpisodeReadyForReview(string episodeId, string? optionalMessage = null)
        {
            var episode = GetActiveTreatmentEpisode();
            if (episode == null || !string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
            {
                if (TryMarkEpisodeReadyForReviewWithoutEpisodeState(episodeId, optionalMessage))
                    return;

                _monitor.Log(
                    $"[MarkTreatmentEpisodeReadyForReview] Active episode mismatch: expected {episodeId}, " +
                    $"got {episode?.EpisodeId ?? "(none)"}",
                    LogLevel.Warn);
                return;
            }

            if (episode.AwaitingHarveyReview || episode.IsCompleted)
                return;

            episode.ObjectivesCompleted = true;
            episode.AwaitingHarveyReview = true;
            episode.ReadyForReviewTime = Game1.timeOfDay;

            _questService.UpdateQuest(episode.QuestId, objective: StressQuestCopy.ReadyForReviewObjective);

            SyncLegacyTreatmentReviewFlags(episode);
            AddReadyForReviewTopic(episode);

            _stressLoadService.Recalculate();

            ShowHudMessage(optionalMessage ?? StressQuestCopy.ReadyForReviewHud);

            _trustService?.OnTreatmentMarkedReadyForReview(episodeId);

            _monitor.Log(
                $"[MarkTreatmentEpisodeReadyForReview] episode={episodeId}, quest={episode.QuestId}, AwaitingHarveyReview=true",
                LogLevel.Info);
        }

        /// <summary>Fallback для старых сейвов: episode state потерян, но квест в журнале активен.</summary>
        private bool TryMarkEpisodeReadyForReviewWithoutEpisodeState(string episodeId, string? optionalMessage)
        {
            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
                return false;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(definition.QuestId);
            if (treatment == null || !treatment.TreatmentStarted || treatment.IsCured || treatment.IsCompleted)
                return false;

            if (treatment.AwaitingHarveyReview)
                return true;

            treatment.ObjectivesCompleted = true;
            treatment.AwaitingHarveyReview = true;
            treatment.ReadyForReviewDate = SDate.Now();

            RepairEpisodeStateForReview(episodeId, definition, treatment);

            _questService.UpdateQuest(definition.QuestId, objective: StressQuestCopy.ReadyForReviewObjective);
            ShowHudMessage(optionalMessage ?? StressQuestCopy.ReadyForReviewHud);

            if (TreatmentTopics.GetReadyForReviewTopic(treatment.BuffId) is { } reviewTopic)
                ConversationHelper.AddTopic(reviewTopic, 2);

            _stressLoadService.Recalculate();

            _monitor.Log(
                $"[MarkTreatmentEpisodeReadyForReview] Fallback review для {episodeId}, quest={definition.QuestId}",
                LogLevel.Warn);

            return true;
        }

        public bool CompleteTreatmentEpisode(string episodeId, string message = "Лечение завершено.")
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
            {
                if (TryCompleteEpisodeViaQuestTreatment(episodeId, message))
                    return true;

                _monitor.Log(
                    $"[CompleteTreatmentEpisode] Episode {episodeId} не активен (current={episode?.EpisodeId ?? "(none)"})",
                    LogLevel.Warn);
                return false;
            }

            if (!episode.AwaitingHarveyReview)
            {
                _monitor.Log(
                    $"[CompleteTreatmentEpisode] Episode {episodeId} не ждёт review — пропуск",
                    LogLevel.Warn);
                return false;
            }

            return FinalizeCompletedEpisode(episode, episodeId, message);
        }

        private bool TryCompleteEpisodeViaQuestTreatment(string episodeId, string message)
        {
            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
                return false;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(definition.QuestId);
            if (treatment == null || !treatment.AwaitingHarveyReview || treatment.IsCompleted)
                return false;

            var buffId = treatment.BuffId;
            var activeCauseIds = _stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .Select(kvp => kvp.Key)
                .ToList();

            var episodeStub = new TreatmentEpisodeState
            {
                EpisodeId = episodeId,
                QuestId = definition.QuestId,
                RelatedCauseIds = definition.RelatedCauses.ToList(),
                ObjectivesCompleted = true,
                PrimaryCauseId = TreatmentEpisodeDefinitions.ResolvePrimaryCauseId(episodeId, activeCauseIds),
            };

            var reduction = EpisodeCauseResolver.ApplyResolvedCauses(
                _data,
                _stressLoadService,
                _buffService,
                _stateService,
                episodeStub);

            _stressLoadService.ReduceStressByTreatment(Math.Max(reduction, 10), episodeId);

            _stateService.CompleteTreatment(definition.QuestId);
            if (_data.StressState.HasActiveQuest(definition.QuestId))
                return false;

            SetEpisodeImmunity(episodeId, DefaultEpisodeImmunityDays);
            _treatmentService?.ApplyCompletionRewards(buffId, message, removeReviewTopic: true);

            _data.ActiveTreatmentEpisode = null;
            _stressLoadService.Recalculate();

            _trustService?.OnTimelyReviewCompleted();
            _trustService?.OnTreatmentEpisodeCompleted(episodeId);

            _monitor.Log(
                $"[CompleteTreatmentEpisode] Fallback завершение episode={episodeId} через quest={definition.QuestId}",
                LogLevel.Info);

            return true;
        }

        private bool FinalizeCompletedEpisode(TreatmentEpisodeState episode, string episodeId, string message)
        {
            var primaryBuffId = ResolvePrimaryBuffId(episode);

            CompleteLegacyTreatmentRecord(episode, primaryBuffId);

            if (_questService.HasQuest(episode.QuestId))
                _questService.CompleteQuest(episode.QuestId);

            var reduction = EpisodeCauseResolver.ApplyResolvedCauses(
                _data,
                _stressLoadService,
                _buffService,
                _stateService,
                episode);

            _stressLoadService.ReduceStressByTreatment(Math.Max(reduction, 10), episodeId);

            episode.IsCompleted = true;
            episode.IsCured = true;
            episode.AwaitingHarveyReview = false;
            episode.ObjectivesCompleted = false;

            SetEpisodeImmunity(episodeId, DefaultEpisodeImmunityDays);

            if (!string.IsNullOrEmpty(primaryBuffId))
                _treatmentService?.ApplyCompletionRewards(primaryBuffId, message, removeReviewTopic: true);

            _data.ActiveTreatmentEpisode = null;
            _stressLoadService.Recalculate();

            _trustService?.OnTimelyReviewCompleted();
            _trustService?.OnTreatmentEpisodeCompleted(episodeId);

            _monitor.Log(
                $"[CompleteTreatmentEpisode] Episode {episodeId} завершён, load -{reduction}, unrelated causes сохранены",
                LogLevel.Info);

            return true;
        }

        private void RepairEpisodeStateForReview(
            string episodeId,
            TreatmentEpisodeDefinition definition,
            TreatmentState treatment)
        {
            var activeCauseIds = _stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .Select(kvp => kvp.Key)
                .ToList();

            if (_data.ActiveTreatmentEpisode == null
                || _data.ActiveTreatmentEpisode.IsCompleted
                || _data.ActiveTreatmentEpisode.IsCured)
            {
                _data.ActiveTreatmentEpisode = new TreatmentEpisodeState
                {
                    EpisodeId = episodeId,
                    QuestId = definition.QuestId,
                    TreatmentStarted = true,
                    ObjectivesCompleted = true,
                    AwaitingHarveyReview = true,
                    ReadyForReviewTime = Game1.timeOfDay,
                    RelatedCauseIds = definition.RelatedCauses
                        .Where(activeCauseIds.Contains)
                        .ToList(),
                    PrimaryCauseId = TreatmentEpisodeDefinitions.ResolvePrimaryCauseId(episodeId, activeCauseIds)
                        ?? TreatmentEpisodeDefinitions.ResolvePrimaryCauseId(episodeId, definition.RelatedCauses),
                };
                return;
            }

            if (!string.Equals(_data.ActiveTreatmentEpisode.EpisodeId, episodeId, StringComparison.Ordinal))
                return;

            _data.ActiveTreatmentEpisode.ObjectivesCompleted = true;
            _data.ActiveTreatmentEpisode.AwaitingHarveyReview = true;
            _data.ActiveTreatmentEpisode.ReadyForReviewTime = Game1.timeOfDay;
        }

        private void AddReadyForReviewTopic(TreatmentEpisodeState episode)
        {
            var primaryBuffId = ResolvePrimaryBuffId(episode);
            if (string.IsNullOrEmpty(primaryBuffId))
                return;

            if (TreatmentTopics.GetReadyForReviewTopic(primaryBuffId) is { } reviewTopic)
                ConversationHelper.AddTopic(reviewTopic, 2);
        }

        public EpisodeEvaluationContext BuildContext()
        {
            var activeCauseIds = _stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .Select(kvp => kvp.Key)
                .ToList();

            return new EpisodeEvaluationContext
            {
                StressLoad = _stressLoadService.GetCurrentStressLoad(),
                Severity = _stressLoadService.GetSeverity(),
                ActiveCauseIds = activeCauseIds,
                GotoroFlashbackActive = _data.StressLoad.GotoroFlashbackActive,
                WarTraumaFlag = _data.StressLoad.WarTraumaFlag,
                IsLightning = Game1.isLightning,
                HasActiveTreatment = HasActiveTreatmentEpisode(),
                AwaitingHarveyReview = GetTreatmentAwaitingReview() != null,
            };
        }

        public EpisodeSelectionResult EvaluateSelection()
        {
            var ctx = BuildContext();

            if (ctx.AwaitingHarveyReview)
            {
                var reviewEpisode = GetTreatmentAwaitingReview();
                var reason = ctx.GotoroFlashbackActive && ctx.IsLightning
                    ? "Awaiting Harvey review — storm/Gotoro overlay does not start new episode"
                    : "Active treatment awaiting Harvey review";

                return new EpisodeSelectionResult
                {
                    Action = EpisodeSelectionAction.AwaitingReview,
                    EpisodeId = reviewEpisode?.EpisodeId,
                    PrimaryBuffId = reviewEpisode != null ? ResolvePrimaryBuffId(reviewEpisode) : null,
                    Reason = reason,
                };
            }

            if (ctx.HasActiveTreatment)
            {
                var activeEpisode = GetActiveTreatmentEpisode();
                var reason = ctx.GotoroFlashbackActive
                    ? $"Active treatment ({activeEpisode?.EpisodeId ?? _stressLoadService.GetActiveTreatmentEpisodeId()}) — Gotoro deferred (overlay only)"
                    : "Active treatment in progress — reminder only";

                return new EpisodeSelectionResult
                {
                    Action = EpisodeSelectionAction.ReminderOnly,
                    EpisodeId = activeEpisode?.EpisodeId ?? _stressLoadService.GetActiveTreatmentEpisodeId(),
                    PrimaryBuffId = activeEpisode != null ? ResolvePrimaryBuffId(activeEpisode) : null,
                    Reason = reason,
                };
            }

            var matching = TreatmentEpisodeDefinitions.GetMatchingEpisodes(ctx);
            var matchingIds = matching
                .Where(def => !IsEpisodeOnImmunity(def.EpisodeId))
                .Select(m => m.EpisodeId)
                .ToList();

            if (matchingIds.Count == 0)
            {
                if (ShouldUseAmbientOnly(ctx))
                {
                    return new EpisodeSelectionResult
                    {
                        Action = EpisodeSelectionAction.AmbientOnly,
                        Reason = "StressLoad below episode thresholds; only light self-resolve causes",
                    };
                }

                return new EpisodeSelectionResult
                {
                    Action = EpisodeSelectionAction.None,
                    Reason = "No episode triggers matched",
                };
            }

            var selected = TreatmentEpisodeDefinitions.SelectBestEpisode(ctx);
            if (selected == null || IsEpisodeOnImmunity(selected.EpisodeId))
            {
                return new EpisodeSelectionResult
                {
                    Action = EpisodeSelectionAction.None,
                    MatchingEpisodeIds = matchingIds,
                    Reason = "Matching episodes found but none selected or on immunity",
                };
            }

            if (!selected.RequiresHarveyTreatment)
            {
                return new EpisodeSelectionResult
                {
                    Action = EpisodeSelectionAction.AmbientOnly,
                    EpisodeId = selected.EpisodeId,
                    MatchingEpisodeIds = matchingIds,
                    Reason = "Episode does not require Harvey treatment quest",
                };
            }

            if (!HasUntreatedEpisodeStress(selected, ctx))
            {
                return new EpisodeSelectionResult
                {
                    Action = EpisodeSelectionAction.None,
                    EpisodeId = selected.EpisodeId,
                    MatchingEpisodeIds = matchingIds,
                    Reason = "Episode matched but related stress already under treatment",
                };
            }

            var primaryBuff = TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(
                selected.EpisodeId,
                ctx.ActiveCauseIds);

            return new EpisodeSelectionResult
            {
                Action = EpisodeSelectionAction.StartEpisode,
                EpisodeId = selected.EpisodeId,
                PrimaryBuffId = primaryBuff,
                DisplayName = selected.DisplayName,
                MatchingEpisodeIds = matchingIds,
                Reason = matchingIds.Count > 1
                    ? $"Selected {selected.EpisodeId} from {matchingIds.Count} candidates"
                    : $"Selected {selected.EpisodeId}",
            };
        }

        public string? GetCandidateEpisodeId()
        {
            var ctx = BuildContext();
            var best = TreatmentEpisodeDefinitions.SelectBestEpisode(ctx);
            if (best == null || IsEpisodeOnImmunity(best.EpisodeId))
                return null;

            return best.EpisodeId;
        }

        public string BuildDebugSnapshot()
        {
            var ctx = BuildContext();
            var selection = EvaluateSelection();
            var sb = new StringBuilder();

            sb.AppendLine("=== Treatment Episodes ===");
            sb.AppendLine($"Selection action: {selection.Action}");
            sb.AppendLine($"Selected episode: {selection.EpisodeId ?? "(none)"}");
            sb.AppendLine($"Primary buff (compat): {selection.PrimaryBuffId ?? "(none)"}");
            sb.AppendLine($"Reason: {selection.Reason ?? "(none)"}");

            if (selection.MatchingEpisodeIds.Count > 0)
                sb.AppendLine($"Matching: {string.Join(", ", selection.MatchingEpisodeIds)}");

            var active = _data.ActiveTreatmentEpisode;
            if (active != null)
            {
                sb.AppendLine("ActiveTreatmentEpisode:");
                sb.AppendLine($"  EpisodeId={active.EpisodeId}, QuestId={active.QuestId}");
                sb.AppendLine($"  Started={active.TreatmentStarted}, ObjectivesCompleted={active.ObjectivesCompleted}");
                sb.AppendLine($"  AwaitingReview={active.AwaitingHarveyReview}, Completed={active.IsCompleted}");
                sb.AppendLine($"  Related=[{string.Join(", ", active.RelatedCauseIds)}]");
                sb.AppendLine($"  PrimaryCause={active.PrimaryCauseId ?? "(none)"}");
            }

            sb.AppendLine("Definitions:");
            foreach (var def in TreatmentEpisodeDefinitions.All)
            {
                var matched = TreatmentEpisodeDefinitions.MatchesTrigger(def, ctx);
                sb.AppendLine(
                    $"  {def.EpisodeId}: matched={matched}, immunity={IsEpisodeOnImmunity(def.EpisodeId)}, " +
                    $"priority={def.Priority}, minLoad={def.MinStressLoad}, quest={def.QuestId}");
            }

            return sb.ToString();
        }

        private bool ShouldUseAmbientOnly(EpisodeEvaluationContext ctx)
        {
            if (ctx.ActiveCauseIds.Count == 0)
                return true;

            if (ctx.StressLoad >= 25)
                return false;

            // Активный дебафф без лечения требует полного диалога/квеста, а не ambient-подсказки.
            if (StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data).Count > 0)
                return false;

            if (DarknessLegacyHelper.NeedsHarveyDarknessTherapy(_data, _stateService))
                return false;

            return ctx.ActiveCauseIds.All(StressCauses.CanSelfResolve);
        }

        private bool HasUntreatedEpisodeStress(TreatmentEpisodeDefinition episode, EpisodeEvaluationContext ctx)
        {
            foreach (var causeId in episode.RelatedCauses)
            {
                if (!ctx.HasCause(causeId))
                    continue;

                if (causeId == StressCauses.Darkness
                    && DarknessLegacyHelper.NeedsHarveyDarknessTherapy(_data, _stateService))
                {
                    return true;
                }

                if (!StressCauses.CauseToBuff.TryGetValue(causeId, out var buffId))
                    continue;

                if (StressDebuffSelector.IsUntreatedDebuff(_stateService, buffId, _data))
                    return true;
            }

            return ctx.HasCause(StressCauses.GotoroFlashback)
                   && StressDebuffSelector.IsUntreatedDebuff(_stateService, BuffIds.Panic);
        }

        private bool IsEpisodeOnImmunity(string episodeId)
        {
            if (!_data.EpisodeImmunityUntil.TryGetValue(episodeId, out var until))
                return false;

            return SDate.Now() <= until;
        }

        private void SetEpisodeImmunity(string episodeId, int days)
        {
            if (days <= 0)
                return;

            _data.EpisodeImmunityUntil[episodeId] = SDate.Now().AddDays(days);
        }

        private static string? ResolvePrimaryBuffId(TreatmentEpisodeState episode)
        {
            if (!string.IsNullOrEmpty(episode.PrimaryCauseId)
                && StressCauses.CauseToBuff.TryGetValue(episode.PrimaryCauseId, out var buffId))
            {
                return buffId;
            }

            return TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(
                episode.EpisodeId,
                episode.RelatedCauseIds);
        }

        private void SyncLegacyTreatmentReviewFlags(TreatmentEpisodeState episode)
        {
            var byQuest = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (byQuest != null)
            {
                byQuest.ObjectivesCompleted = true;
                byQuest.AwaitingHarveyReview = true;
                byQuest.ReadyForReviewDate = SDate.Now();
            }

            var primaryBuffId = ResolvePrimaryBuffId(episode);
            if (string.IsNullOrEmpty(primaryBuffId))
                return;

            var treatment = _data.StressState.GetActiveTreatment(primaryBuffId);
            if (treatment == null || treatment == byQuest)
                return;

            treatment.ObjectivesCompleted = true;
            treatment.AwaitingHarveyReview = true;
            treatment.ReadyForReviewDate = SDate.Now();
        }

        private void CompleteLegacyTreatmentRecord(TreatmentEpisodeState episode, string? primaryBuffId)
        {
            if (string.IsNullOrEmpty(primaryBuffId))
                return;

            var treatment = _data.StressState.GetActiveTreatment(primaryBuffId);
            if (treatment == null)
                return;

            treatment.ObjectivesCompleted = false;
            treatment.AwaitingHarveyReview = false;
            treatment.ReadyForReviewDate = null;
            treatment.IsCompleted = true;
            treatment.IsCured = true;
            treatment.CompletedDate = SDate.Now();
            _data.StressState.AddTreatmentToHistory(primaryBuffId, treatment);

            _data.StressState.TreatmentFlags.SetTreatmentActive(primaryBuffId, false);
            _data.StressState.TreatmentFlags.SetQuestAddedToJournal(episode.QuestId, false);
            _data.StressState.RemoveTreatment(treatment.TreatmentKey);
        }

        private static void ShowHudMessage(string text)
            => Game1.addHUDMessage(new HUDMessage(text, HUDMessage.newQuest_type));
    }
}
