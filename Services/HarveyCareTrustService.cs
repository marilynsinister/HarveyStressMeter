using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    public sealed class HarveyCareTrustService
    {
        public const float MinStressGainMultiplier = 0.70f;

        private static readonly int[] LevelThresholds = { 0, 25, 50, 100, 160 };

        private readonly SaveData _data;
        private readonly ModConfig _config;
        private readonly IMonitor _monitor;

        public HarveyCareTrustService(SaveData data, ModConfig config, IMonitor monitor)
        {
            _data = data;
            _config = config;
            _monitor = monitor;
        }

        public HarveyCareTrustState State => _data.HarveyCareTrust;

        public int GetTrustPoints() => State.TrustPoints;

        public int GetTrustLevel() => GetEffectiveTrustLevel();

        public int GetRawTrustLevel() => CalculateLevelFromPoints(State.TrustPoints);

        public int GetMaxTrustLevelForRelationship()
        {
            if (HarveyFriendshipHelper.IsMarriedToHarvey() || HarveyFriendshipHelper.IsDatingHarvey())
                return HarveyCareTrustLevels.Anchor;

            var hearts = HarveyFriendshipHelper.GetHarveyHearts();
            if (hearts >= 8)
                return HarveyCareTrustLevels.Anchor;
            if (hearts >= 6)
                return HarveyCareTrustLevels.SafePerson;
            if (hearts >= 3)
                return HarveyCareTrustLevels.TrustedDoctor;
            return HarveyCareTrustLevels.FamiliarDoctor;
        }

        public int GetEffectiveTrustLevel()
            => Math.Min(GetRawTrustLevel(), GetMaxTrustLevelForRelationship());

        public float GetEffectiveStressGainMultiplier()
        {
            var level = GetTrustLevel();
            float mult = level switch
            {
                // «Стоп» как договор в обе стороны: она тоже может остановить его — и слушается охотнее.
                HarveyCareTrustLevels.Anchor when IsConsentGranted(HarveyCareConsents.StrictCare)
                    && IsConsentGranted(HarveyCareConsents.HarveyLimits) => 0.72f,
                HarveyCareTrustLevels.Anchor when IsConsentGranted(HarveyCareConsents.StrictCare) => 0.75f,
                HarveyCareTrustLevels.Anchor => 0.80f,
                HarveyCareTrustLevels.SafePerson => 0.85f,
                HarveyCareTrustLevels.TrustedDoctor => 0.90f,
                HarveyCareTrustLevels.FamiliarDoctor when State.AssignmentBoostDaysRemaining > 0 => 0.95f,
                _ => 1f,
            };

            return Math.Max(MinStressGainMultiplier, mult);
        }

        public int GetTreatmentStressReductionBonus()
        {
            int bonus = GetTrustLevel() switch
            {
                HarveyCareTrustLevels.Anchor => 5,
                HarveyCareTrustLevels.SafePerson => 3,
                HarveyCareTrustLevels.TrustedDoctor => 2,
                _ => 0,
            };

            // Вечерние проверки: Харви раньше замечает, что ей хуже, и лечение действует чуть сильнее.
            if (bonus > 0 && IsConsentGranted(HarveyCareConsents.EveningChecks))
                bonus++;

            return bonus;
        }

        public float GetFlashbackStabilizationMultiplier()
        {
            float mult = GetTrustLevel() switch
            {
                HarveyCareTrustLevels.Anchor => 1.35f,
                HarveyCareTrustLevels.SafePerson => 1.20f,
                _ => 1f,
            };

            // Рука, которую можно сжать, возвращает в настоящее быстрее голоса.
            if (mult > 1f && IsConsentGranted(HarveyCareConsents.HoldHand))
                mult += 0.10f;

            return mult;
        }

        public float GetRescueChanceBonus()
        {
            float bonus = GetTrustLevel() switch
            {
                HarveyCareTrustLevels.Anchor => 0.15f,
                HarveyCareTrustLevels.SafePerson => 0.12f,
                HarveyCareTrustLevels.TrustedDoctor => 0.08f,
                _ => 0f,
            };

            // Ему разрешили искать — он знает её места и выходит сразу, не сомневаясь.
            if (IsConsentGranted(HarveyCareConsents.SearchInStorm))
                bonus += 0.05f;

            return bonus;
        }

        public bool IsHarveySafePersonUnlocked()
            => State.SafePersonUnlocked || GetTrustLevel() >= HarveyCareTrustLevels.SafePerson;

        public bool CanHarveyForestRescue()
            => (State.ForestRescueUnlocked || GetTrustLevel() >= HarveyCareTrustLevels.SafePerson)
               && GetTrustLevel() >= HarveyCareTrustLevels.SafePerson
               && IsConsentAllowed(HarveyCareConsents.SearchInStorm);

        public bool ConsentQuestionsEnabled => _config.EnableHarveyConsentQuestions;

        public HarveyConsentAnswer GetConsent(string consentId)
            => State.Consents.TryGetValue(consentId, out var answer) ? answer : HarveyConsentAnswer.NotAsked;

        /// <summary>Разрешено явно.</summary>
        public bool IsConsentGranted(string consentId)
            => _config.EnableHarveyConsentQuestions && GetConsent(consentId) == HarveyConsentAnswer.Granted;

        /// <summary>
        /// Не запрещено: «да» или ещё не спрашивал (старые сохранения, уровень только что открылся).
        /// «Пока нет» и «нет» Харви соблюдает. При выключенных вопросах — всё по уровню доверия.
        /// </summary>
        public bool IsConsentAllowed(string consentId)
            => !_config.EnableHarveyConsentQuestions
               || GetConsent(consentId) is HarveyConsentAnswer.NotAsked or HarveyConsentAnswer.Granted;

        /// <summary>Следующий уровень доверия и сколько очков до него; null — максимум или упёрлись в потолок отношений.</summary>
        public (int Level, int PointsLeft)? GetNextLevelProgress()
        {
            int raw = GetRawTrustLevel();
            if (raw >= HarveyCareTrustLevels.Anchor)
                return null;

            int next = raw + 1;
            return (next, Math.Max(0, LevelThresholds[next] - State.TrustPoints));
        }

        /// <summary>Очков хватает на уровень выше, но отношения с Харви пока не позволяют.</summary>
        public bool IsCappedByRelationship()
            => GetRawTrustLevel() > GetMaxTrustLevelForRelationship();

        public void AwardTrust(string reason, int points)
        {
            if (points <= 0)
                return;

            var before = State.TrustPoints;
            State.TrustPoints = Math.Min(before + points, _config.MaxHarveyCareTrustPoints);
            State.LastTrustGainDay = (int)Game1.Date.TotalDays;
            RefreshTrustLevelAndUnlocks();

            _monitor.Log(
                $"[HarveyCareTrust] +{points} ({reason}) → {State.TrustPoints} pts, " +
                $"level {GetTrustLevel()} ({HarveyCareTrustLevels.GetDisplayName(GetTrustLevel())})",
                LogLevel.Debug);
        }

        public void PenalizeTrust(string reason, int points)
        {
            if (points <= 0)
                return;

            var today = (int)Game1.Date.TotalDays;
            if (today - State.LastTrustPenaltyDay < _config.TrustPenaltyCooldownDays)
                return;

            var actual = Math.Min(points, _config.MaxTrustPenaltyPerEvent);

            // Она обещала говорить, когда он перегибает, — срыв для него сигнал, а не предательство.
            if (IsConsentGranted(HarveyCareConsents.HarveyLimits))
                actual = Math.Max(1, actual / 2);
            State.TrustPoints = Math.Max(0, State.TrustPoints - actual);
            State.LastTrustPenaltyDay = today;

            // Разговор после штрафа может вернуть половину (HarveyConsentService.TryStartRepairTalk).
            State.PendingRepairPoints = Math.Max(1, actual / 2);
            State.PendingRepairReason = reason;
            State.PendingRepairDay = today;
            RefreshTrustLevelAndUnlocks();

            _monitor.Log(
                $"[HarveyCareTrust] -{actual} ({reason}) → {State.TrustPoints} pts, level {GetTrustLevel()}",
                LogLevel.Debug);
        }

        /// <summary>Очки из другого мода (Injury: режим травм) — одна шкала доверия на всё лечение.</summary>
        public void AwardTrustFromSource(string source, string reason, int points)
        {
            if (points <= 0)
                return;

            int before = State.TrustPoints;
            AwardTrust($"{source}:{reason}", points);
            int gained = State.TrustPoints - before;
            if (gained > 0)
                State.PointsBySource[source] = State.PointsBySource.GetValueOrDefault(source) + gained;
        }

        public void PenalizeTrustFromSource(string source, string reason, int points)
            => PenalizeTrust($"{source}:{reason}", points);

        /// <summary>
        /// Trigger action из сцен осмотра: фермерша сказала «стоп», Харви остановился.
        /// Не чаще раза в день: доверие растёт от того, что он слушает, а не от повторов.
        /// </summary>
        public bool OnExamStopRespected()
        {
            int today = (int)Game1.Date.TotalDays;
            if (State.LastExamStopRespectedDay == today)
                return false;

            State.LastExamStopRespectedDay = today;
            AwardTrust(HarveyCareTrustReasons.ExamStopRespected, 2);
            return true;
        }

        /// <summary>Фермерша хочет вернуться к договорённости: Харви спросит заново при следующем разговоре, без штрафа.</summary>
        public void RequestConsentRevisit(string consentId)
        {
            State.ConsentRevisits.Add(consentId);
            _monitor.Log($"[Consent] Пересмотр «{consentId}» — Харви спросит снова.", LogLevel.Debug);
        }

        public void OnTreatmentObjectivesCompleted(string episodeId)
        {
            if (string.Equals(State.LastAmbientTrustEpisodeId, episodeId, StringComparison.Ordinal))
                return;

            State.LastAmbientTrustEpisodeId = episodeId;
            AwardTrust(HarveyCareTrustReasons.AmbientRecommendation, 5);
        }

        public void OnTreatmentMarkedReadyForReview(string episodeId)
        {
            State.ReviewOfferedAbsoluteDay = (int)Game1.Date.TotalDays;
            OnTreatmentObjectivesCompleted(episodeId);
        }

        public void OnTreatmentEpisodeCompleted(string episodeId)
        {
            var isGotoro = string.Equals(episodeId, StressEpisodes.GotoroFlashback, StringComparison.Ordinal);
            AwardTrust(
                isGotoro ? HarveyCareTrustReasons.GotoroEpisodeComplete : HarveyCareTrustReasons.TreatmentEpisodeComplete,
                isGotoro ? 15 : 10);

            State.SuccessfulAssignments++;
            State.DaysSinceLastSuccessfulAssignment = 0;
            State.IgnoredAssignments = 0;
            State.ReviewOfferedAbsoluteDay = 0;

            if (GetTrustLevel() >= HarveyCareTrustLevels.FamiliarDoctor)
                State.AssignmentBoostDaysRemaining = Math.Max(State.AssignmentBoostDaysRemaining, 2);
        }

        public void OnTimelyReviewCompleted()
        {
            if (State.ReviewOfferedAbsoluteDay <= 0)
                return;

            var today = (int)Game1.Date.TotalDays;
            if (today - State.ReviewOfferedAbsoluteDay <= 1)
                AwardTrust(HarveyCareTrustReasons.TimelyReview, 10);

            State.ReviewOfferedAbsoluteDay = 0;
        }

        public void OnSafeLocationStabilized(bool harveyHelped = false)
        {
            AwardTrust(HarveyCareTrustReasons.SafeLocationStabilized, 5);
            if (harveyHelped)
                State.FlashbacksStabilizedWithHarvey++;
        }

        public void OnForestRescueCompleted()
        {
            AwardTrust(HarveyCareTrustReasons.ForestRescue, 10);
            State.FlashbacksStabilizedWithHarvey++;
        }

        public void TryAwardSupportiveTalk()
        {
            if (State.SupportiveTalkTrustToday)
                return;

            if (_data.StressLoad.HasActiveTreatment || _data.ActiveTreatmentEpisode?.TreatmentStarted == true)
                return;

            var severity = _data.StressLoad.Severity;
            if (severity < StressSeverity.High)
                return;

            State.SupportiveTalkTrustToday = true;
            AwardTrust(HarveyCareTrustReasons.SupportiveTalk, 2);
        }

        public void ResetDailyState()
        {
            State.SupportiveTalkTrustToday = false;

            if (State.AssignmentBoostDaysRemaining > 0)
                State.AssignmentBoostDaysRemaining--;

            if (State.SuccessfulAssignments > 0 || _data.ActiveTreatmentEpisode?.TreatmentStarted == true)
                State.DaysSinceLastSuccessfulAssignment++;
        }

        public void OnDayStarted()
        {
            // Потолок по отношениям мог сдвинуться (букет, свадьба) — уровень и флаги пересчитываем каждое утро.
            RefreshTrustLevelAndUnlocks();
            ResetDailyState();
            EvaluateIgnoredAssignmentPenalty();
        }

        public void ResetTrustState()
        {
            _data.HarveyCareTrust = new HarveyCareTrustState();
            RefreshTrustLevelAndUnlocks();
        }

        public void SetTrustPointsForDebug(int points)
        {
            State.TrustPoints = Math.Clamp(points, 0, _config.MaxHarveyCareTrustPoints);
            RefreshTrustLevelAndUnlocks();
        }

        /// <summary>DEV/MCP: add trust points without production side-effects (LastTrustGainDay, etc.).</summary>
        public void AddTrustPointsForDebug(int points, string? reason = null)
        {
            if (points <= 0)
                return;

            var before = State.TrustPoints;
            State.TrustPoints = Math.Min(before + points, _config.MaxHarveyCareTrustPoints);
            RefreshTrustLevelAndUnlocks();

            _monitor.Log(
                $"[HarveyCareTrust] MCP +{points} ({reason ?? "mcp"}) → {State.TrustPoints} pts, " +
                $"effective level {GetTrustLevel()}",
                LogLevel.Debug);
        }

        /// <summary>DEV/MCP: remove trust points, bypassing PenalizeTrust cooldown.</summary>
        public void RemoveTrustPointsForDebug(int points, string? reason = null)
        {
            if (points <= 0)
                return;

            var before = State.TrustPoints;
            State.TrustPoints = Math.Max(0, State.TrustPoints - points);
            RefreshTrustLevelAndUnlocks();

            _monitor.Log(
                $"[HarveyCareTrust] MCP -{Math.Min(points, before)} ({reason ?? "mcp"}) → {State.TrustPoints} pts, " +
                $"effective level {GetTrustLevel()}",
                LogLevel.Debug);
        }

        public string BuildDebugSnapshot()
        {
            var level = GetTrustLevel();
            var sb = new StringBuilder();
            sb.AppendLine("=== HarveyCareTrust ===");
            sb.AppendLine($"TrustPoints: {State.TrustPoints} (raw level {GetRawTrustLevel()}, cap {GetMaxTrustLevelForRelationship()})");
            sb.AppendLine($"TrustLevel: {level} ({HarveyCareTrustLevels.GetDisplayName(level)})");
            sb.AppendLine($"EffectiveStressGainMultiplier: {GetEffectiveStressGainMultiplier():P0}");
            sb.AppendLine($"FlashbackStabilizationMultiplier: {GetFlashbackStabilizationMultiplier():P0}");
            sb.AppendLine($"TreatmentReductionBonus: +{GetTreatmentStressReductionBonus()}");
            sb.AppendLine($"RescueChanceBonus: +{GetRescueChanceBonus():P0}");
            sb.AppendLine($"SafePersonUnlocked: {IsHarveySafePersonUnlocked()}");
            sb.AppendLine($"ForestRescueUnlocked: {State.ForestRescueUnlocked} (CanRescue={CanHarveyForestRescue()})");
            sb.AppendLine($"GroundingDialogueUnlocked: {State.GroundingDialogueUnlocked}");
            sb.AppendLine($"SuccessfulAssignments: {State.SuccessfulAssignments}, Ignored: {State.IgnoredAssignments}");
            sb.AppendLine($"FlashbacksStabilizedWithHarvey: {State.FlashbacksStabilizedWithHarvey}");
            sb.AppendLine($"DaysSinceLastSuccess: {State.DaysSinceLastSuccessfulAssignment}");
            sb.AppendLine($"AssignmentBoostDaysRemaining: {State.AssignmentBoostDaysRemaining}");
            foreach (var consentId in HarveyCareConsents.All)
                sb.AppendLine($"Consent {consentId}: {GetConsent(consentId)} (asked day {(State.ConsentAskedDay.TryGetValue(consentId, out var d) ? d : 0)})");
            sb.AppendLine($"PendingRepair: {State.PendingRepairPoints} pts ({State.PendingRepairReason ?? "-"}, day {State.PendingRepairDay})");
            return sb.ToString().TrimEnd();
        }

        /// <summary>DEV/MCP: machine-readable snapshot for AI assert-friendly MCP tools.</summary>
        public string BuildMcpSnapshot()
        {
            var cap = GetMaxTrustLevelForRelationship();
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"TrustPoints: {State.TrustPoints}");
            sb.AppendLine($"RawTrustLevel: {GetRawTrustLevel()} ({HarveyCareTrustLevels.GetDisplayName(GetRawTrustLevel())})");
            sb.AppendLine($"EffectiveTrustLevel: {GetTrustLevel()} ({HarveyCareTrustLevels.GetDisplayName(GetTrustLevel())})");
            sb.AppendLine($"FriendshipPointsWithHarvey: {GetHarveyFriendshipPoints()}");
            sb.AppendLine($"HeartsWithHarvey: {HarveyFriendshipHelper.GetHarveyHearts()}");
            sb.AppendLine($"RelationshipStatus: {GetHarveyRelationshipStatus()}");
            sb.AppendLine($"Cap: {cap} ({HarveyCareTrustLevels.GetDisplayName(cap)})");
            sb.AppendLine($"SafePersonUnlocked: {IsHarveySafePersonUnlocked()}");
            sb.AppendLine($"ForestRescueUnlocked: {State.ForestRescueUnlocked}");
            sb.AppendLine($"CanHarveyForestRescue: {CanHarveyForestRescue()}");
            sb.AppendLine($"EffectiveStressGainMultiplier: {GetEffectiveStressGainMultiplier():0.###}");
            sb.AppendLine($"TreatmentReductionBonus: {GetTreatmentStressReductionBonus()}");
            sb.AppendLine($"RescueChanceBonus: {GetRescueChanceBonus():0.###}");
            sb.AppendLine($"FlashbackStabilizationMultiplier: {GetFlashbackStabilizationMultiplier():0.###}");
            sb.AppendLine($"IgnoredAssignments: {State.IgnoredAssignments}");
            sb.AppendLine($"DaysSinceLastSuccessfulAssignment: {State.DaysSinceLastSuccessfulAssignment}");
            sb.AppendLine($"TrustIgnoredAssignmentDaysThreshold: {_config.TrustIgnoredAssignmentDays}");
            sb.AppendLine($"LastTrustPenaltyDay: {State.LastTrustPenaltyDay}");
            sb.AppendLine($"TrustPenaltyCooldownDays: {_config.TrustPenaltyCooldownDays}");
            sb.AppendLine($"AssignmentBoostDaysRemaining: {State.AssignmentBoostDaysRemaining}");
            sb.AppendLine($"GroundingDialogueUnlocked: {State.GroundingDialogueUnlocked}");
            return sb.ToString().TrimEnd();
        }

        private static int GetHarveyFriendshipPoints()
            => Game1.player.friendshipData.TryGetValue("Harvey", out var friendship)
                ? friendship.Points
                : 0;

        private static string GetHarveyRelationshipStatus()
        {
            if (HarveyFriendshipHelper.IsMarriedToHarvey())
                return "Married";

            if (HarveyFriendshipHelper.IsDatingHarvey())
                return "Dating";

            return "none";
        }

        private void EvaluateIgnoredAssignmentPenalty()
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !episode.TreatmentStarted || episode.AwaitingHarveyReview)
                return;

            if (State.DaysSinceLastSuccessfulAssignment < _config.TrustIgnoredAssignmentDays)
                return;

            State.IgnoredAssignments++;
            PenalizeTrust(HarveyCareTrustReasons.LongIgnoredAssignment, 5);
        }

        private void RefreshTrustLevelAndUnlocks()
        {
            State.TrustLevel = GetEffectiveTrustLevel();
            State.GroundingDialogueUnlocked = State.TrustLevel >= HarveyCareTrustLevels.TrustedDoctor;
            State.SafePersonUnlocked = State.TrustLevel >= HarveyCareTrustLevels.SafePerson;
            State.ForestRescueUnlocked = State.TrustLevel >= HarveyCareTrustLevels.SafePerson;
            SyncLevelFlags();
        }

        /// <summary>
        /// Флаги уровня для CP-условий (PLAYER_HAS_MAIL Current HarveyCareTrust_L2 Received):
        /// сюжетные сцены открываются доверием в лечении, а не только сердечками.
        /// </summary>
        public void SyncLevelFlags()
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            int level = State.TrustLevel;
            for (int i = HarveyCareTrustLevels.FamiliarDoctor; i <= HarveyCareTrustLevels.Anchor; i++)
            {
                string flag = LevelFlagPrefix + i;
                if (level >= i)
                    Game1.player.mailReceived.Add(flag);
                else
                    Game1.player.mailReceived.Remove(flag);
            }
        }

        public const string LevelFlagPrefix = "HarveyCareTrust_L";

        private static int CalculateLevelFromPoints(int points)
        {
            if (points >= LevelThresholds[4])
                return HarveyCareTrustLevels.Anchor;
            if (points >= LevelThresholds[3])
                return HarveyCareTrustLevels.SafePerson;
            if (points >= LevelThresholds[2])
                return HarveyCareTrustLevels.TrustedDoctor;
            if (points >= LevelThresholds[1])
                return HarveyCareTrustLevels.FamiliarDoctor;
            return HarveyCareTrustLevels.NoTrustBonus;
        }
    }
}
