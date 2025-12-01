using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
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
        
        private StressDialoguesContainer? _dialoguesData;
        private string? _pendingBuffIdForTreatment;
        
        // ⭐ ИСПРАВЛЕНИЕ: Флаг для предотвращения бесконечного цикла при показе диалогов
        private bool _isShowingDialogue = false;

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
        /// Проверяет наличие активного дебаффа без лечения и возвращает buffId
        /// </summary>
        public string? CheckForActiveDebuffWithoutTreatment()
        {
            if (_dialoguesData == null) return null;
            
            // ⭐ ИСПРАВЛЕНИЕ: Если уже показываем диалог, не проверяем снова
            if (_isShowingDialogue)
            {
                _monitor.Log($"[StressDialogueService] Диалог уже показывается, пропускаем проверку", LogLevel.Debug);
                return null;
            }

            // Порядок приоритета проверки баффов
            var buffPriority = new[]
            {
                BuffIds.Social,      // Самый важный - социальная тревожность
                BuffIds.Tired,
                BuffIds.Overwork,
                BuffIds.NoSleep,
                BuffIds.Lonely,
                BuffIds.Hunger,
                BuffIds.TooCold,
                BuffIds.Thunder
            };

            foreach (var buffId in buffPriority)
            {
                // ⭐ ИСПРАВЛЕНИЕ: Проверяем внутримодовое состояние через StateService
                // Не используем HasActiveBuffInGame, потому что бафф может быть снят, но лечение не завершено
                
                // 1. Проверяем, есть ли активное лечение этого типа
                var treatment = _stateService.GetActiveTreatment(buffId);
                
                // 2. Если лечения нет, но бафф активен в игре - это новый случай
                if (treatment == null && _stateService.HasActiveBuffInGame(buffId))
                {
                    _monitor.Log($"[StressDialogueService] Найден активный дебафф без лечения: {buffId}", LogLevel.Debug);
                    return buffId;
                }
                
                // 3. Если есть лечение, но оно не начато (TreatmentStarted=false) - показываем диалог
                if (treatment != null && !treatment.TreatmentStarted)
                {
                    _monitor.Log($"[StressDialogueService] Найден дебафф с незапущенным лечением: {buffId}", LogLevel.Debug);
                    return buffId;
                }
                
                // 4. Если лечение начато (есть квест) или завершено (IsCured) - пропускаем
                if (treatment != null && (treatment.TreatmentStarted || treatment.IsCured))
                {
                    _monitor.Log($"[StressDialogueService] Дебафф {buffId} уже в процессе лечения или излечен, пропускаем", LogLevel.Trace);
                    continue;
                }
            }

            return null;
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
        /// Показывает диалог стресса и запоминает buffId для запуска лечения
        /// </summary>
        public void ShowStressDialogue(string buffId, string dialogueText)
        {
            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey == null)
            {
                _monitor.Log("[StressDialogueService] Harvey не найден!", LogLevel.Error);
                return;
            }

            // ⭐ ИСПРАВЛЕНИЕ: Устанавливаем флаг что диалог показывается
            _isShowingDialogue = true;
            
            // Запоминаем buffId для запуска лечения после закрытия диалога
            _pendingBuffIdForTreatment = buffId;

            // Создаём диалог программно
            var dialogue = new Dialogue(harvey, null, dialogueText);
            harvey.CurrentDialogue.Push(dialogue);
            Game1.drawDialogue(harvey);

            _monitor.Log($"[StressDialogueService] ✅ Показан диалог для {buffId}", LogLevel.Info);
        }

        /// <summary>
        /// Проверяет, был ли закрыт диалог стресса, и запускает лечение
        /// Вызывается из GameLogicHandler при HandleHarveyDialogueEnd
        /// </summary>
        public void CheckAndStartTreatmentAfterDialogue()
        {
            if (string.IsNullOrEmpty(_pendingBuffIdForTreatment))
                return;

            var buffId = _pendingBuffIdForTreatment;
            _pendingBuffIdForTreatment = null;
            
            // ⭐ ИСПРАВЛЕНИЕ: Сбрасываем флаг показа диалога
            _isShowingDialogue = false;

            // Получаем отображаемое название баффа
            var dialogueData = _dialoguesData?.Dialogues.FirstOrDefault(d => d.BuffId == buffId);
            var displayName = dialogueData?.DisplayName ?? buffId;

            // Запускаем лечение
            _monitor.Log($"[StressDialogueService] Запуск лечения для {buffId} после закрытия диалога", LogLevel.Info);
            _treatmentService.StartTreatment(buffId, displayName);
        }

        /// <summary>
        /// Сбрасывает ожидающее лечение (например, при выходе из игры)
        /// </summary>
        public void ClearPendingTreatment()
        {
            _pendingBuffIdForTreatment = null;
            _isShowingDialogue = false; // ⭐ ИСПРАВЛЕНИЕ: Также сбрасываем флаг показа диалога
        }

        /// <summary>
        /// Проверяет, нужно ли показать диалог стресса при клике на Харви
        /// </summary>
        public bool ShouldShowStressDialogue(out string? buffId, out string? dialogueText)
        {
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
                    buffId = null;
                    dialogueText = null;
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

            return true;
        }
    }
}

