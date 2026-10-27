using HarveyOverhaul.Core.Api;
using HarveyOverhaul.Core.Models;
using HarveyOverhaul.Core.Services;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace HarveyStressMeter.Services;

/// <summary>Регистрирует stress-интенты в Core без прямого обращения к Injury.</summary>
public sealed class StressMedicalIntentProvider
{
    private const int SyncIntervalTicks = 60;

    private readonly IMonitor _monitor;
    private readonly StateService _stateService;
    private readonly SaveData _data;
    private readonly StressTreatmentReviewService _reviewService;
    private readonly TreatmentEpisodeService _episodeService;
    private IHarveyCoreApi? _coreApi;
    private DarknessService? _darknessService;
    private int _ticksSinceSync;

    public void SetDarknessService(DarknessService darknessService) => _darknessService = darknessService;

    public StressMedicalIntentProvider(
        IMonitor monitor,
        StateService stateService,
        SaveData data,
        StressTreatmentReviewService reviewService,
        TreatmentEpisodeService episodeService)
    {
        _monitor = monitor;
        _stateService = stateService;
        _data = data;
        _reviewService = reviewService;
        _episodeService = episodeService;
    }

    public void SetCoreApi(IHarveyCoreApi? coreApi)
    {
        if (coreApi == null)
            return;

        RegisterWithCore(coreApi);
    }

    public void RegisterWithCore(IHarveyCoreApi coreApi)
    {
        _coreApi = coreApi;
        coreApi.RegisterMedicalIntentPublisher(
            HarveyProviderRegistry.StressProviderId,
            PublishMedicalIntents);
    }

    public void PublishMedicalIntents()
    {
        if (_coreApi == null)
            return;

        var intents = CollectIntents();
        _coreApi.RegisterMedicalIntents(HarveyProviderRegistry.StressProviderId, intents);
        _coreApi.SetHarveyClickStressSummary(BuildStressDebugSummary());
    }

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (++_ticksSinceSync < SyncIntervalTicks)
            return;

