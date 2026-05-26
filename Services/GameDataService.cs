using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для загрузки игровых данных из JSON файлов
    /// </summary>
    public class GameDataService
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;

        private GameDataContainer _gameData = new();
        private Dictionary<string, BuffData> _buffDataById = new();
        private Dictionary<string, QuestData> _questDataById = new();
        private Dictionary<string, MailData> _mailDataById = new();

        public GameDataService(IMonitor monitor, IModHelper helper)
        {
            _monitor = monitor;
            _helper = helper;
        }

        /// <summary>
        /// Загружает все данные при старте мода
        /// </summary>
        public void LoadAllData()
        {
            LoadQuests();
            LoadMails();
            
            _monitor.Log($"[GameDataService] ✅ Загружено: {_questDataById.Count} квестов, {_mailDataById.Count} писем", LogLevel.Info);
        }

        /// <summary>
        /// Загружает метаданные квестов из mod assets/stress_quests.json (Title, Description, Objective).
        /// Журнал игры требует те же ID в Data/Quests — их добавляет HarveyOverhaul [CP] (assets/Code/questsStress.json).
        /// </summary>
        private void LoadQuests()
        {
            try
            {
                var questsContainer = _helper.Data.ReadJsonFile<Dictionary<string, List<QuestData>>>("assets/stress_quests.json");
                
                if (questsContainer != null && questsContainer.ContainsKey("Quests"))
                {
                    _gameData.Quests = questsContainer["Quests"];
                    _questDataById = _gameData.Quests.ToDictionary(q => q.Id, q => q);
                    _monitor.Log($"[GameDataService] Загружено {_gameData.Quests.Count} квестов", LogLevel.Debug);
                }
                else
                {
                    _monitor.Log("[GameDataService] Не удалось загрузить квесты из assets/stress_quests.json", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"[GameDataService] ОШИБКА при загрузке квестов: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Загружает данные о письмах
        /// </summary>
        private void LoadMails()
        {
            try
            {
                var mailsContainer = _helper.Data.ReadJsonFile<Dictionary<string, List<MailData>>>("assets/stress_mails.json");
                
                if (mailsContainer != null && mailsContainer.ContainsKey("Mails"))
                {
                    _gameData.Mails = mailsContainer["Mails"];
                    _mailDataById = _gameData.Mails.ToDictionary(m => m.Id, m => m);
                    _monitor.Log($"[GameDataService] Загружено {_gameData.Mails.Count} писем", LogLevel.Debug);
                }
                else
                {
                    _monitor.Log("[GameDataService] Не удалось загрузить письма из assets/stress_mails.json", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"[GameDataService] ОШИБКА при загрузке писем: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Получает данные квеста по ID
        /// </summary>
        public QuestData? GetQuestData(string questId)
        {
            return _questDataById.TryGetValue(questId, out var quest) ? quest : null;
        }

        /// <summary>
        /// Получает данные письма по ID
        /// </summary>
        public MailData? GetMailData(string mailId)
        {
            return _mailDataById.TryGetValue(mailId, out var mail) ? mail : null;
        }

        /// <summary>
        /// Получает все квесты
        /// </summary>
        public List<QuestData> GetAllQuests()
        {
            return _gameData.Quests;
        }

        /// <summary>
        /// Получает все письма
        /// </summary>
        public List<MailData> GetAllMails()
        {
            return _gameData.Mails;
        }

        /// <summary>
        /// Получает квесты для конкретного баффа (по соглашению об именовании)
        /// </summary>
        public List<QuestData> GetQuestsForBuff(string buffId)
        {
            // Преобразуем buffStressTired в HarveyMod_TiredRecovery
            var buffName = buffId.Replace("buffStress", "").Replace("buff", "");
            return _gameData.Quests.Where(q => q.Id.Contains(buffName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Получает письмо для конкретного баффа
        /// </summary>
        public MailData? GetMailForBuff(string buffId)
        {
            var buffName = buffId.Replace("buffStress", "").Replace("buff", "");
            return _gameData.Mails.FirstOrDefault(m => m.Id.Contains(buffName, StringComparison.OrdinalIgnoreCase));
        }
    }
}

