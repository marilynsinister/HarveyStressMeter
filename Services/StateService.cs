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

            int deduped = _data.StressState.DedupeTreatmentHistory();
            if (deduped > 0)
                _monitor.Log($"[StateService] TreatmentHistory: удалено дублей по TreatmentKey: {deduped}", LogLevel.Info);

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
            bool buffApplied = _buffService.ApplyBuffFromData(buffId);
            
            if (!buffApplied)
            {
                _monitor.Log($"[StateService] ❌ Не удалось применить дебафф '{buffId}'. Лечение создано, но бафф не применен в игре.", LogLevel.Error);
                return;
            }

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
            // ⭐ ИСПРАВЛЕНО: Изменен уровень лога на Trace чтобы не спамить
            if (!_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[StateService.UpdateProgress] Квест '{questId}' не активен", LogLevel.Trace);
                return;
            }

            var treatment = _data.StressState.GetActiveTreatmentByQuest(questId);
            if (treatment == null)
            {
                _monitor.Log($"[StateService.UpdateProgress] Лечение для квеста '{questId}' не найдено", LogLevel.Trace);
                return;
            }

            // ⭐ НОВОЕ: Убеждаемся, что Progress инициализирован
            if (treatment.Progress == null)
            {
                treatment.Progress = new TreatmentProgress();
                _monitor.Log($"[StateService.UpdateProgress] ⚠️ Progress был null для квеста '{questId}', инициализирован", LogLevel.Warn);
            }

            // ⭐ НОВОЕ: Логируем состояние ПЕРЕД обновлением
            _monitor.Log($"[StateService.UpdateProgress] ═══ ОБНОВЛЕНИЕ ПРОГРЕССА '{questId}' ═══", LogLevel.Debug);
            LogProgressState(questId, treatment.Progress, "ПЕРЕД обновлением");

            // Вызываем делегат для обновления
            updateAction(treatment.Progress);

            // ⭐ НОВОЕ: Логируем состояние ПОСЛЕ обновления
            LogProgressState(questId, treatment.Progress, "ПОСЛЕ обновления");

            _monitor.Log($"[StateService.UpdateProgress] ✅ Прогресс квеста '{questId}' обновлен", LogLevel.Debug);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Логирует состояние прогресса квеста
        /// </summary>
        private void LogProgressState(string questId, TreatmentProgress progress, string stage)
        {
            if (progress == null)
            {
                _monitor.Log($"[StateService.UpdateProgress] Progress == null на этапе {stage}", LogLevel.Warn);
                return;
            }

            _monitor.Log($"[StateService.UpdateProgress] ─── Состояние {stage} ───", LogLevel.Debug);
            
            // Общие поля
            _monitor.Log($"[StateService.UpdateProgress]   SecondsNearHarvey: {progress.SecondsNearHarvey}", LogLevel.Debug);
            _monitor.Log($"[StateService.UpdateProgress]   EveningInLightSeconds: {progress.EveningInLightSeconds}", LogLevel.Debug);
            _monitor.Log($"[StateService.UpdateProgress]   TalkedUniqueToday: {progress.TalkedUniqueToday}", LogLevel.Debug);
            
            // Поля для Social квеста
            _monitor.Log($"[StateService.UpdateProgress]   SocialTalksAfterQuest: {progress.SocialTalksAfterQuest}", LogLevel.Debug);
            
            // Другие поля
            _monitor.Log($"[StateService.UpdateProgress]   AteAnyFood: {progress.AteAnyFood}", LogLevel.Debug);
            _monitor.Log($"[StateService.UpdateProgress]   WarmSeconds: {progress.WarmSeconds}", LogLevel.Debug);
            _monitor.Log($"[StateService.UpdateProgress]   EarlySleepStreak: {progress.EarlySleepStreak}", LogLevel.Debug);
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

            // ⭐ ИСПРАВЛЕНО: Удаляем из активных по правильному ключу (TreatmentKey, а не buffId)
            bool removed = _data.StressState.RemoveTreatment(treatment.TreatmentKey);
            if (removed)
            {
                _monitor.Log($"[StateService] ✅ Лечение удалено из ActiveTreatments: {treatment.TreatmentKey}", LogLevel.Info);
            }
            else
            {
                _monitor.Log($"[StateService] ⚠️ Не удалось удалить лечение {treatment.TreatmentKey} из ActiveTreatments", LogLevel.Warn);
            }

            _monitor.Log($"[StateService] ✅ Лечение завершено: квест='{questId}', бафф='{buffId}'", LogLevel.Info);
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Синхронизирует состояние с игрой
        /// - Очищает завершенные лечения
        /// - Восстанавливает потерянные баффы для АКТИВНЫХ лечений
        /// </summary>
        public void SyncWithGame()
        {
            int cleanedCount = 0;
            int restoredCount = 0;

            foreach (var (treatmentKey, treatment) in _data.StressState.ActiveTreatments.ToList())
            {
                var questId = treatment.QuestId;
                var buffId = treatment.BuffId;

                // ⭐ ИСПРАВЛЕНО: Сначала очищаем завершенные лечения
                if (treatment.IsCured || treatment.IsCompleted)
                {
                    _data.StressState.RemoveTreatment(treatmentKey);
                    cleanedCount++;
                    _monitor.Log($"[StateService] Очищено завершенное лечение: {treatmentKey}", LogLevel.Debug);
                    continue;
                }

                // Если квест завершен в игре - завершаем лечение
                if (!string.IsNullOrEmpty(questId) && _questService.HasQuest(questId))
                {
                    var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                    if (quest?.completed.Value == true)
                    {
                        CompleteTreatment(questId);
                        cleanedCount++;
                        continue;
                    }
                }

                // ⭐ ИСПРАВЛЕНО: Восстанавливаем баффы ТОЛЬКО для активных (незавершенных) лечений
                if (!_buffService.HasBuff(buffId))
                {
                    _monitor.Log($"[StateService] 🔄 Восстанавливаем потерянный бафф '{buffId}' для активного лечения {treatmentKey}", LogLevel.Info);
                    if (_buffService.ApplyBuffFromData(buffId))
                    {
                        restoredCount++;
                    }
                    else
                    {
                        _monitor.Log($"[StateService] ⚠️ Не удалось восстановить бафф '{buffId}' для лечения {treatmentKey}", LogLevel.Warn);
                    }
                }
            }

            if (cleanedCount > 0)
            {
                _monitor.Log($"[StateService] SyncWithGame: очищено {cleanedCount} завершенных лечений", LogLevel.Info);
            }
            
            if (restoredCount > 0)
            {
                _monitor.Log($"[StateService] SyncWithGame: восстановлено {restoredCount} потерянных баффов", LogLevel.Info);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Восстанавливает все активные баффы из сохраненных данных
        /// Вызывается при загрузке игры и в начале дня для восстановления потерянных баффов
        /// </summary>
        public void RestoreAllActiveBuffs()
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log("[StateService] RestoreAllActiveBuffs: игра еще не готова, пропускаем восстановление", LogLevel.Debug);
                return;
            }

            int restoredCount = 0;
            int skippedCount = 0;

            // Получаем все уникальные активные баффы из активных лечений
            var activeBuffIds = _data.StressState.ActiveTreatments.Values
                .Where(t => !t.IsCured && !t.IsCompleted)
                .Select(t => t.BuffId)
                .Distinct()
                .ToList();

            _monitor.Log($"[StateService] RestoreAllActiveBuffs: найдено {activeBuffIds.Count} активных баффов для восстановления", LogLevel.Info);

            foreach (var buffId in activeBuffIds)
            {
                // Проверяем, есть ли уже бафф в игре
                if (_buffService.HasBuff(buffId))
                {
                    skippedCount++;
                    _monitor.Log($"[StateService] Бафф '{buffId}' уже активен в игре, пропускаем", LogLevel.Debug);
                    continue;
                }

                // Восстанавливаем бафф
                _monitor.Log($"[StateService] 🔄 Восстанавливаем бафф '{buffId}' из сохраненных данных", LogLevel.Info);
                if (_buffService.ApplyBuffFromData(buffId))
                {
                    restoredCount++;
                    _monitor.Log($"[StateService] ✅ Бафф '{buffId}' успешно восстановлен", LogLevel.Info);
                }
                else
                {
                    _monitor.Log($"[StateService] ⚠️ Не удалось восстановить бафф '{buffId}'", LogLevel.Warn);
                }
            }

            if (restoredCount > 0 || skippedCount > 0)
            {
                _monitor.Log($"[StateService] RestoreAllActiveBuffs: восстановлено {restoredCount} баффов, пропущено {skippedCount} (уже активны)", LogLevel.Info);
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
        /// ⭐ НОВОЕ: Получает активное лечение для баффа
        /// </summary>
        public TreatmentState? GetActiveTreatment(string buffId)
        {
            return _data.StressState.GetActiveTreatment(buffId);
        }

        /// <summary>
        /// Проверяет существование квеста в журнале
        /// </summary>
        public bool IsQuestInJournal(string questId)
        {
            return _questService.HasQuest(questId);
        }

        /// <summary>
        /// Проверяет, можно ли выдать бафф (с учетом кулдауна и иммунитета)
        /// </summary>
        public bool CanIssueBuff(string buffId, int cooldownDays = 7)
        {
            // Есть ли уже активный бафф
            if (_data.StressState.HasActiveBuff(buffId))
                return false;

            // ⭐ НОВОЕ: Проверяем индивидуальный иммунитет
            if (_data.StressState.HasImmunity(buffId))
            {
                var endDate = _data.StressState.GetImmunityEndDate(buffId);
                _monitor.Log($"[StateService] Бафф '{buffId}' заблокирован иммунитетом до {endDate}", LogLevel.Debug);
                return false;
            }

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

        /// <summary>
        /// Mod state: есть не-излеченная запись лечения для данного debuff в ActiveTreatments.
        /// </summary>
        public bool HasActiveTreatmentState(string buffId)
        {
            return _data.StressState.HasActiveBuff(buffId);
        }

        /// <summary>
        /// Mod state: лечение начато (TreatmentStarted) и квест ещё активен в state.
        /// </summary>
        public bool HasActiveQuestState(string questId)
        {
            return _data.StressState.HasActiveQuest(questId);
        }

        /// <summary>
        /// Игра: бафф висит на игроке (Game1.player.hasBuff).
        /// </summary>
        public bool HasBuffInGame(string buffId)
        {
            return _buffService.HasBuff(buffId);
        }

        /// <summary>
        /// Игра: квест присутствует в журнале SDV.
        /// </summary>
        public bool HasQuestInGameJournal(string questId)
        {
            return _questService.HasQuest(questId);
        }

        /// <summary>
        /// WARNING: проверяет mod state (ActiveTreatments), не Game1.player.hasBuff.
        /// Используйте <see cref="HasActiveTreatmentState"/> или <see cref="HasBuffInGame"/>.
        /// </summary>
        [Obsolete("Misleading name: checks mod state, not the game. Use HasActiveTreatmentState or HasBuffInGame.")]
        public bool HasActiveBuffInGame(string buffId)
        {
            return HasActiveTreatmentState(buffId);
        }

        /// <summary>
        /// WARNING: проверяет mod state (TreatmentStarted), не journal SDV.
        /// Используйте <see cref="HasActiveQuestState"/> или <see cref="HasQuestInGameJournal"/>.
        /// </summary>
        [Obsolete("Misleading name: checks mod state, not the journal. Use HasActiveQuestState or HasQuestInGameJournal.")]
        public bool HasQuestInJournal(string questId)
        {
            return HasActiveQuestState(questId);
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
        /// Проверяет, заблокировано ли лечение
        /// </summary>
        public bool IsTreatmentLocked(string buffId)
        {
            return _data.StressState.IsTreatmentLocked(buffId);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Проверяет, есть ли иммунитет для дебаффа
        /// </summary>
        public bool HasImmunity(string buffId)
        {
            return _data.StressState.HasImmunity(buffId);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Устанавливает иммунитет для дебаффа на указанное количество дней
        /// </summary>
        public void SetImmunity(string buffId, int days)
        {
            _data.StressState.SetImmunity(buffId, days);
            var endDate = _data.StressState.GetImmunityEndDate(buffId);
            _monitor.Log($"[StateService] ✅ Установлен иммунитет для '{buffId}' на {days} дней (до {endDate})", LogLevel.Info);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Удаляет иммунитет для дебаффа
        /// </summary>
        public void RemoveImmunity(string buffId)
        {
            _data.StressState.RemoveImmunity(buffId);
            _monitor.Log($"[StateService] Иммунитет для '{buffId}' удален", LogLevel.Debug);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Очищает истекшие иммунитеты
        /// Вызывается каждый день
        /// </summary>
        public void CleanupExpiredImmunities()
        {
            _data.StressState.CleanupExpiredImmunities();
        }

        public bool WasTreatmentOfferShownToday(string buffId)
        {
            return _data.StressState.LastTreatmentOfferDateByBuff.TryGetValue(buffId, out var date)
                && IsSameGameDay(date);
        }

        public void MarkTreatmentOfferShown(string buffId)
        {
            if (!StressDialoguePipelineGuard.CanRun(
                    out _,
                    requireDialogueBox: false,
                    requireHarveySpeaker: false))
            {
                return;
            }

            _data.StressState.LastTreatmentOfferDateByBuff[buffId] = SDate.Now();
            _monitor.Log($"[StateService] Treatment offer recorded today for {buffId}", LogLevel.Debug);
        }

        public bool WasTreatmentDeclinedToday(string buffId)
        {
            return _data.StressState.LastTreatmentDeclinedDateByBuff.TryGetValue(buffId, out var date)
                && IsSameGameDay(date);
        }

        public void MarkTreatmentDeclined(string buffId)
        {
            _data.StressState.LastTreatmentDeclinedDateByBuff[buffId] = SDate.Now();
            _monitor.Log($"[StateService] Treatment decline recorded today for {buffId}", LogLevel.Debug);
        }

        private static bool IsSameGameDay(SDate date)
        {
            var today = SDate.Now();
            return date.DaysSinceStart == today.DaysSinceStart;
        }
    }
}

