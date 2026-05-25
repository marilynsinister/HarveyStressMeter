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
        /// <summary>Ключи ответов #$y: первый = согласие, второй = отказ (index 1 и 3 в parseDialogueString).</summary>
        internal const string ConsentAcceptResponseKey = "quickResponse1";
        internal const string ConsentDeclineResponseKey = "quickResponse3";

        private const string ConsentQuestionSuffix =
            "#$b#$y 'Готова начать программу лечения?_Да, давай._Хорошо. Тогда оформлю всё и мы начнём.$h_Не сейчас._Хорошо. Когда будешь готова — поговори со мной снова.$l'";

        private enum ConsentResult
        {
            None,
            Accepted,
            Declined
        }

        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly StateService _stateService;
        private readonly TreatmentService _treatmentService;
        
        private StressDialoguesContainer? _dialoguesData;
        private string? _pendingBuffIdForTreatment;
        private ConsentResult _pendingConsentResult = ConsentResult.None;
        
        // ⭐ ИСПРАВЛЕНИЕ: Флаг для предотвращения бесконечного цикла при показе диалогов
        private bool _isShowingDialogue = false;
        private string? _deferredBuffId;
        private string? _deferredDialogueText;

        /// <summary>Открыт programmatic stress/consent-диалог — fallback-топики добавлять нельзя.</summary>
        public bool IsShowingStressDialogue => _isShowingDialogue;

        /// <summary>Stress-dialogue отложен до закрытия текущего DialogueBox.</summary>
        public bool HasDeferredStressDialogue => _deferredBuffId != null;

        public StressDialogueService(
            IMonitor monitor,
            IModHelper helper,
            StateService stateService,
            TreatmentService treatmentService)
        {
            _monitor = monitor;
            _helper = helper;
            _stateService = stateService;
            _treatmentService = treatmentService;
        }

        /// <summary>
        /// Загружает диалоги из JSON при старте мода
        /// </summary>
        public void LoadDialogues()
        {
            try
            {
                _dialoguesData = _helper.Data.ReadJsonFile<StressDialoguesContainer>("assets/stress_dialogues.json");
                
                if (_dialoguesData == null || _dialoguesData.Dialogues.Count == 0)
                {
                    _monitor.Log("[StressDialogueService] Не удалось загрузить диалоги стресса из assets/stress_dialogues.json", LogLevel.Error);
                    _dialoguesData = new StressDialoguesContainer();
                }
                else
                {
                    _monitor.Log($"[StressDialogueService] ✅ Загружено {_dialoguesData.Dialogues.Count} диалогов стресса", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"[StressDialogueService] ОШИБКА при загрузке диалогов: {ex.Message}", LogLevel.Error);
                _dialoguesData = new StressDialoguesContainer();
            }
        }

        /// <summary>
        /// Проверяет наличие активного дебаффа без лечения и возвращает buffId с наивысшим приоритетом,
        /// пропуская buffId с offer/decline сегодня.
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
            var skippedShown = new List<string>();
            var skippedDeclined = new List<string>();

            foreach (var buffId in StressDebuffSelector.PriorityOrder)
            {
                if (!StressDebuffSelector.IsUntreatedDebuff(_stateService, buffId))
                    continue;

                if (_stateService.WasTreatmentDeclinedToday(buffId))
                {
                    skippedDeclined.Add(buffId);
                    continue;
                }

                if (_stateService.WasTreatmentOfferShownToday(buffId))
                {
                    skippedShown.Add(buffId);
                    continue;
                }

                selected = buffId;
                break;
            }

            if (selected == null)
            {
                foreach (var buffId in skippedShown)
                {
                    _monitor.Log(
                        $"[StressDialogue] Consent dialogue skipped for {buffId}: offer already shown today",
                        LogLevel.Debug);
                }

                foreach (var buffId in skippedDeclined)
                {
                    _monitor.Log(
                        $"[StressDialogue] Consent dialogue skipped for {buffId}: declined today",
                        LogLevel.Debug);
                }

                return null;
            }

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
                _monitor.Log($"[StressDialogueService] Найден активный дебафф без лечения: {selected}", LogLevel.Debug);
            }

            return selected;
        }

        /// <summary>
        /// Получает текст диалога для конкретного баффа в зависимости от отношений с Харви
        /// </summary>
        public string? GetDialogueForBuff(string buffId)
        {
            if (_dialoguesData == null) return null;

            var dialogueData = _dialoguesData.Dialogues.FirstOrDefault(d => d.BuffId == buffId);
            if (dialogueData == null)
            {
                _monitor.Log($"[StressDialogueService] Не найден диалог для баффа {buffId}", LogLevel.Warn);
                return null;
            }

            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey == null) return null;

            var friendship = Game1.player.getFriendshipHeartLevelForNPC("Harvey");
            var isMarried = Game1.player.spouse == "Harvey";
            
            bool isDating = false;
            if (!isMarried && Game1.player.friendshipData.TryGetValue("Harvey", out var data))
            {
                isDating = data.Status == FriendshipStatus.Dating;
            }

            // Выбираем диалог в зависимости от отношений
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

            // ⭐ НОВОЕ: Заменяем @ на имя игрока
            dialogue = dialogue.Replace("@", Game1.player.Name);

            _monitor.Log($"[StressDialogueService] Получен диалог для {buffId} (friendship={friendship}, married={isMarried}, dating={isDating})", LogLevel.Debug);
            
            return dialogue;
        }

        /// <summary>
        /// Откладывает stress-dialogue до закрытия текущего DialogueBox (без принудительного закрытия меню).
        /// </summary>
        public void ScheduleDeferredStressDialogue(string buffId, string dialogueText)
        {
            _deferredBuffId = buffId;
            _deferredDialogueText = dialogueText;
            _monitor.Log(
                $"[StressDialogue] DialogueBox already active — stress dialogue deferred until close (buff={buffId})",
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
        /// Показывает диалог стресса с вопросом о согласии на лечение (#$y).
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

            DisplayStressDialogue(harvey, buffId, dialogueText);
        }

        private void DisplayStressDialogue(NPC harvey, string buffId, string dialogueText)
        {
            _isShowingDialogue = true;
            _pendingConsentResult = ConsentResult.None;
            _pendingBuffIdForTreatment = buffId;

            var fullDialogueText = AppendConsentQuestion(dialogueText);
            var dialogue = new Dialogue(harvey, null, fullDialogueText);
            harvey.CurrentDialogue.Push(dialogue);
            Game1.drawDialogue(harvey);

            _stateService.MarkTreatmentOfferShown(buffId);
            _monitor.Log($"[StressDialogueService] ✅ Показан диалог для {buffId} (с выбором согласия)", LogLevel.Info);
        }

        /// <summary>
        /// Записывает выбор игрока из #$y quick response (вызывается Harmony-патчем).
        /// </summary>
        public bool TryRecordConsentResponse(string? responseKey)
        {
            if (!EnsurePipelineGuard(nameof(TryRecordConsentResponse)))
                return false;

            if (string.IsNullOrEmpty(_pendingBuffIdForTreatment) || !_isShowingDialogue)
                return false;

            if (responseKey == ConsentAcceptResponseKey)
            {
                _pendingConsentResult = ConsentResult.Accepted;
                _monitor.Log(
                    $"[StressDialogueService] Игрок согласилась на лечение {_pendingBuffIdForTreatment}",
                    LogLevel.Info);
                return true;
            }

            if (responseKey == ConsentDeclineResponseKey)
            {
                var buffId = _pendingBuffIdForTreatment!;
                _stateService.MarkTreatmentDeclined(buffId);
                _pendingBuffIdForTreatment = null;
                _pendingConsentResult = ConsentResult.None;
                _monitor.Log(
                    $"[StressDialogueService] Игрок отложила лечение {buffId}",
                    LogLevel.Info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// После закрытия programmatic-диалога: StartTreatment только при явном согласии.
        /// Treatment start is controlled only by C# consent flow. CP topics must not start treatment.
        /// </summary>
        public void CheckAndStartTreatmentAfterDialogue()
        {
            if (!EnsurePipelineGuard(
                    nameof(CheckAndStartTreatmentAfterDialogue),
                    requireDialogueBox: false,
                    requireHarveySpeaker: false))
            {
                _isShowingDialogue = false;
                return;
            }

            if (string.IsNullOrEmpty(_pendingBuffIdForTreatment))
            {
                _isShowingDialogue = false;
                return;
            }

            var buffId = _pendingBuffIdForTreatment;
            var consent = _pendingConsentResult;

            _pendingBuffIdForTreatment = null;
            _pendingConsentResult = ConsentResult.None;
            _isShowingDialogue = false;

            if (consent != ConsentResult.Accepted)
            {
                var reason = consent == ConsentResult.Declined ? "отказ" : "диалог закрыт без выбора";
                _monitor.Log(
                    $"[StressDialogueService] Лечение для {buffId} не начато ({reason})",
                    LogLevel.Info);
                return;
            }

            var dialogueData = _dialoguesData?.Dialogues.FirstOrDefault(d => d.BuffId == buffId);
            var displayName = dialogueData?.DisplayName ?? buffId;

            _monitor.Log($"[StressDialogueService] Запуск лечения для {buffId} после согласия игрока", LogLevel.Info);
            _treatmentService.StartTreatment(buffId, displayName);
        }

        /// <summary>
        /// Сбрасывает ожидающее согласие (выход в меню, загрузка save и т.д.).
        /// </summary>
        public void ClearPendingTreatment()
        {
            _pendingBuffIdForTreatment = null;
            _pendingConsentResult = ConsentResult.None;
            _isShowingDialogue = false;
            ClearDeferredStressDialogue();
        }

        private void ClearDeferredStressDialogue()
        {
            _deferredBuffId = null;
            _deferredDialogueText = null;
        }

        private static string AppendConsentQuestion(string dialogueText)
        {
            if (dialogueText.Contains("$y '", StringComparison.Ordinal))
                return dialogueText;

            return dialogueText + ConsentQuestionSuffix;
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
                PendingBuffIdForTreatment = _pendingBuffIdForTreatment,
                DeferredBuffId = _deferredBuffId,
                PendingConsentResult = _pendingConsentResult.ToString(),
                ResponseAlreadyRecorded = _pendingConsentResult != ConsentResult.None,
            };
        }
    }
}

