using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.UI
{
    /// <summary>UI-тексты окна «План Харви». Редактировать здесь.</summary>
    internal static class HarveyPanelTexts
    {
        public static bool IsInformal =>
            HarveyFriendshipHelper.IsDatingHarvey() || HarveyFriendshipHelper.IsMarriedToHarvey();

        public static string Tone(string formal, string informal) =>
            IsInformal ? informal : formal;

        public static string TalkToHarvey() =>
            Tone("Поговорите с Харви.", "Поговори с Харви.");

        public static string TalkToHarveySoon() =>
            Tone("Лучше не откладывать осмотр.", "Не заставляй его гадать, насколько тебе плохо.");

        public static string AfterAssignmentTalk() =>
            Tone("После этого поговорите с Харви.", "После этого поговори с Харви.");

        public static string HarveyWaitingTalk() =>
            Tone("Харви ждёт контрольный разговор.", "Харви ждёт контрольный разговор.");

        public static class Window
        {
            public const string Title = "План Харви";
        }

        public static class Tabs
        {
            public const string Overview = "Обзор";
            public const string Stress = "Стресс";
            public const string Injuries = "Травмы";
            public const string Plan = "План";
            public const string Trust = "Доверие";
        }

        public static class Overview
        {
            public const string CalmHeadline = "Сегодня всё спокойно";
            public const string CalmBody =
                "Харви не видит повода для срочного осмотра. Можно заниматься делами, но без героизма.";

            public const string AssignmentHeadline = "Назначение Харви";
            public const string HarveyWaitingHeadline = "Харви ждёт контрольный разговор";

            public const string InjuryAttention = "Травма требует внимания.";
            public const string InjuryCareHint = "Следите за повязкой и не рискуйте в шахте.";
            public const string InjuryCareHintInformal = "Следи за повязкой и не рискуй в шахте.";

            public static string CalmAdvice =>
                Tone(
                    "Харви наблюдает за состоянием. Небольшие шаги лучше, чем снова довести себя до срыва.",
                    "Харви присматривает за тобой. Делай паузы — это тоже часть заботы.");

            public static string AssignmentAdvice =>
                Tone(
                    "Харви просит не геройствовать: сначала выполните назначение, потом возвращайтесь к делам.",
                    "Харви просит не геройствовать: сначала назначение, потом дела.");

            public static string ReviewAdvice =>
                Tone(
                    "Харви ждёт вас — можно зайти в клинику или поговорить, когда он рядом.",
                    "Он волнуется за тебя. Зайди к нему, когда сможешь.");

            public static string TrustedAdvice =>
                Tone(
                    "Харви рядом, когда нужен. Делайте паузы — это тоже часть заботы.",
                    "Он рядом, когда нужен. Не заставляй его гадать, как ты.");
        }

        public static class Stress
        {
            public const string SectionAssignment = "Назначение";
            public const string SectionActive = "Сейчас";
            public const string SectionReference = "Справочник";

            public const string NoAssignment =
                "Сейчас нет срочного назначения. Ниже — что Харви обычно советует при стрессе.";

            public const string ProgressPrefix = "Осталось:";

            public const string AnxietyStallHint =
                "Здесь слишком шумно или небезопасно. Найдите место тише.";

            public const string AnxietyStallHintInformal =
                "Здесь слишком шумно или небезопасно. Найди место потише.";

            public static string AnxietyComplete =>
                Tone(
                    "Вы справились. Теперь поговорите с Харви — он захочет убедиться, что дыхание ровное.",
                    "Ты справилась. Теперь поговори с Харви — он захочет убедиться, что дыхание ровное.");

            public static string StageReadyForTalk => "Пора поговорить с Харви.";
            public static string StageInProgress => "Назначение в процессе";
            public static string StageCompleted => "Назначение выполнено";

            public static string RowActive => "Сейчас";
            public static string RowInactive => "Не беспокоит";
        }

        public static class Injuries
        {
            public const string NoInjuriesTitle = "Травм нет";
            public const string NoInjuriesBody =
                "Харви всё равно попросил бы быть осторожнее, но срочного осмотра не требуется.";

            public const string DataUnavailableTitle = "Данные о травмах сейчас недоступны";
            public const string DataUnavailableBody =
                "Если установлен мод HarveyOverhaulInjury, эта вкладка покажет осмотры, фазы лечения и осложнения.";

            public const string ActiveCareTitle = "Травма требует ухода";
            public const string ActiveCareBody =
                "Следуйте назначению Харви и не рискуйте восстановлением.";

            public const string MainInjuryLabel = "Сейчас";
            public const string AdditionalLabel = "Ещё";
            public const string ComplicationsLabel = "Осложнения";
        }

        public static class Plan
        {
            public const string NoPlanTitle = "Плана восстановления нет";
            public const string NoPlanBody = "Сейчас нет строгого режима. Просто берегите себя.";
            public const string NoPlanBodyInformal = "Сейчас нет строгого режима. Просто береги себя.";

            public const string ActiveTitle = "План восстановления";
            public const string StressAssignmentTitle = "Назначение на сегодня";

            public const string InjuryModMissingBody =
                "План восстановления после травмы появится здесь, когда установлен HarveyOverhaulInjury.";

            public const string InjuryDataUnavailableBody =
                "Харви составил план, но сводка пока не загрузилась. Попробуйте открыть окно позже.";
        }

        public static class Trust
        {
            public const string ObservingTitle = "Харви присматривается";
            public const string ObservingBody =
                "Он замечает, когда вы бережёте себя — и когда делаете вид, что всё нормально.";

            public const string ObservingBodyInformal =
                "Он замечает, когда ты бережёшь себя — и когда делаешь вид, что всё нормально.";

            public const string WaryTitle = "Харви насторожен";
            public static string WaryBody =>
                Tone(
                    "Он будет чаще напоминать о режиме, потому что вы недавно рисковали.",
                    "Он будет чаще напоминать о режиме, потому что ты недавно рисковала.");

            public const string CautiousTitle = "Харви осторожно доверяет";
            public static string CautiousBody =>
                Tone(
                    "Он всё ещё волнуется, но видит, что вы стараетесь.",
                    "Он всё ещё волнуется, но видит, что ты стараешься.");

            public const string HighTitle = "Харви доверяет тебе";
            public static string HighBody =>
                Tone(
                    "Он знает: если вы обещали беречь себя, вы правда стараетесь.",
                    "Он знает: если ты обещала беречь себя, ты правда стараешься.");

            public const string PermissionOnlyTitle = "Только с разрешения";
            public static string PermissionOnlyBody =>
                Tone(
                    "Харви не будет давить. Но спросит ещё раз, если испугается за вас.",
                    "Харви не будет давить. Но спросит ещё раз, если испугается за тебя.");

            public const string EveningChecksAllowed = "Вечерние проверки разрешены";
            public const string HoldHandAllowed = "Можно держать за руку";
            public const string StrictCareAllowed = "Строгий уход разрешён";
            public const string EmergencyAccessAllowed = "Экстренный доступ разрешён";

            public static string LevelLine(string levelName, int points) =>
                $"{levelName} · {points} очков доверия";
        }
    }
}
