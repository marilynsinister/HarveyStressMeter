using HarveyOverhaul.Core.Api;
using HarveyOverhaul.Core.Core;
using HarveyOverhaul.Core.Models;
using HarveyOverhaul.Core.Services;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.UI;
using StardewValley;

namespace HarveyStressMeter.Services;

/// <summary>Факты стресса и назначений → HarveyCareDirective (источник правды — Stress save-state).</summary>
public sealed class StressCareDirectiveProvider : IHarveyCareDirectiveProvider
{
    public string ProviderId => HarveyProviderRegistry.StressProviderId;

    private readonly SaveData _data;
    private readonly HandbookManager _handbookManager;

    public StressCareDirectiveProvider(SaveData data, HandbookManager handbookManager)
    {
        _data = data;
        _handbookManager = handbookManager;
    }

    public IReadOnlyList<HarveyCareDirective> GetCareDirectives()
    {
        var directives = new List<HarveyCareDirective>();
        var handled = new HashSet<string>(StringComparer.Ordinal);

        AppendActiveEpisodeDirectives(directives, handled);
        AppendParallelAnxietySpike(directives, handled);
        AppendLegacyTreatments(directives, handled);
        AppendDarknessDirectives(directives);
        AppendActiveStressBuffs(directives, handled);

        return directives;
    }

    private void AppendActiveEpisodeDirectives(List<HarveyCareDirective> directives, HashSet<string> handled)
    {
        var episode = _data.ActiveTreatmentEpisode;
        if (episode == null || !episode.IsActiveEpisode())
            return;

        var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
        if (treatment?.Progress == null)
            return;

        string key = episode.EpisodeId;
        if (!handled.Add(key))
            return;

        var mapped = MapEpisode(episode.EpisodeId, episode, treatment, treatment.Progress);
        if (mapped != null)
            directives.AddRange(mapped);
    }

    private void AppendParallelAnxietySpike(List<HarveyCareDirective> directives, HashSet<string> handled)
    {
        if (handled.Contains(StressEpisodes.AnxietySpike))
            return;

        var treatment = _data.StressState.GetActiveTreatmentByQuest(QuestIds.AnxietySpike);
        if (treatment?.Progress == null || !treatment.IsTreatmentActive())
            return;

        if (!handled.Add(StressEpisodes.AnxietySpike))
            return;

        var pseudoEpisode = new TreatmentEpisodeState
        {
            EpisodeId = StressEpisodes.AnxietySpike,
            QuestId = QuestIds.AnxietySpike,
            TreatmentStarted = true,
            AwaitingHarveyReview = treatment.AwaitingHarveyReview,
        };

        var mapped = MapEpisode(StressEpisodes.AnxietySpike, pseudoEpisode, treatment, treatment.Progress);
        if (mapped != null)
            directives.AddRange(mapped);
    }

    private void AppendLegacyTreatments(List<HarveyCareDirective> directives, HashSet<string> handled)
    {
        foreach (var treatment in _data.StressState.ActiveTreatments.Values
                     .Where(t => !t.IsCompleted && !t.IsCured))
        {
            string? episodeId = TreatmentEpisodeDefinitions.ResolveEpisodeIdForQuest(treatment.QuestId);
            string key = episodeId ?? treatment.QuestId;
            if (string.IsNullOrWhiteSpace(key) || !handled.Add(key))
                continue;

            if (treatment.Progress == null)
            {
                directives.Add(BuildAwaitingStartDirective(
                    key,
                    StressLegacyQuestMap.GetDisplayName(treatment.BuffId, treatment.QuestId)));
                // Бафф этого лечения не показываем отдельным пунктом с прогрессом 0/N: прогресс пойдёт только после разговора.
                if (!string.IsNullOrWhiteSpace(treatment.BuffId))
                    handled.Add($"buff.{treatment.BuffId}");
                continue;
            }

            if (string.Equals(episodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal))
                continue;

            var pseudoEpisode = new TreatmentEpisodeState
            {
                EpisodeId = episodeId ?? key,
                QuestId = treatment.QuestId,
                TreatmentStarted = treatment.TreatmentStarted,
                AwaitingHarveyReview = treatment.AwaitingHarveyReview,
            };

            var mapped = MapEpisode(episodeId ?? key, pseudoEpisode, treatment, treatment.Progress);
            if (mapped != null)
                directives.AddRange(mapped);
            else
                AppendLegacyBuffTreatment(treatment, directives);
        }
    }

