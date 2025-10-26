using System;
using StardewValley;
using StardewModdingAPI;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Вспомогательные методы для работы с топиками разговоров
    /// </summary>
    public static class ConversationHelper
    {
        private static IMonitor? _monitor;

        public static void Initialize(IMonitor monitor)
        {
            _monitor = monitor;
        }

        public static bool HasTopic(string topic)
        {
            var events = Game1.player.activeDialogueEvents;
            if (events == null) return false;
            
            // Проверяем наличие топика (даже с days = 0, т.к. CP может добавлять с 0)
            return events.TryGetValue(topic, out int days) && days >= 0;
        }

        public static void AddTopic(string topic, int days)
        {
            if (days <= 0) days = 1;
            var events = Game1.player.activeDialogueEvents;
            
            if (events.ContainsKey(topic))
            {
                int oldDays = events[topic];
                int newDays = Math.Max(oldDays, days);
                
                // Логируем только если значение действительно изменилось
                if (oldDays != newDays)
                {
                    events[topic] = newDays;
                    _monitor?.Log($"[Топик изменен] {topic}: {oldDays} → {newDays} дней", LogLevel.Info);
                }
            }
            else
            {
                events.Add(topic, days);
                _monitor?.Log($"[Топик добавлен] {topic} на {days} дней", LogLevel.Info);
            }
        }

        public static void RemoveTopic(string topic)
        {
            var events = Game1.player.activeDialogueEvents;
            if (events.ContainsKey(topic))
            {
                int daysRemaining = events[topic];
                events.Remove(topic);
                _monitor?.Log($"[Топик удален] {topic} (оставалось {daysRemaining} дней)", LogLevel.Info);
            }
        }
    }
}

