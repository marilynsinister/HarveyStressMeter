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
using StardewValley.Menus;

namespace HarveyStressMeter.Testing
{
    /// <summary>Executes Stress MCP tools on the game main thread.</summary>
    public sealed class StressMcpToolHandler
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

        private readonly IMonitor _monitor;
        private readonly SaveData _data;
        private readonly TreatmentService _treatmentService;
        private readonly StateService _stateService;
        private readonly BuffService _buffService;
        private readonly QuestService _questService;
        private readonly StressDialogueService _stressDialogueService;
        private readonly ModResetService _modResetService;
        private readonly HarveyCareTrustService _harveyCareTrustService;
        private readonly StressLoadService _stressLoadService;
        private readonly HarveyFlashbackRescueService _harveyFlashbackRescueService;
        private readonly HarveySafePersonAuraService _harveySafePersonAuraService;
        private readonly StressMeterHudService _stressMeterHudService;
        private readonly TreatmentEpisodeService _treatmentEpisodeService;
        private readonly GameLogicHandler _gameLogicHandler;
        private readonly DarknessService _darknessService;
        private readonly DarknessRemissionService _darknessRemissionService;
        private readonly SocialExposureService _socialExposureService;
        private readonly ThunderFlashbackService _thunderFlashbackService;
        private readonly SocialAnxietyTherapyService? _socialAnxietyTherapyService;

        public StressMcpToolHandler(
            IMonitor monitor,
            SaveData data,
            TreatmentService treatmentService,
            StateService stateService,
            BuffService buffService,
            QuestService questService,
            StressDialogueService stressDialogueService,
            ModResetService modResetService,
            HarveyCareTrustService harveyCareTrustService,
            StressLoadService stressLoadService,
            HarveyFlashbackRescueService harveyFlashbackRescueService,
            HarveySafePersonAuraService harveySafePersonAuraService,
            StressMeterHudService stressMeterHudService,
            TreatmentEpisodeService treatmentEpisodeService,
            GameLogicHandler gameLogicHandler,
            DarknessService darknessService,
            DarknessRemissionService darknessRemissionService,
            SocialExposureService socialExposureService,
            ThunderFlashbackService thunderFlashbackService,
            SocialAnxietyTherapyService? socialAnxietyTherapyService = null)
        {
            _monitor = monitor;
            _data = data;
            _treatmentService = treatmentService;
            _stateService = stateService;
            _buffService = buffService;
            _questService = questService;
            _stressDialogueService = stressDialogueService;
            _modResetService = modResetService;
            _harveyCareTrustService = harveyCareTrustService;
            _stressLoadService = stressLoadService;
            _harveyFlashbackRescueService = harveyFlashbackRescueService;
            _harveySafePersonAuraService = harveySafePersonAuraService;
            _stressMeterHudService = stressMeterHudService;
            _treatmentEpisodeService = treatmentEpisodeService;
            _gameLogicHandler = gameLogicHandler;
            _darknessService = darknessService;
            _darknessRemissionService = darknessRemissionService;
            _socialExposureService = socialExposureService;
            _thunderFlashbackService = thunderFlashbackService;
            _socialAnxietyTherapyService = socialAnxietyTherapyService;
        }