    private static List<HarveyCareDirective>? MapEpisode(
        string episodeId,
        TreatmentEpisodeState episode,
        TreatmentState treatment,
        TreatmentProgress progress)
    {
        if (treatment.AwaitingHarveyReview || episode.AwaitingHarveyReview)
        {
            return
            [
                new HarveyCareDirective
                {
                    Id = "stress.appointment.review",
                    Source = HarveyCareDirectiveSource.Stress,
                    Type = HarveyCareDirectiveType.Appointment,
                    Title = "Поговори с Харви",
                    Text = "Назначение выполнено. Харви ждёт контрольный разговор.",
                    Reason = "Харви должен убедиться, что после назначения стало легче.",
                    NextStep = "после разговора назначение закроется, а стресс заметно снизится.",
                    Priority = HarveyCareDirectivePriority.High,
                    State = HarveyCareDirectiveState.Active,
                    HarveyTone = HarveyCareDirectiveTone.Calm,
                },
            ];
        }

        return episodeId switch
        {
            StressEpisodes.AnxietySpike => BuildFindSafePlace(progress),
            StressEpisodes.SocialShutdown => BuildDontStayAlone(progress),
            StressEpisodes.GotoroFlashback => BuildThunderShelter(progress),
            StressEpisodes.PhysicalExhaustion => BuildPhysicalExhaustion(progress, episode),
            StressEpisodes.Burnout => BuildBurnout(progress),
            _ => null,
        };
    }

    private static List<HarveyCareDirective> BuildFindSafePlace(TreatmentProgress progress)
    {
        int goal = EpisodeQuestRules.AnxietySafeSecondsRequired;
        int current = Math.Min(progress.AnxietySafeSeconds, goal);
        bool complete = progress.AnxietySpikeCompletionAnnounced || current >= goal;

        return
        [
            new HarveyCareDirective
            {
                Id = "stress.find_safe_place",
                Source = HarveyCareDirectiveSource.Stress,
                Type = complete ? HarveyCareDirectiveType.Appointment : HarveyCareDirectiveType.ImmediateAction,
                Title = complete ? "Поговори с Харви" : "Найди безопасное место",
                Text = complete
                    ? "Безопасное место найдено. Харви должен проверить, как ты себя чувствуешь."
                    : "Останься там, где спокойно, пока дыхание не выровняется.",
                Current = complete ? 0 : current,
                Goal = complete ? 0 : goal,
                Unit = "сек",
                Priority = HarveyCareDirectivePriority.High,
                // Место найдено, но разговор с Харви ещё впереди: визит активен, а не «✓ выполнено».
                State = HarveyCareDirectiveState.Active,
                CanFailDay = !complete,
                FailureText = "безопасное место ещё не найдено",
                Reason = "Накатила волна тревоги — телу нужно время, чтобы успокоиться.",
                NextStep = complete
                    ? "после разговора с Харви назначение закроется."
                    : "дом, клиника или тихое место в лесу. Когда шкала заполнится — поговори с Харви.",
                HarveyTone = HarveyCareDirectiveTone.Worried,
                HarveyAdvice = complete
                    ? "Сначала восстанови дыхание. Потом приходи ко мне."
                    : "Найди тихий угол — дом, клиника или лес. Я подожду.",
            },
        ];
    }

