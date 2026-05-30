namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Тексты currentObjective для legacy stress treatment квестов (CP + live C# updates).
    /// </summary>
    public static class LegacyTreatmentObjectives
    {
        public const int TooColdWarmSecondsRequired = 120;
        public const int OverworkBreakSecondsRequired = 30;

        public static string Lonely(int talkedToday) =>
            talkedToday >= 3
                ? "✅ Вы поговорили с 3 жителями. Поговорите с Харви."
                : $"Поговорите с жителями: {talkedToday}/3 сегодня.";

        public const string HungerDone = "✅ Вы поели. Поговорите с Харви.";

        public const string NoSleepDone = "✅ Вы легли до 22:00. Поговорите с Харви.";

        public const string NoSleepDefault = "Лягте спать до 22:00 сегодня.";

        public const string NoSleepLateFailed =
            "Лягте спать до 22:00. Последняя попытка: не выполнено.";

        public const string OverworkDailyStart =
            "Сегодня сделайте 3 перерыва по 30 секунд в зоне отдыха (дом, баня, клиника).";

        public static string Overwork(int breaksToday, int breakSeconds, bool breakActive)
        {
            if (breaksToday >= 3)
                return "✅ Все перерывы сделаны. Поговорите с Харви.";

            if (breakActive && breakSeconds > 0 && breakSeconds < OverworkBreakSecondsRequired)
                return $"Перерывы: {breaksToday}/3. Текущий перерыв: {breakSeconds}/{OverworkBreakSecondsRequired} сек.";

            return $"Перерывы: {breaksToday}/3 выполнено.";
        }

        public static string TooColdWarm(int warmSeconds)
        {
            var seconds = System.Math.Min(warmSeconds, TooColdWarmSecondsRequired);
            if (warmSeconds >= TooColdWarmSecondsRequired)
                return "✅ Вы согрелись. Поговорите с Харви.";

            return $"Согрейтесь в тёплом месте: {seconds}/{TooColdWarmSecondsRequired} сек.";
        }

        public const string TooColdHotDrink = "✅ Вы выпили горячий напиток. Поговорите с Харви.";
    }
}
