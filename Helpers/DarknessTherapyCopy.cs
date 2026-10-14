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
            => $"Вечера дома: {required}/{required} — назначение выполнено.\n" +
               "Поговорите с Харви.";

        public static string Step1QuestObjectiveTodayCredited(int evenings, int required)
            => $"Вечера дома: {evenings}/{required}. Сегодняшний вечер зачтён.\n" +
               "Следующий — завтра после 20:00.";

        public static string Step1QuestObjectiveInProgress(int evenings, int required, int secondsToday)
            => $"Вечера дома: {evenings}/{required}. Сегодня: {secondsToday / 60}/{MinutesPerEvening} мин.\n" +
               $"Нужно {FormatDailyRequirement()} дома между 20:00 и 00:00.";

        public static string StartTherapyHud()
            => "Терапия началась. После восьми — час дома при свете. Не проверяй себя темнотой специально: мы лечим страх, а не спорим с ним.";

        public static string TimerHudAtHome(int secondsToday)
            => $"Терапия темноты: дома {secondsToday / 60}/{MinutesPerEvening} мин.";

        public static string TimerHudAwayFromHome(int secondsToday)
            => $"Терапия темноты: {secondsToday / 60}/{MinutesPerEvening} мин. Вернись домой — время идёт только там, с 20:00 до полуночи.";

        public static string TimerHintAtHome()
            => "Останься дома при свете. Можно заниматься делами — главное, не убегай в ночь.";

        public static string AllEveningsCreditedHud(int required)
            => required <= 1
                ? "Все вечера зачтены. Поговорите с Харви."
                : $"Все {required} вечера зачтены. Поговорите с Харви.";

        public static string EveningCreditedHud(int evenings, int required)
        {
            if (evenings >= required)
                return AllEveningsCreditedHud(required);

            return $"Вечер терапии зачтён: {evenings}/{required}. Завтра — следующий.";
        }

        private static string FormatDailyRequirement()
            => "1 игровой час";
    }
}