    private static List<HarveyCareDirective> BuildDontStayAlone(TreatmentProgress progress)
    {
        int goal = SocialShutdownQuestHelper.HarveySecondsRequired;
        int current = Math.Min(progress.SecondsNearHarvey, goal);
        bool complete = progress.IsSocialShutdownQuestCompleted();

        return
        [
            new HarveyCareDirective
            {
                Id = "stress.not_alone",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.ImmediateAction,
                Title = PlayerGrammar.Gendered("Не оставайся один", "Не оставайся одна"),
                Text = $"Побудь рядом с Харви {goal} сек или поговори с близким человеком (4+ сердца). "
                    + $"Малознакомых сегодня — не больше {SocialShutdownQuestHelper.MaxUnfamiliarTalksPerDay}.",
                Reason = $"Ты {PlayerGrammar.Gendered("закрылся", "закрылась")} от людей, а изоляция только усиливает стресс.",
                NextStep = complete
                    ? "поговори с Харви — он закроет назначение."
                    : "достаточно одного пути: время рядом с Харви или разговор с другом.",
                Current = current,
                Goal = goal,
                Unit = "сек",
                Priority = HarveyCareDirectivePriority.High,
                State = complete ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
                CanFailDay = !complete,
                FailureText = "нужно побольше времени рядом с Харви",
                HarveyTone = HarveyCareDirectiveTone.Worried,
            },
        ];
    }

    private static List<HarveyCareDirective> BuildThunderShelter(TreatmentProgress progress)
    {
        return
        [
            new HarveyCareDirective
            {
                Id = "stress.thunder_shelter",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.ImmediateAction,
                Title = "Укройся от грозы",
                Text = "Не стой под открытым небом. Харви просил идти в безопасное место.",
                Reason = "Гроза вызвала тяжёлые воспоминания — под открытым небом станет только хуже.",
                NextStep = "зайди в любое здание или найди Харви, пока гроза не кончится.",
                Current = progress.SecondsNearHarvey,
                Priority = HarveyCareDirectivePriority.Critical,
                HarveyTone = HarveyCareDirectiveTone.Worried,
            },
        ];
    }

    private static List<HarveyCareDirective> BuildPhysicalExhaustion(
        TreatmentProgress progress,
        TreatmentEpisodeState episode)
    {
        var rules = new List<HarveyCareDirective>();

        foreach (string causeId in episode.RelatedCauseIds)
        {
            bool done = progress.EpisodeCausesCompleted.Contains(causeId);
            var rule = MapPhysicalCause(causeId, progress, done);
            if (rule != null)
                rules.Add(rule);
        }

        if (rules.Count == 0)
        {
            rules.Add(new HarveyCareDirective
            {
                Id = "stress.physical_rest",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Сделай перерыв",
                Text = "Харви просит настоящий отдых, не «ещё один ряд грядок».",
                Reason = "Тело на пределе: усталость копится быстрее, чем уходит.",
                Priority = HarveyCareDirectivePriority.Normal,
            });
        }

        return rules;
    }

    private static HarveyCareDirective? MapPhysicalCause(string causeId, TreatmentProgress progress, bool done)
    {
        return causeId switch
        {
            StressCauses.NoSleep => new HarveyCareDirective
            {
                Id = "stress.rule.sleep",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Лечь спать вовремя",
                Text = "Харви просил лечь до полуночи, чтобы день восстановления засчитался.",
                Reason = "Недосып — одна из причин истощения.",
                Priority = HarveyCareDirectivePriority.Normal,
                State = done ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
                CanFailDay = true,
                FailureText = "если ляжешь слишком поздно",
            },
            StressCauses.Overwork => new HarveyCareDirective
            {
                Id = "stress.rule.break",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Сделай перерыв",
                Text = "Остановись, прежде чем организм снова перегрузится.",
                Reason = "Переработка — одна из причин истощения.",
                Priority = HarveyCareDirectivePriority.Normal,
                State = done ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
            },
            _ => null,
        };
    }

