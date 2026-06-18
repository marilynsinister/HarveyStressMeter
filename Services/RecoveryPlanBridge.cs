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
    private IHarveyRecoveryPlanApi? _api;

    public RecoveryPlanBridge(IMonitor monitor)
    {
        _monitor = monitor;
    }

    public void Bind(IModHelper helper)
    {
        _api = helper.ModRegistry.GetApi<IHarveyRecoveryPlanApi>("marilynsinister.HarveyOverhaul.Injury");
        if (_api == null)
        {
            _monitor.Log("[RecoveryPlanBridge] Injury RecoveryPlan API not found.", LogLevel.Trace);
        }
    }

    public bool IsAvailable => _api != null;

    public void EnsureAssignment(string episodeId, int goal = 0)
    {
        if (_api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        _api.AddAssignment(assignmentId, goal);
    }

    public void SyncProgress(string episodeId, int current, int goal)
    {
        if (_api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        _api.SetProgress(assignmentId, current, goal);
    }

    public void CompleteEpisodeAssignment(string episodeId)
    {
        if (_api == null)
            return;

        string? assignmentId = MapEpisodeToAssignment(episodeId);
        if (assignmentId == null)
            return;

        _api.CompleteAssignment(assignmentId);
    }

    public void StartEpisodePlan(string episodeId)
    {
        if (_api == null)
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

        _api.StartPlan("stress", [assignmentId], planId: $"Stress_{episodeId}");
        if (goal > 0)
            _api.SetProgress(assignmentId, 0, goal);
    }

    private static string? MapEpisodeToAssignment(string episodeId) => episodeId switch
    {
        StressEpisodes.AnxietySpike => HarveyRecoveryPlanAssignmentIds.FindSafePlace,
        StressEpisodes.SocialShutdown => HarveyRecoveryPlanAssignmentIds.DontStayAlone,
        _ => null,
    };
}