        _ticksSinceSync = 0;
        PublishMedicalIntents();
    }

    public void SyncIntents(bool logDetails = false)
    {
        PublishMedicalIntents();

        if (logDetails && _coreApi != null)
            _coreApi.ResolveHarveyMedicalIntent(logDetails: true);
    }

    /// <summary>
    /// Стресс-состояние, которое Core выбрал для следующего клика по Харви (null — выбран не стресс или ничего).
    /// Диалог старта должен предлагать именно его: свой порядок PriorityOrder с весами Core расходится
    /// (Гроза — тяжёлый стресс, 500, а Недосып — 300), и тогда оба лечения блокировали друг друга.
    /// </summary>
    public string? GetCoreSelectedStressStateId()
    {
        if (_coreApi == null)
            return null;

        var selected = (_coreApi.GetLastHarveyMedicalIntentResolution()
            ?? _coreApi.ResolveHarveyMedicalIntent()).Selected;

        return selected != null
            && string.Equals(selected.ProviderId, HarveyProviderRegistry.StressProviderId, StringComparison.Ordinal)
            ? selected.StateId
            : null;
    }

    /// <summary>Core выбрал для клика интент другого провайдера (травма) — стресс должен уступить.</summary>
    public bool IsNonStressIntentSelected()
    {
        if (_coreApi == null)
            return false;

        var selected = (_coreApi.GetLastHarveyMedicalIntentResolution()
            ?? _coreApi.ResolveHarveyMedicalIntent()).Selected;

        return selected != null
            && !string.Equals(selected.ProviderId, HarveyProviderRegistry.StressProviderId, StringComparison.Ordinal);
    }

    public bool CanShowStressIntent(string buffId, HarveyMedicalIntentKind kind = HarveyMedicalIntentKind.Stress)
    {
        if (_coreApi == null)
            return true;

        var resolution = _coreApi.GetLastHarveyMedicalIntentResolution()
            ?? _coreApi.ResolveHarveyMedicalIntent();

        var selected = resolution.Selected;
        if (selected == null)
            return true;

        if (!string.Equals(selected.ProviderId, HarveyProviderRegistry.StressProviderId, StringComparison.Ordinal))
        {
            _monitor.Log(
                $"[HarveyStress] intent deferred buff={buffId}: Core selected " +
                $"{selected.ProviderId}/{selected.StateId} (priority {selected.BasePriority})",
                LogLevel.Debug);
            return false;
        }

        if (!string.Equals(selected.StateId, buffId, StringComparison.OrdinalIgnoreCase))
        {
            _monitor.Log(
                $"[HarveyStress] intent deferred buff={buffId}: Core selected stress state {selected.StateId}",
                LogLevel.Debug);
            return false;
        }

        return selected.Kind == kind;
    }

    private string BuildStressDebugSummary()
    {
        var parts = new List<string>();

        foreach (string buffId in StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data))
            parts.Add($"{buffId}(untreated)");

        foreach (var treatment in StressDebuffSelector.GetTreatmentsAwaitingReview(_stateService))
            parts.Add($"{treatment.BuffId}(awaitingReview)");

        return parts.Count == 0 ? "(none)" : string.Join("; ", parts);
    }

    private List<HarveyMedicalIntentRegistration> CollectIntents()
    {
        var intents = new List<HarveyMedicalIntentRegistration>();
        int today = (int)Game1.stats.DaysPlayed;

        foreach (var treatment in StressDebuffSelector.GetTreatmentsAwaitingReview(_stateService))
        {
            intents.Add(BuildReviewIntent(treatment.BuffId, today));
        }

        foreach (string buffId in StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data))
        {
            intents.Add(BuildStartIntent(buffId, today));
        }

        // Терапия темноты: все вечера шага 1 засчитаны — это такой же «приём у Харви», как review.
        if (_darknessService?.IsStep1ReadyForHarvey == true || _darknessService?.NeedsLanternFromHarvey == true)
            intents.Add(BuildReviewIntent(BuffIds.Darkness, today));

        var episodeSelection = _episodeService.EvaluateSelection();

        // Харви начнёт с эпизода (диалог старта выбирает его первым) — Core должен выбрать то же состояние.
        // Иначе бафф эпизода (например, паника у Гото́ро) не был в списке, Core выбирал «Социальную тревогу»,
        // диалог эпизода отклонялся — и Харви не предлагал ничего.
        if (episodeSelection.Action == EpisodeSelectionAction.StartEpisode
            && !string.IsNullOrEmpty(episodeSelection.PrimaryBuffId))
        {
            intents.Add(BuildStartIntent(episodeSelection.PrimaryBuffId, today, episodeStart: true));
        }

        if (episodeSelection.Action == EpisodeSelectionAction.AwaitingReview
            && !string.IsNullOrEmpty(episodeSelection.PrimaryBuffId))
        {
            intents.Add(BuildReviewIntent(episodeSelection.PrimaryBuffId, today));
        }

        return intents
            .GroupBy(i => i.StateId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.BasePriority).First())
            .ToList();
    }

    private static HarveyMedicalIntentRegistration BuildStartIntent(string buffId, int today, bool episodeStart = false)
    {
        // Эпизод объединяет несколько причин сразу — он важнее отдельного стресса.
        bool severe = episodeStart || IsSevereStress(buffId);
        return new HarveyMedicalIntentRegistration
        {
            ProviderId = HarveyProviderRegistry.StressProviderId,
            Kind = HarveyMedicalIntentKind.Stress,
            StateId = buffId,
            BasePriority = episodeStart
                ? HarveyMedicalIntentPriorities.SevereStress + 10
                : severe
                    ? HarveyMedicalIntentPriorities.SevereStress
                    : HarveyMedicalIntentPriorities.MinorStress,
            DangerRank = GetStressDangerRank(buffId),
            StateAgeTicks = today,
            ActionKey = HarveyStressActions.StartTreatment,
            TopicKey = GetStressTopicKey(buffId),
            AllowDuringFestival = severe,
            AllowOutsideClinic = true,
            RequiresClinic = false,
            IsEmergency = severe,
            FallbackLine = "Ты выглядишь измотанной. Давай разберёмся с этим.$u",
        };
    }

    private static HarveyMedicalIntentRegistration BuildReviewIntent(string buffId, int today)
    {
        return new HarveyMedicalIntentRegistration
        {
            ProviderId = HarveyProviderRegistry.StressProviderId,
            Kind = HarveyMedicalIntentKind.Stress,
            StateId = buffId,
            // Выше старта любого стресса (500, эпизод 510): назначение выполнено, план уже зовёт к Харви.
            // При 450 старт грозы/темноты перебивал итог, и оба диалога блокировали друг друга.
            BasePriority = HarveyMedicalIntentPriorities.SevereStress + 20,
            DangerRank = GetStressDangerRank(buffId),
            StateAgeTicks = today,
            IsPhaseReady = true,
            ActionKey = HarveyStressActions.CompleteReview,
            TopicKey = TreatmentTopics.GetReadyForReviewTopic(buffId) ?? $"topicStressTreatment{buffId.Replace("buffStress", "")}ReadyForReview",
            AllowDuringFestival = false,
            AllowOutsideClinic = true,
            RequiresClinic = false,
            IsEmergency = false,
            FallbackLine = "Ты справилась с заданием — поговорим.$h",
        };
    }

    private static bool IsSevereStress(string buffId) =>
        buffId is BuffIds.Social or BuffIds.Darkness or BuffIds.Thunder;

    private static int GetStressDangerRank(string buffId) =>
        buffId switch
        {
            BuffIds.Social => 80,
            BuffIds.Darkness => 75,
            BuffIds.Thunder => 70,
            BuffIds.Overwork => 50,
            BuffIds.NoSleep => 45,
            BuffIds.Hunger => 30,
            BuffIds.Tired => 25,
            BuffIds.Lonely => 20,
            BuffIds.TooCold => 15,
            _ => 10,
        };

    private static string GetStressTopicKey(string buffId)
    {
        if (StressDebuffSelector.BuffToStressTopic.TryGetValue(buffId, out var pair))
            return pair.topic;

        return $"topicStress{buffId.Replace("buffStress", "")}";
    }
}
