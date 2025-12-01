using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Модель для данных о баффе стресса
    /// </summary>
    public class BuffData
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsDebuff { get; set; }
        public int Duration { get; set; }
        public int IconSpriteIndex { get; set; }
        public Dictionary<string, int> Effects { get; set; } = new();
    }

    /// <summary>
    /// Модель для данных о квесте
    /// </summary>
    public class QuestData
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "Basic";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Objective { get; set; } = "";
        public string CompletionMessage { get; set; } = "";
        public int RewardMoney { get; set; } = 0;
        public string? RewardItem { get; set; }
    }

    /// <summary>
    /// Модель для данных о письме
    /// </summary>
    public class MailData
    {
        public string Id { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Text { get; set; } = "";
        public string? AttachedItem { get; set; }
    }

    /// <summary>
    /// Контейнер для всех игровых данных
    /// </summary>
    public class GameDataContainer
    {
        public List<BuffData> StressBuffs { get; set; } = new();
        public List<BuffData> CureBuffs { get; set; } = new();
        public List<QuestData> Quests { get; set; } = new();
        public List<MailData> Mails { get; set; } = new();
    }
}

