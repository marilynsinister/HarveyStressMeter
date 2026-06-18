using System;
using System.Collections.Generic;
using System.Linq;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    public sealed class MedicalLetterScheduler
    {
        private readonly ModConfig _config;
        private readonly SaveData _data;
        private readonly StateService _stateService;
        private readonly BuffService _buffService;
        private readonly IMonitor _monitor;

        public MedicalLetterScheduler(
            ModConfig config,
            SaveData data,
            StateService stateService,
            BuffService buffService,
            IMonitor monitor)
        {
            _config = config;
            _data = data;
            _stateService = stateService;
            _buffService = buffService;
            _monitor = monitor;
        }

        public bool ShouldQueueMedical(bool critical) =>
            _config.MedicalLetters switch
            {
                MedicalLetterMode.Off => false,
                MedicalLetterMode.CriticalOnly => critical,
                MedicalLetterMode.All => true,
                _ => false,
            };

        public bool QueueMedicalLetter(
            string mailId,
            string reason,
            string stateId,
            bool critical,
            string? dedupeKey = null)
        {
            if (string.IsNullOrWhiteSpace(mailId) || string.IsNullOrWhiteSpace(reason))
                return false;

            if (reason == MedicalLetterReasons.StressTreatmentDone && !_config.SendRomanticCareLetters)
            {
                _monitor.Log("[MedicalLetters] blocked romantic care letter (SendRomanticCareLetters=false)", LogLevel.Debug);
                return false;
            }

            if (!ShouldQueueMedical(critical))
            {
                _monitor.Log($"[MedicalLetters] blocked mode={_config.MedicalLetters} reason={reason}", LogLevel.Debug);
                return false;
            }

            int today = Today();
            string key = dedupeKey ?? mailId;
            if (_data.PendingMedicalLetters.Any(p =>
                    string.Equals(p.DedupeKey, key, StringComparison.OrdinalIgnoreCase)
                    && p.DeliverAfterDay >= today))
                return false;

            _data.PendingMedicalLetters.Add(new PendingMedicalLetter
            {
                MailId = mailId,
                Reason = reason,
                StateId = stateId ?? "",
                CreatedDay = today,
                DeliverAfterDay = today + 1,
                Critical = critical,
                DedupeKey = key,
            });

            _monitor.Log(
                $"[MedicalLetters] queued mail={mailId} reason={reason} state={stateId} critical={critical}",
                LogLevel.Info);
            return true;
        }

        public void CancelLettersForState(string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId))
                return;

            int removed = _data.PendingMedicalLetters.RemoveAll(p =>
                string.Equals(p.StateId, stateId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                _monitor.Log($"[MedicalLetters] cancelled {removed} pending for state={stateId}", LogLevel.Info);
        }

        public void CancelLettersForReason(string reason)
        {
            int removed = _data.PendingMedicalLetters.RemoveAll(p =>
                string.Equals(p.Reason, reason, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                _monitor.Log($"[MedicalLetters] cancelled {removed} pending for reason={reason}", LogLevel.Info);
        }

        public void FlushValidLettersForTomorrow()
        {
            int tomorrow = Today() + 1;
            foreach (PendingMedicalLetter letter in _data.PendingMedicalLetters.Where(p => p.DeliverAfterDay <= tomorrow).ToList())
            {
                _data.PendingMedicalLetters.Remove(letter);
                if (!IsLetterStillValid(letter))
                {
                    _monitor.Log(
                        $"[MedicalLetters] not sent (stale): mail={letter.MailId} reason={letter.Reason}",
                        LogLevel.Info);
                    continue;
                }

                Game1.addMailForTomorrow(letter.MailId);
                _monitor.Log($"[MedicalLetters] sent mail={letter.MailId} reason={letter.Reason}", LogLevel.Info);
            }
        }

        public void RemoveStalePendingLetters()
        {
            int today = Today();
            _data.PendingMedicalLetters.RemoveAll(p => p.DeliverAfterDay < today - 1);
        }

        public void ScrubStaleMailForTomorrow()
        {
            if (Game1.player?.mailForTomorrow == null)
                return;

            foreach (string mailId in Game1.player.mailForTomorrow.ToList())
            {
                if (!IsManagedStressMail(mailId))
                    continue;

                string reason = InferReason(mailId);
                if (!IsLetterStillValid(new PendingMedicalLetter { MailId = mailId, Reason = reason, StateId = "" }))
                {
                    Game1.player.mailForTomorrow.Remove(mailId);
                    _monitor.Log($"[MedicalLetters] removed stale mailForTomorrow: {mailId}, reason={reason}", LogLevel.Info);
                }
            }
        }

        private bool IsLetterStillValid(PendingMedicalLetter letter) =>
            letter.Reason switch
            {
                MedicalLetterReasons.StressTreatmentStart =>
                    !string.IsNullOrEmpty(letter.StateId)
                    && (_stateService.HasActiveTreatmentState(letter.StateId)
                        || _buffService.HasBuff(letter.StateId)),

                MedicalLetterReasons.StressTreatmentDone => false,

                MedicalLetterReasons.DarknessWorry =>
                    _data.StressState.HasActiveBuff(BuffIds.Darkness)
                    || _buffService.HasBuff(BuffIds.Darkness),

                MedicalLetterReasons.NoSleep =>
                    _stateService.HasActiveTreatmentState(BuffIds.NoSleep)
                    || _buffService.HasBuff(BuffIds.NoSleep),

                _ => _config.MedicalLetters == MedicalLetterMode.All,
            };

        private static bool IsManagedStressMail(string mailId) =>
            mailId.StartsWith("mailHarveyStress", StringComparison.OrdinalIgnoreCase)
            || mailId.StartsWith("HarveyStress_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mailId, MailIds.DarknessWorry, StringComparison.OrdinalIgnoreCase);

        private static string InferReason(string mailId)
        {
            if (string.Equals(mailId, MailIds.DarknessWorry, StringComparison.OrdinalIgnoreCase))
                return MedicalLetterReasons.DarknessWorry;
            if (string.Equals(mailId, MailIds.GenericDone, StringComparison.OrdinalIgnoreCase))
                return MedicalLetterReasons.StressTreatmentDone;
            if (mailId.StartsWith("mailHarveyStressTreatment", StringComparison.OrdinalIgnoreCase))
                return MedicalLetterReasons.StressTreatmentStart;
            return "";
        }

        private static int Today() => (int)Game1.stats.DaysPlayed;
    }
}