    private static List<HarveyCareDirective> BuildBurnout(TreatmentProgress progress)
    {
        var rules = new List<HarveyCareDirective>
        {
            new()
            {
                Id = "stress.rule.burnout_rest",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Щадящий режим",
                Text = "Сегодня без перегруза — Харви просит беречь силы.",
                Reason = "Это выгорание: несколько причин стресса навалились сразу.",
                Priority = HarveyCareDirectivePriority.Normal,
            },
            new()
            {
                Id = "stress.avoid.mines_burnout",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.Avoid,
                Title = "Не ходи в шахту",
                Text = "Сегодня лучше обойтись без шахты.",
                Reason = "При выгорании опасность и темнота шахты бьют сильнее всего.",
                Priority = HarveyCareDirectivePriority.High,
                State = progress.BurnoutAvoidedMinesToday
                    ? HarveyCareDirectiveState.Done
                    : HarveyCareDirectiveState.Active,
                CanFailDay = true,
                FailureText = "если зайдёшь в шахту",
            },
            new()
            {
                Id = "stress.rule.burnout_sleep",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                // Строже, чем «до полуночи» у травм: отдельный заголовок, чтобы Core не склеил правила.
                Title = "Лечь спать до 22:00",
                Text = "Харви просил лечь до 22:00.",
                Reason = "Ранний сон — самый быстрый способ снять выгорание.",
                Priority = HarveyCareDirectivePriority.Normal,
                CanFailDay = true,
                FailureText = "если ляжешь после 22:00",
            },
        };

        return rules;
    }

    private void AppendDarknessDirectives(List<HarveyCareDirective> directives)
    {
        var d = _data.Darkness;
        if (!d.IsTherapyActive && !d.DarknessRelapseTreatmentActive)
            return;

        directives.Add(new HarveyCareDirective
        {
            Id = "stress.darkness_home",
            Source = HarveyCareDirectiveSource.Stress,
            Type = HarveyCareDirectiveType.TodayRule,
            Title = "Вечером будь дома при свете",
            Text = "Сегодня лучше не проверять себя темнотой. Сначала стабильность.",
            Reason = "Идёт терапия страха темноты — срывы откатывают прогресс.",
            Priority = HarveyCareDirectivePriority.Normal,
            HarveyTone = HarveyCareDirectiveTone.Calm,
        });
    }

    private void AppendActiveStressBuffs(List<HarveyCareDirective> directives, HashSet<string> handled)
    {
        var handbook = _handbookManager.BuildViewModel(_data);
        foreach (var row in handbook.ActiveStates)
        {
            if (string.IsNullOrWhiteSpace(row.BuffId) || !handled.Add($"buff.{row.BuffId}"))
                continue;

            var treatment = _data.StressState.GetActiveTreatment(row.BuffId);

            // Лечение ещё не назначено: счётчики задания (0/N сек) не растут до разговора с Харви — показываем визит.
            // Голод — исключение: «поесть» снимает его и до разговора.
            if (treatment is { TreatmentStarted: false, AwaitingHarveyReview: false }
                && !string.Equals(row.BuffId, BuffIds.Hunger, StringComparison.OrdinalIgnoreCase))
            {
                directives.Add(BuildAwaitingStartDirective($"buff.{row.BuffId}", row.Title));
                continue;
            }

            var mapped = MapStressBuffDirective(row.BuffId, row.Title, treatment?.Progress, treatment);
            if (mapped != null)
            {
                directives.Add(mapped);
                continue;
            }

            string body = !string.IsNullOrWhiteSpace(row.CureSummary)
                ? row.CureSummary
                : row.Effects;
            if (string.IsNullOrWhiteSpace(body))
                continue;

            directives.Add(new HarveyCareDirective
            {
                Id = $"stress.buff.{row.BuffId}",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = row.Title,
                Text = body,
                Priority = HarveyCareDirectivePriority.Normal,
                HarveyTone = HarveyCareDirectiveTone.Worried,
            });
        }
    }

