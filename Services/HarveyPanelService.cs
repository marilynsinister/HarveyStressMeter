using System;
using System.Linq;
using HarveyStressMeter.Api;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.UI;
using StardewModdingAPI;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Собирает read-only view model окна «План Харви» из существующих сервисов и save data.
    /// </summary>
    public sealed class HarveyPanelService
    {
        private const string InjuryModId = "marilynsinister.HarveyOverhaul.Injury";

        private readonly SaveData _data;
        private readonly HandbookManager _handbookManager;
        private readonly StressLoadService _stressLoadService;
        private readonly HarveyCareTrustService _trustService;
        private readonly IModHelper _helper;

        public HarveyPanelService(
            SaveData data,
            HandbookManager handbookManager,
            StressLoadService stressLoadService,
            HarveyCareTrustService trustService,
            IModHelper helper)
        {
            _data = data;
            _handbookManager = handbookManager;
            _stressLoadService = stressLoadService;
            _trustService = trustService;
            _helper = helper;
        }

        public HarveyPanelViewModel BuildViewModel(HarveyPanelTab selectedTab = HarveyPanelTab.Overview)
        {
            var handbook = _handbookManager.BuildViewModel(_data);
            EnrichHandbookTreatmentStages(handbook);
            EnrichHandbookPanelCopy(handbook);

            var episode = _data.ActiveTreatmentEpisode;
            var treatment = episode != null && episode.IsActiveEpisode()
                ? _data.StressState.GetActiveTreatmentByQuest(episode.QuestId)
                : null;
            var assignment = HarveyPanelAssignmentFormatter.Build(
                episode,
                treatment?.Progress,
                _data.OverworkBreaksToday);

            var injuryApi = TryGetInjuryApi();
            var injuryPanel = injuryApi?.GetPanelState();
            var recoveryPlan = injuryApi?.GetRecoveryPlanState();
            var planContent = BuildPlanTabContent(assignment, recoveryPlan);
            var overview = BuildOverviewContent(assignment, injuryPanel, handbook);

            var vm = new HarveyPanelViewModel
            {
                OverviewStateLine = overview.Headline,
                OverviewAssignmentLine = overview.BodyLine,
                OverviewProgressLine = overview.ProgressLine,
                OverviewAfterLine = overview.AfterLine,
                OverviewStressLine = overview.StressLine,
                OverviewInjuriesLine = overview.InjuryLine,
                OverviewAdviceLine = overview.AdviceLine,
                StressAssignmentTitle = assignment.HasAssignment ? assignment.StressTitle : "",
                StressAssignmentProgress = BuildStressProgressBlock(assignment),
                StressAssignmentObjective = assignment.ObjectiveText,
                StressAssignmentAfter = assignment.AfterHint,
                StressNoAssignmentLine = assignment.HasAssignment
                    ? ""
                    : HarveyPanelTexts.Stress.NoAssignment,
                Handbook = handbook,
                InjuriesBody = HarveyPanelInjuryFormatter.FormatInjuriesTab(
                    injuryPanel,
                    _helper.ModRegistry.IsLoaded(InjuryModId),
                    injuryPanel != null),
                PlanTitle = planContent.Title,
                PlanBody = planContent.Body,
                TrustLevelLine = BuildTrustLevelLine(),
                TrustDescriptionLine = BuildTrustDescriptionLine(),
                TrustPermissionsLine = BuildTrustPermissionsLine(),
                TrustPlaceholder = BuildTrustPlaceholder(),
            };

            InitializeTabs(vm, selectedTab);
            return vm;
        }

        private void InitializeTabs(HarveyPanelViewModel vm, HarveyPanelTab selectedTab)
        {
            var selectedKey = selectedTab.ToString();

            foreach (var (tab, label) in new[]
            {
                (HarveyPanelTab.Overview, HarveyPanelTexts.Tabs.Overview),
                (HarveyPanelTab.Stress, HarveyPanelTexts.Tabs.Stress),
                (HarveyPanelTab.Injuries, HarveyPanelTexts.Tabs.Injuries),
                (HarveyPanelTab.Plan, HarveyPanelTexts.Tabs.Plan),
                (HarveyPanelTab.Trust, HarveyPanelTexts.Tabs.Trust),
            })
            {
                var key = tab.ToString();
                vm.Tabs.Add(new HarveyPanelTabButtonViewModel
                {
                    Key = key,
                    Label = label,
                    Active = string.Equals(key, selectedKey, StringComparison.Ordinal),
                });
            }

            vm.SelectTab(selectedKey);
        }

        private sealed class OverviewContent
        {
            public string Headline { get; init; } = "";
            public string BodyLine { get; init; } = "";
            public string ProgressLine { get; init; } = "";
            public string AfterLine { get; init; } = "";
            public string StressLine { get; init; } = "";
            public string InjuryLine { get; init; } = "";
            public string AdviceLine { get; init; } = "";
        }

        private OverviewContent BuildOverviewContent(
            HarveyPanelAssignmentFormatter.Display assignment,
            InjuryPanelStateDto? injuryPanel,
            HandbookViewModel handbook)
        {
            bool hasInjury = injuryPanel?.HasAnyInjury == true;
            bool awaitingReview = assignment.AwaitingHarveyReview
                || _data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true;

            if (awaitingReview)
            {
                return new OverviewContent
                {
                    Headline = HarveyPanelTexts.Overview.HarveyWaitingHeadline,
                    BodyLine = assignment.HasAssignment ? assignment.ShortTitle : HarveyPanelTexts.TalkToHarveySoon(),
                    AfterLine = HarveyPanelTexts.TalkToHarvey(),
                    AdviceLine = HarveyPanelTexts.Overview.ReviewAdvice,
                };
            }

            if (assignment.HasAssignment)
            {
                return new OverviewContent
                {
                    Headline = HarveyPanelTexts.Overview.AssignmentHeadline,
                    BodyLine = assignment.ShortTitle,
                    ProgressLine = assignment.ProgressLine,
                    AfterLine = assignment.AfterHint,
                    StressLine = BuildOverviewStressLine(handbook),
                    InjuryLine = HarveyPanelInjuryFormatter.FormatOverviewLine(injuryPanel),
                    AdviceLine = HarveyPanelTexts.Overview.AssignmentAdvice,
                };
            }

            if (hasInjury)
            {
                return new OverviewContent
                {
                    Headline = HarveyPanelTexts.Overview.InjuryAttention,
                    BodyLine = HarveyPanelInjuryFormatter.FormatOverviewDetail(injuryPanel),
                    StressLine = BuildOverviewStressLine(handbook),
                    AdviceLine = HarveyPanelTexts.TalkToHarveySoon(),
                };
            }

            if (_stressLoadService.GetSeverity() == StressSeverity.Calm && handbook.ActiveStates.Count == 0)
            {
                return new OverviewContent
                {
                    Headline = HarveyPanelTexts.Overview.CalmHeadline,
                    BodyLine = HarveyPanelTexts.Overview.CalmBody,
                    AdviceLine = HarveyPanelTexts.Overview.CalmAdvice,
                };
            }

            return new OverviewContent
            {
                Headline = HarveyPanelTexts.Overview.CalmHeadline,
                BodyLine = HarveyPanelTexts.Overview.CalmBody,
                StressLine = BuildOverviewStressLine(handbook),
                AdviceLine = _trustService.GetTrustLevel() >= HarveyCareTrustLevels.SafePerson
                    ? HarveyPanelTexts.Overview.TrustedAdvice
                    : HarveyPanelTexts.Overview.CalmAdvice,
            };
        }

        private static string BuildStressProgressBlock(HarveyPanelAssignmentFormatter.Display assignment)
        {
            if (!assignment.HasAssignment)
                return "";

            if (!string.IsNullOrWhiteSpace(assignment.StallHint))
                return $"{assignment.ProgressLine}\n{assignment.StallHint}";

            return assignment.ProgressLine;
        }

        private static string BuildOverviewStressLine(HandbookViewModel handbook)
        {
            if (handbook.ActiveStates.Count == 0)
                return "";

            var names = handbook.ActiveStates
                .Select(row => row.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Take(3)
                .ToList();

            if (names.Count == 0)
                return "";

            var joined = string.Join(", ", names);
            return $"Стресс: {joined}.";
        }

        private IHarveyInjuryApi? TryGetInjuryApi()
        {
            if (!_helper.ModRegistry.IsLoaded(InjuryModId))
                return null;

            var api = _helper.ModRegistry.GetApi<IHarveyInjuryApi>(InjuryModId);
            if (api == null || !api.IsAvailable)
                return null;

            return api;
        }

        private PlanTabContent BuildPlanTabContent(
            HarveyPanelAssignmentFormatter.Display assignment,
            RecoveryPlanPanelDto? recoveryPlan)
        {
            if (recoveryPlan != null)
            {
                if (recoveryPlan.HasPlan)
                {
                    return new PlanTabContent
                    {
                        Title = HarveyPanelTexts.Plan.ActiveTitle,
                        Body = recoveryPlan.BodyText,
                    };
                }

                if (assignment.HasAssignment)
                {
                    return new PlanTabContent
                    {
                        Title = HarveyPanelTexts.Plan.StressAssignmentTitle,
                        Body = assignment.ObjectiveText,
                    };
                }

                return new PlanTabContent
                {
                    Title = HarveyPanelTexts.Plan.NoPlanTitle,
                    Body = recoveryPlan.BodyText,
                };
            }

            if (!_helper.ModRegistry.IsLoaded(InjuryModId))
            {
                if (assignment.HasAssignment)
                {
                    return new PlanTabContent
                    {
                        Title = HarveyPanelTexts.Plan.StressAssignmentTitle,
                        Body = assignment.ObjectiveText,
                    };
                }

                return new PlanTabContent
                {
                    Title = HarveyPanelTexts.Plan.NoPlanTitle,
                    Body = HarveyPanelTexts.Tone(
                        HarveyPanelTexts.Plan.NoPlanBody,
                        HarveyPanelTexts.Plan.NoPlanBodyInformal),
                };
            }

            if (assignment.HasAssignment)
            {
                return new PlanTabContent
                {
                    Title = HarveyPanelTexts.Plan.StressAssignmentTitle,
                    Body = assignment.ObjectiveText,
                };
            }

            return new PlanTabContent
            {
                Title = HarveyPanelTexts.Plan.NoPlanTitle,
                Body = HarveyPanelTexts.Plan.InjuryDataUnavailableBody,
            };
        }

        private string BuildTrustLevelLine()
        {
            var level = _trustService.GetTrustLevel();
            var points = _trustService.GetTrustPoints();

            if (level <= HarveyCareTrustLevels.FamiliarDoctor && points <= 0)
                return HarveyPanelTexts.Trust.ObservingTitle;

            return HarveyPanelTexts.Trust.LevelLine(GetTrustTitle(level), points);
        }

        private string BuildTrustDescriptionLine()
        {
            var level = _trustService.GetTrustLevel();
            var points = _trustService.GetTrustPoints();
            var state = _trustService.State;

            if (level <= HarveyCareTrustLevels.FamiliarDoctor && points <= 0)
                return HarveyPanelTexts.Tone(
                    HarveyPanelTexts.Trust.ObservingBody,
                    HarveyPanelTexts.Trust.ObservingBodyInformal);

            if (state.IgnoredAssignments > 0 || points < 40)
                return HarveyPanelTexts.Trust.WaryBody;

            if (level >= HarveyCareTrustLevels.SafePerson)
                return HarveyPanelTexts.Trust.HighBody;

            if (level >= HarveyCareTrustLevels.TrustedDoctor)
                return HarveyPanelTexts.Trust.CautiousBody;

            return HarveyPanelTexts.Trust.CautiousBody;
        }

        private string BuildTrustPermissionsLine()
        {
            var state = _trustService.State;
            var lines = new System.Collections.Generic.List<string>();

            if (state.GroundingDialogueUnlocked || _trustService.GetTrustLevel() >= HarveyCareTrustLevels.TrustedDoctor)
                lines.Add(HarveyPanelTexts.Trust.EveningChecksAllowed);
            else
                lines.Add($"{HarveyPanelTexts.Trust.PermissionOnlyTitle}: {HarveyPanelTexts.Trust.EveningChecksAllowed.ToLowerInvariant()}");

            if (_trustService.IsHarveySafePersonUnlocked())
                lines.Add(HarveyPanelTexts.Trust.HoldHandAllowed);
            else
                lines.Add($"{HarveyPanelTexts.Trust.PermissionOnlyTitle}: {HarveyPanelTexts.Trust.HoldHandAllowed.ToLowerInvariant()}");

            if (_trustService.GetTrustLevel() >= HarveyCareTrustLevels.Anchor)
                lines.Add(HarveyPanelTexts.Trust.StrictCareAllowed);

            if (_trustService.CanHarveyForestRescue())
                lines.Add(HarveyPanelTexts.Trust.EmergencyAccessAllowed);

            return string.Join("\n", lines);
        }

        private string BuildTrustPlaceholder()
        {
            if (_trustService.GetTrustPoints() > 0 || _trustService.GetTrustLevel() > HarveyCareTrustLevels.FamiliarDoctor)
                return "";

            return HarveyPanelTexts.Tone(
                HarveyPanelTexts.Trust.ObservingBody,
                HarveyPanelTexts.Trust.ObservingBodyInformal);
        }

        private static string GetTrustTitle(int level) => level switch
        {
            HarveyCareTrustLevels.Anchor => HarveyPanelTexts.Trust.HighTitle,
            HarveyCareTrustLevels.SafePerson => HarveyPanelTexts.Trust.HighTitle,
            HarveyCareTrustLevels.TrustedDoctor => HarveyPanelTexts.Trust.CautiousTitle,
            HarveyCareTrustLevels.FamiliarDoctor => HarveyPanelTexts.Trust.ObservingTitle,
            _ => HarveyPanelTexts.Trust.ObservingTitle,
        };

        private void EnrichHandbookTreatmentStages(HandbookViewModel handbook)
        {
            foreach (var row in handbook.AllStates)
            {
                if (string.IsNullOrWhiteSpace(row.TreatmentStageText))
                    row.TreatmentStageText = BuildTreatmentStageText(row.BuffId);
            }
        }

        private static void EnrichHandbookPanelCopy(HandbookViewModel handbook)
        {
            foreach (var row in handbook.AllStates)
            {
                row.StatusText = row.StatusText switch
                {
                    "Есть сейчас" => HarveyPanelTexts.Stress.RowActive,
                    "Не активен" => HarveyPanelTexts.Stress.RowInactive,
                    _ => row.StatusText,
                };
            }
        }

        private string BuildTreatmentStageText(string buffId)
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode != null
                && episode.IsActiveEpisode()
                && TreatmentEpisodeDefinitions.TryGet(episode.EpisodeId, out var definition)
                && string.Equals(definition.DefaultPrimaryBuffId, buffId, StringComparison.OrdinalIgnoreCase))
            {
                var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
                if (treatment?.Progress != null)
                {
                    if (episode.AwaitingHarveyReview)
                        return HarveyPanelTexts.Stress.StageReadyForTalk;

                    return EpisodeQuestProgressService.BuildCompactProgressLine(
                        episode.EpisodeId,
                        treatment.Progress,
                        _data.OverworkBreaksToday,
                        episode);
                }
            }

            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);
            if (activeTreatment == null)
                return "";

            if (activeTreatment.AwaitingHarveyReview)
                return HarveyPanelTexts.Stress.StageReadyForTalk;

            return activeTreatment.ObjectivesCompleted
                ? HarveyPanelTexts.Stress.StageCompleted
                : HarveyPanelTexts.Stress.StageInProgress;
        }

        private sealed class PlanTabContent
        {
            public string Title { get; init; } = "";
            public string Body { get; init; } = "";
        }
    }
}
