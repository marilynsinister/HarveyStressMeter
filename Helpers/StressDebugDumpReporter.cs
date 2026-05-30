using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Quests;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Structured MCP / hs.debug dump for AI autotests.</summary>
    public sealed class StressDebugDumpContext
    {
        public IMonitor Monitor { get; init; } = null!;
        public SaveData Data { get; init; } = null!;
        public StateService StateService { get; init; } = null!;
        public StressDialogueService StressDialogueService { get; init; } = null!;
        public HarveyCareTrustService? TrustService { get; init; }
        public StressLoadService? StressLoadService { get; init; }
        public HarveyFlashbackRescueService? RescueService { get; init; }
        public HarveySafePersonAuraService? SafeAuraService { get; init; }
        public TreatmentEpisodeService? EpisodeService { get; init; }
    }

    public static class StressDebugDumpReporter
    {
        private static readonly string[] StressQuestIdPrefixes = { "HarveyMod_", "HarveyStress" };

        private static readonly string[] KnownStressQuestIds =
        {
            QuestIds.Tired,
            QuestIds.Lonely,
            QuestIds.Thunder,
            QuestIds.Hunger,
            QuestIds.Overwork,
            QuestIds.NoSleep,
            QuestIds.TooCold,
            QuestIds.Social,
            QuestIds.Darkness,
            QuestIds.PhysicalExhaustion,
            QuestIds.Burnout,
            QuestIds.AnxietySpike,
            QuestIds.GotoroFlashback,
            QuestIds.SocialShutdown,
            "HarveyMod_DarknessTherapy",
            "HarveyMod_DarknessStep1",
            "HarveyMod_DarknessStep2",
            "HarveyMod_DarknessStep3",
        };

        public static string BuildReport(StressDebugDumpContext ctx)
        {
            var sb = new StringBuilder();
            var untreated = SafeGet(() => StressDebuffSelector.GetUntreatedDebuffs(ctx.StateService, ctx.Data), new List<string>());

            sb.AppendLine($"ActiveTreatments count: {ctx.Data.StressState.ActiveTreatments.Count}");
            sb.AppendLine($"Active untreated: {string.Join(", ", untreated.DefaultIfEmpty("(none)"))}");
            sb.AppendLine();

            AppendSection(sb, "Game", AppendGameSection);
            AppendSection(sb, "Player", AppendPlayerSection);
            AppendSection(sb, "Harvey", ctx, AppendHarveySection);
            AppendSection(sb, "StressLoad", ctx, AppendStressLoadSection);
            AppendSection(sb, "Debuffs", ctx, AppendDebuffsSection);
            AppendSection(sb, "Treatments", ctx, AppendTreatmentsSection);
            AppendSection(sb, "Quests", AppendQuestsSection);
            AppendSection(sb, "Topics", AppendTopicsSection);
            AppendSection(sb, "GotoroRescue", ctx, AppendGotoroRescueSection);
            AppendSummaryFlags(sb, ctx, untreated);

            sb.AppendLine();
            sb.AppendLine("--- Legacy ---");
            try
            {
                var dialogueReporter = new StressDialogueStateReporter(
                    ctx.Data, ctx.StateService, ctx.StressDialogueService, ctx.Monitor);
                sb.Append(dialogueReporter.BuildReport());
            }
            catch (Exception ex)
            {
                sb.AppendLine("[StressDialogueState] (unavailable)");
                sb.AppendLine($"error: {ex.Message}");
            }

            return sb.ToString().TrimEnd();
        }

        private static void AppendSection(StringBuilder sb, string title, Action<StringBuilder> write)
        {
            sb.AppendLine($"# {title}");
            try
            {
                write(sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(unavailable: {ex.Message})");
            }

            sb.AppendLine();
        }

        private static void AppendSection(StringBuilder sb, string title, StressDebugDumpContext ctx, Action<StringBuilder, StressDebugDumpContext> write)
        {
            sb.AppendLine($"# {title}");
            try
            {
                write(sb, ctx);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(unavailable: {ex.Message})");
            }

            sb.AppendLine();
        }

        private static void AppendGameSection(StringBuilder sb)
        {
            if (!Context.IsWorldReady)
            {
                sb.AppendLine("WorldReady: false");
                sb.AppendLine("SaveName: (unavailable)");
                return;
            }

            sb.AppendLine($"WorldReady: {Context.IsWorldReady}");
            sb.AppendLine($"SaveName: {SafeString(() => Game1.player.farmName.Value)}");
            sb.AppendLine($"Date: {SDate.Now()}");
            sb.AppendLine($"Time: {Game1.timeOfDay}");
            sb.AppendLine($"Location: {Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? "(null)"}");
            sb.AppendLine($"Tile: ({Game1.player.TilePoint.X}, {Game1.player.TilePoint.Y})");
            sb.AppendLine($"Weather: {DescribeWeather()}");
            sb.AppendLine($"IsRaining: {Game1.isRaining}");
            sb.AppendLine($"IsLightning: {Game1.isLightning}");
            sb.AppendLine($"EventActive: {GameStateHelper.IsEventActive()}");
            sb.AppendLine($"EventId: {GameStateHelper.GetCurrentEventId() ?? "(none)"}");
            sb.AppendLine($"CurrentMenu: {Game1.activeClickableMenu?.GetType().Name ?? "(none)"}");

            var dialogueBox = HarveyDevTalkHelper.GetDialogueBoxSnapshot();
            sb.AppendLine($"DialogueBoxActive: {dialogueBox.HasDialogueBox || Game1.dialogueUp}");
        }

        private static void AppendPlayerSection(StringBuilder sb)
        {
            if (!Context.IsWorldReady)
            {
                sb.AppendLine("Health: (unavailable)");
                sb.AppendLine("Stamina: (unavailable)");
                sb.AppendLine("InventoryStressItems: (unavailable)");
                return;
            }

            sb.AppendLine($"Health: {Game1.player.health}/{Game1.player.maxHealth}");
            sb.AppendLine($"Stamina: {Game1.player.Stamina:0.#}/{Game1.player.MaxStamina:0.#}");
            sb.AppendLine($"InventoryStressItems: {DescribeInventoryStressItems()}");
        }

        private static void AppendHarveySection(StringBuilder sb, StressDebugDumpContext ctx)
        {
            var harvey = Game1.getCharacterFromName("Harvey");
            var friendshipPoints = Game1.player.friendshipData.TryGetValue("Harvey", out var friendship)
                ? friendship.Points
                : 0;

            sb.AppendLine($"FriendshipPoints: {friendshipPoints}");
            sb.AppendLine($"Hearts: {HarveyFriendshipHelper.GetHarveyHearts()}");
            sb.AppendLine($"RelationshipStatus: {DescribeRelationshipStatus()}");
            sb.AppendLine($"HarveyLocation: {harvey?.currentLocation?.NameOrUniqueName ?? harvey?.currentLocation?.Name ?? "(not loaded)"}");
            sb.AppendLine($"DistanceToPlayer: {FormatHarveyDistance(harvey)}");

            if (ctx.TrustService == null)
            {
                sb.AppendLine("TrustPoints: (unavailable)");
                sb.AppendLine("RawTrustLevel: (unavailable)");
                sb.AppendLine("EffectiveTrustLevel: (unavailable)");
                sb.AppendLine("SafePersonUnlocked: (unavailable)");
                sb.AppendLine("ForestRescueUnlocked: (unavailable)");
                return;
            }

            sb.AppendLine($"TrustPoints: {ctx.TrustService.State.TrustPoints}");
            sb.AppendLine($"RawTrustLevel: {ctx.TrustService.GetRawTrustLevel()}");
            sb.AppendLine($"EffectiveTrustLevel: {ctx.TrustService.GetTrustLevel()}");
            sb.AppendLine($"SafePersonUnlocked: {ctx.TrustService.IsHarveySafePersonUnlocked()}");
            sb.AppendLine($"ForestRescueUnlocked: {ctx.TrustService.State.ForestRescueUnlocked}");
        }

        private static void AppendStressLoadSection(StringBuilder sb, StressDebugDumpContext ctx)
        {
            if (ctx.StressLoadService == null)
            {
                sb.AppendLine("(unavailable)");
                return;
            }

            var load = ctx.StressLoadService;
            var active = load.GetActiveCauses().Values.Where(c => c.IsActive).ToList();
            var activeCount = active.Count;
            var baseWeightSum = active.Sum(c => c.Weight);
            var multiCauseBonus = activeCount >= 5 ? 20 : activeCount >= 3 ? 10 : 0;
            var weatherBonus = active.Any(c => c.CauseId == StressCauses.Thunder) && Game1.isLightning ? 15 : 0;
            var gotoroBonus = ctx.Data.StressLoad.GotoroFlashbackActive
                              || active.Any(c => c.CauseId == StressCauses.GotoroFlashback)
                ? 50
                : 0;

            sb.AppendLine($"CauseLoad: {load.GetCauseLoad()}");
            sb.AppendLine($"StressRecoveryOffset: {load.GetStressRecoveryOffset()}");
            sb.AppendLine($"CurrentStressLoad: {load.GetCurrentStressLoad()}");
            sb.AppendLine($"LastCauseLoad: {ctx.Data.StressLoad.LastCauseLoad}");
            sb.AppendLine($"Severity: {load.GetSeverity()}");
            sb.AppendLine($"ActiveCauses: {activeCount}");
            sb.AppendLine($"PrimaryCause: {load.GetPrimaryCause() ?? "(none)"}");
            sb.AppendLine($"CandidateEpisode: {load.GetCandidateEpisode() ?? "(none)"}");
            sb.AppendLine($"GotoroFlashbackActive: {ctx.Data.StressLoad.GotoroFlashbackActive}");
            sb.AppendLine($"Multipliers/Bonuses: baseWeightSum={baseWeightSum}, multiCauseBonus={multiCauseBonus}, weatherBonus={weatherBonus}, gotoroBonus={gotoroBonus}");

            if (active.Count == 0)
            {
                sb.AppendLine("ActiveCausesList: (none)");
                return;
            }

            sb.AppendLine("ActiveCausesList:");
            foreach (var cause in active.OrderByDescending(c => c.Weight))
            {
                sb.AppendLine(
                    $"  - {cause.CauseId}: weight={cause.Weight}, buff={cause.SourceBuffId ?? "(none)"}, severe={cause.IsSevere}");
            }
        }

        private static void AppendDebuffsSection(StringBuilder sb, StressDebugDumpContext ctx)
        {
            var untreated = StressDebuffSelector.GetUntreatedDebuffs(ctx.StateService, ctx.Data);

            foreach (var buffId in TreatmentTopics.ImplementedBuffIds)
            {
                StressCauses.TryGetCauseForBuff(buffId, out var causeId);
                var stressTopicId = GetStressTopicId(buffId);
                var followupTopic = TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followup) ? followup : null;
                var legacyStartTopic = TreatmentTopics.LegacyStartByBuff.TryGetValue(buffId, out var legacy) ? legacy : null;
                var reviewTopic = TreatmentTopics.GetReadyForReviewTopic(buffId);
                var treatment = ctx.StateService.GetActiveTreatment(buffId);
                var topicStarted = (followupTopic != null && ConversationHelper.HasTopic(followupTopic))
                                   || (legacyStartTopic != null && ConversationHelper.HasTopic(legacyStartTopic))
                                   || (treatment?.TreatmentStarted ?? false);

                sb.AppendLine($"- buffId: {buffId}");
                sb.AppendLine($"  activeInGame: {ctx.StateService.HasBuffInGame(buffId)}");
                sb.AppendLine($"  activeTreatmentState: {ctx.StateService.HasActiveTreatmentState(buffId)}");
                sb.AppendLine($"  untreated: {untreated.Contains(buffId)}");
                sb.AppendLine($"  cause: {causeId ?? "(none)"}");
                sb.AppendLine($"  stressTopic: {stressTopicId ?? "(n/a)"} active={stressTopicId != null && ConversationHelper.HasTopic(stressTopicId)}");
                sb.AppendLine($"  topicStarted: {topicStarted}");
                sb.AppendLine($"  topicReadyForReview: {reviewTopic != null && ConversationHelper.HasTopic(reviewTopic)}");
            }

            sb.AppendLine("- darkness (level system, separate from ActiveTreatment):");
            var d = ctx.Data.Darkness;
            var levelBuff = DarknessLegacyHelper.GetActiveLevelBuffId(ctx.StateService)
                ?? (ctx.StateService.HasBuffInGame(BuffIds.Darkness) ? BuffIds.Darkness : "(none)");
            sb.AppendLine($"  fearLevel: {d.FearLevel}, gameBuff: {levelBuff}");
            sb.AppendLine($"  therapy: {d.IsTherapyActive}, stage: {d.TherapyStage}, usesLevelSystem: {DarknessLegacyHelper.UsesLevelSystem(ctx.Data, ctx.StateService)}");
            if (d.IsTherapyActive && d.TherapyStage >= 1)
            {
                sb.AppendLine(
                    $"  stepQuest: {DarknessLegacyHelper.GetStepQuestIdForStage(d.TherapyStage)} inJournal={DarknessLegacyHelper.HasStepQuestInJournal(d.TherapyStage)}");
                sb.AppendLine(
                    $"  step1: evenings {d.SafeDarknessEveningsCompleted}/{DarknessLegacyHelper.Step1EveningsRequired} today {d.SafeDarknessProgressToday}/{DarknessLegacyHelper.Step1MinutesPerEvening}");
            }

            foreach (var levelId in DarknessLegacyHelper.LevelBuffIds)
            {
                if (!untreated.Contains(levelId))
                    continue;

                sb.AppendLine($"  untreatedLevelBuff: {levelId}");
            }
        }

        private static void AppendTreatmentsSection(StringBuilder sb, StressDebugDumpContext ctx)
        {
            var dialogue = ctx.StressDialogueService.GetDebugSnapshot();
            var episode = ctx.Data.ActiveTreatmentEpisode;

            sb.AppendLine($"ActiveTreatments count: {ctx.Data.StressState.ActiveTreatments.Count}");
            sb.AppendLine($"PendingTreatment buffId: {dialogue.PendingAutoStartBuffId ?? "(none)"}");
            sb.AppendLine($"PendingTreatment episodeId: {dialogue.PendingAutoStartEpisodeId ?? "(none)"}");
            sb.AppendLine($"CurrentEpisode: {episode?.EpisodeId ?? "(none)"}");
            sb.AppendLine($"IsShowingStressDialogue: {dialogue.IsShowingStressDialogue}");
            sb.AppendLine($"HasDeferredStressDialogue: {dialogue.HasDeferredStressDialogue}");

            if (ctx.Data.StressState.ActiveTreatments.Count == 0)
            {
                sb.AppendLine("treatments: (none)");
                return;
            }

            sb.AppendLine("treatments:");
            foreach (var treatment in ctx.Data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
            {
                var hasBuff = ctx.StateService.HasBuffInGame(treatment.BuffId);
                StressCauses.TryGetCauseForBuff(treatment.BuffId, out var causeId);
                var startDate = treatment.TreatmentStartedDate ?? treatment.IssuedDate;
                var daysActive = Math.Max(0, SDate.Now().DaysSinceStart - startDate.DaysSinceStart);
                var reviewTopic = TreatmentTopics.GetReadyForReviewTopic(treatment.BuffId);
                var missedPrescription = hasBuff && !treatment.TreatmentStarted && !treatment.IsCured;
                var ignored = ctx.StateService.WasTreatmentOfferShownToday(treatment.BuffId) && !treatment.TreatmentStarted;

                sb.AppendLine($"  - id: {treatment.TreatmentKey}");
                sb.AppendLine($"    buffId: {treatment.BuffId}");
                sb.AppendLine($"    cause: {causeId ?? "(none)"}");
                sb.AppendLine($"    started: {treatment.TreatmentStarted}");
                sb.AppendLine($"    completed: {treatment.IsCompleted}");
                sb.AppendLine($"    cured: {treatment.IsCured}");
                sb.AppendLine($"    readyForReview: {treatment.AwaitingHarveyReview}");
                sb.AppendLine($"    questId: {treatment.QuestId ?? "(empty)"}");
                sb.AppendLine($"    daysActive: {daysActive}");
                sb.AppendLine($"    ignoredToday: {ignored}");
                sb.AppendLine($"    missedPrescription: {missedPrescription}");
                sb.AppendLine($"    readyForReviewTopic: {reviewTopic ?? "(n/a)"} active={reviewTopic != null && ConversationHelper.HasTopic(reviewTopic)}");
                sb.AppendLine($"    nextStep: {TreatmentNextStep.Resolve(treatment, hasBuff)}");
            }
        }

        private static void AppendQuestsSection(StringBuilder sb)
        {
            if (!Context.IsWorldReady)
            {
                sb.AppendLine("(unavailable)");
                return;
            }

            var active = new List<string>();
            var completed = new List<string>();

            foreach (var questId in KnownStressQuestIds)
            {
                var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                if (quest == null)
                    continue;

                var objective = quest.currentObjective?.Trim();
                var progress = string.IsNullOrEmpty(objective) ? "(no objective text)" : objective;

                if (quest.completed.Value)
                    completed.Add($"{questId} [{progress}]");
                else
                    active.Add($"{questId} [{progress}]");
            }

            foreach (var quest in Game1.player.questLog)
            {
                var questId = quest.id.Value;
                if (KnownStressQuestIds.Contains(questId))
                    continue;

                if (!StressQuestIdPrefixes.Any(p => questId.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var objective = quest.currentObjective?.Trim();
                var progress = string.IsNullOrEmpty(objective) ? "(no objective text)" : objective;

                if (quest.completed.Value)
                    completed.Add($"{questId} [{progress}]");
                else
                    active.Add($"{questId} [{progress}]");
            }

            sb.AppendLine($"activeStressQuests: {(active.Count > 0 ? string.Join("; ", active) : "(none)")}");
            sb.AppendLine($"completedStressQuests: {(completed.Count > 0 ? string.Join("; ", completed) : "(none)")}");
        }

        private static void AppendTopicsSection(StringBuilder sb)
        {
            if (!Context.IsWorldReady)
            {
                sb.AppendLine("(unavailable)");
                return;
            }

            var topics = Game1.player.activeDialogueEvents.Keys
                .Where(IsDumpTopic)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (topics.Count == 0)
            {
                sb.AppendLine("(none)");
                return;
            }

            foreach (var topicId in topics)
            {
                var daysLeft = Game1.player.activeDialogueEvents.TryGetValue(topicId, out var days)
                    ? days.ToString()
                    : "(unavailable)";
                sb.AppendLine($"- {topicId}: daysLeft={daysLeft}");
            }
        }

        private static void AppendGotoroRescueSection(StringBuilder sb, StressDebugDumpContext ctx)
        {
            if (ctx.RescueService == null)
            {
                sb.AppendLine("(unavailable)");
                return;
            }

            var eval = ctx.RescueService.EvaluateRescueWithRoll(ignoreChance: true).Evaluation;
            var rescueSnapshot = ctx.RescueService.BuildMcpSnapshot();
            sb.AppendLine($"FlashbackActive: {ctx.Data.StressLoad.GotoroFlashbackActive}");
            sb.AppendLine($"PendingRescueTopic: {ConversationHelper.HasTopic(TopicIds.GotoroForestRescuePending)}");
            sb.AppendLine($"CanAttempt: {eval.CanAttempt}");
            sb.AppendLine($"BlockReason: {eval.BlockReason ?? "(none)"}");
            sb.AppendLine($"CandidateTier: {eval.Tier ?? "(none)"}");
            sb.AppendLine($"Chance: {eval.RescueChance:0.###}");
            sb.AppendLine($"CooldownElapsed: {ExtractLine(rescueSnapshot, "CooldownElapsed") ?? "(unavailable)"}");
            sb.AppendLine($"CooldownDays: {ExtractLine(rescueSnapshot, "CooldownDays") ?? "(unavailable)"}");
            sb.AppendLine($"LastRescueDay: {ctx.RescueService.State.LastRescueDay}");
        }

        private static void AppendSummaryFlags(StringBuilder sb, StressDebugDumpContext ctx, IReadOnlyList<string> untreated)
        {
            var flags = new List<string>();
            var dialogue = ctx.StressDialogueService.GetDebugSnapshot();
            var load = ctx.StressLoadService?.GetCurrentStressLoad() ?? 0;
            var hasActiveDebuff = TreatmentTopics.ImplementedBuffIds.Any(ctx.StateService.HasBuffInGame);
            var hasActiveTreatment = ctx.Data.StressState.ActiveTreatments.Values.Any(t =>
                t.TreatmentStarted && !t.IsCured && !t.IsCompleted);

            if (load > 0 || hasActiveDebuff || ctx.Data.StressLoad.GotoroFlashbackActive)
                flags.Add("HAS_ACTIVE_STRESS");

            if (untreated.Count > 0)
                flags.Add("HAS_UNTREATED_DEBUFF");

            if (hasActiveTreatment)
                flags.Add("HAS_ACTIVE_TREATMENT");

            if (!string.IsNullOrEmpty(dialogue.PendingAutoStartBuffId)
                || !string.IsNullOrEmpty(dialogue.PendingAutoStartEpisodeId)
                || dialogue.HasDeferredStressDialogue
                || dialogue.IsShowingStressDialogue
                || Game1.dialogueUp)
            {
                flags.Add("HAS_PENDING_DIALOGUE");
            }

            if (GameStateHelper.IsEventActive())
                flags.Add("EVENT_ACTIVE");

            if (ctx.Data.StressLoad.GotoroFlashbackActive
                || ConversationHelper.HasTopic(TopicIds.GotoroFlashbackActive))
            {
                flags.Add("GOTORO_FLASHBACK_ACTIVE");
            }

            if (ConversationHelper.HasTopic(TopicIds.GotoroForestRescuePending))
                flags.Add("RESCUE_PENDING");

            if (ctx.SafeAuraService != null)
            {
                try
                {
                    if (ctx.SafeAuraService.EvaluateProximity().SafeAuraActive)
                        flags.Add("SAFE_AURA_ACTIVE");
                }
                catch
                {
                    // ignore — flag omitted
                }
            }

            sb.AppendLine("# SummaryFlags");
            sb.AppendLine(flags.Count > 0 ? string.Join(", ", flags) : "(none)");
        }

        private static bool IsDumpTopic(string key)
        {
            return key.StartsWith("topicStress", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("HarveyStress", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("hs.", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetStressTopicId(string buffId)
        {
            if (StressDebuffSelector.BuffToStressTopic.TryGetValue(buffId, out var stressPair))
                return stressPair.topic;

            if (buffId == BuffIds.Darkness || DarknessLegacyHelper.IsDarknessLevelBuff(buffId))
                return TopicIds.StressDarkness;

            return null;
        }

        private static string DescribeWeather()
        {
            if (Game1.isLightning)
                return "Storm";

            if (Game1.isSnowing)
                return "Snow";

            if (Game1.isRaining)
                return "Rain";

            if (Game1.isDebrisWeather)
                return "Wind";

            return "Sun";
        }

        private static string DescribeRelationshipStatus()
        {
            if (HarveyFriendshipHelper.IsMarriedToHarvey())
                return "Married";

            if (HarveyFriendshipHelper.IsDatingHarvey())
                return "Dating";

            return "none";
        }

        private static string FormatHarveyDistance(NPC? harvey)
        {
            if (harvey == null)
                return "(not loaded)";

            if (harvey.currentLocation != Game1.currentLocation)
                return "(different location)";

            var dx = Game1.player.TilePoint.X - harvey.TilePoint.X;
            var dy = Game1.player.TilePoint.Y - harvey.TilePoint.Y;
            return $"{Math.Sqrt(dx * dx + dy * dy):0.#} tiles";
        }

        private static string DescribeInventoryStressItems()
        {
            var edible = new List<string>();

            foreach (var item in Game1.player.Items)
            {
                if (item == null)
                    continue;

                if (item is StardewValley.Object obj && obj.Edibility > 0)
                {
                    edible.Add($"{obj.DisplayName}x{obj.Stack}");
                    if (edible.Count >= 5)
                        break;
                }
            }

            if (edible.Count == 0)
                return "edibleItems=(none)";

            return $"edibleItems=[{string.Join(", ", edible)}], edibleCount={Game1.player.Items.Count(i => i is StardewValley.Object o && o.Edibility > 0)}";
        }

        private static string? ExtractLine(string text, string key)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    return line[(key.Length + 1)..].Trim();
            }

            return null;
        }

        private static string SafeString(Func<string> getValue)
        {
            try
            {
                var value = getValue();
                return string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
            }
            catch
            {
                return "(unavailable)";
            }
        }

        private static T SafeGet<T>(Func<T> getValue, T fallback)
        {
            try
            {
                return getValue();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
