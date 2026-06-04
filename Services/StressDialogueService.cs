using System;

using System.Collections.Generic;

using System.Linq;

using StardewModdingAPI;

using StardewValley;

using StardewValley.Menus;

using HarveyStressMeter.Models;

using HarveyStressMeter.Constants;

using HarveyStressMeter.Helpers;



namespace HarveyStressMeter.Services

{

    /// <summary>

    /// Сервис для программного показа диалогов стресса

    /// </summary>

    public class StressDialogueService

    {

        private readonly IMonitor _monitor;

        private readonly IModHelper _helper;

        private readonly StateService _stateService;

        private readonly SaveData _data;

        private readonly TreatmentService _treatmentService;

        private readonly TreatmentEpisodeService _episodeService;

        private readonly StressTreatmentReviewService _reviewService;
        private HarveyCareTrustDialogueService? _trustDialogueService;

        private StressDialoguesContainer? _dialoguesData;

        private string? _pendingBuffIdForTreatment;

        private bool _autoStartTreatmentAfterClose = true;

        private string? _pendingEpisodeIdForTreatment;

        private string? _selectedEpisodeIdForDialogue;

        

        // ⭐ ИСПРАВЛЕНИЕ: Флаг для предотвращения бесконечного цикла при показе диалогов

        private bool _isShowingDialogue = false;

        private string? _deferredBuffId;

        private string? _deferredDialogueText;

        private enum StressHarveyDialogueMode

        {

            None,

            Review,

            Start,

            Reminder,

            Ambient,

        }

        private StressHarveyDialogueMode _activeStressDialogueMode = StressHarveyDialogueMode.None;



        /// <summary>Открыт programmatic stress-диалог — fallback-топики добавлять нельзя.</summary>

        public bool IsShowingStressDialogue => _isShowingDialogue;



        /// <summary>Stress-dialogue отложен до закрытия текущего DialogueBox.</summary>

        public bool HasDeferredStressDialogue => _deferredBuffId != null;



        public StressDialogueService(

            IMonitor monitor,

            IModHelper helper,

            SaveData data,

            StateService stateService,

            TreatmentService treatmentService,

            TreatmentEpisodeService episodeService,

            StressTreatmentReviewService reviewService)

        {

            _monitor = monitor;

            _helper = helper;

            _data = data;

            _stateService = stateService;

            _treatmentService = treatmentService;

            _episodeService = episodeService;

            _reviewService = reviewService;

        }

        public void SetTrustDialogueService(HarveyCareTrustDialogueService trustDialogueService)
            => _trustDialogueService = trustDialogueService;

        /// <summary>

        /// Загружает диалоги из JSON при старте мода

        /// </summary>

        public void LoadDialogues()

        {

            try

            {

                _dialoguesData = _helper.Data.ReadJsonFile<StressDialoguesContainer>("assets/stress_dialogues.json")

                    ?? new StressDialoguesContainer();

                var flowDialogues = _helper.Data.ReadJsonFile<StressDialoguesContainer>("assets/stress_flow_dialogues.json");

                if (flowDialogues?.Dialogues.Count > 0)

                {

                    _dialoguesData.Dialogues.AddRange(flowDialogues.Dialogues);

                }

                

                if (_dialoguesData.Dialogues.Count == 0)

                {

                    _monitor.Log("[StressDialogueService] Не удалось загрузить диалоги стресса", LogLevel.Error);

                }

                else

                {

                    _monitor.Log(

                        $"[StressDialogueService] ✅ Загружено {_dialoguesData.Dialogues.Count} диалогов стресса (legacy + flow)",

                        LogLevel.Info);

                }

            }

            catch (Exception ex)

            {

                _monitor.Log($"[StressDialogueService] ОШИБКА при загрузке диалогов: {ex.Message}", LogLevel.Error);

                _dialoguesData = new StressDialoguesContainer();

            }

        }



        /// <summary>

        /// Проверяет episode-кандидата для старта лечения (StressLoad model).

        /// </summary>

        public EpisodeSelectionResult? CheckForEpisodeStart()

