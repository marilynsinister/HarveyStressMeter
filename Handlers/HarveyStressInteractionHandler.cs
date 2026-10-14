using HarveyOverhaul.Core.Api;
using HarveyOverhaul.Core.Services;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Перехват action-клика по Харви до vanilla/spouse dialogue.
    /// Приоритет: review → start/progress → vanilla.
    /// </summary>
    public sealed class HarveyStressInteractionHandler
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly StressDialogueService _stressDialogueService;
        private readonly StressTreatmentReviewService _reviewService;
        private readonly StressMedicalIntentProvider _medicalIntentProvider;
        private IHarveyCoreApi? _coreApi;
        private DarknessService? _darknessService;

        public void SetDarknessService(DarknessService darknessService) => _darknessService = darknessService;

        public HarveyStressInteractionHandler(
            IMonitor monitor,
            IModHelper helper,
            StressDialogueService stressDialogueService,
            StressTreatmentReviewService reviewService,
            StressMedicalIntentProvider medicalIntentProvider)
        {
            _monitor = monitor;
            _helper = helper;
            _stressDialogueService = stressDialogueService;
            _reviewService = reviewService;
            _medicalIntentProvider = medicalIntentProvider;
        }

        public void SetCoreApi(IHarveyCoreApi? coreApi) => _coreApi = coreApi;

        public bool TryHandleHarveyStressInteraction(ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsPlayerFree)
                return false;

            if (GameStateHelper.IsEventActive())
                return false;

            if (Game1.activeClickableMenu is DialogueBox)
                return false;

            // Клик уже обработал другой мод Harvey Overhaul (меню палаты, вопрос о травме) — не показываем второй диалог.
            if (e.IsSuppressed())
                return false;

            if (!e.Button.IsActionButton())
                return false;

            var location = Game1.currentLocation;
            if (location == null)
                return false;

            var pendingReview = _reviewService.TryFindAnyTreatmentAwaitingReview(
                out var pendingEpisode,
                out var pendingBuff,
                repairStuck: false);

            var cursorTile = _helper.Input.GetCursorPosition().GrabTile;
            var harvey = HarveyHelper.TryGetInteractedHarvey(location, cursorTile, lenientDistance: pendingReview);
            if (harvey == null)
                return false;

            LogHarveyClicked(pendingReview, pendingEpisode, pendingBuff);

            if (TryFinalizeDarknessStep1(harvey))
            {
                SuppressHarveyClickButtons(e);
                return true;
            }

            if (ShouldDeferToHigherPriorityMedicalIntent(pendingBuff))
                return false;

            if (TryShowProgrammaticDialogue(harvey, pendingReview))
            {
                SuppressHarveyClickButtons(e);
                return true;
            }

            _coreApi?.SetHarveyClickGate(false, "StressHandler: no programmatic dialogue");
            _coreApi?.LogHarveyClickDiagnostics("stress-fallback");
            _monitor.Log(
                "[HarveyStress] Harvey clicked, no pending review. Falling back to vanilla dialogue.",
                LogLevel.Debug);
            return false;
        }

        /// <summary>
        /// Harmony/checkAction: перехват review до vanilla/spouse dialogue (action-кнопка, FarmHouse spouse).
        /// </summary>
        public bool TryInterceptHarveyInteraction(NPC harvey, Farmer who, GameLocation location)
        {
            if (!HarveyHelper.IsHarvey(harvey))
                return false;

            if (!Context.IsWorldReady || GameStateHelper.IsEventActive())
                return false;

            if (Game1.activeClickableMenu != null)
                return false;

            if (harvey.currentLocation != location || who.currentLocation != location)
                return false;

            if (TryFinalizeDarknessStep1(harvey))
                return true;

            if (_reviewService.TryFindAnyTreatmentAwaitingReview(
                    out var pendingEpisode,
                    out var pendingBuff,
                    repairStuck: true))
            {
                LogHarveyClicked(pendingReview: true, pendingEpisode, pendingBuff);

                if (ShouldDeferToHigherPriorityMedicalIntent(pendingBuff))
                    return false;

                if (_stressDialogueService.TryShowProgrammaticReviewDialogue(harvey))
                {
                    _coreApi?.SetHarveyClickGate(false, "StressHandler: checkAction review shown");
                    _coreApi?.LogHarveyClickDiagnostics("stress-checkAction-review");
                    _monitor.Log(
                        "[HarveyStress] checkAction intercepted — programmatic review before vanilla dialogue.",
                        LogLevel.Info);
                    return true;
                }

                LogAnxietySpikeStuckStates();
                return false;
            }

            if (ShouldDeferToHigherPriorityMedicalIntent(null))
                return false;

            if (_stressDialogueService.TryShowTreatmentStartOrProgressDialogue(harvey))
            {
                _coreApi?.SetHarveyClickGate(false, "StressHandler: checkAction treatment start shown");
                _coreApi?.LogHarveyClickDiagnostics("stress-checkAction-start");
                _monitor.Log(
                    "[HarveyStress] checkAction intercepted — programmatic treatment start before spouse dialogue.",
                    LogLevel.Info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Три вечера дома при свете засчитаны — Харви подводит итог и закрывает шаг 1 сразу по клику,
        /// даже если сегодня с ним уже говорили. Более срочный приём (Core выбрал другой интент) — первым.
        /// </summary>
        private bool TryFinalizeDarknessStep1(NPC harvey)
        {
            if (TryGiveDarknessLantern(harvey))
                return true;

            if (_darknessService?.IsStep1ReadyForHarvey != true)
                return false;

            if (ShouldDeferToHigherPriorityMedicalIntent(BuffIds.Darkness))
                return false;

            bool relapse = _darknessService.IsRelapseStep1;
            if (!_darknessService.FinalizeStep1FromHarveyClick())
                return false;

            string line = relapse
                ? "Три вечера дома при свете. Ты держала слово — и страх снова отступил.$h#$b#Рецидив закрыт. Если темнота вернётся, ты знаешь дорогу: свет, дом и разговор со мной.$l"
                : "Три вечера дома при свете — я видел, ты держала слово.$h#$b#Первый шаг пройден. Дальше — короткие вечерние выходы: остановка и город, с восьми до десяти. И сразу домой.$0";

            Game1.DrawDialogue(new Dialogue(harvey, null, line));
            _coreApi?.SetHarveyClickGate(false, "StressHandler: darkness step 1 finalized");
            _coreApi?.LogHarveyClickDiagnostics("stress-darkness-step1");
            _monitor.Log("[HarveyStress] Darkness step 1 finalized by Harvey click.", LogLevel.Info);
            return true;
        }

        /// <summary>После шага 2 Харви отдаёт свой фонарь и отправляет в горы (или выдаёт новый взамен потерянного).</summary>
        private bool TryGiveDarknessLantern(NPC harvey)
        {
            if (_darknessService?.NeedsLanternFromHarvey != true)
                return false;

            if (ShouldDeferToHigherPriorityMedicalIntent(BuffIds.Darkness))
                return false;

            bool replacement = _darknessService.GiveLanternFromHarvey();
            string line = replacement
                ? "Фонарь потерялся? Ничего. У меня есть второй — я всегда держу запасной.$0#$b#Береги его. И помни: две минуты в горах ночью, не больше. Потом домой.$s"
                : "Возьми. Это мой фонарь — я носил его на ночных вызовах в Зузу.$s#$b#Он не волшебный. Просто когда держишь в руке свет, темнота становится местом, а не врагом.$0#$b#Финал такой: ночью, с десяти до двух, поднимись в горы. Две минуты с фонарём — и сразу домой. Утром расскажешь.$a";

            Game1.DrawDialogue(new Dialogue(harvey, null, line));
            Game1.playSound("coin");
            _coreApi?.SetHarveyClickGate(false, "StressHandler: darkness lantern given");
            _coreApi?.LogHarveyClickDiagnostics("stress-darkness-lantern");
            return true;
        }

        private bool ShouldDeferToHigherPriorityMedicalIntent(string? stressBuffId)
        {
            if (_coreApi == null)
                return false;

            var resolution = _coreApi.PrepareHarveyMedicalClick(logDetails: true);
            var selected = resolution.Selected;
            if (selected == null)
                return false;

            if (!string.Equals(selected.ProviderId, HarveyProviderRegistry.StressProviderId, StringComparison.Ordinal))
            {
                _coreApi.SetHarveyClickGate(
                    true,
                    $"StressHandler deferred: Core selected {selected.ProviderId}/{selected.StateId}");
                _coreApi.LogHarveyClickDiagnostics("stress-suppressed");
                return true;
            }

            if (!string.IsNullOrEmpty(stressBuffId)
                && !_medicalIntentProvider.CanShowStressIntent(stressBuffId))
            {
                _coreApi.SetHarveyClickGate(
                    true,
                    $"StressHandler deferred: Core selected stress/{selected.StateId}");
                _coreApi.LogHarveyClickDiagnostics("stress-suppressed");
                return true;
            }

            return false;
        }

        private bool TryShowProgrammaticDialogue(NPC harvey, bool pendingReviewKnown)
        {
            if (_stressDialogueService.TryShowProgrammaticReviewDialogue(harvey))
            {
                _coreApi?.SetHarveyClickGate(false, "StressHandler: programmatic review shown");
                _coreApi?.LogHarveyClickDiagnostics("stress-review");
                _monitor.Log(
                    "[HarveyStress] Showing programmatic review before vanilla dialogue.",
                    LogLevel.Info);
                return true;
            }

            if (pendingReviewKnown)
                LogAnxietySpikeStuckStates();

            if (_stressDialogueService.TryShowTreatmentStartOrProgressDialogue(harvey))
            {
                _coreApi?.SetHarveyClickGate(false, "StressHandler: programmatic treatment shown");
                _coreApi?.LogHarveyClickDiagnostics("stress-treatment");
                _monitor.Log(
                    "[HarveyStress] Showing programmatic treatment dialogue before vanilla dialogue.",
                    LogLevel.Info);
                return true;
            }

            return false;
        }

        private void LogHarveyClicked(bool pendingReview, string? episode, string? buff)
        {
            var locationName = Game1.currentLocation?.Name ?? "(null)";
            _monitor.Log(
                $"[HarveyStress] Harvey clicked. PendingReview={pendingReview}, Episode={episode ?? "(none)"}, " +
                $"Buff={buff ?? "(none)"}, location={locationName}",
                LogLevel.Info);
        }

        private void LogAnxietySpikeStuckStates()
        {
            var episode = _reviewService.GetTreatmentEpisodeAwaitingReview();
            if (episode?.EpisodeId == StressEpisodes.AnxietySpike)
            {
                _monitor.Log(
                    "[HarveyStress] AnxietySpike stuck: AwaitingHarveyReview but no review dialogue.",
                    LogLevel.Warn);
                return;
            }

            _reviewService.TryFindAnyTreatmentAwaitingReview(out var episodeId, out _, repairStuck: false);
            if (episodeId == StressEpisodes.AnxietySpike)
            {
                _monitor.Log(
                    "[HarveyStress] AnxietySpike stuck: objectives met but no AwaitingHarveyReview.",
                    LogLevel.Warn);
            }
        }

        private void SuppressHarveyClickButtons(ButtonPressedEventArgs e)
        {
            _helper.Input.Suppress(e.Button);
            _helper.Input.Suppress(SButton.MouseLeft);
        }
    }
}
