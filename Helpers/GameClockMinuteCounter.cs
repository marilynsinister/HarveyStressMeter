using System;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Считает игровые минуты между опросами (Game1.timeOfDay шагает по 10 минут ≈ 7 реальных секунд).
    /// Вызывать каждый тик таймера, даже когда условия не выполнены, — иначе время «вне дома» зачтётся задним числом.
    /// </summary>
    public sealed class GameClockMinuteCounter
    {
        private int _lastTime = -1;
        private int _lastDay = -1;

        /// <summary>Сколько игровых минут прошло с прошлого вызова (0, если часы не сдвинулись или сменился день).</summary>
        public int Poll()
        {
            int now = Game1.timeOfDay;
            int day = Game1.Date.TotalDays;

            if (_lastTime < 0 || day != _lastDay || now < _lastTime)
            {
                _lastTime = now;
                _lastDay = day;
                return 0;
            }

            if (now == _lastTime)
                return 0;

            int minutes = Utility.CalculateMinutesBetweenTimes(_lastTime, now);
            _lastTime = now;
            return Math.Max(0, minutes);
        }

        public void Reset()
        {
            _lastTime = -1;
            _lastDay = -1;
        }
    }
}