        {

            if (_dialoguesData == null)

                return null;



            if (_isShowingDialogue)

            {

                _monitor.Log("[StressDialogueService] Диалог уже показывается, пропускаем episode check", LogLevel.Debug);

                return null;

            }



            var selection = _episodeService.EvaluateSelection();

            if (selection.Action != EpisodeSelectionAction.StartEpisode)

            {

                if (selection.Action != EpisodeSelectionAction.None)

                {

                    _monitor.Log(

                        $"[StressDialogue] Episode selection: {selection.Action}, episode={selection.EpisodeId ?? "(none)"}, reason={selection.Reason}",

                        LogLevel.Debug);

                }

                return null;

            }



            if (selection.MatchingEpisodeIds.Count > 1)

            {

                _monitor.Log(

                    $"[StressDialogue] Multiple episode candidates: {string.Join(", ", selection.MatchingEpisodeIds)}. Selected: {selection.EpisodeId}",

                    LogLevel.Debug);

            }



            if (!string.IsNullOrEmpty(selection.PrimaryBuffId)

                && CannotOfferStartDialogueAgain(selection.PrimaryBuffId, selection.EpisodeId, out var blockReason))

            {

                _monitor.Log(

                    $"[StressDialogue] Episode start blocked ({selection.EpisodeId}, buff={selection.PrimaryBuffId}): {blockReason}",

                    LogLevel.Debug);

                return null;

            }



            return selection;

        }



        /// <summary>

        /// Legacy: проверяет наличие активного untreated stress debuff с наивысшим приоритетом.

        /// </summary>

        public string? CheckForActiveDebuffWithoutTreatment()

        {

            if (_dialoguesData == null) return null;



            if (_isShowingDialogue)

            {

                _monitor.Log("[StressDialogueService] Диалог уже показывается, пропускаем проверку", LogLevel.Debug);

                return null;

            }



            string? selected = SelectStartEligibleUntreatedBuff();



            if (selected == null)

                return null;



            var otherUntreated = StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data)

                .Where(id => id != selected)

                .ToList();



            if (otherUntreated.Count > 0)

            {

                _monitor.Log(

                    $"[StressDialogue] Multiple active stress debuffs found: {string.Join(", ", otherUntreated.Prepend(selected))}. Selected: {selected}",

                    LogLevel.Debug);

            }

            else

            {

                _monitor.Log($"[StressDialogueService] Найден активный untreated debuff: {selected}", LogLevel.Debug);

            }



            return selected;

        }



        /// <summary>

        /// Получает текст диалога для конкретного баффа в зависимости от отношений с Харви

        /// </summary>

        public string? GetDialogueForBuff(string buffId)

        {

            var dialogueKey = DarknessLegacyHelper.IsDarknessLevelBuff(buffId)
                ? BuffIds.Darkness
                : buffId;

            return GetDialogueByKey(dialogueKey);

        }



        /// <summary>Диалог по ключу (episode_*, ambient_*, reminder_*).</summary>

        public string? GetDialogueByKey(string dialogueKey, bool preferWarTraumaVariant = false)

        {

            if (_dialoguesData == null || string.IsNullOrEmpty(dialogueKey))

                return null;



            if (preferWarTraumaVariant)

            {

                var warKey = dialogueKey + StressDialogueKeys.WarTraumaSuffix;

                var warDialogue = FindDialogueEntry(warKey);

                if (warDialogue != null)

                    return FormatDialogue(warDialogue, warKey);

            }



            var entry = FindDialogueEntry(dialogueKey);

            if (entry == null)

            {

                _monitor.Log($"[StressDialogueService] Не найден диалог для ключа {dialogueKey}", LogLevel.Warn);

                return null;

            }



            return FormatDialogue(entry, dialogueKey);

        }



        public string? GetAmbientDialogue(string causeId)

            => GetDialogueByKey(StressDialogueKeys.AmbientForCause(causeId));



        public string? GetReminderDialogue(string? episodeId = null)

        {

            var keys = new[]

            {

                StressDialogueKeys.ReminderActiveTreatment1,

                StressDialogueKeys.ReminderActiveTreatment2,

                StressDialogueKeys.ReminderActiveTreatment3,

            };



            if (!string.IsNullOrEmpty(episodeId)

                && TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition)

                && !string.IsNullOrEmpty(definition.ReminderDialogueKey))

            {

                var episodeReminder = GetDialogueByKey(definition.ReminderDialogueKey);

                if (episodeReminder != null)

                    return episodeReminder;

            }



            var index = Math.Abs(Game1.dayOfMonth + Game1.timeOfDay) % keys.Length;

