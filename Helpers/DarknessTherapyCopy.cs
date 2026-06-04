namespace HarveyStressMeter.Helpers
{
    /// <summary>Игровые строки терапии темноты (HUD, квест, подсказки).</summary>
    public static class DarknessTherapyCopy
    {
        public const int MinutesPerEvening = DarknessLegacyHelper.Step1MinutesPerEvening;
        public const int EveningsRequired = DarknessLegacyHelper.Step1EveningsRequired;

        public static string Step1QuestObjectiveAwaitingHarvey(int required)
            => $"Зачтённые вечера: {required}/{required}.\n" +
               "Приди к Харви и поговори. Без этого я не переведу тебя на следующий шаг.";

        public static string Step1QuestObjectiveTodayCredited(int evenings, int required)
            => $"Зачтённые вечера: {evenings}/{required}.\n" +
               "Сегодняшний вечер зачтён. Завтра после 20:00 — снова час дома при свете.";

        public static string Step1QuestObjectiveInProgress(int evenings, int required, int minutesToday)
            => $"Зачтённые вечера: {evenings}/{required}.\n" +
               $"Сегодня: {minutesToday}/{MinutesPerEvening} минут (после 20:00, дома при свете).\n" +
               "Можно заниматься делами. Не уходи в ночь и не оставляй себя одну с паникой.";

        public static string StartTherapyHud()
            => "Терапия началась. После восьми — час дома при свете. Не проверяй себя темнотой специально: мы лечим страх, а не спорим с ним.";

        public static string TimerHudAtHome(int minutes)
            => $"Терапия темноты: {minutes}/{MinutesPerEvening} минут";

        public static string TimerHudAwayFromHome(int minutes)
            => $"Терапия темноты: {minutes}/{MinutesPerEvening} минут. Вернись домой при свете — таймер идёт только там, после 20:00.";

        public static string TimerHintAtHome()
            => "Останься дома при свете. Можно заниматься делами — главное, не убегай в ночь.";

        public static string AllEveningsCreditedHud(int required)
            => required <= 1
                ? "Вечер зачтён. Приди поговорить с Харви — я проверю, очень внимательно."
                : $"Все {required} вечера за тобой. Приди поговорить с Харви — я проверю, очень внимательно.";

        public static string EveningCreditedHud(int evenings, int required)
        {
            if (evenings >= required)
                return AllEveningsCreditedHud(required);

            return evenings switch
            {
                1 =>
                    $"Хорошо. Первый вечер зачтён (1/{required}). Ты не обязана побеждать страх сразу — достаточно не отдавать ему весь дом.",
                2 when required >= 2 =>
                    $"Второй вечер зачтён (2/{required}). Ты держишься. Завтра после восьми — снова час дома при свете.",
                _ =>
                    $"Вечер зачтён ({evenings}/{required}).",
            };
        }
    }
}
