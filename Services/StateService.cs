using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис управления состоянием системы стресса
    /// Единый источник правды для всех операций с баффами и квестами
    /// </summary>
    public class StateService
    {
        private readonly SaveData _data;
        private readonly IMonitor _monitor;
        private readonly BuffService _buffService;
        private readonly QuestService _questService;

        public StateService(SaveData data, IMonitor monitor, BuffService buffService, QuestService questService)
        {
            _data = data;
            _monitor = monitor;
            _buffService = buffService;
            _questService = questService;
        }

        /// <summary>
        /// Мигрирует старые данные в новую структуру PlayerStressState
        /// </summary>
        public void MigrateOldData()
        {
#pragma warning disable CS0618 // Используем устаревшие поля для миграции

            bool migrated = false;

            // Миграция ActiveLockedDebuffs -> ActiveTreatments
            foreach (var (buffId, questId) in _data.ActiveLockedDebuffs)
            {
                if (!_data.StressState.HasActiveBuff(buffId))
                {
                    // ⭐ ИСПРАВЛЕНО: Создаем лечение с уникальным ключом для миграции
                    var instanceNumber = _data.StressState.GetNextInstanceNumber(buffId);
                    var treatmentKey = TreatmentState.GenerateTreatmentKey(buffId, instanceNumber);
                    
                    var treatment = new TreatmentState
                    {
                        BuffId = buffId,
                        QuestId = questId,
                        TreatmentKey = treatmentKey,
                        InstanceNumber = instanceNumber,
                        IssuedDate = _data.LastIssuedDay.TryGetValue(buffId, out var date) ? date : SDate.Now(),
                        TreatmentStarted = true,
                        TreatmentStartedDate = SDate.Now(),
                        AddedToGameLog = _questService.HasQuest(questId),
                        Progress = _data.Treatment.TryGetValue(buffId, out var progress) ? progress : new TreatmentProgress()
                    };

                    _data.StressState.AddTreatment(treatment);
                    migrated = true;
                }
            }

            // Миграция LastIssuedDay
            foreach (var (buffId, date) in _data.LastIssuedDay)
            {
                if (!_data.StressState.LastIssuedDay.ContainsKey(buffId))
                {
                    _data.StressState.LastIssuedDay[buffId] = date;
                    migrated = true;
                }
            }

            // Миграция StressBuffStates -> TreatmentHistory
            foreach (var (buffId, treatments) in _data.StressBuffStates)
            {
                if (!_data.StressState.TreatmentHistory.ContainsKey(buffId))
                {
                    // Копируем существующие TreatmentState из StressBuffStates
                    _data.StressState.TreatmentHistory[buffId] = new List<TreatmentState>(treatments);
                    migrated = true;
                }
            }

            if (migrated)
            {
                _monitor.Log("[StateService] Миграция старых данных в PlayerStressState завершена", LogLevel.Info);
            }

#pragma warning restore CS0618
        }

        /// <summary>
        /// Применяет дебафф стресса
        /// </summary>
        public void ApplyStressBuff(string buffId, string description)
        {
            // Проверяем, нет ли уже активного баффа
            if (_data.StressState.HasActiveBuff(buffId))
            {
                _monitor.Log($"[StateService] Бафф '{buffId}' уже активен, пропускаем", LogLevel.Debug);
                return;
            }

            // ⭐ ИСПРАВЛЕНО: Создаем новое лечение с уникальным ключом
            var instanceNumber = _data.StressState.GetNextInstanceNumber(buffId);
            var treatmentKey = TreatmentState.GenerateTreatmentKey(buffId, instanceNumber);
            
            var treatment = new TreatmentState
            {
                BuffId = buffId,
                TreatmentKey = treatmentKey,
                InstanceNumber = instanceNumber,
                IssuedDate = SDate.Now(),
                TreatmentStarted = false,
                IsCured = false,
                IsCompleted = false,
                Progress = new TreatmentProgress()
            };

            _data.StressState.AddTreatment(treatment);
            _data.StressState.LastIssuedDay[buffId] = SDate.Now();

            // Применяем бафф в игре
            _buffService.ApplyBuffFromData(buffId);

            _monitor.Log($"[StateService] ✅ Применен дебафф '{buffId}': {description}", LogLevel.Info);
        }

        /// <summary>
        /// Создает новое лечение (выдает дебафф)
        /// </summary>
        public void CreateTreatment(string buffId)
        {
            if (_data.StressState.HasActiveBuff(buffId))
            {
                _monitor.Log($"[StateService] ⚠️ Дебафф '{buffId}' уже активен", LogLevel.Warn);
                return;
            }

            var treatment = new TreatmentState
            {
                BuffId = buffId,
                IssuedDate = SDate.Now(),
                TreatmentStarted = false,
                IsCured = false,
                IsCompleted = false,
                Progress = new TreatmentProgress()
            };

            _data.StressState.ActiveTreatments[buffId] = treatment;
            _data.StressState.LastIssuedDay[buffId] = SDate.Now();

            _monitor.Log($"[StateService] ✅ Создано новое лечение для дебаффа '{buffId}'", LogLevel.Info);
        }
        public void StartTreatment(string buffId, string questId)
        {
            if (!_data.StressState.HasActiveBuff(buffId))
            {
                _monitor.Log($"[StateService] ⚠️ Попытка начать лечение для неактивного баффа '{buffId}'", LogLevel.Warn);
                return;
            }

            // ⭐ НОВОЕ: Дополнительная проверка - не активен ли уже квест
            if (_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[StateService] ⚠️ Квест '{questId}' уже активен", LogLevel.Warn);
                return;
            }

            // Получаем существующее лечение или создаем новое
            var treatment = _data.StressState.GetActiveTreatment(buffId);
            if (treatment == null)
            {
                _monitor.Log($"[StateService] ❌ Лечение для баффа '{buffId}' не найдено", LogLevel.Error);
                return;
            }

            // Обновляем состояние лечения
            treatment.TreatmentStarted = true;
            treatment.TreatmentStartedDate = SDate.Now();
            treatment.QuestId = questId;
            treatment.Progress = new TreatmentProgress(); // Инициализируем прогресс

            // ⭐ НОВОЕ: Устанавливаем флаг активного лечения
            _data.StressState.TreatmentFlags.SetTreatmentActive(buffId, true);
            _monitor.Log($"[StateService] ✅ Флаг активного лечения установлен для {buffId}", LogLevel.Info);

            // ⭐ НОВОЕ: Логируем состояние ПЕРЕД добавлением квеста
            _monitor.Log($"[StateService] ═══ ДОБАВЛЕНИЕ КВЕСТА В ЖУРНАЛ ═══", LogLevel.Info);
            _monitor.Log($"[StateService] QuestId: {questId}", LogLevel.Info);
            _monitor.Log($"[StateService] BuffId: {buffId}", LogLevel.Info);
            _monitor.Log($"[StateService] Бафф активен в игре: {_buffService.HasBuff(buffId)}", LogLevel.Info);

            // Добавляем квест в игру
            _questService.AddQuest(questId);
            treatment.AddedToGameLog = _questService.HasQuest(questId);

            // ⭐ НОВОЕ: Устанавливаем флаг добавления квеста в журнал
            _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, treatment.AddedToGameLog);
            _monitor.Log($"[StateService] ✅ Флаг добавления квеста в журнал установлен: {treatment.AddedToGameLog}", LogLevel.Info);

            // ⭐ НОВОЕ: Детальная проверка успешности добавления
            if (!treatment.AddedToGameLog)
            {
                _monitor.Log($"[StateService] ❌ КРИТИЧЕСКАЯ ОШИБКА: Квест '{questId}' не добавлен в журнал!", LogLevel.Error);
                _monitor.Log($"[StateService] Проверьте, что квест присутствует в Data/Quests через Content Patcher", LogLevel.Error);

                // ⭐ НОВОЕ: Пытаемся диагностировать проблему
                try
                {
                    var questData = Game1.content.Load<Dictionary<string, string>>("Data/Quests");
                    bool questExists = questData.ContainsKey(questId);
                    _monitor.Log($"[StateService] Квест '{questId}' существует в Data/Quests: {questExists}", LogLevel.Error);

                    if (!questExists)
                    {
                        _monitor.Log($"[StateService] ⚠️ Content Patcher НЕ загрузил квест! Проверьте content.json", LogLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    _monitor.Log($"[StateService] Ошибка при проверке Data/Quests: {ex.Message}", LogLevel.Error);
                }

                return;
            }

            _monitor.Log($"[StateService] ✅ Начато лечение: бафф='{buffId}', квест='{questId}', добавлен в журнал={treatment.AddedToGameLog}", LogLevel.Info);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Обновляет прогресс квеста через делегат
        /// Позволяет обновлять прогресс квеста безопасно
        /// </summary>
        public void UpdateProgress(string questId, Action<TreatmentProgress> updateAction)
        {
            if (!_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[StateService.UpdateProgress] ⚠️ Квест '{questId}' не активен", LogLevel.Warn);
                return;
            }

            var treatment = _data.StressState.GetActiveTreatmentByQuest(questId);
            if (treatment == null)
            {
                _monitor.Log($"[StateService.UpdateProgress] ⚠️ Лечение для квеста '{questId}' не найдено", LogLevel.Warn);
                return;
            }

            // ⭐ НОВОЕ: Убеждаемся, что Progress инициализирован
            if (treatment.Progress == null)
            {
                treatment.Progress = new TreatmentProgress();
                _monitor.Log($"[StateService.UpdateProgress] ⚠️ Progress был null для квеста '{questId}', инициализирован", LogLevel.Warn);
            }

            // Вызываем делегат для обновления
            updateAction(treatment.Progress);

            _monitor.Log($"[StateService.UpdateProgress] ✅ Прогресс квеста '{questId}' обновлен", LogLevel.Debug);
        }

        /// <summary>
        /// Завершает лечение (завершает квест, снимает бафф)
        /// </summary>
        public void CompleteTreatment(string questId)
        {
            if (!_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[StateService] ⚠️ Попытка завершить неактивный квест '{questId}'", LogLevel.Warn);
                return;
            }

            var treatment = _data.StressState.GetActiveTreatmentByQuest(questId);
            if (treatment == null)
            {
                _monitor.Log($"[StateService] ❌ Лечение для квеста '{questId}' не найдено", LogLevel.Error);
                return;
            }

            var buffId = treatment.BuffId;

            _monitor.Log($"[StateService] ═══ ЗАВЕРШЕНИЕ ЛЕЧЕНИЯ ═══", LogLevel.Info);
            _monitor.Log($"[StateService] QuestId: {questId}", LogLevel.Info);
            _monitor.Log($"[StateService] BuffId: {buffId}", LogLevel.Info);
            _monitor.Log($"[StateService] Бафф активен ДО снятия: {_buffService.HasBuff(buffId)}", LogLevel.Info);

            // Помечаем лечение как завершенное
            treatment.IsCompleted = true;
            treatment.IsCured = true;
            treatment.CompletedDate = SDate.Now();
            _data.StressState.AddTreatmentToHistory(buffId, treatment);

            // Завершаем квест в игре
            _questService.CompleteQuest(questId);

            // Снимаем бафф в игре
            _buffService.RemoveBuff(buffId);

            // ⭐ НОВОЕ: Сбрасываем флаг активного лечения
            _data.StressState.TreatmentFlags.SetTreatmentActive(buffId, false);
            _monitor.Log($"[StateService] ✅ Флаг активного лечения сброшен для {buffId}", LogLevel.Info);

            // ⭐ НОВОЕ: Сбрасываем флаг добавления квеста в журнал
            _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, false);
            _monitor.Log($"[StateService] ✅ Флаг добавления квеста в журнал сброшен для {questId}", LogLevel.Info);

            _monitor.Log($"[StateService] Бафф активен ПОСЛЕ снятия: {_buffService.HasBuff(buffId)}", LogLevel.Info);

            // Удаляем из активных
            _data.StressState.ActiveTreatments.Remove(buffId);

            _monitor.Log($"[StateService] ✅ Лечение завершено: квест='{questId}', бафф='{buffId}'", LogLevel.Info);
        }

        /// <summary>
        /// Синхронизирует состояние с игрой
        /// Вызывается при загрузке сохранения и периодически для восстановления
        /// </summary>
        public void SyncWithGame()
        {
            int restored = 0;
            int removed = 0;

            // Проверяем все активные лечения
            foreach (var (buffId, treatment) in _data.StressState.ActiveTreatments.ToList())
            {
                var questId = treatment.QuestId;

                // Если квест завершен в игре - завершаем его в нашем состоянии
                if (!string.IsNullOrEmpty(questId) && _questService.IsQuestCompleted(questId, out var gameQuest) && gameQuest != null)
                {
                    if (ReflectionHelper.TryGetMember<bool>(gameQuest, "completed", out var completed) && completed)
                    {
                        _monitor.Log($"[StateService] Квест '{questId}' завершен в игре, завершаем в состоянии", LogLevel.Debug);
                        CompleteTreatment(questId);
                        removed++;
                        continue;
                    }
                }

                // Если квест пропал из журнала - восстанавливаем или удаляем
                // Используем флаг вместо проверки через QuestService
                bool questInJournal = !string.IsNullOrEmpty(questId) && _questService.HasQuest(questId);
                bool flagSaysAdded = !string.IsNullOrEmpty(questId) && _data.StressState.TreatmentFlags.IsQuestAddedToJournal(questId);
                
                if (!questInJournal && !string.IsNullOrEmpty(questId))
                {
                    if (flagSaysAdded)
                    {
                        _monitor.Log($"[StateService] ⚠️ Квест '{questId}' пропал из журнала (флаг говорит что был добавлен), удаляем из состояния", LogLevel.Warn);
                        _data.StressState.ActiveTreatments.Remove(buffId);
                        _buffService.RemoveBuff(buffId);
                        _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, false);
                        removed++;
                    }
                    else
                    {
                        _monitor.Log($"[StateService] Квест '{questId}' не был добавлен в журнал (флаг подтверждает), повторная попытка", LogLevel.Debug);
                        _questService.AddQuest(questId);
                        treatment.AddedToGameLog = _questService.HasQuest(questId);
                        _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, treatment.AddedToGameLog);
                    }
                    continue;
                }
                else if (questInJournal && !flagSaysAdded && !string.IsNullOrEmpty(questId))
                {
                    // Квест есть в журнале, но флаг не установлен - синхронизируем
                    _monitor.Log($"[StateService] Синхронизация: квест '{questId}' есть в журнале, но флаг не установлен", LogLevel.Debug);
                    _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, true);
                    treatment.AddedToGameLog = true;
                }

                // Восстанавливаем бафф если он пропал
                if (!_buffService.HasBuff(buffId))
                {
                    _monitor.Log($"[StateService] Восстанавливаем бафф '{buffId}' для квеста '{questId}'", LogLevel.Debug);
                    _buffService.ApplyBuffFromData(buffId);
                    restored++;
                }
            }

            if (restored > 0 || removed > 0)
            {
                _monitor.Log($"[StateService] Синхронизация: восстановлено {restored}, удалено {removed}", LogLevel.Info);
            }
        }

        /// <summary>
        /// Получает прогресс лечения для квеста
        /// </summary>
        public TreatmentProgress? GetProgress(string questId)
        {
            var treatment = _data.StressState.GetActiveTreatmentByQuest(questId);
            return treatment?.Progress;
        }


        /// <summary>
        /// Проверяет существование квеста в журнале через флаги (более надежно чем QuestService)
        /// </summary>
        public bool IsQuestInJournal(string questId)
        {
            // Сначала проверяем флаг
            bool flagSaysAdded = _data.StressState.TreatmentFlags.IsQuestAddedToJournal(questId);
            
            // Затем проверяем реальное состояние в журнале
            bool actuallyInJournal = _questService.HasQuest(questId);
            
            // Если есть расхождение - синхронизируем
            if (flagSaysAdded != actuallyInJournal)
            {
                _monitor.Log($"[StateService.IsQuestInJournal] Расхождение для квеста '{questId}': флаг={flagSaysAdded}, журнал={actuallyInJournal}", LogLevel.Warn);
                _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, actuallyInJournal);
                
                // Обновляем состояние квеста если он есть в ActiveTreatments
                var treatment = _data.StressState.GetActiveTreatmentByQuest(questId);
                if (treatment != null)
                {
                    treatment.AddedToGameLog = actuallyInJournal;
                }
            }
            
            return actuallyInJournal;
        }

        /// <summary>
        /// Проверяет, можно ли выдать бафф (с учетом кулдауна)
        /// </summary>
        public bool CanIssueBuff(string buffId, int cooldownDays = 7)
        {
            // Есть ли уже активный бафф
            if (_data.StressState.HasActiveBuff(buffId))
                return false;

            // Проверяем кулдаун
            if (_data.StressState.LastIssuedDay.TryGetValue(buffId, out var lastIssued))
            {
                int daysSince = SDate.Now().DaysSinceStart - lastIssued.DaysSinceStart;
                if (daysSince < cooldownDays)
                {
                    _monitor.Log($"[StateService] Бафф '{buffId}' на кулдауне: {daysSince}/{cooldownDays} дней", LogLevel.Debug);
                    return false;
                }
            }

            return true;
        }

        #region Централизованные проверки состояния

        /// <summary>
        /// Проверяет, есть ли активный бафф
        /// ⭐ ИЗМЕНЕНО: Теперь использует внутреннее состояние мода вместо проверки игры
        /// </summary>
        public bool HasActiveBuffInGame(string buffId)
        {
            // ⭐ НОВОЕ: Используем внутреннее состояние мода вместо проверки игры
            return _data.StressState.HasActiveBuff(buffId);
        }

        /// <summary>
        /// Проверяет, есть ли квест в журнале игры
        /// ⭐ ИЗМЕНЕНО: Теперь использует внутреннее состояние мода вместо проверки журнала
        /// </summary>
        public bool HasQuestInJournal(string questId)
        {
            // ⭐ НОВОЕ: Используем внутреннее состояние мода вместо проверки журнала
            bool result = _data.StressState.HasActiveQuest(questId);
            
            return result;
        }

        /// <summary>
        /// Проверяет, есть ли активное лечение для данного баффа
        /// </summary>
        public bool HasActiveTreatment(string buffId)
        {
            return _data.StressState.HasActiveBuff(buffId);
        }

        /// <summary>
        /// Проверяет, есть ли активный квест лечения
        /// </summary>
        public bool HasActiveQuestTreatment(string questId)
        {
            return _data.StressState.HasActiveQuest(questId);
        }

        /// <summary>
        /// Проверяет, заблокировано ли лечение (есть в ActiveTreatments)
        /// </summary>
        public bool IsTreatmentLocked(string buffId)
        {
            return _data.StressState.IsTreatmentLocked(buffId);
        }

        /// <summary>
        /// Проверяет, можно ли выдать новый бафф (учитывая кулдаун и активные лечения)
        /// </summary>
        public bool CanIssueNewBuff(string buffId, int cooldownDays = 7)
        {
            // Проверяем кулдаун
            if (!_data.StressState.CanIssueNewBuff(buffId, cooldownDays))
                return false;

            // Проверяем, нет ли уже активного лечения
            if (_data.StressState.HasActiveBuff(buffId))
                return false;

            return true;
        }

        /// <summary>
        /// Проверяет, есть ли валидное лечение в истории
        /// </summary>
        public bool HasValidTreatmentInHistory(string buffId)
        {
            return _data.StressState.HasValidTreatmentInHistory(buffId, SDate.Now());
        }

        #endregion
    }
}

