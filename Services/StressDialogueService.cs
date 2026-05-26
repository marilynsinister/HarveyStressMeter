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



        /// <summary>Открыт programmatic stress-диалог — fallback-топики добавлять нельзя.</summary>

        public bool IsShowingStressDialogue => _isShowingDialogue;



        /// <summary>Stress-dialogue отложен до закрытия текущего DialogueBox.</summary>

        public bool HasDeferredStressDialogue => _deferredBuffId != null;



        public StressDialogueService(

            IMonitor monitor,

            IModHelper helper,

            StateService stateService,

            TreatmentService treatmentService,

            TreatmentEpisodeService episodeService,

            StressTreatmentReviewService reviewService)

        {

            _monitor = monitor;

            _helper = helper;

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



            string? selected = null;



            foreach (var buffId in StressDebuffSelector.PriorityOrder)

            {

                if (!StressDebuffSelector.IsUntreatedDebuff(_stateService, buffId))

                    continue;



                selected = buffId;

                break;

            }



            if (selected == null)

                return null;



            var otherUntreated = StressDebuffSelector.GetUntreatedDebuffs(_stateService)

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

            return GetDialogueByKey(buffId);

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



            string dialogue;

            if (isMarried)

                dialogue = dialogueData.DialogueMarried;

            else if (isDating)

                dialogue = dialogueData.DialogueDating;

            else if (friendship >= 7)

                dialogue = dialogueData.DialogueHighFriendship;

            else if (friendship >= 3)

                dialogue = dialogueData.DialogueMediumFriendship;

            else

                dialogue = dialogueData.DialogueLowFriendship;



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

            ClearDeferredStressDialogue();



            DisplayStressDialogue(harvey, buffId, dialogueText);

            return true;

        }



        /// <summary>

        /// Показывает стартовую реплику лечения стресса (без #$y-вопроса).

        /// </summary>

        public void ShowStressDialogue(string buffId, string dialogueText)

        {

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

                ScheduleDeferredStressDialogue(buffId, dialogueText);

                return;

            }



            DisplayStressDialogue(harvey, buffId, dialogueText, _selectedEpisodeIdForDialogue);

            _selectedEpisodeIdForDialogue = null;

        }



        private void DisplayStressDialogue(NPC harvey, string buffId, string dialogueText, string? episodeId = null)

        {

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



            var dialogue = new Dialogue(harvey, null, dialogueText);

            harvey.CurrentDialogue.Push(dialogue);

            Game1.drawDialogue(harvey);



            _stateService.MarkTreatmentOfferShown(buffId);

            _monitor.Log(

                $"[StressDialogueService] ✅ Показана стартовая реплика лечения episode={episodeId ?? "(buff-only)"} buff={buffId}",

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



            if (!string.IsNullOrEmpty(episodeId))

            {

                _monitor.Log(

                    $"[StressDialogueService] Старт TreatmentEpisode {episodeId} (primary buff {buffId})",

                    LogLevel.Info);

                _treatmentService.StartTreatmentEpisode(episodeId);

                return;

            }



            var dialogueData = _dialoguesData?.Dialogues.FirstOrDefault(d => d.BuffId == buffId);

            var displayName = dialogueData?.DisplayName ?? buffId;



            _monitor.Log(

                $"[StressDialogueService] Старт лечения/квеста для {buffId} после реплики Харви",

                LogLevel.Info);

            _treatmentService.StartTreatment(buffId, displayName);

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

            ClearDeferredStressDialogue();

        }



        private void ClearDeferredStressDialogue()

        {

            _deferredBuffId = null;

            _deferredDialogueText = null;

        }



        /// <summary>

        /// Проверяет, нужно ли показать диалог стресса при клике на Харви

        /// </summary>

        public bool ShouldShowStressDialogue(out string? buffId, out string? dialogueText)

        {

            buffId = null;

            dialogueText = null;



            if (!EnsurePipelineGuard(nameof(ShouldShowStressDialogue)))

                return false;



            // ⭐ НОВОЕ: Проверяем, нет ли уже топика завершения лечения

            // Если есть топик Cured, Content Patcher покажет свой диалог

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

            _autoStartTreatmentAfterClose = true;



            if (_reviewService.TryPrepareEpisodeReviewDialogue(out buffId, out dialogueText))

            {

                _autoStartTreatmentAfterClose = false;

                _monitor.Log(

                    $"[StressDialogueService] Episode review dialogue (buff={buffId})",

                    LogLevel.Debug);

                return true;

            }

            var episodeSelection = _episodeService.EvaluateSelection();

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

                return false;



            if (episodeSelection.Action == EpisodeSelectionAction.AmbientOnly)

            {

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



                _autoStartTreatmentAfterClose = false;

                _monitor.Log(

                    $"[StressDialogueService] Ambient notice for cause={causeId}, load={ctx.StressLoad}",

                    LogLevel.Debug);

                return true;

            }



            if (episodeSelection.Action == EpisodeSelectionAction.ReminderOnly

                && !string.IsNullOrEmpty(episodeSelection.EpisodeId))

            {

                buffId = episodeSelection.PrimaryBuffId

                    ?? TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(

                        episodeSelection.EpisodeId,

                        _episodeService.BuildContext().ActiveCauseIds);

                dialogueText = GetReminderDialogue(episodeSelection.EpisodeId);



                if (dialogueText == null)

                    return false;



                _autoStartTreatmentAfterClose = false;

                _monitor.Log(

                    $"[StressDialogueService] Reminder dialogue for episode={episodeSelection.EpisodeId}",

                    LogLevel.Debug);

                return true;

            }



            episodeSelection = CheckForEpisodeStart();

            if (episodeSelection != null)

            {

                buffId = episodeSelection.PrimaryBuffId;

                dialogueText = GetDialogueForEpisode(episodeSelection.EpisodeId!, buffId!);



                if (dialogueText == null)

                {

                    _monitor.Log($"[StressDialogueService] Диалог не найден для episode {episodeSelection.EpisodeId}", LogLevel.Warn);

                    return false;

                }



                _selectedEpisodeIdForDialogue = episodeSelection.EpisodeId;

                _monitor.Log(

                    $"[StressDialogueService] ShouldShowStressDialogue=true, episode={episodeSelection.EpisodeId}, primaryBuff={buffId}",

                    LogLevel.Debug);

                return true;

            }



            buffId = CheckForActiveDebuffWithoutTreatment();

            

            if (buffId == null)

            {

                dialogueText = null;

                return false;

            }



            dialogueText = GetDialogueForBuff(buffId);

            

            if (dialogueText == null)

            {

                _monitor.Log($"[StressDialogueService] Диалог не найден для {buffId}", LogLevel.Warn);

                return false;

            }



            _monitor.Log($"[StressDialogueService] ShouldShowStressDialogue=true, buffId={buffId}", LogLevel.Debug);

            return true;

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


