using System;
using System.Collections.Generic;
using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Данные сохранения мода
    /// </summary>
    public sealed class SaveData
    {
        public SDate LastDay { get; set; } = SDate.Now();
        public HashSet<string> TalkedNpcsToday { get; set; } = new();

        // Переутомление: перерывы
        public int OverworkBreaksToday { get; set; } = 0;
        public int OverworkBreakSeconds { get; set; } = 0;
        public bool OverworkBreakActive { get; set; } = false;

        public bool TalkedToHarveyToday { get; set; } = false;

        // ⭐ НОВОЕ: Счетчики для накопительных дебаффов
        /// <summary>Количество дней подряд без разговоров с NPC (не считая Харви)</summary>
        public int DaysWithoutTalking { get; set; } = 0;
        
        /// <summary>Количество дней подряд без еды</summary>
        public int DaysWithoutEating { get; set; } = 0;
        
        /// <summary>Количество дней подряд позднего отхода ко сну (после 00:00)</summary>
        public int DaysWithLateSleep { get; set; } = 0;

        /// <summary>
        /// Полное состояние системы стресса игрока
        /// ЕДИНЫЙ ИСТОЧНИК ПРАВДЫ для всей логики баффов и квестов
        /// </summary>
        public PlayerStressState StressState { get; set; } = new();

        /// <summary>
        /// Прогресс страха темноты (новая система с уровнями)
        /// </summary>
        public DarknessProgress Darkness { get; set; } = new();

        // ============================================
        // УСТАРЕВШИЕ ПОЛЯ (для обратной совместимости)
        // Будут мигрированы в StressState при загрузке
        // ============================================
        
        [Obsolete("Используйте StressState.ActiveBuffs")]
        public Dictionary<string, string> ActiveLockedDebuffs { get; set; } = new();
        
        [Obsolete("Используйте StressState.ActiveQuests")]
        public Dictionary<string, TreatmentProgress> Treatment { get; set; } = new();
        
        [Obsolete("Используйте StressState.LastIssuedDay")]
        public Dictionary<string, SDate> LastIssuedDay { get; set; } = new();
        
        [Obsolete("Используйте StressState.TreatmentHistory")]
        public Dictionary<string, List<TreatmentState>> StressBuffStates { get; set; } = new();
    }
}

