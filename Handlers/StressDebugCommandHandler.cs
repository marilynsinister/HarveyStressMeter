using System;
using System.Collections.Generic;
using System.Linq;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// DEV/TEST SMAPI console commands for stress dialogue pipeline QA.
    /// Registered when ModConfig.EnableDevTestCommands is true.
    /// </summary>
    public sealed class StressDebugCommandHandler
    {
        private const string DevPrefix = "[DEV/TEST]";

        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly SaveData _data;
        private readonly TreatmentService _treatmentService;
        private readonly StateService _stateService;
        private readonly BuffService _buffService;
        private readonly QuestService _questService;
        private readonly StressDialogueService _stressDialogueService;
        private readonly ModResetService _modResetService;
        private readonly StressTreatmentReviewService _stressTreatmentReviewService;
        private readonly ThunderFlashbackService _thunderFlashbackService;
        private readonly HarveyFlashbackRescueService _harveyFlashbackRescueService;
        private readonly HarveyCareTrustService _harveyCareTrustService;
        private readonly HarveySafePersonAuraService _harveySafePersonAuraService;
        private readonly StressSystemsCoordinator _stressSystemsCoordinator;
        private readonly StressLoadService _stressLoadService;
        private readonly TreatmentEpisodeService _treatmentEpisodeService;
        private readonly StressGameplayEffectService _stressGameplayEffectService;
        private readonly DarknessService _darknessService;
        private readonly SocialExposureService _socialExposureService;

        private bool _pendingTalkHarveyWarp = true;

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

        public StressDebugCommandHandler(
            IMonitor monitor,
            IModHelper helper,
            SaveData data,
            TreatmentService treatmentService,
            StateService stateService,
            BuffService buffService,
            QuestService questService,
            StressDialogueService stressDialogueService,
            ModResetService modResetService,
            StressTreatmentReviewService stressTreatmentReviewService,
            ThunderFlashbackService thunderFlashbackService,
            HarveyFlashbackRescueService harveyFlashbackRescueService,
            HarveyCareTrustService harveyCareTrustService,
            HarveySafePersonAuraService harveySafePersonAuraService,
            StressSystemsCoordinator stressSystemsCoordinator,
            StressLoadService stressLoadService,
            TreatmentEpisodeService treatmentEpisodeService,
            StressGameplayEffectService stressGameplayEffectService,
            DarknessService darknessService,
            SocialExposureService socialExposureService)
        {
            _monitor = monitor;
            _helper = helper;
            _data = data;
            _treatmentService = treatmentService;
            _stateService = stateService;
            _buffService = buffService;
            _questService = questService;
            _stressDialogueService = stressDialogueService;
            _modResetService = modResetService;
            _stressTreatmentReviewService = stressTreatmentReviewService;
            _thunderFlashbackService = thunderFlashbackService;
            _harveyFlashbackRescueService = harveyFlashbackRescueService;
            _harveyCareTrustService = harveyCareTrustService;
            _harveySafePersonAuraService = harveySafePersonAuraService;
            _stressSystemsCoordinator = stressSystemsCoordinator;
            _stressLoadService = stressLoadService;
            _treatmentEpisodeService = treatmentEpisodeService;
            _stressGameplayEffectService = stressGameplayEffectService;
            _darknessService = darknessService;
            _socialExposureService = socialExposureService;
        }

        public void RegisterCommands()
        {
            _monitor.Log($"{DevPrefix} Registering hs.test.* and stress_* console commands", LogLevel.Warn);

            RegisterStressTreatmentCommands();
            RegisterDarknessDebugCommands();
            RegisterSocialExposureDebugCommands();

            _helper.ConsoleCommands.Add(
                "hs.test.add-stress",
                "DEV/TEST: apply stress debuff via production ApplyStressBuff (no treatment start).",
                AddStress);
            _helper.ConsoleCommands.Add(
                "hs.test.remove-stress",
                "DEV/TEST: remove stress debuff, topics and active treatment for buffId.",
                RemoveStress);
            _helper.ConsoleCommands.Add(
                "hs.test.start-treatment",
                "DEV/TEST: call TreatmentService.StartTreatment (same path as post-dialogue auto-start).",
                StartTreatment);
            _helper.ConsoleCommands.Add(
                "hs.test.complete-treatment",
                "DEV/TEST: complete treatment via TreatmentService.CompleteTreatment.",
                CompleteTreatment);
            _helper.ConsoleCommands.Add(
                "hs.test.set-topic",
                "DEV/TEST: set dialogue topic (topicId, days).",
                SetTopic);
            _helper.ConsoleCommands.Add(
                "hs.test.clear-topic",
                "DEV/TEST: remove dialogue topic.",
                ClearTopic);
            _helper.ConsoleCommands.Add(
                "hs.test.clear-offer-flags",
                "DEV/TEST: clear WasTreatmentOfferShownToday / WasTreatmentDeclinedToday for buffId.",
                ClearOfferFlags);
            _helper.ConsoleCommands.Add(
                "hs.test.force-dialogue-check",
                "DEV/TEST: run stress dialogue pipeline check (respects PipelineGuard).",
                (_, __) => ForceDialogueCheck());
            _helper.ConsoleCommands.Add(
                "hs.test.talk-harvey",
                "DEV/TEST: warp to Harvey (optional --no-warp) and open dialogue via checkAction.",
                TalkHarvey);
            _helper.ConsoleCommands.Add(
                "hs.test.consent",
                "DEV/TEST: close or advance open stress start dialogue (legacy consent command). Usage: hs.test.consent accept|decline [--no-finish]",
                ChooseConsent);
            _helper.ConsoleCommands.Add(
                "hs.test.dialogue-advance",
                "DEV/TEST: click through DialogueBox. Usage: hs.test.dialogue-advance [steps]",
                AdvanceDialogue);
            _helper.ConsoleCommands.Add(
                "hs.test.list-responses",
                "DEV/TEST: list #$y responses in open DialogueBox.",
                (_, __) => ListResponses());
            _helper.ConsoleCommands.Add(
                "hs.test.choose-response",
                "DEV/TEST: pick dialogue response. Usage: hs.test.choose-response <key|index> [--no-advance] [--no-finish]",
                ChooseResponse);
            _helper.ConsoleCommands.Add(
                "hs.test.game-context",
                "DEV/TEST: event/menu/dialogue question snapshot.",
                (_, __) => LogDev(TestContextReporter.BuildReport()));
            _helper.ConsoleCommands.Add(
                "hs.test.list",
                "DEV/TEST: list implemented buffs with quest/topic mappings.",
                (_, __) => ListImplementedBuffs());
        }

        private void RegisterStressTreatmentCommands()
        {
            _helper.ConsoleCommands.Add(
                "stress_debug_state",
                "DEV/TEST: StressLoad, episodes, flashback, next step.",
                (_, __) => StressDebugState());
            _helper.ConsoleCommands.Add(
                "stress_set_load",
                "DEV/TEST: set CurrentStressLoad. Usage: stress_set_load <value>",
                StressSetLoad);
            _helper.ConsoleCommands.Add(
                "stress_add_cause",
                "DEV/TEST: add stress cause. Usage: stress_add_cause <causeId>",
                StressAddCause);
            _helper.ConsoleCommands.Add(
                "stress_remove_cause",
                "DEV/TEST: remove stress cause. Usage: stress_remove_cause <causeId>",
                StressRemoveCause);
            _helper.ConsoleCommands.Add(
                "stress_recalc",
                "DEV/TEST: force StressLoad + candidate episode recalc.",
                (_, __) => StressRecalc());
            _helper.ConsoleCommands.Add(
                "stress_episode_start",
                "DEV/TEST: force StartTreatmentEpisode. Usage: stress_episode_start <episodeId>",
                StressEpisodeStart);
            _helper.ConsoleCommands.Add(
                "stress_episode_ready",
                "DEV/TEST: mark episode AwaitingHarveyReview. Usage: stress_episode_ready <episodeId>",
                StressEpisodeReady);
            _helper.ConsoleCommands.Add(
                "stress_episode_complete",
                "DEV/TEST: force CompleteTreatmentEpisode. Usage: stress_episode_complete <episodeId>",
                StressEpisodeComplete);
            _helper.ConsoleCommands.Add(
                "stress_force_debuff",
                "DEV/TEST: apply stress debuff (no treatment). Usage: stress_force_debuff <buffId>",
                StressForceDebuff);
            _helper.ConsoleCommands.Add(
                "stress_force_start",
                "DEV/TEST: StartTreatment for buffId. Usage: stress_force_start <buffId>",
                StressForceStart);
            _helper.ConsoleCommands.Add(
                "stress_force_ready",
                "DEV/TEST: MarkTreatmentReadyForReview. Usage: stress_force_ready <buffId>",
                StressForceReady);
            _helper.ConsoleCommands.Add(
                "stress_force_complete",
                "DEV/TEST: CompleteTreatment (debug only). Usage: stress_force_complete <buffId>",
                StressForceComplete);
            _helper.ConsoleCommands.Add(
                "stress_reset",
                "DEV/TEST: alias for stress_reset_all.",
                (_, __) => StressResetAll());
            _helper.ConsoleCommands.Add(
                "stress_reset_all",
                "DEV/TEST: full reset (load, causes, episodes, treatments, flashback, quests/topics).",
                (_, __) => StressResetAll());
            _helper.ConsoleCommands.Add(
                "stress_flashback_trigger",
                "DEV/TEST: force thunder/Gotoro flashback roll (ignores daily limit).",
                (_, __) => ForceFlashbackTrigger());
            _helper.ConsoleCommands.Add(
                "stress_flashback_stabilize",
                "DEV/TEST: stabilize active thunder flashback.",
                (_, __) => ForceFlashbackStabilize());
            _helper.ConsoleCommands.Add(
                "stress_flashback_reset",
                "DEV/TEST: reset thunder flashback state.",
                (_, __) => ForceFlashbackReset());
            _helper.ConsoleCommands.Add(
                "stress_rescue_check",
                "DEV/TEST: evaluate Harvey forest rescue conditions.",
                (_, __) => StressRescueCheck());
            _helper.ConsoleCommands.Add(
                "stress_rescue_trigger",
                "DEV/TEST: force Harvey forest rescue. Usage: stress_rescue_trigger [MidTrust|HighTrust|Dating|Married]",
                StressRescueTrigger);
            _helper.ConsoleCommands.Add(
                "stress_rescue_reset",
                "DEV/TEST: reset Harvey flashback rescue state.",
                (_, __) => StressRescueReset());
            _helper.ConsoleCommands.Add(
                "stress_trust_set",
                "DEV/TEST: set HarveyCareTrust points. Usage: stress_trust_set <points>",
                StressTrustSet);
            _helper.ConsoleCommands.Add(
                "stress_trust_add",
                "DEV/TEST: add HarveyCareTrust points. Usage: stress_trust_add <points>",
                StressTrustAdd);
            _helper.ConsoleCommands.Add(
                "stress_trust_reset",
                "DEV/TEST: reset HarveyCareTrust state.",
                (_, __) => StressTrustReset());
            _helper.ConsoleCommands.Add(
                "stress_trust_debug",
                "DEV/TEST: HarveyCareTrust snapshot only.",
                (_, __) => StressTrustDebug());
            _helper.ConsoleCommands.Add(
                "stress_treatment_debug",
                "DEV/TEST: treatments, episode, causes, debuffs (MCP snapshot).",
                (_, __) => StressTreatmentDebug());
            _helper.ConsoleCommands.Add(
                "stress_repair_sync",
                "DEV/TEST: restore missing quests/buffs, episode quest, darkness sync; log before/after snapshot.",
                (_, __) => StressRepairSync());
            _helper.ConsoleCommands.Add(
                "stress_force_remove_quest",
                "DEV/TEST: remove treatment quest from journal (state kept). Usage: stress_force_remove_quest <buffId>",
                StressForceRemoveQuest);
        }

        private void RegisterSocialExposureDebugCommands()
        {
            _helper.ConsoleCommands.Add(
                "stress_social_get",
                "DEV/TEST: social exposure snapshot (SocialExposureToday, thresholds, recovery).",
                (_, __) => StressSocialGet());
            _helper.ConsoleCommands.Add(
                "stress_social_set",
                "DEV/TEST: set SocialExposureToday 0–100. Usage: stress_social_set <0..100>",
                StressSocialSet);
            _helper.ConsoleCommands.Add(
                "stress_social_add",
                "DEV/TEST: add to SocialExposureToday. Usage: stress_social_add <amount>",
                StressSocialAdd);
            _helper.ConsoleCommands.Add(
                "stress_social_reset",
                "DEV/TEST: reset SocialExposureToday and daily threshold HUD flags.",
                (_, __) => StressSocialReset());
        }

        private void StressSocialGet()
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            _monitor.Log($"[SocialExposure]\n{_socialExposureService.BuildDebugSnapshot()}", LogLevel.Info);
            TreatmentDebugReporter.ShowHud($"social: {_socialExposureService.GetCompactStatusLabel()} {_socialExposureService.ExposureToday}/100");
        }

        private void StressSocialSet(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            if (args.Length < 1 || !int.TryParse(args[0], out var value) || value < 0 || value > 100)
            {
                LogDevError($"Usage: {command} <0..100>");
                return;
            }

            _socialExposureService.SetExposure(value);
            StressSocialGet();
        }

        private void StressSocialAdd(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            if (args.Length < 1 || !int.TryParse(args[0], out var amount))
            {
                LogDevError($"Usage: {command} <amount>");
                return;
            }

            _socialExposureService.AddExposure(amount, "debug stress_social_add");
            StressSocialGet();
        }

        private void StressSocialReset()
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            _socialExposureService.ResetDaily();
            StressSocialGet();
        }

        private void RegisterDarknessDebugCommands()
        {
            _helper.ConsoleCommands.Add(
                "stress_darkness_debug",
                "DEV/TEST: darkness FearLevel, therapy, buffs, quests, topics.",
                (_, __) => StressDarknessDebug());
            _helper.ConsoleCommands.Add(
                "stress_darkness_set_level",
                "DEV/TEST: set FearLevel 1–3, sync buffs/topics. Usage: stress_darkness_set_level <1|2|3>",
                StressDarknessSetLevel);
            _helper.ConsoleCommands.Add(
                "stress_darkness_start_therapy",
                "DEV/TEST: DarknessService.StartTherapy + HarveyMod_DarknessStep1.",
                (_, __) => StressDarknessStartTherapy());
            _helper.ConsoleCommands.Add(
                "stress_darkness_step1_progress",
                "DEV/TEST: step1 progress. Usage: stress_darkness_step1_progress <evenings> <today>",
                StressDarknessStep1Progress);
            _helper.ConsoleCommands.Add(
                "stress_darkness_sync",
                "DEV/TEST: DarknessService.SyncDarknessState + snapshot.",
                (_, __) => StressDarknessSync());
        }

        private void StressDarknessDebug()
        {
            if (!EnsureWorldReadyForDarkness())
                return;

            var snapshot = _darknessService.BuildDebugSnapshot();
            DarknessDebugReporter.LogSnapshot(_monitor, "stress_darkness_debug", snapshot);
            TreatmentDebugReporter.ShowHud("darkness debug — see SMAPI log");
        }

        private void StressDarknessSetLevel(string command, string[] args)
        {
            if (!EnsureWorldReadyForDarkness())
                return;

            if (args.Length < 1 || !int.TryParse(args[0], out var level))
            {
                LogDevError($"Usage: {command} <1|2|3>");
                return;
            }

            if (!_darknessService.ApplyDebugFearLevel(level))
            {
                LogDevError($"Invalid level {level}. Use 1, 2, or 3.");
                return;
            }

            RefreshGameplayEffects();
            var snapshot = _darknessService.BuildDebugSnapshot();
            DarknessDebugReporter.LogSnapshot(_monitor, $"stress_darkness_set_level {level}", snapshot);
            TreatmentDebugReporter.ShowHud($"darkness level set to {level}");
        }

        private void StressDarknessStartTherapy()
        {
            if (!EnsureWorldReadyForDarkness())
                return;

            _darknessService.StartTherapy();
            RefreshGameplayEffects();
            var snapshot = _darknessService.BuildDebugSnapshot();
            DarknessDebugReporter.LogSnapshot(_monitor, "stress_darkness_start_therapy", snapshot);
            TreatmentDebugReporter.ShowHud("darkness therapy started");
        }

        private void StressDarknessStep1Progress(string command, string[] args)
        {
            if (!EnsureWorldReadyForDarkness())
                return;

            if (args.Length < 2
                || !int.TryParse(args[0], out var evenings)
                || !int.TryParse(args[1], out var today))
            {
                LogDevError($"Usage: {command} <evenings> <today>  (e.g. {command} 2 4)");
                return;
            }

            if (!_darknessService.ApplyDebugStep1Progress(evenings, today))
            {
                LogDevError("Step1 progress failed — start therapy (stress_darkness_start_therapy) at stage 1 first.");
                StressDarknessDebug();
                return;
            }

            var snapshot = _darknessService.BuildDebugSnapshot();
            DarknessDebugReporter.LogSnapshot(_monitor, $"stress_darkness_step1_progress {evenings} {today}", snapshot);
            TreatmentDebugReporter.ShowHud($"darkness step1 evenings={evenings} today={today}");
        }

        private void StressDarknessSync()
        {
            if (!EnsureWorldReadyForDarkness())
                return;

            _darknessService.SyncDarknessState("debug command");
            RefreshGameplayEffects();
            var snapshot = _darknessService.BuildDebugSnapshot();
            DarknessDebugReporter.LogSnapshot(_monitor, "stress_darkness_sync", snapshot);
            TreatmentDebugReporter.ShowHud("darkness sync done");
        }

        private void StressTreatmentDebug()
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            var snapshot = TreatmentDebugReporter.BuildTreatmentDebugSnapshot(
                _data,
                _stateService,
                _stressLoadService,
                _treatmentEpisodeService,
                _stressDialogueService);

            foreach (var line in snapshot.Split('\n'))
                _monitor.Log($"{TreatmentDebugReporter.DevPrefix} {line}", LogLevel.Info);

            TreatmentDebugReporter.ShowHud("treatment debug — see SMAPI log");
        }

        private void StressRepairSync()
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            _treatmentService.RepairSync("stress_repair_sync");
            RefreshGameplayEffects();
            TreatmentDebugReporter.ShowHud("stress_repair_sync done — see SMAPI log");
        }

        private void StressForceRemoveQuest(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                LogDevError("Load a save first.");
                return;
            }

            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            var treatment = _stateService.GetActiveTreatment(buffId);
            if (treatment == null)
            {
                LogDevError($"No active treatment for buffId={buffId}. Start treatment first.");
                return;
            }

            if (string.IsNullOrEmpty(treatment.QuestId))
            {
                LogDevError($"Treatment {treatment.TreatmentKey} has no QuestId.");
                return;
            }

            if (!_questService.HasQuest(treatment.QuestId))
            {
                LogDevWarn($"Quest {treatment.QuestId} already missing from journal.");
                WriteBuffReport(buffId);
                return;
            }

            Game1.player.removeQuest(treatment.QuestId);
            LogDev(
                $"Removed quest {treatment.QuestId} from journal (TreatmentState kept). " +
                $"Run stress_repair_sync to verify restore.");
            WriteBuffReport(buffId);
        }

        private bool EnsureWorldReadyForDarkness()
        {
            if (Context.IsWorldReady)
                return true;

            LogDevError("Load a save first (world not ready).");
            return false;
        }

        private void LogFullState(string? header = null)
        {
            StressLoadDebugReporter.LogFullState(
                _monitor,
                _data,
                _stressLoadService,
                _treatmentEpisodeService,
                _thunderFlashbackService,
                _harveyFlashbackRescueService,
                _harveyCareTrustService,
                _harveySafePersonAuraService,
                _stressSystemsCoordinator,
                _stressGameplayEffectService,
                _stateService,
                header);
            WriteStressDialogueSummary();
        }

        private void ShowHudSummary(string action)
        {
            TreatmentDebugReporter.ShowHud(
                $"{action} | {StressLoadDebugReporter.BuildHudSummary(_stressLoadService, _treatmentEpisodeService, _data)}");
        }

        private void RefreshGameplayEffects()
        {
            if (Context.IsWorldReady)
                _stressGameplayEffectService.UpdateEffects();
        }

        private void ForceFlashbackTrigger()
        {
            _thunderFlashbackService.TriggerFlashback(force: true);
            RefreshGameplayEffects();
            LogFullState("stress_flashback_trigger");
            ShowHudSummary("flashback triggered");
        }

        private void ForceFlashbackStabilize()
        {
            if (!_thunderFlashbackService.State.IsActive)
            {
                _thunderFlashbackService.State.EnteredForestDuringFlashback = true;
                _thunderFlashbackService.State.ForestShelterSeconds =
                    _thunderFlashbackService.State.RequiredForestShelterSeconds;
            }

            _thunderFlashbackService.StabilizeFlashback();
            RefreshGameplayEffects();
            LogFullState("stress_flashback_stabilize");
            ShowHudSummary("flashback stabilized");
        }

        private void ForceFlashbackReset()
        {
            _thunderFlashbackService.ResetFlashbackState();
            RefreshGameplayEffects();
            LogFullState("stress_flashback_reset");
            ShowHudSummary("flashback reset");
        }

        private void StressRescueCheck()
        {
            var eval = _harveyFlashbackRescueService.EvaluateRescue(ignoreChance: true);
            _monitor.Log($"{DevPrefix} === stress_rescue_check ===", LogLevel.Info);
            _monitor.Log($"{DevPrefix} {_harveyFlashbackRescueService.BuildDebugSnapshot()}", LogLevel.Info);
            _monitor.Log(
                $"{DevPrefix} canAttempt={eval.CanAttempt}, block={eval.BlockReason ?? "(none)"}, " +
                $"tier={eval.Tier ?? "(none)"}, chance={eval.RescueChance:P0}",
                LogLevel.Info);
            ShowHudSummary("rescue check");
        }

        private void StressRescueTrigger(string command, string[] args)
        {
            if (!_thunderFlashbackService.State.IsActive
                || !_thunderFlashbackService.State.IsGotoroFlashback)
            {
                _thunderFlashbackService.TriggerFlashback(force: true);
                _data.StressLoad.GotoroFlashbackActive = true;
            }

            if (!GameStateHelper.IsForestShelterLocation())
            {
                LogDevError("Warp to Forest/Woods/SecretWoods first.");
                return;
            }

            _harveyFlashbackRescueService.State.ForestSecondsBeforeRescue = Math.Max(
                _harveyFlashbackRescueService.State.ForestSecondsBeforeRescue,
                60);

            string? tier = null;
            if (args.Length > 0)
            {
                if (!FlashbackRescueTiers.TryParse(args[0], out tier!))
                {
                    LogDevError(
                        $"Usage: {command} [MidTrust|HighTrust|Dating|Married]");
                    return;
                }
            }

            if (!_harveyFlashbackRescueService.TryTriggerRescue(tier, force: true))
            {
                LogDevError("Rescue trigger failed — see log for block reason.");
                StressRescueCheck();
                return;
            }

            RefreshGameplayEffects();
            LogFullState("stress_rescue_trigger");
            ShowHudSummary($"rescue triggered ({tier ?? "auto"})");
        }

        private void StressRescueReset()
        {
            _harveyFlashbackRescueService.ResetRescueState();
            RefreshGameplayEffects();
            LogFullState("stress_rescue_reset");
            ShowHudSummary("rescue reset");
        }

        private void StressTrustSet(string command, string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var points))
            {
                LogDevError($"Usage: {command} <points>");
                return;
            }

            _harveyCareTrustService.SetTrustPointsForDebug(points);
            LogFullState("stress_trust_set");
            ShowHudSummary($"trust set to {points}");
        }

        private void StressTrustAdd(string command, string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var points))
            {
                LogDevError($"Usage: {command} <points>");
                return;
            }

            _harveyCareTrustService.AwardTrust("debug", points);
            LogFullState("stress_trust_add");
            ShowHudSummary($"trust +{points}");
        }

        private void StressTrustReset()
        {
            _harveyCareTrustService.ResetTrustState();
            LogFullState("stress_trust_reset");
            ShowHudSummary("trust reset");
        }

        private void StressTrustDebug()
        {
            foreach (var line in _harveyCareTrustService.BuildDebugSnapshot().Split('\n'))
                _monitor.Log($"{DevPrefix} {line}", LogLevel.Info);
            ShowHudSummary("trust debug");
        }

        private void StressDebugState()
        {
            LogFullState("stress_debug_state");
            ShowHudSummary("stress_debug_state");
        }

        private void StressSetLoad(string command, string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[0], out var value))
            {
                LogDevError($"Usage: {command} <value> (0–{_stressLoadService.GetMaxStressLoad()})");
                return;
            }

            _stressLoadService.SetStressLoadForDebug(value);
            RefreshGameplayEffects();
            LogFullState("stress_set_load");
            ShowHudSummary($"load set to {value}");
        }

        private void StressAddCause(string command, string[] args)
        {
            if (args.Length < 1 || !StressLoadDebugReporter.TryResolveCauseId(args[0], out var causeId))
            {
                LogDevError(
                    $"Usage: {command} <causeId>. Known: {string.Join(", ", StressCauses.BaseWeights.Keys)}");
                return;
            }

            _stressLoadService.AddCause(causeId);

            if (StressCauses.CauseToBuff.TryGetValue(causeId, out var buffId)
                && !_buffService.HasBuff(buffId))
            {
                _buffService.ApplyBuffFromData(buffId);
            }

            RefreshGameplayEffects();
            LogFullState($"stress_add_cause {causeId}");
            ShowHudSummary($"cause added: {causeId}");
        }

        private void StressRemoveCause(string command, string[] args)
        {
            if (args.Length < 1 || !StressLoadDebugReporter.TryResolveCauseId(args[0], out var causeId))
            {
                LogDevError(
                    $"Usage: {command} <causeId>. Known: {string.Join(", ", StressCauses.BaseWeights.Keys)}");
                return;
            }

            if (!_stressLoadService.GetActiveCauses().ContainsKey(causeId))
            {
                LogDevError($"Cause '{causeId}' is not active — nothing to remove.");
                return;
            }

            _stressLoadService.RemoveCause(causeId);

            if (StressCauses.CauseToBuff.TryGetValue(causeId, out var buffId)
                && _buffService.HasBuff(buffId))
            {
                _buffService.RemoveBuff(buffId);
            }

            RefreshGameplayEffects();
            LogFullState($"stress_remove_cause {causeId}");
            ShowHudSummary($"cause removed: {causeId}");
        }

        private void StressRecalc()
        {
            _stressLoadService.Recalculate();
            RefreshGameplayEffects();
            LogFullState("stress_recalc");
            ShowHudSummary("recalculated");
        }

        private void StressEpisodeStart(string command, string[] args)
        {
            if (args.Length < 1 || !StressLoadDebugReporter.TryResolveEpisodeId(args[0], out var episodeId))
            {
                LogDevError(
                    $"Usage: {command} <episodeId>. Known: {string.Join(", ", TreatmentEpisodeDefinitions.All.Select(d => d.EpisodeId))}");
                return;
            }

            if (_treatmentEpisodeService.HasActiveTreatmentEpisode())
            {
                LogDevError(
                    $"Cannot start '{episodeId}': active episode already running " +
                    $"({_data.ActiveTreatmentEpisode?.EpisodeId ?? _stressLoadService.GetActiveTreatmentEpisodeId()}). " +
                    "Use stress_reset_all or complete current episode first.");
                LogFullState("stress_episode_start (failed)");
                return;
            }

            if (!_treatmentEpisodeService.StartTreatmentEpisode(episodeId))
            {
                LogDevError(
                    $"Cannot start '{episodeId}': StartTreatmentEpisode returned false. " +
                    "Possible reasons: episode immunity cooldown, unknown episode, or no related active causes. " +
                    "Add causes with stress_add_cause first.");
                LogFullState("stress_episode_start (failed)");
                return;
            }

            RefreshGameplayEffects();
            LogFullState($"stress_episode_start {episodeId}");
            ShowHudSummary($"episode started: {episodeId}");
        }

        private void StressEpisodeReady(string command, string[] args)
        {
            if (args.Length < 1 || !StressLoadDebugReporter.TryResolveEpisodeId(args[0], out var episodeId))
            {
                LogDevError(
                    $"Usage: {command} <episodeId>. Known: {string.Join(", ", TreatmentEpisodeDefinitions.All.Select(d => d.EpisodeId))}");
                return;
            }

            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null)
            {
                LogDevError($"Cannot mark ready: no ActiveTreatmentEpisode. Use stress_episode_start {episodeId} first.");
                LogFullState("stress_episode_ready (failed)");
                return;
            }

            if (!string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
            {
                LogDevError(
                    $"Cannot mark ready: active episode is '{episode.EpisodeId}', not '{episodeId}'.");
                LogFullState("stress_episode_ready (failed)");
                return;
            }

            if (episode.AwaitingHarveyReview)
            {
                LogDevWarn($"Episode '{episodeId}' already AwaitingHarveyReview.");
            }
            else if (episode.IsCompleted)
            {
                LogDevError($"Episode '{episodeId}' is already completed.");
                LogFullState("stress_episode_ready (failed)");
                return;
            }
            else
            {
                _treatmentEpisodeService.MarkTreatmentEpisodeReadyForReview(
                    episodeId,
                    $"{DevPrefix} stress_episode_ready — objectives marked complete.");
            }

            RefreshGameplayEffects();
            LogFullState($"stress_episode_ready {episodeId}");
            ShowHudSummary($"episode ready: {episodeId}");
        }

        private void StressEpisodeComplete(string command, string[] args)
        {
            if (args.Length < 1 || !StressLoadDebugReporter.TryResolveEpisodeId(args[0], out var episodeId))
            {
                LogDevError(
                    $"Usage: {command} <episodeId>. Known: {string.Join(", ", TreatmentEpisodeDefinitions.All.Select(d => d.EpisodeId))}");
                return;
            }

            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null)
            {
                LogDevError($"Cannot complete: no ActiveTreatmentEpisode. Use stress_episode_start {episodeId} first.");
                LogFullState("stress_episode_complete (failed)");
                return;
            }

            if (!string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
            {
                LogDevError(
                    $"Cannot complete: active episode is '{episode.EpisodeId}', not '{episodeId}'.");
                LogFullState("stress_episode_complete (failed)");
                return;
            }

            if (episode.IsCompleted)
            {
                LogDevError($"Episode '{episodeId}' is already completed.");
                LogFullState("stress_episode_complete (failed)");
                return;
            }

            if (!episode.AwaitingHarveyReview)
            {
                LogDevWarn(
                    $"Episode '{episodeId}' was not awaiting review — forcing AwaitingHarveyReview for debug complete.");
                episode.AwaitingHarveyReview = true;
                episode.ObjectivesCompleted = true;
                _stressLoadService.Recalculate();
            }

            _stressTreatmentReviewService.ClearPendingReview();
            _treatmentEpisodeService.CompleteTreatmentEpisode(
                episodeId,
                $"{DevPrefix} stress_episode_complete — treatment finished (debug).");

            if (_data.ActiveTreatmentEpisode != null)
            {
                LogDevError(
                    $"CompleteTreatmentEpisode did not clear episode '{episodeId}'. " +
                    "Check SMAPI log for [CompleteTreatmentEpisode] warnings.");
            }

            RefreshGameplayEffects();
            LogFullState($"stress_episode_complete {episodeId}");
            ShowHudSummary($"episode completed: {episodeId}");
        }

        private void StressForceDebuff(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            AddStress(command, args);
            TreatmentDebugReporter.ShowHud($"debuff applied: {buffId}");
        }

        private void StressForceStart(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            StartTreatment(command, args);
            TreatmentDebugReporter.ShowHud($"StartTreatment: {buffId}");
        }

        private void StressForceReady(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!TreatmentTopics.IsImplementedBuff(buffId))
            {
                LogDevError($"buffId '{buffId}' is not implemented.");
                return;
            }

            if (!_stateService.HasBuffInGame(buffId))
            {
                LogDevError($"Cannot mark ready: buff '{buffId}' is not active. Use stress_force_debuff first.");
                return;
            }

            var treatment = _stateService.GetActiveTreatment(buffId);
            if (treatment == null || !treatment.TreatmentStarted)
            {
                LogDevWarn($"Treatment not started for {buffId} — calling StartTreatment first.");
                _treatmentService.StartTreatment(buffId, GetDisplayName(buffId));
                treatment = _stateService.GetActiveTreatment(buffId);
            }

            if (treatment?.AwaitingHarveyReview == true)
            {
                LogDevWarn($"Already AwaitingHarveyReview for {buffId}.");
                TreatmentDebugReporter.LogTreatmentDetail(_monitor, _stateService, buffId);
                WriteStressDialogueSummary();
                TreatmentDebugReporter.ShowHud($"already awaiting review: {buffId}");
                return;
            }

            _treatmentService.MarkTreatmentReadyForReview(
                buffId,
                $"{DevPrefix} stress_force_ready — objectives marked complete.");

            LogDev($"MarkTreatmentReadyForReview invoked for {buffId}.");
            TreatmentDebugReporter.LogTreatmentDetail(_monitor, _stateService, buffId);
            WriteStressDialogueSummary();
            TreatmentDebugReporter.ShowHud($"ready for Harvey review: {buffId}");
        }

        private void StressForceComplete(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!TreatmentTopics.IsImplementedBuff(buffId))
            {
                LogDevError($"buffId '{buffId}' is not implemented.");
                return;
            }

            var treatment = _stateService.GetActiveTreatment(buffId);
            if (treatment == null)
            {
                LogDevError($"Cannot complete: no active treatment for '{buffId}'.");
                return;
            }

            if (!treatment.TreatmentStarted && !treatment.AwaitingHarveyReview)
            {
                LogDevWarn($"Treatment not started for {buffId} — forcing StartTreatment before complete.");
                _treatmentService.StartTreatment(buffId, GetDisplayName(buffId));
            }

            _stressTreatmentReviewService.ClearPendingReview();
            _treatmentService.CompleteTreatment(
                buffId,
                $"{DevPrefix} stress_force_complete — treatment finished (debug).");

            LogDev($"CompleteTreatment invoked for {buffId} (debug force).");
            TreatmentDebugReporter.LogTreatmentDetail(_monitor, _stateService, buffId);
            WriteStressDialogueSummary();
            TreatmentDebugReporter.ShowHud($"treatment completed: {buffId}");
        }

        private void StressResetAll()
        {
            LogDev("=== stress_reset_all ===");
            _thunderFlashbackService.ResetFlashbackState();
            _stressTreatmentReviewService.ClearPendingReview();
            _stressDialogueService.ClearPendingTreatment();

            var result = _modResetService.ResetAll();

            LogDev($"Removed buffs: {result.RemovedBuffs}");
            LogDev($"Removed quests: {result.RemovedQuests}");
            LogDev($"Removed topics: {result.RemovedTopics}");
            LogDev("Mod save reset to initial state (StressLoad, causes, episodes, treatments, flashback).");

            RefreshGameplayEffects();
            LogFullState("stress_reset_all");
            ShowHudSummary(
                $"reset done (buffs={result.RemovedBuffs}, quests={result.RemovedQuests}, topics={result.RemovedTopics})");
        }

        private void AddStress(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!IsImplementedBuff(buffId))
            {
                LogDevError($"buffId '{buffId}' is not in TreatmentTopics.ImplementedBuffIds.");
                return;
            }

            if (_stateService.HasActiveTreatmentState(buffId) && _stateService.HasBuffInGame(buffId))
            {
                LogDevWarn($"Stress debuff already active: {buffId}. Report only.");
                WriteBuffReport(buffId);
                WriteStressDialogueSummary();
                return;
            }

            _stateService.RemoveImmunity(buffId);
            _data.StressState.LastIssuedDay.Remove(buffId);

            var displayName = GetDisplayName(buffId);
            _treatmentService.ApplyStressBuff(buffId, displayName);

            LogDev($"Applied stress debuff via ApplyStressBuff: {buffId}");
            WriteBuffReport(buffId);
            WriteStressDialogueSummary();
        }

        private void RemoveStress(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!IsImplementedBuff(buffId))
            {
                LogDevError($"buffId '{buffId}' is not in TreatmentTopics.ImplementedBuffIds.");
                return;
            }

            int removedTreatments = 0;
            foreach (var treatment in _data.StressState.GetActiveTreatmentsByBuff(buffId).ToList())
            {
                if (!string.IsNullOrEmpty(treatment.QuestId) && _questService.HasQuest(treatment.QuestId))
                    Game1.player.removeQuest(treatment.QuestId);

                if (_data.StressState.RemoveTreatment(treatment.TreatmentKey))
                    removedTreatments++;
            }

            if (_buffService.HasBuff(buffId))
                _buffService.RemoveBuff(buffId);

            foreach (var topicId in GetRelatedTopicIds(buffId))
            {
                if (ConversationHelper.HasTopic(topicId))
                    ConversationHelper.RemoveTopic(topicId);
            }

            _stateService.ClearTreatmentOfferFlags(buffId);

            LogDev($"Removed stress state for {buffId} (treatments={removedTreatments}).");
            WriteBuffReport(buffId);
            WriteStressDialogueSummary();
        }

        private void StartTreatment(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!TreatmentTopics.IsImplementedBuff(buffId))
            {
                LogDevError(
                    $"buffId '{buffId}' is not implemented. Allowed: {string.Join(", ", TreatmentTopics.ImplementedBuffIds)}");
                return;
            }

            if (!_stateService.HasBuffInGame(buffId))
            {
                LogDevError($"Cannot start treatment: buff '{buffId}' is not active. Use hs.test.add-stress first.");
                return;
            }

            var treatment = _stateService.GetActiveTreatment(buffId);
            if (treatment?.TreatmentStarted == true)
            {
                LogDevWarn($"Treatment already started for {buffId}.");
                WriteBuffReport(buffId);
                WriteStressDialogueSummary();
                return;
            }

            _treatmentService.StartTreatment(buffId, GetDisplayName(buffId));
            LogDev($"StartTreatment invoked for {buffId} (production path).");
            WriteBuffReport(buffId);
            WriteStressDialogueSummary();
        }

        private void CompleteTreatment(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!TreatmentTopics.IsImplementedBuff(buffId))
            {
                LogDevError(
                    $"buffId '{buffId}' is not implemented. Allowed: {string.Join(", ", TreatmentTopics.ImplementedBuffIds)}");
                return;
            }

            var treatment = _stateService.GetActiveTreatment(buffId);
            if (treatment == null || !treatment.TreatmentStarted)
            {
                LogDevError($"Cannot complete: no started treatment for '{buffId}'. Use hs.test.start-treatment first.");
                return;
            }

            _treatmentService.CompleteTreatment(buffId, $"{DevPrefix} Treatment completed via hs.test.complete-treatment.");
            LogDev($"CompleteTreatment invoked for {buffId} (production path).");
            WriteBuffReport(buffId);
            WriteStressDialogueSummary();
        }

        private void SetTopic(string command, string[] args)
        {
            if (args.Length < 2)
            {
                LogDevError("Usage: hs.test.set-topic <topicId> <days>");
                return;
            }

            var topicId = args[0];
            if (!int.TryParse(args[1], out var days) || days < 0)
            {
                LogDevError("days must be a non-negative integer.");
                return;
            }

            if (IsLegacyStartedTopic(topicId))
            {
                LogDevWarn("Legacy Started topic is cleanup-only and must not start treatment.");
            }

            ConversationHelper.AddTopic(topicId, days);
            LogDev($"Topic set: {topicId} ({days} days).");
            WriteStressDialogueSummary();
        }

        private void ClearTopic(string command, string[] args)
        {
            if (args.Length < 1)
            {
                LogDevError("Usage: hs.test.clear-topic <topicId>");
                return;
            }

            var topicId = args[0];
            if (ConversationHelper.HasTopic(topicId))
            {
                ConversationHelper.RemoveTopic(topicId);
                LogDev($"Topic removed: {topicId}.");
            }
            else
            {
                LogDevWarn($"Topic not active: {topicId}.");
            }

            WriteStressDialogueSummary();
        }

        private void ClearOfferFlags(string command, string[] args)
        {
            if (!TryRequireBuffArg(args, out var buffId, command))
                return;

            if (!IsImplementedBuff(buffId))
            {
                LogDevError($"buffId '{buffId}' is not in TreatmentTopics.ImplementedBuffIds.");
                return;
            }

            var shownBefore = _stateService.WasTreatmentOfferShownToday(buffId);
            var declinedBefore = _stateService.WasTreatmentDeclinedToday(buffId);

            _stateService.ClearTreatmentOfferFlags(buffId);

            LogDev(
                $"Cleared offer flags for {buffId} (shownToday was {shownBefore}, declinedToday was {declinedBefore}).");
            WriteStressDialogueSummary();
        }

        private void TalkHarvey(string command, string[] args)
        {
            var warp = !args.Any(a => string.Equals(a, "--no-warp", StringComparison.OrdinalIgnoreCase));

            if (Game1.activeClickableMenu != null)
            {
                LogDevError("Close the current menu before talk-harvey.");
                WriteStressDialogueSummary();
                return;
            }

            _pendingTalkHarveyWarp = warp;
            _helper.Events.GameLoop.UpdateTicked += OnTalkHarveyNextTick;
            LogDev(warp
                ? "talk-harvey scheduled (warp if needed, then checkAction)..."
                : "talk-harvey scheduled (same location only)...");
        }

        private void OnTalkHarveyNextTick(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            _helper.Events.GameLoop.UpdateTicked -= OnTalkHarveyNextTick;

            HarveyDevTalkHelper.TryTalkToHarvey(_monitor, _pendingTalkHarveyWarp);
            WriteStressDialogueSummary();
        }

        private void ChooseConsent(string command, string[] args)
        {
            if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
            {
                LogDevError("Usage: hs.test.consent accept|decline [--no-finish]");
                WriteStressDialogueSummary();
                return;
            }

            var choice = args[0].ToLowerInvariant();
            if (choice is not ("accept" or "decline" or "yes" or "no"))
            {
                LogDevError("Usage: hs.test.consent accept|decline [--no-finish]");
                WriteStressDialogueSummary();
                return;
            }

            var accept = choice is "accept" or "yes";
            var finish = !args.Any(a => string.Equals(a, "--no-finish", StringComparison.OrdinalIgnoreCase));

            if (Game1.activeClickableMenu is not DialogueBox)
            {
                LogDevError("No DialogueBox open. Run hs.test.talk-harvey first.");
                WriteStressDialogueSummary();
                return;
            }

            if (!HarveyDevTalkHelper.TryChooseConsent(_monitor, accept, finish))
            {
                LogDevError("Could not close/advance stress start dialogue.");
                WriteStressDialogueSummary();
                return;
            }

            WriteStressDialogueSummary();
        }

        private void AdvanceDialogue(string command, string[] args)
        {
            var steps = 1;
            if (args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed > 0)
                steps = Math.Min(parsed, HarveyDevTalkHelper.MaxDialogueAdvancesPublic);

            if (Game1.activeClickableMenu is not DialogueBox)
            {
                LogDevError("No DialogueBox open.");
                WriteStressDialogueSummary();
                return;
            }

            HarveyDevTalkHelper.AdvanceDialogue(_monitor, steps);
            WriteStressDialogueSummary();
        }

        private void ListResponses()
        {
            LogDev(TestContextReporter.BuildReport());
        }

        private void ChooseResponse(string command, string[] args)
        {
            if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
            {
                LogDevError("Usage: hs.test.choose-response <responseKey|index> [--no-advance] [--no-finish]");
                ListResponses();
                return;
            }

            var advance = !args.Any(a => string.Equals(a, "--no-advance", StringComparison.OrdinalIgnoreCase));
            var finish = !args.Any(a => string.Equals(a, "--no-finish", StringComparison.OrdinalIgnoreCase));

            if (Game1.activeClickableMenu is not DialogueBox)
            {
                LogDevError("No DialogueBox open.");
                ListResponses();
                return;
            }

            string? responseKey = null;
            int? responseIndex = null;
            if (int.TryParse(args[0], out var idx))
                responseIndex = idx;
            else
                responseKey = args[0];

            HarveyDevTalkHelper.TryChooseResponse(
                _monitor,
                responseKey: responseKey,
                responseIndex: responseIndex,
                advanceToQuestion: advance,
                finishDialogue: finish);
            WriteStressDialogueSummary();
        }

        private void ForceDialogueCheck()
        {
            LogDev("=== force-dialogue-check ===");

            StressDialoguePipelineGuard.CanRun(
                out var relaxedReason,
                requireDialogueBox: false,
                requireHarveySpeaker: false);
            LogDev($"PipelineGuard (world ready, no event): {relaxedReason}");

            StressDialoguePipelineGuard.CanRun(
                out var dialogueReason,
                requireDialogueBox: true,
                requireHarveySpeaker: true);
            LogDev($"PipelineGuard (DialogueBox + Harvey speaker): {dialogueReason}");

            if (dialogueReason != StressDialoguePipelineGuard.BlockReason.None)
            {
                LogDev($"Blocked by guard: {dialogueReason}");
                WriteStressDialogueSummary();
                return;
            }

            if (_stressDialogueService.ShouldShowStressDialogue(out var buffId, out var dialogueText))
            {
                LogDev($"ShouldShowStressDialogue=true, buffId={buffId}, hasDialogueText={!string.IsNullOrEmpty(dialogueText)}");
            }
            else
            {
                var candidate = _stressDialogueService.CheckForActiveDebuffWithoutTreatment();
                LogDev(
                    candidate == null
                        ? "ShouldShowStressDialogue=false (no untreated debuff eligible for start dialogue)."
                        : $"ShouldShowStressDialogue=false, but untreated debuff exists: {candidate} (dialogue/guard/cured topic?)");
            }

            WriteStressDialogueSummary();
        }

        private void ListImplementedBuffs()
        {
            LogDev("=== Implemented stress buff mappings ===");

            foreach (var buffId in TreatmentTopics.ImplementedBuffIds)
            {
                QuestIdsLookup.TryGetValue(buffId, out var questId);
                var stressTopicId = GetStressTopicId(buffId) ?? "(n/a)";
                TreatmentTopics.LegacyStartByBuff.TryGetValue(buffId, out var legacyStart);
                TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followup);

                _monitor.Log(
                    $"{DevPrefix} {buffId} | quest={questId ?? "(n/a)"} | stress={stressTopicId} | legacyStarted={legacyStart ?? "(n/a)"} | followup={followup ?? "(n/a)"}",
                    LogLevel.Info);
            }

            WriteStressDialogueSummary();
        }

        private void WriteBuffReport(string buffId)
        {
            var treatment = _stateService.GetActiveTreatment(buffId);
            var stressTopicId = GetStressTopicId(buffId);
            var stressTopicActive = stressTopicId != null && ConversationHelper.HasTopic(stressTopicId);

            _monitor.Log($"{DevPrefix} --- buff report ---", LogLevel.Info);
            _monitor.Log($"{DevPrefix} buffId: {buffId}", LogLevel.Info);
            _monitor.Log($"{DevPrefix} hasBuffInGame: {_stateService.HasBuffInGame(buffId)}", LogLevel.Info);
            _monitor.Log($"{DevPrefix} hasActiveTreatmentState: {_stateService.HasActiveTreatmentState(buffId)}", LogLevel.Info);
            _monitor.Log($"{DevPrefix} TreatmentStarted: {treatment?.TreatmentStarted ?? false}", LogLevel.Info);
            _monitor.Log($"{DevPrefix} QuestId: {treatment?.QuestId ?? "(empty)"}", LogLevel.Info);
            _monitor.Log($"{DevPrefix} stress topic active: {stressTopicActive}", LogLevel.Info);
        }

        private void WriteStressDialogueSummary()
        {
            new StressDialogueStateReporter(_data, _stateService, _stressDialogueService, _monitor).WriteReport();
        }

        private bool TryRequireBuffArg(string[] args, out string buffId, string commandName)
        {
            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                LogDevError($"Usage: {commandName} <buffId>");
                buffId = "";
                return false;
            }

            buffId = args[0];
            return true;
        }

        private static bool IsImplementedBuff(string buffId)
            => TreatmentTopics.ImplementedBuffIds.Contains(buffId);

        private static string GetDisplayName(string buffId)
            => BuffDisplayNames.TryGetValue(buffId, out var name) ? name : buffId;

        private static bool IsLegacyStartedTopic(string topicId)
            => TreatmentTopics.LegacyStartByBuff.Values.Contains(topicId);

        private static IEnumerable<string> GetRelatedTopicIds(string buffId)
        {
            var stressTopicId = GetStressTopicId(buffId);
            if (stressTopicId != null)
                yield return stressTopicId;

            if (TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followup))
                yield return followup;

            if (TreatmentTopics.LegacyStartByBuff.TryGetValue(buffId, out var legacyStart))
                yield return legacyStart;

            var suffix = BuffIdToTreatmentSuffix(buffId);
            yield return $"topicStressTreatment{suffix}Cured";

            if (TreatmentTopics.GetReadyForReviewTopic(buffId) is { } readyForReview)
                yield return readyForReview;
        }

        private static string? GetStressTopicId(string buffId)
        {
            if (StressDebuffSelector.BuffToStressTopic.TryGetValue(buffId, out var stressPair))
                return stressPair.topic;

            if (buffId == BuffIds.Darkness)
                return TopicIds.StressDarkness;

            return null;
        }

        private static string BuffIdToTreatmentSuffix(string buffId)
        {
            return buffId.StartsWith("buffStress", StringComparison.Ordinal)
                ? buffId["buffStress".Length..]
                : buffId;
        }

        private static readonly Dictionary<string, string> QuestIdsLookup = new()
        {
            [BuffIds.Tired] = QuestIds.Tired,
            [BuffIds.Lonely] = QuestIds.Lonely,
            [BuffIds.Thunder] = QuestIds.Thunder,
            [BuffIds.Hunger] = QuestIds.Hunger,
            [BuffIds.Overwork] = QuestIds.Overwork,
            [BuffIds.NoSleep] = QuestIds.NoSleep,
            [BuffIds.TooCold] = QuestIds.TooCold,
            [BuffIds.Social] = QuestIds.Social,
            [BuffIds.Darkness] = QuestIds.Darkness,
        };

        private void LogDev(string message)
            => _monitor.Log($"{DevPrefix} {message}", LogLevel.Info);

        private void LogDevWarn(string message)
            => _monitor.Log($"{DevPrefix} {message}", LogLevel.Warn);

        private void LogDevError(string message)
            => _monitor.Log($"{DevPrefix} ERROR: {message}", LogLevel.Error);
    }
}