    private static void AppendLegacyBuffTreatment(TreatmentState treatment, List<HarveyCareDirective> directives)
    {
        var mapped = MapStressBuffDirective(
            treatment.BuffId,
            StressLegacyQuestMap.GetDisplayName(treatment.BuffId, treatment.QuestId),
            treatment.Progress,
            treatment);
        if (mapped != null)
            directives.Add(mapped);
    }

    private static HarveyCareDirective? MapStressBuffDirective(
        string buffId,
        string title,
        TreatmentProgress? progress,
        TreatmentState? treatment)
    {
        progress ??= new TreatmentProgress();
        bool awaitingReview = treatment?.AwaitingHarveyReview == true;

        if (awaitingReview)
        {
            return new HarveyCareDirective
            {
                Id = $"stress.review.{buffId}",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.Appointment,
                Title = string.IsNullOrWhiteSpace(title) ? "Поговори с Харви" : $"Контрольный разговор: {title}",
                Text = "Назначение выполнено. Харви ждёт контрольный разговор.",
                Reason = "Харви должен убедиться, что после назначения стало легче.",
                NextStep = "после разговора назначение закроется, а дебафф уйдёт.",
                Priority = HarveyCareDirectivePriority.High,
                State = HarveyCareDirectiveState.Active,
            };
        }

        if (string.Equals(buffId, BuffIds.Hunger, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.Hunger, StringComparison.Ordinal))
        {
            return new HarveyCareDirective
            {
                Id = "stress.hunger",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.ImmediateAction,
                Title = "Поесть",
                Text = progress.AteAnyFood
                    ? "Еда принята. Вернись к Харви, если он просил."
                    : "Слабость от голода. Съешь любую еду и дай организму силы.",
                Reason = $"Ты несколько дней почти не {PlayerGrammar.Gendered("ел", "ела")} — отсюда слабость.",
                Priority = HarveyCareDirectivePriority.High,
                State = progress.AteAnyFood ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
                HarveyTone = HarveyCareDirectiveTone.Worried,
            };
        }

        if (string.Equals(buffId, BuffIds.NoSleep, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.NoSleep, StringComparison.Ordinal))
        {
            return new HarveyCareDirective
            {
                Id = "stress.no_sleep",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Лечь спать до 22:00",
                Text = "Недосып копится. Харви просил лечь раньше — сегодня до 22:00.",
                Reason = "Несколько ночей подряд ты ложишься слишком поздно.",
                NextStep = "засчитается утром, если ляжешь до 22:00.",
                Priority = HarveyCareDirectivePriority.Normal,
                State = progress.EarlySleepStreak > 0
                    ? HarveyCareDirectiveState.Done
                    : HarveyCareDirectiveState.Active,
                CanFailDay = true,
                FailureText = "если снова ляжешь слишком поздно",
                HarveyTone = HarveyCareDirectiveTone.Worried,
            };
        }

        if (string.Equals(buffId, BuffIds.Tired, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.Tired, StringComparison.Ordinal))
        {
            int goal = EpisodeQuestRules.PhysicalTiredRestSecondsRequired;
            int current = Math.Min(progress.TiredRestSeconds, goal);
            return new HarveyCareDirective
            {
                Id = "stress.tired",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.ImmediateAction,
                Title = "Отдохни дома",
                Text = "Усталость накопилась. Побудь дома без тяжёлой работы (убери кирку, топор и мотыгу из рук).",
                Reason = "Выносливость упала почти до нуля.",
                Current = current,
                Goal = goal,
                Unit = "сек",
                Priority = HarveyCareDirectivePriority.High,
                State = current >= goal ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
                HarveyTone = HarveyCareDirectiveTone.Worried,
            };
        }

        if (string.Equals(buffId, BuffIds.Social, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.Social, StringComparison.Ordinal))
        {
            int goal = SocialAnxietyTherapyService.HarveySecondsRequired;
            int current = Math.Min(progress.SecondsNearHarvey, goal);
            int talks = progress.SocialTalksAfterQuest;
            bool complete = progress.IsSocialQuestCompleted();
            return new HarveyCareDirective
            {
                Id = "stress.social_anxiety",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.ImmediateAction,
                Title = "Мягкое общение",
                // Два пути: 3 разговора + время рядом с Харви, или 5 разговоров.
                // Раньше пункт показывал только таймер и ставил ✓ после 60 сек, хотя разговоров не хватало.
                Text = $"Путь А: 3 разговора + {goal} сек рядом с Харви. Путь Б: 5 разговоров.\n"
                    + $"   Сейчас: разговоров {talks}, рядом с Харви {current}/{goal} сек.",
                Reason = "Общения сегодня было слишком много, и наступил социальный срыв.",
                NextStep = complete
                    ? "поговори с Харви — он закроет назначение."
                    : "разговоры считаются с момента назначения, по одному на человека в день.",
                Priority = HarveyCareDirectivePriority.High,
                State = complete ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
                HarveyTone = HarveyCareDirectiveTone.Worried,
            };
        }

        if (string.Equals(buffId, BuffIds.Overwork, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.Overwork, StringComparison.Ordinal))
        {
            return new HarveyCareDirective
            {
                Id = "stress.overwork",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Сделай перерыв",
                Text = LegacyTreatmentObjectives.OverworkDailyStart,
                Reason = $"Ты {PlayerGrammar.Gendered("работал", "работала")} до изнеможения — глубокой ночью с пустой выносливостью.",
                Priority = HarveyCareDirectivePriority.Normal,
                HarveyTone = HarveyCareDirectiveTone.Worried,
            };
        }

        if (string.Equals(buffId, BuffIds.TooCold, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.TooCold, StringComparison.Ordinal))
        {
            int goal = LegacyTreatmentObjectives.TooColdWarmSecondsRequired;
            int current = Math.Min(progress.WarmSeconds, goal);
            return new HarveyCareDirective
            {
                Id = "stress.too_cold",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.ImmediateAction,
                Title = "Согрейся",
                Text = LegacyTreatmentObjectives.TooColdWarm(progress.WarmSeconds),
                Reason = $"Ты {PlayerGrammar.Gendered("замёрз", "замёрзла")} вечером на холоде.",
                Current = current,
                Goal = goal,
                Unit = "сек",
                Priority = HarveyCareDirectivePriority.High,
                State = current >= goal ? HarveyCareDirectiveState.Done : HarveyCareDirectiveState.Active,
            };
        }

        if (string.Equals(buffId, BuffIds.Lonely, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment?.QuestId, QuestIds.Lonely, StringComparison.Ordinal))
        {
            return new HarveyCareDirective
            {
                Id = "stress.lonely",
                Source = HarveyCareDirectiveSource.Stress,
                Type = HarveyCareDirectiveType.TodayRule,
                Title = "Мягкий контакт",
                Text = LegacyTreatmentObjectives.Lonely(progress.TalkedUniqueToday),
                Reason = $"Несколько дней ты ни с кем не {PlayerGrammar.Gendered("разговаривал", "разговаривала")}.",
                Priority = HarveyCareDirectivePriority.Normal,
            };
        }

        return null;
    }

    private static HarveyCareDirective BuildAwaitingStartDirective(string key, string displayName) => new()
    {
        Id = $"stress.await.{key}",
        Source = HarveyCareDirectiveSource.Stress,
        Type = HarveyCareDirectiveType.Appointment,
        Title = string.IsNullOrWhiteSpace(displayName) ? "Поговори с Харви" : $"Поговори с Харви: {displayName}",
        Text = "Харви видит признаки перегруза. Поговори с ним, чтобы начать лечение.",
        Reason = string.IsNullOrWhiteSpace(displayName) ? "" : $"Появилось состояние «{displayName}».",
        NextStep = "Харви предложит назначение — согласись, и в плане появятся конкретные шаги.",
        Priority = HarveyCareDirectivePriority.Normal,
        HarveyTone = HarveyCareDirectiveTone.Calm,
    };
}
