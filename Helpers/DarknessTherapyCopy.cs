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

        /// <summary>Описание квеста «Шаг 1» с подсчётом вечеров и тем, как его закрыть.</summary>
        public static string Step1Description(int evenings, int required, string todayStatus)
            => "Харви попросил начать с самого мягкого: после 20:00 побыть дома при свете. " +
               $"Один вечер засчитывается, когда наберётся {MinutesPerEvening} игровых минут дома между 20:00 и полуночью " +
               "(не больше одного вечера в день; можно заниматься делами, но не выходить из дома).\n\n" +
               $"Вечера дома: {evenings}/{required}.\n" +
               todayStatus + "\n\n" +
               $"Как завершить: набери {required} {EveningsWord(required)} — и поговори с Харви. Он закроет этот шаг.";

        public static string Step1DescriptionToday(int minutesToday)
            => $"Сегодня: {minutesToday}/{MinutesPerEvening} мин дома.";

        public static string Step1DescriptionTodayCredited()
            => "Сегодня вечер уже зачтён. Следующий — завтра после 20:00.";

        public static string Step1DescriptionAwaitingHarvey(int required)
            => $"Все {required} {EveningsWord(required)} есть. Теперь поговори с Харви.";

        private static string EveningsWord(int n)
            => (n % 10 == 1 && n % 100 != 11) ? "вечер"
               : (n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14)) ? "вечера"
               : "вечеров";

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
