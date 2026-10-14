using System.Linq;
using HarveyOverhaul.Core.Models;
using HarveyOverhaul.Core.Api;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.UI;
using StardewValley;

namespace HarveyStressMeter.Api;

/// <summary>Отдаёт данные стресса для вкладок Обзор/Стресс/Доверие. План — через <see cref="StressCareDirectiveProvider"/>.</summary>
public sealed class StressPanelProvider : IHarveyPanelActionProvider
{
    public const string ProviderId = "marilynsinister.HarveyStressMeter";

    /// <summary>Кнопка в секции «Границы»: вернуться к одной из договорённостей.</summary>
    private const string RevisitActionId = "consent_revisit";

    private HarveyConsentService? _consentService;

    private readonly SaveData _data;
    private readonly HandbookManager _handbookManager;
    private readonly StressLoadService _stressLoadService;
    private readonly HarveyCareTrustService _trustService;

    public StressPanelProvider(
        SaveData data,
        HandbookManager handbookManager,
        StressLoadService stressLoadService,
        HarveyCareTrustService trustService)
    {
        _data = data;
        _handbookManager = handbookManager;
        _stressLoadService = stressLoadService;
        _trustService = trustService;
    }

    public void SetConsentService(HarveyConsentService consentService)
        => _consentService = consentService;

    public string UniqueId => ProviderId;

    public bool HandlePanelAction(string actionId)
    {
        if (actionId != RevisitActionId || _consentService == null)
            return false;

        var options = _consentService.GetRevisitableConsents();
        if (options.Count == 0)
        {
            Game1.addHUDMessage(new HUDMessage("Пересматривать пока нечего: Харви ещё ни о чём не спрашивал.", HUDMessage.error_type));
            return false;
        }

        // Окно закрываем, вопрос — со следующего тика, когда меню уйдёт.
        Game1.exitActiveMenu();
        DelayedAction.functionAfterDelay(() =>
        {
            var responses = options
                .Select(id => new Response(id, HarveyConsentService.GetConsentLabel(id)))
                .Append(new Response("cancel", "Пока ничего"))
                .ToArray();

            Game1.currentLocation?.createQuestionDialogue(
                "К какой договорённости с Харви вернуться? Он спросит заново — прежний ответ ни к чему не обязывает.",
                responses,
                (_, answer) =>
                {
                    if (answer == "cancel")
                        return;

                    _trustService.RequestConsentRevisit(answer);
                    Game1.addHUDMessage(new HUDMessage(
                        "Харви вернётся к этому в разговоре — не раньше завтра, если сегодня вы уже говорили о границах.",
                        HUDMessage.newQuest_type));
                });
        }, 100);

        return false;
    }
    public string DisplayName => "Harvey Stress";
    public int Priority => 100;

    public HarveyPanelContribution GetPanelContribution()
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

        var overview = BuildOverviewContent(assignment, handbook);
        var overviewSections = BuildOverviewSections(assignment, handbook);
        var stressSections = BuildStressSections(handbook, assignment);
        var trustSections = BuildTrustSections();

        bool awaitingReview = HasPendingTreatmentReview()
            || assignment.AwaitingHarveyReview
            || _data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true;

