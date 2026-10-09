using HarveyOverhaul.Core.Api;
using HarveyOverhaul.Core.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Api;

/// <summary>Отдаёт состояния стресса другим модам через Core (Injury не знает buff/topic ID стресса).</summary>
public sealed class StressStateApi : IHarveyStressStateApi
{
    private readonly SaveData _data;
    private readonly StateService _stateService;
    private readonly TreatmentService _treatmentService;
    private readonly StressLoadService _stressLoadService;
    private readonly IMonitor _monitor;

    public StressStateApi(
        SaveData data,
        StateService stateService,
        TreatmentService treatmentService,
        StressLoadService stressLoadService,
        IMonitor monitor)
    {
        _data = data;
        _stateService = stateService;
        _treatmentService = treatmentService;
        _stressLoadService = stressLoadService;
        _monitor = monitor;
    }

    public bool HasCondition(string conditionId)
    {
        if (!Context.IsWorldReady)
            return false;

        return conditionId switch
        {
            HarveyStressConditions.Thunder => HasBuffOrTreatment(BuffIds.Thunder)
                || HasAnyCause(StressCauses.Thunder, StressCauses.ThunderRelapse, StressCauses.ThunderSensitivity),
            HarveyStressConditions.Darkness => HasBuffOrTreatment(BuffIds.Darkness)
                || _data.Darkness.IsTherapyActive
                || _data.Darkness.DarknessRelapseTreatmentActive
                || _data.Darkness.FearLevel > 0,
            HarveyStressConditions.SocialAnxiety => HasBuffOrTreatment(BuffIds.Social)
                || IsEpisodeActive(StressEpisodes.SocialShutdown),
            _ => false,
        };
    }

    public bool RequestReaction(string conditionId, string sourceProviderId)
    {
        if (!Context.IsWorldReady)
            return false;

        switch (conditionId)
        {
            case HarveyStressConditions.Thunder:
                if (!_stateService.HasActiveTreatmentState(BuffIds.Thunder)
                    && !_stateService.HasImmunity(BuffIds.Thunder))
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
                }

                bool active = _stateService.HasBuffInGame(BuffIds.Thunder);
                _monitor.Log(
                    $"[StressStateApi] RequestReaction {conditionId} from {sourceProviderId}: active={active} " +
                    $"(immunity={_stateService.HasImmunity(BuffIds.Thunder)})",
                    LogLevel.Debug);
                return active;

            default:
                _monitor.Log(
                    $"[StressStateApi] RequestReaction {conditionId} from {sourceProviderId}: not supported.",
                    LogLevel.Debug);
                return false;
        }
    }

    private bool HasBuffOrTreatment(string buffId)
        => _stateService.HasBuffInGame(buffId) || _stateService.HasActiveTreatmentState(buffId);

    private bool HasAnyCause(params string[] causeIds)
    {
        var causes = _stressLoadService.GetActiveCauses();
        return causeIds.Any(causes.ContainsKey);
    }

    private bool IsEpisodeActive(string episodeId)
    {
        var episode = _data.ActiveTreatmentEpisode;
        return episode != null
            && episode.IsActiveEpisode()
            && string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal);
    }
}
