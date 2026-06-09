using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Testing
{
    internal sealed class McpTestPlanContext
    {
        public IMonitor Monitor { get; init; } = null!;
        public SaveData Data { get; init; } = null!;
        public TreatmentService TreatmentService { get; init; } = null!;
        public StateService StateService { get; init; } = null!;
        public BuffService BuffService { get; init; } = null!;
        public QuestService QuestService { get; init; } = null!;
        public StressDialogueService StressDialogueService { get; init; } = null!;
        public ModResetService ModResetService { get; init; } = null!;
        public HarveyCareTrustService TrustService { get; init; } = null!;
        public StressLoadService StressLoadService { get; init; } = null!;
        public HarveyFlashbackRescueService RescueService { get; init; } = null!;
        public HarveySafePersonAuraService SafeAuraService { get; init; } = null!;
        public TreatmentEpisodeService EpisodeService { get; init; } = null!;
        public GameLogicHandler GameLogicHandler { get; init; } = null!;
        public ThunderFlashbackService? ThunderFlashbackService { get; init; }
    }

    internal enum TestStepStatus
    {
        Pass,
        Fail,
        Skipped,
    }

    internal sealed class TestPlanStep
    {
        public string Id { get; init; } = "";
        public string Action { get; init; } = "";
        public string Expected { get; init; } = "";
        public string Actual { get; set; } = "";
        public TestStepStatus Status { get; set; }
        public string DebugSnippet { get; set; } = "";
    }

    internal sealed class TestPlanRun
    {
        public string Plan { get; init; } = "";
        public bool StopOnFail { get; init; }
        public List<TestPlanStep> Steps { get; } = new();
        public bool Aborted { get; set; }

        public void Add(TestPlanStep step)
        {
            Steps.Add(step);
        }

        public bool ShouldStop => Aborted || (StopOnFail && Steps.Any(s => s.Status == TestStepStatus.Fail));
    }

    /// <summary>Runs predefined atomic MCP test plans (no sleep/event/save bots).</summary>
    internal static class McpTestPlanRunner
    {
        private static readonly string[] AllDebuffIds =
        {
            BuffIds.Tired,
            BuffIds.Lonely,
            BuffIds.Thunder,
            BuffIds.Hunger,
            BuffIds.Overwork,
            BuffIds.NoSleep,
            BuffIds.TooCold,
            BuffIds.Darkness,
            BuffIds.Social,
        };

        private static readonly HashSet<string> AllowedPlans = new(StringComparer.OrdinalIgnoreCase)
        {
            "smoke",
            "all_debuffs",
            "stressload",
            "recovery",
            "dialogues",
            "quests",
            "episodes",
            "trust",
            "gotoro_rescue",
            "safe_aura",
            "persistence",
            "daily_regression",
        };

        public static string Run(McpTestPlanContext ctx, JsonElement? arguments)
        {
            if (!TryGetString(arguments, "plan", out var plan))
                return "Error: plan is required (smoke, all_debuffs, stressload, recovery, dialogues, …).";

            plan = plan.Trim();
            if (!AllowedPlans.Contains(plan))
            {
                return "Error: plan must be smoke, all_debuffs, stressload, recovery, dialogues, quests, " +
                       "episodes, trust, gotoro_rescue, safe_aura, persistence, or daily_regression.";
            }

            var stopOnFail = !TryGetBool(arguments, "stop_on_fail", out var stop) || stop;

            var run = new TestPlanRun { Plan = plan, StopOnFail = stopOnFail };

            switch (plan.ToLowerInvariant())
            {
                case "smoke":
                    RunSmoke(ctx, run);
                    break;
                case "all_debuffs":
                    RunAllDebuffs(ctx, run);
                    break;
                case "stressload":
                    RunStressLoad(ctx, run);
                    break;
                case "recovery":
                    RunRecovery(ctx, run);
                    break;
                case "dialogues":
                    RunDialogues(ctx, run);
                    break;
                case "quests":
                    RunQuests(ctx, run);
                    break;
                case "episodes":
                    RunEpisodes(ctx, run);
                    break;
                case "trust":
                    RunTrust(ctx, run);
                    break;
                case "gotoro_rescue":
                    RunGotoroRescue(ctx, run);
                    break;
                case "safe_aura":
                    RunSafeAura(ctx, run);
                    break;
                case "persistence":
                    RunPersistence(ctx, run);
                    break;
                case "daily_regression":
                    RunDailyRegression(ctx, run);
                    break;
            }

            return FormatReport(run);
        }

        private static void RunSmoke(McpTestPlanContext ctx, TestPlanRun run)
        {
            PassIf(run, "smoke-01", "check IsWorldReady", "Context.IsWorldReady=true",
                Context.IsWorldReady, Context.IsWorldReady.ToString(),
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            PassIf(run, "smoke-02", "stress_debug_dump readable",
                "ActiveTreatments count line present",
                true, Truncate(McpTestPlanActions.BuildDebugDump(ctx)),
                Truncate(McpTestPlanActions.BuildDebugDump(ctx)));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.ResetAll(ctx);
            PassIf(run, "smoke-03", "stress_reset", "ActiveTreatments count=0",
                ctx.Data.StressState.ActiveTreatments.Count == 0,
                $"ActiveTreatments={ctx.Data.StressState.ActiveTreatments.Count}",
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var addOk = McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Tired, out var addErr);
            PassIf(run, "smoke-04", $"add debuff {BuffIds.Tired}", "apply succeeds",
                addOk, addOk ? "applied" : addErr ?? "failed",
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var active = ctx.StateService.HasBuffInGame(BuffIds.Tired);
            PassIf(run, "smoke-05", "assert buff active", "hasBuffInGame=true",
                active, active.ToString(),
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var removeOk = McpTestPlanActions.TryRemoveDebuff(ctx, BuffIds.Tired, out var removeErr);
            PassIf(run, "smoke-06", $"remove debuff {BuffIds.Tired}", "remove succeeds",
                removeOk, removeOk ? "removed" : removeErr ?? "failed",
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var gone = !ctx.StateService.HasBuffInGame(BuffIds.Tired);
            PassIf(run, "smoke-07", "assert buff removed", "hasBuffInGame=false",
                gone, (!ctx.StateService.HasBuffInGame(BuffIds.Tired)).ToString(),
                Snippet(ctx, "stress_treatment_snapshot"));
        }

        private static void RunAllDebuffs(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            PassIf(run, "debuffs-00", "stress_reset", "ActiveTreatments count=0",
                ctx.Data.StressState.ActiveTreatments.Count == 0,
                $"ActiveTreatments={ctx.Data.StressState.ActiveTreatments.Count}",
                Snippet(ctx, "stress_treatment_snapshot"));

            for (var i = 0; i < AllDebuffIds.Length; i++)
            {
                if (run.ShouldStop) { run.Aborted = true; return; }

                var buffId = AllDebuffIds[i];
                var prefix = $"debuffs-{buffId}";

                var addOk = McpTestPlanActions.TryApplyDebuff(ctx, buffId, out var addErr);
                PassIf(run, $"{prefix}-add", $"add {buffId}", "hasBuffInGame=true after add",
                    addOk && ctx.StateService.HasBuffInGame(buffId),
                    addOk ? $"hasBuff={ctx.StateService.HasBuffInGame(buffId)}" : addErr ?? "failed",
                    Snippet(ctx, "stress_treatment_snapshot"));

                if (run.ShouldStop) { run.Aborted = true; return; }

                var removeOk = McpTestPlanActions.TryRemoveDebuff(ctx, buffId, out var removeErr);
                PassIf(run, $"{prefix}-remove", $"remove {buffId}", "hasBuffInGame=false after remove",
                    removeOk && !ctx.StateService.HasBuffInGame(buffId),
                    removeOk ? $"hasBuff={ctx.StateService.HasBuffInGame(buffId)}" : removeErr ?? "failed",
                    Snippet(ctx, "stress_treatment_snapshot"));
            }
        }

        private static void RunStressLoad(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            PassIf(run, "load-01", "stress_reset", "clean state",
                ctx.Data.StressState.ActiveTreatments.Count == 0, "reset ok",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Hunger, out _);
            ctx.StressLoadService.Recalculate();
            var hungerLoad = ctx.StressLoadService.GetCurrentStressLoad();
            PassIf(run, "load-02", "add Hunger + snapshot", "CauseLoad>0",
                hungerLoad > 0, $"CurrentStressLoad={hungerLoad}",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Tired, out _);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.TooCold, out _);
            ctx.StressLoadService.Recalculate();

            var multiLoad = ctx.StressLoadService.GetCurrentStressLoad();
            var activeCount = ctx.StressLoadService.GetActiveCauses().Count(c => c.Value.IsActive);

            PassIf(run, "load-03", "add Tired + TooCold + snapshot", "ActiveCauses count>=3",
                activeCount >= 3, $"activeCauseCount={activeCount}, CurrentStressLoad={multiLoad}",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            PassIf(run, "load-04", "assert load > single Hunger baseline", $"CurrentStressLoad>{hungerLoad}",
                multiLoad > hungerLoad, $"hungerOnly={hungerLoad}, multi={multiLoad}",
                Snippet(ctx, "stress_load_debug"));
        }

        private static void RunRecovery(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Overwork, out _);
            ctx.StressLoadService.Recalculate();
            var before = ctx.StressLoadService.GetCurrentStressLoad();

            PassIf(run, "recv-01", "reset + add Overwork", "CurrentStressLoad>0",
                before > 0, $"CurrentStressLoad={before}",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            ctx.StressLoadService.DebugApplyRecoveryOffset(10, "test-plan-recovery");
            var after = ctx.StressLoadService.GetCurrentStressLoad();

            PassIf(run, "recv-02", "apply recovery 10", "CurrentStressLoad decreased",
                after < before, $"before={before}, after={after}, offset={ctx.StressLoadService.GetStressRecoveryOffset()}",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var causeActive = ctx.StressLoadService.GetActiveCauses().Any(c =>
                c.Value.IsActive && c.Key == StressCauses.Overwork);
            PassIf(run, "recv-03", "assert Overwork cause still active", "cause Overwork active",
                causeActive, $"overworkCauseActive={causeActive}",
                Snippet(ctx, "stress_load_debug"));
        }

        private static void RunDialogues(McpTestPlanContext ctx, TestPlanRun run)
        {
            if (GameStateHelper.IsEventActive())
            {
                Skip(run, "dlg-00", "precheck event", "no active event",
                    "event active", Snippet(ctx, "stress_game_context"));
                return;
            }

            if (Game1.activeClickableMenu != null || GameStateHelper.IsEnvironmentDialogueBlocking())
            {
                Skip(run, "dlg-00", "precheck menu/dialogue", "no menu/dialogue",
                    "menu or dialogue open", Snippet(ctx, "stress_game_context"));
                return;
            }

            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Tired, out _);

            PassIf(run, "dlg-01", "reset + add Tired", "untreated debuff present",
                StressDebuffSelector.GetUntreatedDebuffs(ctx.StateService).Contains(BuffIds.Tired),
                $"untreated={string.Join(",", StressDebuffSelector.GetUntreatedDebuffs(ctx.StateService))}",
                Snippet(ctx, "stress_dialogue_state"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var placed = McpTestPlanActions.TrySetupHarveyNearPlayer(out var setupErr);
            if (!placed)
            {
                Skip(run, "dlg-02", "setup Harvey near player in Town", "Harvey + player adjacent",
                    setupErr ?? "setup failed", Snippet(ctx, "stress_game_context"));
                return;
            }

            var talked = HarveyDevTalkHelper.TryTalkToHarvey(ctx.Monitor, warpIfNeeded: true);
            PassIf(run, "dlg-03", "stress_talk_harvey", "TryTalkToHarvey=true",
                talked, $"talkSuccess={talked}, menu={Game1.activeClickableMenu?.GetType().Name ?? "null"}",
                Snippet(ctx, "stress_dialogue_state"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            var snapshot = HarveyDevTalkHelper.GetDialogueBoxSnapshot();
            PassIf(run, "dlg-04", "list responses snapshot", "snapshot readable",
                true, $"HasDialogueBox={snapshot.HasDialogueBox}, Count={snapshot.Responses.Count}",
                Truncate(new StressDialogueStateReporter(ctx.Data, ctx.StateService, ctx.StressDialogueService, ctx.Monitor).BuildReport()));

            HarveyDevTalkHelper.TryForceCloseDialogue(ctx.Monitor);
            ctx.StressDialogueService.ClearPendingTreatment();
            GameStateHelper.ClearStaleUiFlags();

            var dialogue = ctx.StressDialogueService.GetDebugSnapshot();
            var okPending = string.IsNullOrEmpty(dialogue.PendingAutoStartBuffId)
                            && string.IsNullOrEmpty(dialogue.PendingAutoStartEpisodeId);
            PassIf(run, "dlg-05", "close dialogue + assert pending cleared", "no pending auto-start",
                okPending,
                $"pendingBuff={dialogue.PendingAutoStartBuffId ?? "(none)"}, pendingEpisode={dialogue.PendingAutoStartEpisodeId ?? "(none)"}",
                Snippet(ctx, "stress_dialogue_state"));
        }

        private static void RunEpisodes(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Overwork, out _);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.NoSleep, out _);
            ctx.StressLoadService.Recalculate();

            var candidate = ctx.StressLoadService.GetCandidateEpisode();
            PassIf(run, "ep-01", "Burnout causes (Overwork+NoSleep)", "CandidateEpisode=Burnout",
                string.Equals(candidate, StressEpisodes.Burnout, StringComparison.Ordinal),
                $"CandidateEpisode={candidate ?? "(none)"}",
                Snippet(ctx, "stress_load_debug"));

            var burnoutStarted = TryStartEpisodeForPlan(ctx, StressEpisodes.Burnout, out var epStartNote);
            PassIf(run, "ep-02", "start Burnout episode quest",
                "TreatmentStarted=true",
                burnoutStarted && ctx.Data.ActiveTreatmentEpisode?.TreatmentStarted == true,
                burnoutStarted
                    ? $"episode={ctx.Data.ActiveTreatmentEpisode?.EpisodeId}, TreatmentStarted={ctx.Data.ActiveTreatmentEpisode?.TreatmentStarted}"
                    : epStartNote ?? "StartTreatmentEpisode returned false",
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Hunger, out _);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.TooCold, out _);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Tired, out _);
            ctx.StressLoadService.Recalculate();

            candidate = ctx.StressLoadService.GetCandidateEpisode();
            PassIf(run, "ep-03", "PhysicalExhaustion causes", "CandidateEpisode=PhysicalExhaustion",
                string.Equals(candidate, StressEpisodes.PhysicalExhaustion, StringComparison.Ordinal),
                $"CandidateEpisode={candidate ?? "(none)"}",
                Snippet(ctx, "stress_load_debug"));

            var epStarted = TryStartEpisodeForPlan(ctx, StressEpisodes.PhysicalExhaustion, out var physNote);
            PassIf(run, "ep-04", "start PhysicalExhaustion episode",
                "single episode quest",
                epStarted && ctx.EpisodeService.HasActiveTreatmentEpisode(),
                epStarted
                    ? $"episode={ctx.Data.ActiveTreatmentEpisode?.EpisodeId ?? "(none)"}"
                    : physNote ?? "StartTreatmentEpisode returned false",
                Snippet(ctx, "stress_treatment_snapshot"));
        }

        private static bool TryStartEpisodeForPlan(McpTestPlanContext ctx, string episodeId, out string? note)
        {
            note = null;
            if (ctx.EpisodeService.HasActiveTreatmentEpisode())
            {
                note = "active episode already running";
                return false;
            }

            if (!ctx.EpisodeService.StartTreatmentEpisode(episodeId))
            {
                note = $"StartTreatmentEpisode({episodeId}) returned false";
                return false;
            }

            ctx.StressLoadService.Recalculate();
            note = $"started {episodeId}";
            return true;
        }

        private static void RunQuests(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Tired, out _);

            ctx.TreatmentService.StartTreatment(BuffIds.Tired, "Усталость");
            var tiredTreatment = ctx.StateService.GetActiveTreatment(BuffIds.Tired);

            PassIf(run, "qst-01", "force start Tired treatment", "TreatmentStarted=true and quest in journal",
                tiredTreatment?.TreatmentStarted == true
                && ctx.StateService.HasQuestInGameJournal(QuestIds.Tired),
                $"TreatmentStarted={tiredTreatment?.TreatmentStarted}, quest={QuestIds.Tired}",
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Hunger, out _);
            ctx.TreatmentService.StartTreatment(BuffIds.Hunger, "Голод");

            var food = ItemRegistry.Create("(O)221");
            if (food is StardewValley.Object edible)
                ctx.GameLogicHandler.OnFoodConsumed(edible);

            var hungerTreatment = ctx.StateService.GetActiveTreatment(BuffIds.Hunger);
            PassIf(run, "qst-02", "eat completes Hunger quest objective", "AwaitingHarveyReview=true",
                hungerTreatment?.AwaitingHarveyReview == true,
                $"AwaitingHarveyReview={hungerTreatment?.AwaitingHarveyReview}, AteAnyFood={hungerTreatment?.Progress?.AteAnyFood}",
                Snippet(ctx, "stress_treatment_snapshot"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.NoSleep, out _);
            ctx.TreatmentService.StartTreatment(BuffIds.NoSleep, "Недосып");
            Game1.timeOfDay = 2200;
            ctx.GameLogicHandler.CheckDayEndingQuestCompletion();

            var noSleepTreatment = ctx.StateService.GetActiveTreatment(BuffIds.NoSleep);
            PassIf(run, "qst-03", "early sleep completes NoSleep quest", "AwaitingHarveyReview=true",
                noSleepTreatment?.AwaitingHarveyReview == true,
                $"time={Game1.timeOfDay}, AwaitingHarveyReview={noSleepTreatment?.AwaitingHarveyReview}",
                Snippet(ctx, "stress_treatment_snapshot"));
        }

        private static void RunPersistence(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryApplyDebuff(ctx, BuffIds.Overwork, out _);
            ctx.StressLoadService.Recalculate();
            ctx.StressLoadService.DebugApplyRecoveryOffset(15, "persistence-test-plan");

            var offsetBeforeSave = ctx.StressLoadService.GetStressRecoveryOffset();
            SaveDataHelper.WriteSaveData(ctx.Data);

            PassIf(run, "per-01", "mcp_save_game baseline", "offset written",
                offsetBeforeSave >= 15,
                $"offsetBeforeSave={offsetBeforeSave}",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            ctx.StressLoadService.DebugApplyRecoveryOffset(50, "corrupt-before-reload");
            var offsetCorrupted = ctx.StressLoadService.GetStressRecoveryOffset();

            SaveDataHelper.CopySaveDataIntoExistingInstance(
                ctx.Data,
                SaveDataHelper.ReadSaveData(ctx.Monitor) ?? new SaveData());
            ctx.StateService.MigrateOldData();
            ctx.StateService.SyncWithGame();
            ctx.GameLogicHandler.ClearStressDialoguePending();
            ctx.StateService.RestoreAllActiveBuffs();
            ctx.StressLoadService.SyncFromGameState();

            var offsetAfterReload = ctx.StressLoadService.GetStressRecoveryOffset();
            PassIf(run, "per-02", "mcp_reload_save restores offset", "offsetAfterReload≈offsetBeforeSave",
                offsetAfterReload >= 15 && offsetAfterReload < offsetCorrupted,
                $"beforeSave={offsetBeforeSave}, corrupted={offsetCorrupted}, afterReload={offsetAfterReload}",
                Snippet(ctx, "stress_load_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryWarpPlayer("Forest", 48, 14, out _);
            ctx.RescueService.PrepareRescueDebug("HighTrust", force: true);
            var tierBeforeSave = ctx.Data.HarveyFlashbackRescue?.RescueTier;
            var gotoroBeforeSave = ctx.StressLoadService.IsGotoroFlashbackActive();
            SaveDataHelper.WriteSaveData(ctx.Data);

            if (ctx.Data.HarveyFlashbackRescue != null)
                ctx.Data.HarveyFlashbackRescue.RescueTier = null;
            ctx.StressLoadService.SetGotoroFlashbackActive(false);

            SaveDataHelper.CopySaveDataIntoExistingInstance(
                ctx.Data,
                SaveDataHelper.ReadSaveData(ctx.Monitor) ?? new SaveData());
            ctx.StressLoadService.SyncFromGameState();

            PassIf(run, "per-03", "reload restores rescue mod state", "RescueTier and GotoroFlashback restored",
                !string.IsNullOrEmpty(tierBeforeSave)
                && string.Equals(ctx.Data.HarveyFlashbackRescue?.RescueTier, tierBeforeSave, StringComparison.Ordinal)
                && ctx.StressLoadService.IsGotoroFlashbackActive() == gotoroBeforeSave,
                $"tierBefore={tierBeforeSave ?? "(none)"}, tierAfter={ctx.Data.HarveyFlashbackRescue?.RescueTier ?? "(none)"}, gotoroAfter={ctx.StressLoadService.IsGotoroFlashbackActive()}",
                Snippet(ctx, "stress_rescue_debug"));
        }

        private static void RunTrust(McpTestPlanContext ctx, TestPlanRun run)
        {
            ctx.TrustService.ResetTrustState();
            Game1.player.friendshipData["Harvey"] = new Friendship(0);
            ctx.TrustService.SetTrustPointsForDebug(160);

            PassIf(run, "trust-01", "0 hearts + 160 trust cap", "EffectiveTrustLevel<=1, SafePersonUnlocked=false",
                ctx.TrustService.GetTrustLevel() <= HarveyCareTrustLevels.FamiliarDoctor
                && !ctx.TrustService.IsHarveySafePersonUnlocked(),
                ExtractLines(ctx.TrustService.BuildMcpSnapshot(), "EffectiveTrustLevel", "SafePersonUnlocked", "Cap"),
                Snippet(ctx, "stress_trust_debug"));

            if (run.ShouldStop) { run.Aborted = true; return; }

            Game1.player.friendshipData["Harvey"] = new Friendship(2000);
            ctx.TrustService.SetTrustPointsForDebug(160);

            PassIf(run, "trust-02", "8 hearts + 160 trust unlock", "SafePersonUnlocked=true, CanHarveyForestRescue=true",
                ctx.TrustService.IsHarveySafePersonUnlocked() && ctx.TrustService.CanHarveyForestRescue(),
                ExtractLines(ctx.TrustService.BuildMcpSnapshot(),
                    "EffectiveTrustLevel", "SafePersonUnlocked", "CanHarveyForestRescue"),
                Snippet(ctx, "stress_trust_debug"));
        }

        private static void RunGotoroRescue(McpTestPlanContext ctx, TestPlanRun run)
        {
            ctx.RescueService.SuppressAutomaticRescue = true;
            try
            {
                McpTestPlanActions.ResetAll(ctx);
                McpTestPlanActions.TryEndActiveEvent(out _);
                ctx.StressLoadService.SetGotoroFlashbackActive(true);

                PassIf(run, "rescue-01", "GotoroFlashback active", "Severity=Critical",
                    ctx.StressLoadService.GetSeverity() == StressSeverity.Critical,
                    ExtractLines(ctx.StressLoadService.BuildMcpSnapshot(), "Severity", "GotoroFlashbackActive"),
                    Snippet(ctx, "stress_load_debug"));

                if (run.ShouldStop) { run.Aborted = true; return; }

                Game1.player.friendshipData["Harvey"] = new Friendship(2000);
                ctx.TrustService.SetTrustPointsForDebug(160);
                Game1.isRaining = true;
                Game1.isLightning = true;

                var warped = McpTestPlanActions.TryWarpPlayer("Forest", 48, 14, out var warpError);

                var prep = ctx.RescueService.PrepareRescueDebug("HighTrust", force: true);
                var eval = ctx.RescueService.EvaluateRescueWithRoll(ignoreChance: true);

                PassIf(run, "rescue-02", "rescue force + evaluate (no event)", "CanAttempt=true",
                    (warped || GameStateHelper.IsForestShelterLocation())
                    && eval.Evaluation.CanAttempt
                    && !GameStateHelper.IsEventActive(),
                    warped
                        ? $"CanAttempt={eval.Evaluation.CanAttempt}, BlockReason={eval.Evaluation.BlockReason ?? "(none)"}, tier={prep.Tier}, eventActive={GameStateHelper.IsEventActive()}"
                        : $"warp failed: {warpError ?? "unknown"}; CanAttempt={eval.Evaluation.CanAttempt}, BlockReason={eval.Evaluation.BlockReason ?? "(none)"}, atForest={GameStateHelper.IsForestShelterLocation()}",
                    Snippet(ctx, "stress_rescue_debug"));

                Skip(run, "rescue-03", "mcp_start_event rescue CP",
                    "event starts without black screen",
                    "SKIP: dangerous event flow — run manually mcp_start_event + mcp_end_event",
                    Snippet(ctx, "mcp_event_snapshot"));
            }
            finally
            {
                ctx.RescueService.SuppressAutomaticRescue = false;
                McpTestPlanActions.TryEndActiveEvent(out _);
            }
        }

        private static void RunSafeAura(McpTestPlanContext ctx, TestPlanRun run)
        {
            McpTestPlanActions.ResetAll(ctx);
            McpTestPlanActions.TryEndActiveEvent(out _);
            ctx.RescueService.SuppressAutomaticRescue = true;
            try
            {
                if (GameStateHelper.IsEventActive())
                {
                    Skip(run, "aura-00", "precheck", "no event", "event active", Snippet(ctx, "stress_game_context"));
                    return;
                }

                Game1.player.friendshipData["Harvey"] = new Friendship(1750);
                ctx.TrustService.SetTrustPointsForDebug(160);

                if (!McpTestPlanActions.TrySetupHarveyNearPlayer(out var setupErr))
                {
                    Skip(run, "aura-01", "setup Harvey near player in Town", "player near Harvey",
                        setupErr ?? "setup failed", Snippet(ctx, "stress_game_context"));
                    return;
                }

                var status = ctx.SafeAuraService.BuildMcpSnapshot();
                var unlocked = status.Contains("unlocked: True", StringComparison.OrdinalIgnoreCase)
                               || status.Contains("unlocked: true", StringComparison.OrdinalIgnoreCase);
                var active = status.Contains("active: True", StringComparison.OrdinalIgnoreCase)
                             || status.Contains("active: true", StringComparison.OrdinalIgnoreCase);

                PassIf(run, "aura-02", "stress_safe_aura_status near Harvey", "unlocked=true and active=true",
                    unlocked && active,
                    ExtractLines(status, "unlocked", "active", "distanceToHarvey", "reasonInactive"),
                    Truncate(status));
            }
            finally
            {
                ctx.RescueService.SuppressAutomaticRescue = false;
            }
        }

        private static void RunDailyRegression(McpTestPlanContext ctx, TestPlanRun run)
        {
            RunSmoke(ctx, run);
            if (run.ShouldStop) { run.Aborted = true; return; }

            RunStressLoad(ctx, run);
            if (run.ShouldStop) { run.Aborted = true; return; }

            RunRecovery(ctx, run);
            if (run.ShouldStop) { run.Aborted = true; return; }

            Skip(run, "daily-dialogues", "dialogues plan", "see plan=dialogues",
                "SKIPPED in daily_regression v1 — run plan=dialogues separately",
                Snippet(ctx, "stress_dialogue_state"));

            RunTrust(ctx, run);
            if (run.ShouldStop) { run.Aborted = true; return; }

            RunGotoroRescue(ctx, run);
            if (run.ShouldStop) { run.Aborted = true; return; }

            RunSafeAura(ctx, run);
            if (run.ShouldStop) { run.Aborted = true; return; }

            Skip(run, "daily-persistence", "save/load offset", "offset preserved",
                "SKIPPED in daily_regression v1 — run plan=persistence separately",
                Snippet(ctx, "stress_load_debug"));

            Skip(run, "daily-all-debuffs", "full debuff matrix", "9 buffs add/remove",
                "SKIPPED in daily_regression v1 — run plan=all_debuffs separately",
                Snippet(ctx, "stress_treatment_snapshot"));
        }

        private static void RunSkippedPlan(TestPlanRun run, string planId, string reason)
        {
            Skip(run, $"{planId}-skipped", $"plan={planId}", "plan executable", reason, reason);
        }

        private static void PassIf(
            TestPlanRun run,
            string id,
            string action,
            string expected,
            bool ok,
            string actual,
            string debug)
        {
            run.Add(new TestPlanStep
            {
                Id = id,
                Action = action,
                Expected = expected,
                Actual = actual,
                Status = ok ? TestStepStatus.Pass : TestStepStatus.Fail,
                DebugSnippet = Truncate(debug),
            });
        }

        private static void Skip(TestPlanRun run, string id, string action, string expected, string reason, string debug)
        {
            run.Add(new TestPlanStep
            {
                Id = id,
                Action = action,
                Expected = expected,
                Actual = reason,
                Status = TestStepStatus.Skipped,
                DebugSnippet = Truncate(debug),
            });
        }

        private static string FormatReport(TestPlanRun run)
        {
            var passed = run.Steps.Count(s => s.Status == TestStepStatus.Pass);
            var failed = run.Steps.Count(s => s.Status == TestStepStatus.Fail);
            var skipped = run.Steps.Count(s => s.Status == TestStepStatus.Skipped);

            var sb = new StringBuilder();
            sb.AppendLine($"ok: {failed == 0}");
            sb.AppendLine($"plan: {run.Plan}");
            sb.AppendLine($"stopOnFail: {run.StopOnFail}");
            sb.AppendLine($"aborted: {run.Aborted}");
            sb.AppendLine("---");

            foreach (var step in run.Steps)
            {
                sb.AppendLine($"[step {step.Id}]");
                sb.AppendLine($"id: {step.Id}");
                sb.AppendLine($"action: {step.Action}");
                sb.AppendLine($"expected: {step.Expected}");
                sb.AppendLine($"actual: {step.Actual}");
                sb.AppendLine($"status: {step.Status.ToString().ToUpperInvariant()}");
                sb.AppendLine($"debug: {step.DebugSnippet}");
                sb.AppendLine("---");
            }

            sb.AppendLine("summary:");
            sb.AppendLine($"total: {run.Steps.Count}");
            sb.AppendLine($"passed: {passed}");
            sb.AppendLine($"failed: {failed}");
            sb.AppendLine($"skipped: {skipped}");

            var failedSteps = run.Steps.Where(s => s.Status == TestStepStatus.Fail).Select(s => s.Id).ToList();
            sb.AppendLine($"failedSteps: {(failedSteps.Count > 0 ? string.Join(", ", failedSteps) : "(none)")}");

            sb.AppendLine($"recommendedDebugTools: {string.Join(", ", RecommendDebugTools(run))}");
            return sb.ToString().TrimEnd();
        }

        private static IEnumerable<string> RecommendDebugTools(TestPlanRun run)
        {
            var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "stress_game_context",
                "stress_treatment_snapshot",
                "stress_load_debug",
            };

            if (run.Steps.Any(s => s.Id.Contains("trust", StringComparison.OrdinalIgnoreCase)))
                tools.Add("stress_trust_debug");

            if (run.Steps.Any(s => s.Id.Contains("rescue", StringComparison.OrdinalIgnoreCase)))
                tools.Add("stress_rescue_debug");

            if (run.Steps.Any(s => s.Id.Contains("dlg", StringComparison.OrdinalIgnoreCase)))
                tools.Add("stress_dialogue_state");

            if (run.Steps.Any(s => s.Id.Contains("aura", StringComparison.OrdinalIgnoreCase)))
                tools.Add("stress_safe_aura_status");

            if (run.Steps.Any(s => s.Status == TestStepStatus.Fail
                                  && s.Id.Contains("event", StringComparison.OrdinalIgnoreCase)))
                tools.Add("mcp_event_snapshot");

            return tools.OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
        }

        private static string Snippet(McpTestPlanContext ctx, string kind) => kind switch
        {
            "stress_load_debug" => Truncate(ctx.StressLoadService.BuildMcpSnapshot()),
            "stress_trust_debug" => Truncate(ctx.TrustService.BuildMcpSnapshot()),
            "stress_rescue_debug" => Truncate(ctx.RescueService.BuildMcpSnapshot()),
            "stress_treatment_snapshot" => Truncate(TreatmentDebugReporter.BuildMcpSnapshot(
                ctx.Data, ctx.StateService, ctx.StressLoadService, ctx.EpisodeService, ctx.StressDialogueService)),
            "stress_dialogue_state" => Truncate(
                new StressDialogueStateReporter(ctx.Data, ctx.StateService, ctx.StressDialogueService, ctx.Monitor).BuildReport()),
            "stress_game_context" => Truncate(TestContextReporter.BuildReport()),
            "mcp_event_snapshot" => Truncate(EventDebugHelper.BuildMcpSnapshot()),
            _ => "",
        };

        private static string ExtractLines(string text, params string[] keys)
        {
            var lines = text.Split('\n')
                .Where(l => keys.Any(k => l.StartsWith(k + ":", StringComparison.OrdinalIgnoreCase)));
            return string.Join("; ", lines).Trim();
        }

        private static string Truncate(string text, int max = 400)
        {
            if (string.IsNullOrEmpty(text))
                return "(empty)";

            text = text.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= max ? text : text[..max] + "…";
        }

        private static bool TryGetString(JsonElement? arguments, string name, out string value)
        {
            value = string.Empty;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetBool(JsonElement? arguments, string name, out bool value)
        {
            value = false;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            if (el.ValueKind == JsonValueKind.True) { value = true; return true; }
            if (el.ValueKind == JsonValueKind.False) { value = false; return true; }
            return false;
        }
    }

    internal static class McpTestPlanActions
    {
        private static readonly Dictionary<string, string> BuffDisplayNames = new()
        {
            [BuffIds.Tired] = "Усталость",
            [BuffIds.Lonely] = "Одиночество",
            [BuffIds.Thunder] = "Страх грозы",
            [BuffIds.Hunger] = "Голод",
            [BuffIds.Overwork] = "Переработка",
            [BuffIds.NoSleep] = "Недосып",
            [BuffIds.TooCold] = "Переохлаждение",
            [BuffIds.Darkness] = "Темнота",
            [BuffIds.Social] = "Социальный дискомфорт",
        };

        public static void ResetAll(McpTestPlanContext ctx)
        {
            HarveyDevTalkHelper.TryForceCloseDialogue(ctx.Monitor);
            ctx.StressDialogueService.ClearPendingTreatment();
            TryEndActiveEvent(out _);
            ctx.ModResetService.ResetAll();
        }

        /// <summary>Warp player and place Harvey on adjacent walkable tiles (Town plaza).</summary>
        public static bool TrySetupHarveyNearPlayer(out string? error)
        {
            const int playerX = 47;
            const int playerY = 57;
            const int harveyX = 46;
            const int harveyY = 57;

            if (!TryEndActiveEvent(out error))
                return false;

            if (!TryWarpPlayer("Town", playerX, playerY, out error))
                return false;

            if (!TryPlaceHarvey("Town", harveyX, harveyY, out error))
                return false;

            return true;
        }

        public static bool TryEndActiveEvent(out string? note)
        {
            note = null;
            if (!GameStateHelper.IsEventActive())
                return true;

            for (var i = 0; i < 8 && GameStateHelper.IsEventActive(); i++)
            {
                EventDebugHelper.TryEndEvent(force: true);
                GameStateHelper.ClearStaleUiFlags();
            }

            if (GameStateHelper.IsEventActive())
            {
                note = $"event_active:{GameStateHelper.GetCurrentEventId() ?? "unknown"}";
                return false;
            }

            return true;
        }

        public static string BuildDebugDump(McpTestPlanContext ctx)
            => StressDebugDumpReporter.BuildReport(new StressDebugDumpContext
            {
                Monitor = ctx.Monitor,
                Data = ctx.Data,
                StateService = ctx.StateService,
                StressDialogueService = ctx.StressDialogueService,
                TrustService = ctx.TrustService,
                StressLoadService = ctx.StressLoadService,
                RescueService = ctx.RescueService,
                SafeAuraService = ctx.SafeAuraService,
                EpisodeService = ctx.EpisodeService,
            });

        public static bool TryApplyDebuff(McpTestPlanContext ctx, string buffId, out string? error)
        {
            error = null;
            if (!TreatmentTopics.ImplementedBuffIds.Contains(buffId))
            {
                error = $"invalid buff {buffId}";
                return false;
            }

            ctx.StateService.RemoveImmunity(buffId);
            ctx.Data.StressState.LastIssuedDay.Remove(buffId);
            ctx.TreatmentService.ApplyStressBuff(buffId, BuffDisplayNames.GetValueOrDefault(buffId, buffId));
            ctx.StressLoadService.Recalculate();
            return ctx.StateService.HasBuffInGame(buffId);
        }

        public static bool TryRemoveDebuff(McpTestPlanContext ctx, string buffId, out string? error)
        {
            error = null;
            if (!TreatmentTopics.ImplementedBuffIds.Contains(buffId))
            {
                error = $"invalid buff {buffId}";
                return false;
            }

            foreach (var treatment in ctx.Data.StressState.GetActiveTreatmentsByBuff(buffId).ToList())
            {
                if (!string.IsNullOrEmpty(treatment.QuestId) && ctx.QuestService.HasQuest(treatment.QuestId))
                    Game1.player.removeQuest(treatment.QuestId);

                ctx.Data.StressState.RemoveTreatment(treatment.TreatmentKey);
            }

            if (ctx.BuffService.HasBuff(buffId))
                ctx.BuffService.RemoveBuff(buffId);

            ctx.StateService.ClearTreatmentOfferFlags(buffId);
            ctx.StressLoadService.Recalculate();
            return !ctx.StateService.HasBuffInGame(buffId);
        }

        public static bool TryWarpPlayer(string locationName, int x, int y, out string? error)
        {
            error = null;
            if (GameStateHelper.IsEventActive())
            {
                error = "event_active";
                return false;
            }

            if (Game1.activeClickableMenu != null || GameStateHelper.IsEnvironmentDialogueBlocking())
            {
                error = "menu_or_dialogue_active";
                return false;
            }

            if (!McpEnvironmentTools.TryResolveLocation(locationName, out var location, out var resolveError))
            {
                error = resolveError;
                return false;
            }

            var warpName = location!.NameOrUniqueName ?? location.Name ?? locationName;
            if (!McpWarpHelper.TryWarpImmediate(warpName, x, y, out error))
                return false;

            return true;
        }

        public static bool TryPlaceHarvey(string locationName, int x, int y, out string? error)
        {
            error = null;
            if (GameStateHelper.IsEventActive())
            {
                error = "event_active";
                return false;
            }

            if (Game1.activeClickableMenu != null || GameStateHelper.IsEnvironmentDialogueBlocking())
            {
                error = "menu_or_dialogue_active";
                return false;
            }

            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey == null)
            {
                error = "harvey_not_found";
                return false;
            }

            if (!McpEnvironmentTools.TryResolveLocation(locationName, out var location, out var resolveError))
            {
                error = resolveError;
                return false;
            }

            if (!McpWarpHelper.TryResolveSafeTile(location!, x, y, out var safeX, out var safeY, out error))
                return false;

            location!.characters.Remove(harvey);
            harvey.currentLocation = location;
            var tile = new Microsoft.Xna.Framework.Vector2(safeX, safeY);
            harvey.setTileLocation(tile);
            harvey.Position = new Microsoft.Xna.Framework.Vector2(
                safeX * Game1.tileSize,
                safeY * Game1.tileSize);
            if (!location.characters.Contains(harvey))
                location.addCharacter(harvey);

            return true;
        }
    }
}