        return new HarveyPanelContribution
        {
            ProviderId = ProviderId,
            HasPendingHarveyReview = awaitingReview,
            HasPriorityAppointment = awaitingReview,
            HasActiveRecoveryPlan = handbook.ActiveStates.Count > 0
                || assignment.HasAssignment
                || HasPendingTreatmentReview(),
            OverviewFields = overview,
            OverviewSections = overviewSections,
            StressSections = stressSections,
            TrustSections = trustSections,
            StressFields = new HarveyPanelStressFields
            {
                AssignmentTitle = assignment.HasAssignment ? assignment.StressTitle : "",
                AssignmentProgress = BuildStressProgressBlock(assignment),
                AssignmentObjective = assignment.ObjectiveText,
                AssignmentAfter = assignment.AfterHint,
                NoAssignmentLine = assignment.HasAssignment ? "" : HarveyPanelTexts.Stress.NoAssignment,
                Handbook = handbook,
            },
            TrustFields = new HarveyPanelTrustFields
            {
                LevelLine = BuildTrustLevelLine(),
                DescriptionLine = BuildTrustDescriptionLine(),
                PermissionsLine = BuildTrustPermissionsLine(),
                Placeholder = BuildTrustPlaceholder(),
            },
        };
    }

    private List<HarveyPanelSectionDto> BuildOverviewSections(
        HarveyPanelAssignmentFormatter.Display assignment,
        HandbookViewModel handbook)
    {
        var sections = new List<HarveyPanelSectionDto>();
        bool awaitingReview = assignment.AwaitingHarveyReview
            || _data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true;

        if (handbook.ActiveStates.Count > 0)
        {
            sections.Add(new HarveyPanelSectionDto
            {
                Title = "Стресс сейчас",
                Body = BuildOverviewStressLine(handbook),
                Priority = 10,
                Severity = HarveyPanelSeverity.Warning,
            });
        }

        if (assignment.HasAssignment)
        {
            sections.Add(new HarveyPanelSectionDto
            {
                Title = assignment.ShortTitle,
                Body = assignment.ObjectiveText,
                Status = assignment.ProgressLine,
                Priority = 20,
                Severity = awaitingReview ? HarveyPanelSeverity.Urgent : HarveyPanelSeverity.Normal,
            });
        }

        if (awaitingReview)
        {
            sections.Add(new HarveyPanelSectionDto
            {
                Title = HarveyPanelTexts.Overview.HarveyWaitingHeadline,
                Body = HarveyPanelTexts.TalkToHarvey(),
                Priority = 5,
                Severity = HarveyPanelSeverity.Urgent,
            });
        }

        string advice = awaitingReview
            ? HarveyPanelTexts.Overview.ReviewAdvice
            : assignment.HasAssignment
                ? HarveyPanelTexts.Overview.AssignmentAdvice
                : _trustService.GetTrustLevel() >= HarveyCareTrustLevels.SafePerson
                    ? HarveyPanelTexts.Overview.TrustedAdvice
                    : HarveyPanelTexts.Overview.CalmAdvice;

        sections.Add(new HarveyPanelSectionDto
        {
            Title = "Совет Харви",
            Body = advice,
            Priority = 90,
            Severity = HarveyPanelSeverity.Info,
        });

        return sections;
    }

    private static List<HarveyPanelSectionDto> BuildStressSections(
        HandbookViewModel handbook,
        HarveyPanelAssignmentFormatter.Display assignment)
    {
        var sections = new List<HarveyPanelSectionDto>();

        foreach (var row in handbook.ActiveStates)
        {
            var body = string.Join("\n",
                new[]
                {
                    row.Effects,
                    string.IsNullOrWhiteSpace(row.Causes) ? null : $"Причины: {row.Causes}",
                    string.IsNullOrWhiteSpace(row.CureSummary) ? null : $"Помогает: {row.CureSummary}",
                    string.IsNullOrWhiteSpace(row.TreatmentStageText) ? null : $"Прогресс: {row.TreatmentStageText}",
                }.Where(line => !string.IsNullOrWhiteSpace(line)));

            sections.Add(new HarveyPanelSectionDto
            {
                Title = row.Title,
                Body = body,
                Status = row.StatusText,
                Priority = 10,
                Severity = HarveyPanelSeverity.Warning,
            });
        }

        if (assignment.HasAssignment && !string.IsNullOrWhiteSpace(assignment.ProgressLine))
        {
            sections.Add(new HarveyPanelSectionDto
            {
                Title = "Назначение",
                Body = assignment.ObjectiveText,
                Status = assignment.ProgressLine,
                Priority = 0,
                Severity = HarveyPanelSeverity.Normal,
            });
        }

        return sections;
    }

    private List<HarveyPanelSectionDto> BuildTrustSections()
    {
        var sections = new List<HarveyPanelSectionDto>
        {
            new HarveyPanelSectionDto
            {
                Title = BuildTrustLevelLine(),
                Body = BuildTrustDescriptionLine(),
                Status = BuildTrustPermissionsLine(),
                Priority = 10,
                Severity = HarveyPanelSeverity.Info,
            },
        };

        if (_trustService.ConsentQuestionsEnabled)
        {
            sections.Add(new HarveyPanelSectionDto
            {
                Title = HarveyPanelTexts.Trust.BoundariesTitle,
                Body = HarveyPanelTexts.Trust.BoundariesBody,
                Status = BuildBoundariesStatus(),
                Priority = 20,
                Severity = HarveyPanelSeverity.Info,
                ActionLabel = _consentService?.GetRevisitableConsents().Count > 0 ? "Пересмотреть договорённость" : "",
                ActionId = RevisitActionId,
            });
        }

        return sections;
    }

    /// <summary>Разрешения из «Границ»: открыто ли по уровню и что ответила фермерша.</summary>
    private string BuildBoundariesStatus()
    {
        var level = _trustService.GetTrustLevel();
        var lines = new List<string>();

        foreach (var (consentId, label) in new[]
                 {
                     (HarveyCareConsents.EveningChecks, HarveyPanelTexts.Trust.PerkEveningChecks),
                     (HarveyCareConsents.HoldHand, HarveyPanelTexts.Trust.PerkHoldHand),
                     (HarveyCareConsents.SearchInStorm, HarveyPanelTexts.Trust.PerkSearchInStorm),
                     (HarveyCareConsents.StrictCare, HarveyPanelTexts.Trust.PerkStrictCare),
                     (HarveyCareConsents.HarveyLimits, HarveyPanelTexts.Trust.PerkHarveyLimits),
                 })
        {
            // Зеркальный вопрос — только в браке; до свадьбы строку не показываем.
            if (HarveyCareConsents.RequiresMarriage(consentId) && !HarveyFriendshipHelper.IsMarriedToHarvey())
                continue;

            string state = level < HarveyCareConsents.RequiredTrustLevel(consentId)
                ? HarveyPanelTexts.Trust.ConsentLocked
                : _trustService.GetConsent(consentId) switch
                {
                    HarveyConsentAnswer.Granted => HarveyPanelTexts.Trust.ConsentGranted,
                    HarveyConsentAnswer.NotYet => HarveyPanelTexts.Trust.ConsentNotYet,
                    HarveyConsentAnswer.Never => HarveyPanelTexts.Trust.ConsentNever,
                    _ => HarveyPanelTexts.Trust.ConsentNotAsked,
                };
            lines.Add($"{label} — {state}");
        }

        return string.Join("\n", lines);
    }

    private bool HasActiveStressAssignment()
    {
        var episode = _data.ActiveTreatmentEpisode;
        if (episode != null && episode.IsActiveEpisode())
        {
            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress != null && !episode.AwaitingHarveyReview)
                return true;
        }

        return _data.StressState.ActiveTreatments.Values.Any(t =>
            t.TreatmentStarted
            && !t.IsCompleted
            && !t.IsCured
            && !t.AwaitingHarveyReview);
    }

    private bool HasPendingTreatmentReview()
    {
        if (_data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true)
            return true;

        return _data.StressState.ActiveTreatments.Values.Any(t => t.AwaitingHarveyReview);
    }

    private HarveyPanelOverviewFields BuildOverviewContent(
        HarveyPanelAssignmentFormatter.Display assignment,
        HandbookViewModel handbook)
    {
        bool awaitingReview = assignment.AwaitingHarveyReview
            || _data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true;

        if (awaitingReview)
        {
            return new HarveyPanelOverviewFields
            {
                StateLine = HarveyPanelTexts.Overview.HarveyWaitingHeadline,
                AssignmentLine = assignment.HasAssignment ? assignment.ShortTitle : HarveyPanelTexts.TalkToHarveySoon(),
                AfterLine = HarveyPanelTexts.TalkToHarvey(),
                AdviceLine = HarveyPanelTexts.Overview.ReviewAdvice,
            };
        }

        if (assignment.HasAssignment)
        {
            return new HarveyPanelOverviewFields
            {
                StateLine = HarveyPanelTexts.Overview.AssignmentHeadline,
                AssignmentLine = assignment.ShortTitle,
                ProgressLine = assignment.ProgressLine,
                AfterLine = assignment.AfterHint,
                StressLine = BuildOverviewStressLine(handbook),
                AdviceLine = HarveyPanelTexts.Overview.AssignmentAdvice,
            };
        }

        if (_stressLoadService.GetSeverity() == StressSeverity.Calm && handbook.ActiveStates.Count == 0)
        {
            return new HarveyPanelOverviewFields
            {
                StateLine = HarveyPanelTexts.Overview.CalmHeadline,
                AssignmentLine = HarveyPanelTexts.Overview.CalmBody,
                AdviceLine = HarveyPanelTexts.Overview.CalmAdvice,
            };
        }

        return new HarveyPanelOverviewFields
        {
            StateLine = HarveyPanelTexts.Overview.CalmHeadline,
            AssignmentLine = HarveyPanelTexts.Overview.CalmBody,
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

        return $"Стресс: {string.Join(", ", names)}.";
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
        var body = BuildTrustMoodLine();
        var extra = new List<string>();

        if (_trustService.State.PendingRepairPoints > 0)
            extra.Add(HarveyPanelTexts.Trust.RepairPending);

        // Одна шкала на всё лечение: очки за режим травм приходят сюда же из Injury.
        int injuryPoints = _trustService.State.PointsBySource.GetValueOrDefault("injury");
        if (injuryPoints > 0)
            extra.Add(HarveyPanelTexts.Trust.InjuryPointsLine(injuryPoints));

        if (_trustService.IsCappedByRelationship())
            extra.Add(HarveyPanelTexts.Trust.CappedByRelationship);
        else if (_trustService.GetNextLevelProgress() is { } next)
            extra.Add(HarveyPanelTexts.Trust.NextLevelLine(next.PointsLeft));

        return extra.Count == 0 ? body : body + "\n" + string.Join("\n", extra);
    }

    private string BuildTrustMoodLine()
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

        return HarveyPanelTexts.Trust.CautiousBody;
    }

    private string BuildTrustPermissionsLine()
    {
        // С «Границами» разрешения показывает отдельная секция — там видно и ответ фермерши.
        if (_trustService.ConsentQuestionsEnabled)
            return "";

        var state = _trustService.State;
        var lines = new List<string>();

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
}
