using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Модель для хранения диалогов стресса из JSON
    /// </summary>
    public class StressDialogueData
    {
        /// <summary>
        /// ID баффа (например, "buffStressSocial")
        /// </summary>
        public string BuffId { get; set; } = "";

        /// <summary>
        /// Текст диалога для низкого уровня дружбы (0-2 сердца)
        /// </summary>
        public string DialogueLowFriendship { get; set; } = "";

        /// <summary>
        /// Текст диалога для среднего уровня дружбы (3-6 сердец)
        /// </summary>
        public string DialogueMediumFriendship { get; set; } = "";

        /// <summary>
        /// Текст диалога для высокого уровня дружбы (7-10 сердец)
        /// </summary>
        public string DialogueHighFriendship { get; set; } = "";

        /// <summary>
        /// Текст диалога для свиданий
        /// </summary>
        public string DialogueDating { get; set; } = "";

        /// <summary>
        /// Текст диалога для брака
        /// </summary>
        public string DialogueMarried { get; set; } = "";

        /// <summary>
        /// Отображаемое название дебаффа
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Портрет Харви для этого диалога ($s, $h, $l, и т.д.)
        /// </summary>
        public string Portrait { get; set; } = "$s";
    }

    /// <summary>
    /// Контейнер для всех диалогов стресса
    /// </summary>
    public class StressDialoguesContainer
    {
        public List<StressDialogueData> Dialogues { get; set; } = new();
    }
}

