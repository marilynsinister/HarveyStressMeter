namespace HarveyStressMeter.Helpers
{
    /// <summary>Игровые строки терапии темноты (HUD, квест, подсказки).</summary>
    public static class DarknessTherapyCopy
    {
        public const int SecondsPerEvening = DarknessLegacyHelper.Step1SecondsPerEvening;
        public const int MinutesPerEvening = DarknessLegacyHelper.Step1MinutesPerEvening;
        public const int EveningsRequired = DarknessLegacyHelper.Step1EveningsRequired;

        public const string Step1QuestTitle = "Терапия темноты";

        public static string Step1QuestObjectiveAwaitingHarvey(int required)
            => $"{Step1QuestTitle}\n" +
               $"Вечера дома при мягком свете: {required}/{required}\n" +
               "✅ Назначение выполнено.\n" +
               "Поговорите с Харви.";

        public static string Step1QuestObjectiveTodayCredited(int evenings, int required)
            => $"{Step1QuestTitle}\n" +
               $"Вечера дома при мягком свете: {evenings}/{required}\n" +
               "✅ Сегодняшний вечер зачтён.\n" +
               "Продолжайте терапию в другой вечер.";

        public static string Step1QuestObjectiveInProgress(int evenings, int required, int secondsToday)
            => $"{Step1QuestTitle}\n" +
               $"Вечера дома при мягком свете: {evenings}/{required}\n" +
               $"⬜ Сегодня: побыть дома при свете {FormatDailyRequirement()} после 20:00.\n" +
               $"Прогресс сегодня: {secondsToday}/{SecondsPerEvening} сек.";

        public static string StartTherapyHud()
            => "Терапия началась. После восьми — час дома при свете. Не проверяй себя темнотой специально: мы лечим страх, а не спорим с ним.";

        public static string TimerHudAtHome(int secondsToday)
            => $"Терапия темноты: {secondsToday}/{SecondsPerEvening} сек.";

        public static string TimerHudAwayFromHome(int secondsToday)
            => $"Терапия темноты: {secondsToday}/{SecondsPerEvening} сек. Вернись домой при свете — таймер идёт только там, после 20:00.";

        public static string TimerHintAtHome()
            => "Останься дома при свете. Можно заниматься делами — главное, не убегай в ночь.";

        public static string AllEveningsCreditedHud(int required)
            => required <= 1
                ? "✅ Все вечера зачтены. Поговорите с Харви."
                : $"✅ Все {required} вечера зачтены. Поговорите с Харви.";

        public static string EveningCreditedHud(int evenings, int required)
        {
            if (evenings >= required)
                return AllEveningsCreditedHud(required);

            return $"✅ Вечер терапии зачтён ({evenings}/{required})";
        }

        private static string FormatDailyRequirement()
            => $"{MinutesPerEvening} минут";
    }
}