        public string Execute(string toolName, JsonElement? arguments)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            return toolName switch
            {
                "stress_reset" => Reset(),
                "stress_debug_dump" => DebugDump(),
                "stress_dialogue_state" => DialogueState(),
                "stress_game_context" => GameContext(),
                "stress_add_debuff" => AddDebuff(arguments),
                "stress_remove_debuff" => RemoveDebuff(arguments),
                "stress_talk_harvey" => TalkHarvey(arguments),
                "stress_show_dialogue" => ShowDialogue(),
                "stress_consent" => Consent(arguments),
                "stress_list_responses" => ListResponses(),
                "stress_choose_response" => ChooseResponse(arguments),
                "stress_dialogue_advance" => AdvanceDialogue(arguments),
                "stress_close_dialogue" => CloseDialogue(),
                "mcp_set_time" => McpEnvironmentTools.SetTime(arguments),
                "mcp_add_minutes" => McpEnvironmentTools.AddMinutes(arguments),
                "mcp_set_weather" => McpEnvironmentTools.SetWeather(arguments),
                "mcp_warp" => McpEnvironmentTools.Warp(arguments),
                "mcp_wait_seconds" => McpEnvironmentTools.WaitSecondsNotSupportedHere(),
                "mcp_set_friendship" => McpSocialTools.SetFriendship(arguments),
                "mcp_set_relationship" => McpSocialTools.SetRelationship(arguments),
                "mcp_get_friendship" => McpSocialTools.GetFriendship(arguments),
                "mcp_place_npc" => McpSocialTools.PlaceNpc(arguments),
                "mcp_add_topic" => McpSocialTools.AddTopic(arguments),
                "mcp_remove_topic" => McpSocialTools.RemoveTopic(arguments),
                "mcp_has_topic" => McpSocialTools.HasTopic(arguments),
                "mcp_list_topics" => McpSocialTools.ListTopics(arguments),
                "stress_trust_debug" => McpTrustTools.TrustDebug(_harveyCareTrustService),
                "stress_trust_set" => McpTrustTools.TrustSet(_harveyCareTrustService, arguments),
                "stress_trust_add" => McpTrustTools.TrustAdd(_harveyCareTrustService, arguments),
                "stress_trust_remove" => McpTrustTools.TrustRemove(_harveyCareTrustService, arguments),
                "stress_trust_reset" => McpTrustTools.TrustReset(_harveyCareTrustService),
                "stress_load_debug" => McpStressLoadTools.LoadDebug(_stressLoadService),
                "stress_set_load" => McpStressLoadTools.SetLoad(_stressLoadService, arguments),
                "stress_apply_recovery" => McpStressLoadTools.ApplyRecovery(_stressLoadService, arguments),
                "stress_clear_recovery_offset" => McpStressLoadTools.ClearRecoveryOffset(_stressLoadService),
                "stress_gotoro_set_active" => McpStressLoadTools.GotoroSetActive(_stressLoadService, arguments),
                "stress_force_recalculate" => McpStressLoadTools.ForceRecalculate(_stressLoadService),
                "stress_rescue_debug" => McpRescueTools.RescueDebug(_harveyFlashbackRescueService),
                "stress_rescue_evaluate" => McpRescueTools.RescueEvaluate(_harveyFlashbackRescueService, arguments),
                "stress_rescue_force" => McpRescueTools.RescueForce(_harveyFlashbackRescueService, arguments),
                "stress_rescue_clear" => McpRescueTools.RescueClear(_harveyFlashbackRescueService),
                "stress_thunder_debug" => McpThunderTools.ThunderDebug(_thunderFlashbackService, _stressLoadService),
                "stress_thunder_force_relapse" => McpThunderTools.ThunderForceRelapse(_thunderFlashbackService, arguments),
                "stress_thunder_stabilize_harvey" => McpThunderTools.ThunderStabilizeHarvey(_thunderFlashbackService, arguments),
                "stress_thunder_clear" => McpThunderTools.ThunderClear(_thunderFlashbackService),
                "stress_safe_aura_status" => McpSafeAuraTools.SafeAuraStatus(_harveySafePersonAuraService),
                "stress_safe_aura_force_tick" => McpSafeAuraTools.SafeAuraForceTick(_harveySafePersonAuraService),
                "stress_hud_snapshot" => McpHudTools.HudSnapshot(_stressMeterHudService),
                "stress_treatment_snapshot" => McpHudTools.TreatmentSnapshot(
                    _data,
                    _stateService,
                    _stressLoadService,
                    _treatmentEpisodeService,
                    _stressDialogueService),
                "stress_treatment_debug" => McpDarknessTools.TreatmentDebug(
                    _data,
                    _stateService,
                    _stressLoadService,
                    _treatmentEpisodeService,
                    _stressDialogueService),
                "stress_darkness_debug" => McpDarknessTools.DarknessDebug(_darknessService),
                "stress_darkness_set_level" => McpDarknessTools.DarknessSetLevel(_darknessService, arguments),
                "stress_darkness_start_therapy" => McpDarknessTools.DarknessStartTherapy(_darknessService),
                "stress_darkness_step1_progress" => McpDarknessTools.DarknessStep1Progress(_darknessService, arguments),
                "stress_darkness_sync" => McpDarknessTools.DarknessSync(_darknessService),
                "stress_darkness_status" => McpDarknessTools.DarknessStatus(_darknessService),
                "stress_darkness_add_minutes" => McpDarknessTools.DarknessAddMinutes(_darknessService, arguments),
                "stress_darkness_complete_evening" => McpDarknessTools.DarknessCompleteEvening(_darknessService),
                "stress_darkness_reset_therapy" => McpDarknessTools.DarknessResetTherapy(_darknessService),
                "stress_darkness_remission_status" => McpDarknessTools.DarknessRemissionStatus(_darknessRemissionService),
                "stress_darkness_relapse_add" => McpDarknessTools.DarknessRelapseAdd(_darknessRemissionService, arguments),
                "stress_darkness_relapse_set" => McpDarknessTools.DarknessRelapseSet(_darknessRemissionService, arguments),
                "stress_darkness_remission_start" => McpDarknessTools.DarknessRemissionStart(_darknessRemissionService, _data, _darknessService),
                "stress_darkness_remission_clear" => McpDarknessTools.DarknessRemissionClear(_darknessRemissionService),
                "stress_darkness_force_relapse" => McpDarknessTools.DarknessForceRelapse(_darknessRemissionService, _darknessService),
                "stress_social_get" => McpSocialExposureTools.SocialGet(_socialExposureService),
                "stress_social_set" => McpSocialExposureTools.SocialSet(_socialExposureService, arguments),
                "stress_social_add" => McpSocialExposureTools.SocialAdd(_socialExposureService, arguments),
                "stress_social_reset" => McpSocialExposureTools.SocialReset(_socialExposureService),
                "harvey_stress_social_start" => _socialAnxietyTherapyService == null
                    ? "Error: SocialAnxietyTherapyService unavailable."
                    : McpSocialAnxietyTools.SocialStart(
                        _socialAnxietyTherapyService, _treatmentService, _stateService),
                "harvey_stress_social_set_timer" => _socialAnxietyTherapyService == null
                    ? "Error: SocialAnxietyTherapyService unavailable."
                    : McpSocialAnxietyTools.SocialSetTimer(_socialAnxietyTherapyService, arguments),
                "harvey_stress_social_ready" => _socialAnxietyTherapyService == null
                    ? "Error: SocialAnxietyTherapyService unavailable."
                    : McpSocialAnxietyTools.SocialReady(_socialAnxietyTherapyService),
                "harvey_stress_social_complete" => _socialAnxietyTherapyService == null
                    ? "Error: SocialAnxietyTherapyService unavailable."
                    : McpSocialAnxietyTools.SocialComplete(_socialAnxietyTherapyService),
                "harvey_stress_social_reset" => _socialAnxietyTherapyService == null
                    ? "Error: SocialAnxietyTherapyService unavailable."
                    : McpSocialAnxietyTools.SocialReset(_socialAnxietyTherapyService),
                "harvey_stress_debug_state" => _socialAnxietyTherapyService == null
                    ? "Error: SocialAnxietyTherapyService unavailable."
                    : McpSocialAnxietyTools.SocialDebugState(_socialAnxietyTherapyService),
                "mcp_event_snapshot" => McpEventTools.EventSnapshot(),
                "mcp_start_event" => McpEventTools.StartEvent(_monitor, arguments),
                "mcp_end_event" => McpEventTools.EndEvent(arguments),
                "mcp_event_advance" => McpEventTools.AdvanceEvent(arguments),
                "stress_run_test_plan" => McpTestPlanRunner.Run(CreateTestPlanContext(), arguments),
                "stress_force_start" => McpTreatmentTools.ForceStart(
                    _treatmentService, _stateService, arguments),
                "stress_episode_start" => McpTreatmentTools.EpisodeStart(
                    _treatmentEpisodeService, _stateService, _stressLoadService, arguments),
                "mcp_eat_item" => McpPlayerActionTools.EatItem(_gameLogicHandler, _stateService, arguments),
                "mcp_sleep" => McpPlayerActionTools.Sleep(_gameLogicHandler, _stateService, _data, arguments),
                "mcp_save_game" => McpSaveTools.SaveGame(_data, _stressLoadService, _monitor),
                "mcp_reload_save" => McpSaveTools.ReloadSave(
                    _data, _stateService, _darknessService, _stressLoadService, _gameLogicHandler, _monitor),
                _ => $"Error: unknown tool '{toolName}'.",
            };
        }

        private McpTestPlanContext CreateTestPlanContext() => new()
        {
            Monitor = _monitor,
            Data = _data,
            TreatmentService = _treatmentService,
            StateService = _stateService,
            BuffService = _buffService,
            QuestService = _questService,
            StressDialogueService = _stressDialogueService,
            ModResetService = _modResetService,
            TrustService = _harveyCareTrustService,
            StressLoadService = _stressLoadService,
            RescueService = _harveyFlashbackRescueService,
            SafeAuraService = _harveySafePersonAuraService,
            EpisodeService = _treatmentEpisodeService,
            GameLogicHandler = _gameLogicHandler,
            ThunderFlashbackService = _thunderFlashbackService,
        };

        private string? EventBlockMessage(string operation)
        {
            if (!GameStateHelper.IsEventActive())
                return null;

            var eventId = GameStateHelper.GetCurrentEventId() ?? "unknown";
            return $"Error: event active (EventId={eventId}). {operation} blocked.\n\n{TestContextReporter.BuildReport()}";
        }

        private string GameContext()
            => TestContextReporter.BuildReport();

        private string Reset()
        {
            HarveyDevTalkHelper.TryForceCloseDialogue(_monitor);
            _stressDialogueService.ClearPendingTreatment();

            _harveyFlashbackRescueService.SuppressAutomaticRescue = true;
            try
            {
                for (var i = 0; i < 8 && GameStateHelper.IsEventActive(); i++)
                {
                    EventDebugHelper.TryEndEvent(force: true);
                    GameStateHelper.ClearStaleUiFlags();
                }
            }
            finally
            {
                _harveyFlashbackRescueService.SuppressAutomaticRescue = false;
            }

            var result = _modResetService.ResetAll();
            _harveyFlashbackRescueService.ResetRescueState();
            GameStateHelper.ForceClearFadeAndWarpFlags();
            GameStateHelper.ClearStaleUiFlags();
            _stressLoadService.ClearDebugOverrides();
            _stressLoadService.SetGotoroFlashbackActive(false);
            _stressLoadService.Recalculate();
            var sb = new StringBuilder();
            sb.AppendLine("=== stress_reset ===");
            sb.AppendLine($"Removed buffs: {result.RemovedBuffs}");
            sb.AppendLine($"Removed quests: {result.RemovedQuests}");
            sb.AppendLine($"Removed topics: {result.RemovedTopics}");
            sb.AppendLine();
            sb.Append(DialogueState());
            return sb.ToString();
        }

        private string DebugDump()
        {
            return StressDebugDumpReporter.BuildReport(new StressDebugDumpContext
            {
                Monitor = _monitor,
                Data = _data,
                StateService = _stateService,
                StressDialogueService = _stressDialogueService,
                TrustService = _harveyCareTrustService,
                StressLoadService = _stressLoadService,
                RescueService = _harveyFlashbackRescueService,
                SafeAuraService = _harveySafePersonAuraService,
                EpisodeService = _treatmentEpisodeService,
            });
        }

        private string DialogueState()
            => new StressDialogueStateReporter(_data, _stateService, _stressDialogueService, _monitor).BuildReport();

        private string AddDebuff(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "buff_id", out var buffId))
                return "Error: buff_id is required.";

            if (!TreatmentTopics.ImplementedBuffIds.Contains(buffId))
                return $"Error: buff_id '{buffId}' is not in TreatmentTopics.ImplementedBuffIds.";

            if (_stateService.HasActiveTreatmentState(buffId) && _stateService.HasBuffInGame(buffId))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Warning: stress debuff already active: {buffId}");
                sb.Append(FormatBuffReport(buffId));
                sb.AppendLine();
                sb.Append(DialogueState());
                return sb.ToString().TrimEnd();
            }

            _stateService.RemoveImmunity(buffId);
            _data.StressState.LastIssuedDay.Remove(buffId);
            _treatmentService.ApplyStressBuff(buffId, GetDisplayName(buffId));

            var result = new StringBuilder();
            result.AppendLine($"Applied stress debuff: {buffId}");
            result.Append(FormatBuffReport(buffId));
            result.AppendLine();
            result.Append(DialogueState());
            return result.ToString().TrimEnd();
        }

        private string RemoveDebuff(JsonElement? arguments)
        {
            if (!TryGetString(arguments, "buff_id", out var buffId))
                return "Error: buff_id is required.";

            if (!TreatmentTopics.ImplementedBuffIds.Contains(buffId))
                return $"Error: buff_id '{buffId}' is not in TreatmentTopics.ImplementedBuffIds.";

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

            var result = new StringBuilder();
            result.AppendLine($"Removed stress state for {buffId} (treatments={removedTreatments}).");
            result.Append(FormatBuffReport(buffId));
            result.AppendLine();
            result.Append(DialogueState());
            return result.ToString().TrimEnd();
        }

        private string ShowDialogue()
        {
            if (EventBlockMessage("stress_show_dialogue") is { } blocked)
                return blocked;

            if (Game1.activeClickableMenu is DialogueBox)
            {
                return "Error: close the current DialogueBox before stress_show_dialogue.";
            }

            var buffId = _stressDialogueService.CheckForActiveDebuffWithoutTreatment();
            if (string.IsNullOrEmpty(buffId))
            {
                return "Error: no untreated stress debuff eligible for start dialogue.";
            }

            var dialogueText = _stressDialogueService.GetDialogueForBuff(buffId);
            if (string.IsNullOrEmpty(dialogueText))
            {
                return $"Error: dialogue text not found for {buffId}.";
            }

            _stressDialogueService.ShowStressDialogue(buffId, dialogueText);

            var result = new StringBuilder();
            result.AppendLine($"stress_show_dialogue: opened for {buffId}");
            result.AppendLine($"Menu: {Game1.activeClickableMenu?.GetType().Name ?? "null"}");
            result.AppendLine($"Speaker: {Game1.currentSpeaker?.Name ?? "null"}");
            result.AppendLine();
            result.Append(DialogueState());
            return result.ToString().TrimEnd();
        }

        private string TalkHarvey(JsonElement? arguments)
        {
            if (EventBlockMessage("stress_talk_harvey") is { } blocked)
                return blocked;

            if (Game1.activeClickableMenu != null)
                return "Error: close the current menu before stress_talk_harvey.";

            var warp = !(TryGetBool(arguments, "no_warp", out var noWarp) && noWarp);
            var ok = HarveyDevTalkHelper.TryTalkToHarvey(_monitor, warp);

            var result = new StringBuilder();
            result.AppendLine(warp
                ? "talk-harvey: warp (if needed) + checkAction executed."
                : "talk-harvey: checkAction executed (no warp).");
            result.AppendLine($"Success: {ok}");
            result.AppendLine($"Menu: {Game1.activeClickableMenu?.GetType().Name ?? "null"}");
            result.AppendLine($"Speaker: {Game1.currentSpeaker?.Name ?? "null"}");

            if (GameStateHelper.IsEventActive())
            {
                result.AppendLine(
                    $"Warning: event active after talk (EventId={GameStateHelper.GetCurrentEventId() ?? "unknown"}). Stress pipeline blocked.");
            }

            result.AppendLine();
            result.Append(TestContextReporter.BuildReport());
            result.AppendLine();
            result.Append(DialogueState());
            return result.ToString().TrimEnd();
        }

        private string Consent(JsonElement? arguments)
        {
            if (EventBlockMessage("stress_consent") is { } blocked)
                return blocked;
            if (!TryGetString(arguments, "choice", out var choice))
                return "Error: choice is required (accept|decline).";

            var normalized = choice.Trim().ToLowerInvariant();
            if (normalized is not ("accept" or "decline" or "yes" or "no"))
                return "Error: choice must be accept or decline.";

            var accept = normalized is "accept" or "yes";
            var finish = !(TryGetBool(arguments, "no_finish", out var noFinish) && noFinish);

            if (Game1.activeClickableMenu is not DialogueBox)
                return "Error: no DialogueBox open. Run stress_talk_harvey first.";

            if (!HarveyDevTalkHelper.TryChooseConsent(_monitor, accept, finish))
            {
                var result = new StringBuilder();
                result.AppendLine($"Error: could not close/advance stress start dialogue. finish={finish}");
                result.AppendLine();
                result.Append(DialogueState());
                return result.ToString().TrimEnd();
            }

            var ok = new StringBuilder();
            ok.AppendLine($"stress start dialogue: closed/advanced (legacy consent={accept}, finish={finish})");
            ok.AppendLine();
            ok.Append(DialogueState());
            return ok.ToString().TrimEnd();
        }

        private string ListResponses()
        {
            var snapshot = HarveyDevTalkHelper.GetDialogueBoxSnapshot();
            var result = new StringBuilder();
            result.AppendLine("[DialogueResponses]");
            result.AppendLine($"HasDialogueBox={snapshot.HasDialogueBox}, IsQuestion={snapshot.IsQuestion}, Count={snapshot.Responses.Count}");

            if (snapshot.Responses.Count == 0)
            {
                result.AppendLine("(none — advance dialogue or wait for #$y question)");
            }
            else
            {
                foreach (var r in snapshot.Responses)
                    result.AppendLine($"[{r.Index}] {r.ResponseKey}: {r.ResponseText}");
            }

            result.AppendLine();
            result.Append(TestContextReporter.BuildReport());
            return result.ToString().TrimEnd();
        }

        private string ChooseResponse(JsonElement? arguments)
        {
            if (EventBlockMessage("stress_choose_response") is { } blocked)
                return blocked;

            string? responseKey = null;
            int? responseIndex = null;

            if (TryGetString(arguments, "response_key", out var key))
                responseKey = key;
            else if (TryGetInt(arguments, "index", out var idx))
                responseIndex = idx;
            else
                return "Error: response_key or index is required.";

            var advance = !(TryGetBool(arguments, "no_advance", out var noAdvance) && noAdvance);
            var finish = !(TryGetBool(arguments, "no_finish", out var noFinish) && noFinish);

            if (Game1.activeClickableMenu is not DialogueBox)
                return "Error: no DialogueBox open.";

            if (!HarveyDevTalkHelper.TryChooseResponse(
                    _monitor,
                    responseKey: responseKey,
                    responseIndex: responseIndex,
                    advanceToQuestion: advance,
                    finishDialogue: finish))
            {
                var fail = new StringBuilder();
                fail.AppendLine("Error: choose-response failed.");
                fail.AppendLine();
                fail.Append(ListResponses());
                return fail.ToString().TrimEnd();
            }

            var ok = new StringBuilder();
            ok.AppendLine($"choose-response: key={responseKey ?? "null"}, index={responseIndex?.ToString() ?? "null"}, finish={finish}");
            ok.AppendLine();
            ok.Append(DialogueState());
            return ok.ToString().TrimEnd();
        }

        private string CloseDialogue()
        {
            var hadMenu = Game1.activeClickableMenu != null;
            HarveyDevTalkHelper.TryForceCloseDialogue(_monitor);
            _stressDialogueService.ClearPendingTreatment();

            var result = new StringBuilder();
            result.AppendLine(hadMenu
                ? "stress_close_dialogue: menu force-closed."
                : "stress_close_dialogue: no active menu.");
            result.AppendLine();
            result.Append(DialogueState());
            return result.ToString().TrimEnd();
        }

        private string AdvanceDialogue(JsonElement? arguments)
        {
            if (EventBlockMessage("stress_dialogue_advance") is { } blocked)
                return blocked;

            var steps = 1;
            if (TryGetInt(arguments, "steps", out var parsed) && parsed > 0)
                steps = Math.Min(parsed, HarveyDevTalkHelper.MaxDialogueAdvancesPublic);

            if (Game1.activeClickableMenu is not DialogueBox)
                return "Error: no DialogueBox open.";

            HarveyDevTalkHelper.AdvanceDialogue(_monitor, steps);

            var result = new StringBuilder();
            result.AppendLine($"dialogue-advance: {steps} step(s)");
            result.AppendLine();
            result.Append(DialogueState());
            return result.ToString().TrimEnd();
        }

        private string FormatBuffReport(string buffId)
        {
            var treatment = _stateService.GetActiveTreatment(buffId);
            var stressTopicId = GetStressTopicId(buffId);
            var stressTopicActive = stressTopicId != null && ConversationHelper.HasTopic(stressTopicId);

            var sb = new StringBuilder();
            sb.AppendLine("--- buff report ---");
            sb.AppendLine($"buffId: {buffId}");
            sb.AppendLine($"hasBuffInGame: {_stateService.HasBuffInGame(buffId)}");
            sb.AppendLine($"hasActiveTreatmentState: {_stateService.HasActiveTreatmentState(buffId)}");
            sb.AppendLine($"TreatmentStarted: {treatment?.TreatmentStarted ?? false}");
            sb.AppendLine($"ObjectivesCompleted: {treatment?.ObjectivesCompleted ?? false}");
            sb.AppendLine($"AwaitingHarveyReview: {treatment?.AwaitingHarveyReview ?? false}");
            sb.AppendLine($"ReadyForReviewDate: {treatment?.ReadyForReviewDate?.ToString() ?? "(null)"}");
            sb.AppendLine($"QuestId: {treatment?.QuestId ?? "(empty)"}");
            sb.AppendLine($"BuffId: {buffId}");
            sb.AppendLine($"Next step: {TreatmentNextStep.Resolve(treatment, _stateService.HasBuffInGame(buffId))}");
            sb.AppendLine($"stress topic active: {stressTopicActive}");
            if (TreatmentTopics.GetReadyForReviewTopic(buffId) is { } reviewTopic)
                sb.AppendLine($"readyForReview topic active: {ConversationHelper.HasTopic(reviewTopic)}");
            return sb.ToString();
        }

        private static string GetDisplayName(string buffId)
            => BuffDisplayNames.TryGetValue(buffId, out var name) ? name : buffId;

        private static string? GetStressTopicId(string buffId)
        {
            if (StressDebuffSelector.BuffToStressTopic.TryGetValue(buffId, out var stressPair))
                return stressPair.topic;

            if (buffId == BuffIds.Darkness)
                return TopicIds.StressDarkness;

            return null;
        }

        private static IEnumerable<string> GetRelatedTopicIds(string buffId)
        {
            var stressTopicId = GetStressTopicId(buffId);
            if (stressTopicId != null)
                yield return stressTopicId;

            if (TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followup))
                yield return followup;

            if (TreatmentTopics.LegacyStartByBuff.TryGetValue(buffId, out var legacyStart))
                yield return legacyStart;

            var suffix = buffId.StartsWith("buffStress", StringComparison.Ordinal)
                ? buffId["buffStress".Length..]
                : buffId;
            yield return $"topicStressTreatment{suffix}Cured";
            yield return $"topicStressTreatment{suffix}ReadyForReview";
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

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            return el.TryGetInt32(out value);
        }

    }
}