            return GetDialogueByKey(keys[index]);

        }



        public string? GetReviewDialogueForEpisode(string episodeId)

        {

            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))

                return null;



            var preferWarTrauma = episodeId == StressEpisodes.GotoroFlashback

                && _episodeService.BuildContext().WarTraumaFlag

                && ShouldUseWarTraumaTone();



            var reviewKey = !string.IsNullOrEmpty(definition.ReadyForReviewDialogueKey)

                ? definition.ReadyForReviewDialogueKey

                : StressDialogueKeys.EpisodeReview(episodeId);



            return GetDialogueByKey(reviewKey, preferWarTrauma);

        }



        public string? ResolveAmbientCauseId(IReadOnlyCollection<string> activeCauseIds)

        {

            var ambientOrder = new[]

            {

                StressCauses.Hunger,

                StressCauses.TooCold,

                StressCauses.Tired,

                StressCauses.Darkness,

                StressCauses.Social,

                StressCauses.Lonely,

            };



            foreach (var causeId in ambientOrder)

            {

                if (!activeCauseIds.Contains(causeId))

                    continue;

                if (!StressCauses.CanSelfResolve(causeId))

                    continue;

                if (GetAmbientDialogue(causeId) != null)

                    return causeId;

            }



            return null;

        }



        private static string? FirstNonEmpty(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return null;
        }

        private StressDialogueData? FindDialogueEntry(string key)

        {

            return _dialoguesData?.Dialogues.FirstOrDefault(d =>

                string.Equals(d.DialogueKey, key, StringComparison.Ordinal)

                || string.Equals(d.BuffId, key, StringComparison.Ordinal));

        }



        private string FormatDialogue(StressDialogueData dialogueData, string key)

        {

            var harvey = Game1.getCharacterFromName("Harvey");

            if (harvey == null)

                return null;



            var friendship = Game1.player.getFriendshipHeartLevelForNPC("Harvey");

            var isMarried = Game1.player.spouse == "Harvey";

            var isDating = !isMarried

                && Game1.player.friendshipData.TryGetValue("Harvey", out var data)

                && data.Status == FriendshipStatus.Dating;



            string? dialogue = isMarried
                ? FirstNonEmpty(dialogueData.DialogueMarried, dialogueData.DialogueDating, dialogueData.DialogueHighFriendship)
                : isDating
                    ? FirstNonEmpty(dialogueData.DialogueDating, dialogueData.DialogueHighFriendship, dialogueData.DialogueMediumFriendship)
                    : friendship >= 7
                        ? FirstNonEmpty(dialogueData.DialogueHighFriendship, dialogueData.DialogueMediumFriendship, dialogueData.DialogueLowFriendship)
                        : friendship >= 3
                            ? FirstNonEmpty(dialogueData.DialogueMediumFriendship, dialogueData.DialogueLowFriendship)
                            : dialogueData.DialogueLowFriendship;

            if (string.IsNullOrWhiteSpace(dialogue))
            {
                _monitor.Log($"[StressDialogueService] Пустой текст диалога для ключа {key}", LogLevel.Warn);
                return null;
            }

            dialogue = dialogue.Replace("@", Game1.player.Name);



            _monitor.Log(

                $"[StressDialogueService] Диалог {key} (friendship={friendship}, married={isMarried}, dating={isDating})",

                LogLevel.Debug);



            return dialogue;

        }



        private static bool ShouldUseWarTraumaTone()

        {

            var friendship = Game1.player.getFriendshipHeartLevelForNPC("Harvey");

            var isMarried = Game1.player.spouse == "Harvey";

            var isDating = !isMarried

                && Game1.player.friendshipData.TryGetValue("Harvey", out var data)

                && data.Status == FriendshipStatus.Dating;

            return isMarried || isDating || friendship >= 7;

        }



        public string? GetDialogueForEpisode(string episodeId, string primaryBuffId)

        {

            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))

            {

                _monitor.Log($"[StressDialogueService] Episode definition not found: {episodeId}", LogLevel.Warn);

                return GetDialogueForBuff(primaryBuffId);

            }



            var preferWarTrauma = episodeId == StressEpisodes.GotoroFlashback

                && _episodeService.BuildContext().WarTraumaFlag

                && ShouldUseWarTraumaTone();



            var startKey = !string.IsNullOrEmpty(definition.StartDialogueKey)

                ? definition.StartDialogueKey

                : StressDialogueKeys.EpisodeStart(episodeId);



            _monitor.Log(

                $"[StressDialogueService] Episode start dialogue {startKey} (episode={episodeId}, warTrauma={preferWarTrauma})",

                LogLevel.Debug);



            return GetDialogueByKey(startKey, preferWarTrauma)

                ?? GetDialogueForBuff(primaryBuffId);

        }



        /// <summary>

        /// Откладывает stress-dialogue до закрытия текущего DialogueBox (без принудительного закрытия меню).

        /// </summary>

        public void ScheduleDeferredStressDialogue(string buffId, string dialogueText)

        {

            _deferredBuffId = buffId;

            _deferredDialogueText = dialogueText;

            _monitor.Log(

                $"[StressDialogue] DialogueBox already active — stress start dialogue deferred until close (buff={buffId})",

                LogLevel.Debug);

        }



        /// <summary>

        /// Показывает отложенный stress-dialogue после закрытия CP/vanilla DialogueBox.

        /// </summary>

        public bool TryShowDeferredStressDialogue()

        {

            if (_deferredBuffId == null || _deferredDialogueText == null)

                return false;



            var harvey = Game1.getCharacterFromName("Harvey");

            if (harvey == null)

            {

                _monitor.Log("[StressDialogueService] Harvey не найден — deferred stress dialogue skipped", LogLevel.Warn);

                ClearDeferredStressDialogue();

                return false;

            }



            if (!EnsurePipelineGuard(nameof(TryShowDeferredStressDialogue), requireDialogueBox: false, knownHarveyNpc: harvey))

                return false;



            if (Game1.activeClickableMenu is DialogueBox)

            {

                _monitor.Log(

                    "[StressDialogue] DialogueBox already active — stress dialogue deferred/skipped",

                    LogLevel.Debug);

                return false;

            }



            var buffId = _deferredBuffId;

            var dialogueText = _deferredDialogueText;



            if (CannotOfferStartDialogueAgain(buffId, null, out var blockReason)

                || _stateService.WasTreatmentOfferShownToday(buffId))

            {

                _monitor.Log(

                    $"[StressDialogue] Deferred start skipped (buff={buffId}): {blockReason ?? "OfferShownToday"}",

                    LogLevel.Debug);

                ClearDeferredStressDialogue();

                return false;

            }



            ClearDeferredStressDialogue();



            _activeStressDialogueMode = StressHarveyDialogueMode.Start;

            _autoStartTreatmentAfterClose = true;

            DisplayStressDialogue(harvey, buffId, dialogueText);

            return true;

        }



        /// <summary>

        /// Показывает стартовую реплику лечения стресса (без #$y-вопроса).

        /// </summary>

        public void ShowStressDialogue(string buffId, string dialogueText)

        {

            if (_activeStressDialogueMode == StressHarveyDialogueMode.Start

                && CannotOfferStartDialogueAgain(buffId, _selectedEpisodeIdForDialogue, out var blockReason))

            {

                _monitor.Log(

                    $"[StressDialogue] ShowStressDialogue blocked (buff={buffId}): {blockReason}",

                    LogLevel.Debug);

                _selectedEpisodeIdForDialogue = null;

                _activeStressDialogueMode = StressHarveyDialogueMode.None;

                return;

            }

            var harvey = Game1.getCharacterFromName("Harvey");

            if (harvey == null)

            {

                _monitor.Log("[StressDialogueService] Harvey не найден!", LogLevel.Error);

                return;

            }



            if (!EnsurePipelineGuard(nameof(ShowStressDialogue), requireDialogueBox: false, knownHarveyNpc: harvey))

                return;



            if (Game1.activeClickableMenu is DialogueBox)

            {

                // HandleHarveyDialogue вызывается уже при открытом DialogueBox — заменяем, не откладываем.

                if (Game1.currentSpeaker is NPC { Name: "Harvey" })

                {

                    DisplayStressDialogue(harvey, buffId, dialogueText, _selectedEpisodeIdForDialogue);

                    _selectedEpisodeIdForDialogue = null;

                    return;

                }



                ScheduleDeferredStressDialogue(buffId, dialogueText);

                return;

            }



            DisplayStressDialogue(harvey, buffId, dialogueText, _selectedEpisodeIdForDialogue);

            _selectedEpisodeIdForDialogue = null;

        }



        private void DisplayStressDialogue(NPC harvey, string buffId, string dialogueText, string? episodeId = null)

        {

            if (_activeStressDialogueMode == StressHarveyDialogueMode.Start

                && CannotOfferStartDialogueAgain(buffId, episodeId, out var blockReason))

            {

                _monitor.Log(

                    $"[StressDialogue] DisplayStressDialogue blocked (buff={buffId}, episode={episodeId ?? "(none)"}): {blockReason}",

                    LogLevel.Debug);

                _pendingBuffIdForTreatment = null;

                _pendingEpisodeIdForTreatment = null;

                _isShowingDialogue = false;

                _activeStressDialogueMode = StressHarveyDialogueMode.None;

                return;

            }

            if (_autoStartTreatmentAfterClose)

            {

                _pendingBuffIdForTreatment = buffId;

                _pendingEpisodeIdForTreatment = episodeId;

            }

            else

            {

                _pendingBuffIdForTreatment = null;

                _pendingEpisodeIdForTreatment = null;

            }

            _isShowingDialogue = true;



            if (_activeStressDialogueMode == StressHarveyDialogueMode.Start)

                _treatmentService.RemovePreConsentStressTopics(buffId);

            harvey.CurrentDialogue.Clear();

            var dialogue = new Dialogue(harvey, null, dialogueText);

            harvey.CurrentDialogue.Push(dialogue);

            Game1.drawDialogue(harvey);



            if (_activeStressDialogueMode is StressHarveyDialogueMode.Start or StressHarveyDialogueMode.Ambient)

                _stateService.MarkTreatmentOfferShown(buffId);

            _monitor.Log(

                $"[StressDialogueService] ✅ Показана реплика mode={_activeStressDialogueMode} episode={episodeId ?? "(none)"} buff={buffId}",

                LogLevel.Info);

        }



        /// <summary>

        /// После закрытия programmatic stress-диалога автоматически запускает лечение/квест.

        /// </summary>

        public void CheckAndStartTreatmentAfterDialogue()

        {

            if (string.IsNullOrEmpty(_pendingBuffIdForTreatment) && string.IsNullOrEmpty(_pendingEpisodeIdForTreatment))

            {

                _isShowingDialogue = false;

                _activeStressDialogueMode = StressHarveyDialogueMode.None;

                return;

            }



            if (!EnsurePipelineGuard(

                    nameof(CheckAndStartTreatmentAfterDialogue),

                    requireDialogueBox: false,

                    requireHarveySpeaker: false))

            {

                _isShowingDialogue = false;

                return;

            }



            var buffId = _pendingBuffIdForTreatment;

            var episodeId = _pendingEpisodeIdForTreatment;

            _pendingBuffIdForTreatment = null;

            _pendingEpisodeIdForTreatment = null;

            _isShowingDialogue = false;

            _activeStressDialogueMode = StressHarveyDialogueMode.None;



            if (IsTreatmentStartComplete(

                    DarknessLegacyHelper.ResolveTreatmentBuffId(buffId ?? ""),

                    episodeId,

                    out var skipReason))

            {

                _monitor.Log(

                    $"[StressDialogue] Auto-start skipped (buff={buffId}, episode={episodeId ?? "(none)"}): {skipReason}",

                    LogLevel.Debug);

                ClearDeferredStressDialogue();

                return;

            }



            if (!string.IsNullOrEmpty(episodeId))

            {

                _monitor.Log(

                    $"[StressDialogueService] Старт TreatmentEpisode {episodeId} (primary buff {buffId})",

                    LogLevel.Info);

                _treatmentService.StartTreatmentEpisode(episodeId, suppressPostStartBubble: true);

                ClearDeferredStressDialogue();

                return;

            }



            if (string.IsNullOrEmpty(buffId))

                return;



            var treatmentBuffId = DarknessLegacyHelper.ResolveTreatmentBuffId(buffId);

            var dialogueData = _dialoguesData?.Dialogues.FirstOrDefault(d =>
                d.BuffId == treatmentBuffId || d.BuffId == buffId);

            var displayName = dialogueData?.DisplayName
                ?? (DarknessLegacyHelper.IsDarknessLevelBuff(buffId) ? "Страх темноты" : buffId);



            _monitor.Log(

                $"[StressDialogueService] Старт лечения/квеста для {treatmentBuffId} (dialogue buff {buffId}) после реплики Харви",

                LogLevel.Info);

            _treatmentService.StartTreatment(treatmentBuffId, displayName, suppressPostStartBubble: true);

            ClearDeferredStressDialogue();

        }



        /// <summary>

        /// Сбрасывает ожидающий auto-start (выход в меню, загрузка save и т.д.).

        /// </summary>

        public void ClearPendingTreatment()

        {

            _pendingBuffIdForTreatment = null;

            _pendingEpisodeIdForTreatment = null;

            _selectedEpisodeIdForDialogue = null;

            _isShowingDialogue = false;

            _activeStressDialogueMode = StressHarveyDialogueMode.None;

            ClearDeferredStressDialogue();

        }



        private void ClearDeferredStressDialogue()

        {

            _deferredBuffId = null;

            _deferredDialogueText = null;

        }



        /// <summary>

        /// Проверяет, нужно ли показать диалог стресса при клике на Харви.

        /// Один клик — один режим: review → start → reminder → ambient.

        /// </summary>

        public bool ShouldShowStressDialogue(out string? buffId, out string? dialogueText)

        {

            buffId = null;

            dialogueText = null;



            if (!EnsurePipelineGuard(nameof(ShouldShowStressDialogue)))

                return false;



            PurgeStaleDeferredStartDialogue();



            var curedTopics = new[]

            {

                "topicStressTreatmentSocialCured",

                "topicStressTreatmentTiredCured",

                "topicStressTreatmentLonelyCured",

                "topicStressTreatmentThunderCured",

                "topicStressTreatmentHungerCured",

                "topicStressTreatmentOverworkCured",

                "topicStressTreatmentNoSleepCured",

                "topicStressTreatmentTooColdCured"

            };



            foreach (var topic in curedTopics)

            {

                if (ConversationHelper.HasTopic(topic))

                {

                    _monitor.Log($"[StressDialogueService] Обнаружен топик завершения {topic}, пропускаем программный диалог", LogLevel.Debug);

                    return false;

                }

            }



            _selectedEpisodeIdForDialogue = null;

            _activeStressDialogueMode = StressHarveyDialogueMode.None;

            _autoStartTreatmentAfterClose = false;



            var episodeSelection = _episodeService.EvaluateSelection();



            // 1. review — цель квеста выполнена, завершение через разговор

            if (TrySelectReviewDialogue(out buffId, out dialogueText))

            {

                _activeStressDialogueMode = StressHarveyDialogueMode.Review;

                _autoStartTreatmentAfterClose = false;

                _monitor.Log(

                    $"[StressDialogueService] Mode=review (buff={buffId})",

                    LogLevel.Debug);

                return true;

            }



            if (_trustDialogueService != null

                && _trustDialogueService.TrySelectTrustProgressDialogue(

                    episodeSelection,

                    out _,

                    out var trustText))

            {

                buffId = BuffIds.TrustProgress;

                dialogueText = trustText;

                _autoStartTreatmentAfterClose = false;

                _monitor.Log("[StressDialogueService] HarveyCareTrust progress dialogue", LogLevel.Debug);

                return true;

            }



            if (episodeSelection.Action == EpisodeSelectionAction.AwaitingReview)

            {

                _monitor.Log("[StressDialogueService] AwaitingReview — CP review path, programmatic modes skipped", LogLevel.Debug);

                return false;

            }



            // 2. start — первое назначение лечения, квест после закрытия диалога

            if (TrySelectStartDialogue(out buffId, out dialogueText, out var startEpisodeId))

            {

                _selectedEpisodeIdForDialogue = startEpisodeId;

                _activeStressDialogueMode = StressHarveyDialogueMode.Start;

                _autoStartTreatmentAfterClose = true;

                _monitor.Log(

                    $"[StressDialogueService] Mode=start (episode={startEpisodeId ?? "(none)"}, buff={buffId})",

                    LogLevel.Debug);

                return true;

            }



            // 3. reminder — лечение активно, короткое напоминание без нового квеста

            if (TrySelectReminderDialogue(episodeSelection, out buffId, out dialogueText))

            {

                _activeStressDialogueMode = StressHarveyDialogueMode.Reminder;

                _autoStartTreatmentAfterClose = false;

                _monitor.Log(

                    $"[StressDialogueService] Mode=reminder (episode={episodeSelection.EpisodeId}, buff={buffId})",

                    LogLevel.Debug);

                return true;

            }



            // 4. ambient — замечает состояние, лечение не назначает

            if (TrySelectAmbientDialogue(episodeSelection, out buffId, out dialogueText))

            {

                _activeStressDialogueMode = StressHarveyDialogueMode.Ambient;

                _autoStartTreatmentAfterClose = false;

                _monitor.Log(

                    $"[StressDialogueService] Mode=ambient (buff={buffId})",

                    LogLevel.Debug);

                return true;

            }



            return false;

        }



        private bool TrySelectReviewDialogue(out string? buffId, out string? dialogueText)

        {

            buffId = null;

            dialogueText = null;

            return _reviewService.TryPrepareEpisodeReviewDialogue(out buffId, out dialogueText);

        }



        private bool TrySelectStartDialogue(out string? buffId, out string? dialogueText, out string? episodeId)

        {

            buffId = null;

            dialogueText = null;

            episodeId = null;



            if (HasDeferredStressDialogue)

            {

                _monitor.Log("[StressDialogue] Start offer skipped: deferred start dialogue pending", LogLevel.Debug);

                return false;

            }



            var episodeSelection = CheckForEpisodeStart();

            if (episodeSelection != null)

            {

                buffId = episodeSelection.PrimaryBuffId;

                dialogueText = GetDialogueForEpisode(episodeSelection.EpisodeId!, buffId!);



                if (dialogueText == null)

                {

                    _monitor.Log($"[StressDialogueService] Диалог не найден для episode {episodeSelection.EpisodeId}", LogLevel.Warn);

                    return false;

                }



                episodeId = episodeSelection.EpisodeId;

                return true;

            }



            buffId = CheckForActiveDebuffWithoutTreatment();

            if (buffId == null)

                return false;



            dialogueText = GetDialogueForBuff(buffId);

            if (dialogueText == null)

            {

                _monitor.Log($"[StressDialogueService] Диалог не найден для {buffId}", LogLevel.Warn);

                return false;

            }



            return true;

        }



        private bool TrySelectReminderDialogue(

            EpisodeSelectionResult episodeSelection,

            out string? buffId,

            out string? dialogueText)

        {

            buffId = null;

            dialogueText = null;



            if (episodeSelection.Action != EpisodeSelectionAction.ReminderOnly

                || string.IsNullOrEmpty(episodeSelection.EpisodeId))

            {

                return false;

            }



            buffId = episodeSelection.PrimaryBuffId

                ?? TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(

                    episodeSelection.EpisodeId,

                    _episodeService.BuildContext().ActiveCauseIds);

            dialogueText = GetReminderDialogue(episodeSelection.EpisodeId);



            return dialogueText != null;

        }



        private bool TrySelectAmbientDialogue(

            EpisodeSelectionResult episodeSelection,

            out string? buffId,

            out string? dialogueText)

        {

            buffId = null;

            dialogueText = null;



            if (episodeSelection.Action != EpisodeSelectionAction.AmbientOnly)

                return false;



            var ctx = _episodeService.BuildContext();

            var causeId = ResolveAmbientCauseId(ctx.ActiveCauseIds);

            if (causeId == null)

            {

                _monitor.Log("[StressDialogueService] Ambient-only: no ambient cause dialogue found", LogLevel.Debug);

                return false;

            }



            buffId = StressCauses.CauseToBuff.GetValueOrDefault(causeId, causeId);

            dialogueText = GetAmbientDialogue(causeId);



            if (dialogueText == null)

                return false;



            if (_stateService.WasTreatmentOfferShownToday(buffId))

            {

                _monitor.Log(

                    $"[StressDialogueService] Ambient notice already shown today for {buffId}",

                    LogLevel.Debug);

                return false;

            }



            _monitor.Log(

                $"[StressDialogueService] Ambient notice for cause={causeId}, load={ctx.StressLoad}",

                LogLevel.Debug);

            return true;

        }



        private static readonly Dictionary<string, string> BuffIdToQuestId = new()

        {

            [BuffIds.Thunder] = QuestIds.Thunder,

            [BuffIds.Darkness] = QuestIds.Darkness,

            [BuffIds.Lonely] = QuestIds.Lonely,

            [BuffIds.Overwork] = QuestIds.Overwork,

            [BuffIds.Hunger] = QuestIds.Hunger,

            [BuffIds.TooCold] = QuestIds.TooCold,

            [BuffIds.Social] = QuestIds.Social,

            [BuffIds.NoSleep] = QuestIds.NoSleep,

            [BuffIds.Tired] = QuestIds.Tired,

        };



        private string? SelectStartEligibleUntreatedBuff()

        {

            foreach (var buffId in StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data))

            {

                if (!CannotOfferStartDialogueAgain(buffId, null, out var reason))

                    return buffId;

                _monitor.Log(

                    $"[StressDialogue] Skipping untreated {buffId} for start offer: {reason}",

                    LogLevel.Debug);

            }

            return null;

        }



        private void PurgeStaleDeferredStartDialogue()

        {

            if (_deferredBuffId == null)

                return;

            if (IsTreatmentStartComplete(_deferredBuffId, null, out var reason)

                || _stateService.WasTreatmentOfferShownToday(_deferredBuffId))

            {

                _monitor.Log(

                    $"[StressDialogue] Purging stale deferred start (buff={_deferredBuffId}, reason={reason ?? "OfferShownToday"})",

                    LogLevel.Debug);

                ClearDeferredStressDialogue();

            }

        }



        private bool CannotOfferStartDialogueAgain(string buffId, string? episodeId, out string reason)

        {

            reason = "";

            if (string.IsNullOrEmpty(buffId))

                return false;

            var resolvedBuff = DarknessLegacyHelper.ResolveTreatmentBuffId(buffId);

            if (_stateService.WasTreatmentOfferShownToday(resolvedBuff)

                || (!string.Equals(resolvedBuff, buffId, StringComparison.Ordinal)

                    && _stateService.WasTreatmentOfferShownToday(buffId)))

            {

                reason = "OfferShownToday";

                return true;

            }

            if (IsTreatmentStartComplete(resolvedBuff, episodeId, out reason))

                return true;

            if (_isShowingDialogue

                && !string.IsNullOrEmpty(_pendingBuffIdForTreatment)

                && string.Equals(_pendingBuffIdForTreatment, buffId, StringComparison.Ordinal))

            {

                reason = "PendingStartDialogue";

                return true;

            }

            return false;

        }



        private bool IsTreatmentStartComplete(string buffId, string? episodeId, out string reason)

        {

            reason = "";

            if (!string.IsNullOrEmpty(episodeId))

            {

                var episode = _data.ActiveTreatmentEpisode;

                if (episode != null

                    && string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal)

                    && episode.IsActiveEpisode())

                {

                    reason = "EpisodeTreatmentStarted";

                    return true;

                }

                if (TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition)

                    && (_stateService.HasQuestInGameJournal(definition.QuestId)

                        || _stateService.HasActiveQuestState(definition.QuestId)))

                {

                    reason = "EpisodeQuestActive";

                    return true;

                }

            }

            if (string.IsNullOrEmpty(buffId))

                return false;

            var treatment = _stateService.GetActiveTreatment(buffId);

            if (treatment != null && treatment.TreatmentStarted && !treatment.IsCured)

            {

                reason = "TreatmentStarted";

                return true;

            }

            var questId = ResolveQuestIdForStartGuard(buffId, episodeId);

            if (!string.IsNullOrEmpty(questId)

                && (_stateService.HasQuestInGameJournal(questId) || _stateService.HasActiveQuestState(questId)))

            {

                reason = "QuestActive";

                return true;

            }

            return false;

        }



        private static string? ResolveQuestIdForStartGuard(string buffId, string? episodeId)

        {

            if (!string.IsNullOrEmpty(episodeId)

                && TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))

            {

                return definition.QuestId;

            }

            return BuffIdToQuestId.TryGetValue(buffId, out var questId) ? questId : null;

        }



        private bool EnsurePipelineGuard(

            string caller,

            bool requireDialogueBox = true,

            bool requireHarveySpeaker = true,

            NPC? knownHarveyNpc = null)

        {

            if (StressDialoguePipelineGuard.CanRun(

                    out var reason,

                    requireDialogueBox,

                    requireHarveySpeaker,

                    knownHarveyNpc))

            {

                return true;

            }



            StressDialoguePipelineGuard.LogBlocked(_monitor, caller, reason);

            return false;

        }



        /// <summary>Read-only snapshot для debug-команды stress_dialogue_state.</summary>

        public StressDialogueDebugSnapshot GetDebugSnapshot()

        {

            return new StressDialogueDebugSnapshot

            {

                IsShowingStressDialogue = _isShowingDialogue,

                HasDeferredStressDialogue = HasDeferredStressDialogue,

                PendingAutoStartBuffId = _pendingBuffIdForTreatment,

                PendingAutoStartEpisodeId = _pendingEpisodeIdForTreatment ?? _selectedEpisodeIdForDialogue,

                DeferredBuffId = _deferredBuffId,

            };

        }

    }

}


