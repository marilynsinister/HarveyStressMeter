using HarveyOverhaul.Core.Api;
using HarveyOverhaul.Core.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;

namespace HarveyStressMeter.Services;

/// <summary>Синхронизирует stress-назначения с единым RecoveryPlan (Injury save-state).</summary>
public sealed class RecoveryPlanBridge
{
    private readonly IMonitor _monitor;
    private IHarveyCoreApi? _coreApi;

    public RecoveryPlanBridge(IMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>RecoveryPlan берётся через Core: Injury регистрирует его там, прямой зависимости Stress → Injury нет.</summary>
    public void Bind(IHarveyCoreApi coreApi)
    {
        _coreApi = coreApi;
        if (_coreApi.GetRecoveryPlanApi() == null)
            _monitor.Log("[RecoveryPlanBridge] RecoveryPlan API not registered in Core yet (Injury not loaded?).", LogLevel.Trace);
    }

    // Резолвим при каждом вызове: Injury может зарегистрироваться в Core позже Stress.
    private IHarveyRecoveryPlanApi? PlanApi => _coreApi?.GetRecoveryPlanApi();

    public bool IsAvailable => PlanApi != null;

    public void EnsureAssignment(string episodeId, int goal = 0)
    {
        var api = PlanApi;
        if (api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        api.AddAssignment(assignmentId, goal);
    }

    public void SyncProgress(string episodeId, int current, int goal)
    {
        var api = PlanApi;
        if (api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        api.SetProgress(assignmentId, current, goal);
    }

    public void CompleteEpisodeAssignment(string episodeId)
    {
        var api = PlanApi;
        if (api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        api.CompleteAssignment(assignmentId);
    }

    public void StartEpisodePlan(string episodeId)
    {
        var api = PlanApi;
        if (api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        int goal = episodeId switch
        {
            StressEpisodes.AnxietySpike => EpisodeQuestRules.AnxietySafeSecondsRequired,
            StressEpisodes.SocialShutdown => SocialShutdownQuestHelper.HarveySecondsRequired,
            _ => 0,
        };

        api.StartPlan("stress", [assignmentId], planId: $"Stress_{episodeId}");
        if (goal > 0)
            api.SetProgress(assignmentId, 0, goal);
    }

    private static string? MapEpisodeToAssignment(string episodeId) => episodeId switch
    {
        StressEpisodes.AnxietySpike => HarveyRecoveryPlanAssignmentIds.FindSafePlace,
        StressEpisodes.SocialShutdown => HarveyRecoveryPlanAssignmentIds.DontStayAlone,
        _ => null,
    };
}
