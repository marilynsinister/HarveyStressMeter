using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// «Границы» и разговор после штрафа доверия. После обычного разговора с Харви он может:
    /// 1) поговорить о недавнем срыве (вернуть часть очков доверия);
    /// 2) спросить разрешения, когда доверие дорастает до уровня (HarveyCareConsents).
    /// Не больше одного такого разговора в день.
    /// </summary>
    public sealed class HarveyConsentService
    {
        private const int TrustForGranted = 3;
        private const int TrustForRespectedBoundary = 2;

        /// <summary>Сколько тиков держится флаг разговора без шагов (страховка, если окно закрыли без ответа).</summary>
        private const int FlowTimeoutTicks = 60 * 60;

        private static bool _flowActive;
        private static int _flowTick;

        private readonly SaveData _data;
        private readonly ModConfig _config;
        private readonly HarveyCareTrustService _trustService;
        private readonly IMonitor _monitor;

        public HarveyConsentService(SaveData data, ModConfig config, HarveyCareTrustService trustService, IMonitor monitor)
        {
            _data = data;
            _config = config;
            _trustService = trustService;
            _monitor = monitor;
        }

        /// <summary>Идёт наш разговор — stress-пайплайн не должен перехватывать эти реплики Харви.</summary>
        public static bool IsFlowActive => _flowActive && Game1.ticks - _flowTick < FlowTimeoutTicks;

        private HarveyCareTrustState State => _trustService.State;

        private static int Today => (int)Game1.Date.TotalDays;

        public void OnDayStarted()
        {
            _flowActive = false;

            if (State.PendingRepairPoints > 0 && Today - State.PendingRepairDay > _config.TrustRepairWindowDays)
                ClearRepair();
        }

        /// <summary>Вызывается после обычного разговора с Харви: через миг, когда диалог закроется, решаем, о чём ещё поговорить.</summary>
        public void QueueAfterHarveyTalk()
        {
            if (IsFlowActive || State.LastConsentAskDay == Today)
                return;

            DelayedAction.functionAfterDelay(TryStartFollowUp, 250);
        }

        /// <summary>Какой вопрос Харви задаст следующим; null — спрашивать нечего.</summary>
        public string? GetNextConsentToAsk()
        {
            if (!_config.EnableHarveyConsentQuestions)
                return null;

            int level = _trustService.GetTrustLevel();

            // Сначала — то, к чему она сама попросила вернуться (кнопка во вкладке «Доверие»).
            foreach (var consentId in HarveyCareConsents.All)
            {
                if (State.ConsentRevisits.Contains(consentId) && IsAvailable(consentId, level))
                    return consentId;
            }

            foreach (var consentId in HarveyCareConsents.All)
            {
                if (!IsAvailable(consentId, level))
                    continue;

                var answer = _trustService.GetConsent(consentId);
                if (answer == HarveyConsentAnswer.NotAsked)
                    return consentId;

                if (answer == HarveyConsentAnswer.NotYet
                    && Today - State.ConsentAskedDay.GetValueOrDefault(consentId) >= _config.ConsentReaskDays)
                    return consentId;
            }

            return null;
        }

        private static bool IsAvailable(string consentId, int level)
            => level >= HarveyCareConsents.RequiredTrustLevel(consentId)
               && (!HarveyCareConsents.RequiresMarriage(consentId) || HarveyFriendshipHelper.IsMarriedToHarvey());

        /// <summary>Договорённости, к которым можно вернуться: Харви уже спрашивал, и ответ был.</summary>
        public IReadOnlyList<string> GetRevisitableConsents()
            => HarveyCareConsents.All
                .Where(id => _trustService.GetConsent(id) != HarveyConsentAnswer.NotAsked
                             && !State.ConsentRevisits.Contains(id))
                .ToList();

        public static string GetConsentLabel(string consentId) => consentId switch
        {
            HarveyCareConsents.EveningChecks => "Вечерние проверки",
            HarveyCareConsents.HoldHand => "Держать за руку в грозу",
            HarveyCareConsents.SearchInStorm => "Искать, если уйду в грозу",
            HarveyCareConsents.StrictCare => "Сказать «стоп» и увести отдыхать",
            HarveyCareConsents.HarveyLimits => "Говорить ему, когда он перегибает",
            _ => consentId,
        };

        private void TryStartFollowUp()
        {
            if (!CanStartNow(out var harvey))
                return;

            if (State.PendingRepairPoints > 0)
            {
                StartRepairTalk(harvey);
                return;
            }

            var consentId = GetNextConsentToAsk();
            if (consentId != null)
                StartConsentQuestion(harvey, consentId);
        }

        private bool CanStartNow(out NPC harvey)
        {
            harvey = null!;
            if (!Context.IsWorldReady || !Context.IsPlayerFree || Game1.eventUp || Game1.isFestival())
                return false;

            if (Game1.activeClickableMenu != null || IsFlowActive || State.LastConsentAskDay == Today)
                return false;

            var npc = Game1.currentLocation?.getCharacterFromName("Harvey");
            if (npc == null)
                return false;

            harvey = npc;
            return true;
        }

        // ---------- Разговор после штрафа ----------

        private void StartRepairTalk(NPC harvey)
        {
            State.LastConsentAskDay = Today;
            int points = State.PendingRepairPoints;
            string reason = State.PendingRepairReason ?? "";

            string intro = reason == HarveyCareTrustReasons.LongIgnoredAssignment
                ? "Нам нужно поговорить. Про назначение, которое ты бросила.$s#$b#Я не сержусь. Мне было страшно — а это хуже."
                : "Нам нужно поговорить про то, что было.$s#$b#Я не сержусь. Но мне важно понять.";

            Ask(
                harvey,
                intro,
                "Что ты скажешь Харви?",
                new[]
                {
                    new Response("sorry", "Прости. Я зря рисковала."),
                    new Response("needed", "Мне нужно было так сделать."),
                    new Response("silent", "Не хочу об этом говорить."),
                },
                answer =>
                {
                    ClearRepair();
                    switch (answer)
                    {
                        case "sorry":
                            _trustService.AwardTrust(HarveyCareTrustReasons.TrustRepaired, points);
                            return "Спасибо, что сказала.$l#$b#Давай начнём заново. С сегодняшнего дня — маленькими шагами.";
                        case "needed":
                            _trustService.AwardTrust(HarveyCareTrustReasons.TrustRepaired, points / 2);
                            return "Хорошо. Тогда в следующий раз скажи мне заранее.$0#$b#Я не буду спорить. Я просто буду знать — и меньше бояться.";
                        default:
                            return "Ладно. Не сейчас.$s#$b#Я никуда не денусь.";
                    }
                });
        }

        private void ClearRepair()
        {
            State.PendingRepairPoints = 0;
            State.PendingRepairReason = null;
        }

        // ---------- «Границы» ----------

        private void StartConsentQuestion(NPC harvey, string consentId)
        {
            State.LastConsentAskDay = Today;
            State.ConsentAskedDay[consentId] = Today;
            var texts = ConsentTexts.For(consentId);

            // Вернуться к договорённости можно без последствий: новый ответ просто заменяет старый.
            bool revisit = State.ConsentRevisits.Remove(consentId);
            string intro = revisit ? ConsentTexts.RevisitIntro(consentId) : texts.Intro;

            Ask(
                harvey,
                intro,
                texts.Question,
                new[]
                {
                    new Response("yes", texts.Yes),
                    new Response("later", texts.Later),
                    new Response("never", texts.Never),
                },
                answer =>
                {
                    var result = answer switch
                    {
                        "yes" => HarveyConsentAnswer.Granted,
                        "never" => HarveyConsentAnswer.Never,
                        _ => HarveyConsentAnswer.NotYet,
                    };

                    State.Consents[consentId] = result;
                    if (State.ConsentTrustAwarded.Add(consentId))
                    {
                        // «Нет» тоже укрепляет доверие: Харви спросил и послушал.
                        _trustService.AwardTrust(
                            result == HarveyConsentAnswer.Granted
                                ? HarveyCareTrustReasons.ConsentGranted
                                : HarveyCareTrustReasons.BoundaryRespected,
                            result == HarveyConsentAnswer.Granted ? TrustForGranted : TrustForRespectedBoundary);
                    }

                    _monitor.Log($"[Consent] {consentId} → {result}", LogLevel.Info);
                    return result switch
                    {
                        HarveyConsentAnswer.Granted => texts.ReplyYes,
                        HarveyConsentAnswer.Never => texts.ReplyNever,
                        _ => texts.ReplyLater,
                    };
                });
        }

        // ---------- Общий поток: реплика Харви → выбор → ответ Харви ----------

        private void Ask(NPC harvey, string intro, string question, Response[] responses, Func<string, string> onAnswer)
        {
            Touch();
            Game1.DrawDialogue(new Dialogue(harvey, null, intro));
            Game1.afterDialogues = () =>
            {
                Touch();
                Game1.currentLocation.createQuestionDialogue(
                    question,
                    responses,
                    (_, answer) =>
                    {
                        Touch();
                        string reply = onAnswer(answer);
                        // Ответ Харви — со следующего тика, когда окно вопроса закроется.
                        DelayedAction.functionAfterDelay(() =>
                        {
                            Touch();
                            Game1.DrawDialogue(new Dialogue(harvey, null, reply));
                            Game1.afterDialogues = () => DelayedAction.functionAfterDelay(() => _flowActive = false, 250);
                        }, 50);
                    });
            };
        }

        private static void Touch()
        {
            _flowActive = true;
            _flowTick = Game1.ticks;
        }

        private sealed record ConsentText(
            string Intro,
            string Question,
            string Yes,
            string Later,
            string Never,
            string ReplyYes,
            string ReplyLater,
            string ReplyNever);

        private static class ConsentTexts
        {
            /// <summary>Начало разговора, когда фермерша сама попросила вернуться к договорённости.</summary>
            public static string RevisitIntro(string consentId) => consentId switch
            {
                HarveyCareConsents.EveningChecks => "Ты хотела вернуться к вечерним проверкам.$0#$b#Что бы ты ни решила сейчас — я не обижусь. Договорённость не клятва.",
                HarveyCareConsents.HoldHand => "Ты хотела ещё раз поговорить про грозу. Про руку.$s#$b#Скажи как есть. Я подстроюсь.",
                HarveyCareConsents.SearchInStorm => "Ты хотела вернуться к тому, искать ли тебя в грозу.$s#$b#Я слушаю. Без споров — обещаю.",
                HarveyCareConsents.StrictCare => "Ты хотела пересмотреть моё «стоп».$0#$b#Правильно. Правила для того и нужны, чтобы их иногда пересматривать. Даже мои.",
                HarveyCareConsents.HarveyLimits => "Ты хотела вернуться к тому, как останавливать меня.$h#$b#Говори. Я даже не буду спорить. Почти.",
                _ => "Ты хотела вернуться к нашей договорённости.$0",
            };

            public static ConsentText For(string consentId) => consentId switch
            {
                HarveyCareConsents.EveningChecks => new(
                    "Можно вопрос? Не медицинский. Почти.$s#$b#Иногда вечером я думаю: как ты там, на ферме. И не знаю, имею ли право зайти или спросить.",
                    "Можно Харви иногда проверять, как ты, по вечерам?",
                    "Да. Заходи, если волнуешься.",
                    "Пока нет.",
                    "Нет. И не спрашивай больше.",
                    "*выдыхает* Спасибо.$h#$b#Я не буду злоупотреблять. Постучу — а если ты устала, просто скажи. Я уйду.",
                    "Хорошо. «Пока нет» — честный ответ.$0#$b#Когда-нибудь я спрошу ещё раз. Или ты скажешь сама.",
                    "Понял. Больше не спрошу.$0#$b#Спасибо, что сказала прямо. Так легче, чем гадать."),

                HarveyCareConsents.HoldHand => new(
                    "Хочу договориться заранее — пока сейчас спокойно.$s#$b#Когда гроза накрывает тебя, я говорю, считаю, жду. Иногда этого мало.",
                    "Можно Харви брать тебя за руку, когда тебе плохо в грозу?",
                    "Да. Держи меня за руку.",
                    "Пока нет. Только голос.",
                    "Нет. Не трогай меня в такие моменты.",
                    "Хорошо. Я протяну руку — а возьмёшь её ты.$l#$b#Так будет всегда: ты первой берёшь, ты первой отпускаешь.",
                    "Голос — значит, голос.$0#$b#Я буду рядом на том расстоянии, какое ты выберешь.",
                    "Понял. Не трону.$0#$b#*тихо* Спасибо, что сказала. Теперь я точно не сделаю хуже."),

                HarveyCareConsents.HarveyLimits => new(
                    "Котёнок, я долго думал, как это сказать.$s#$b#Я прошу тебя останавливаться, когда ты себя загоняешь. А кто останавливает меня? Я ведь тоже перегибаю. С пледами, с правилами, с крыльцом в полночь.",
                    "Скажешь Харви, когда он перегибает с заботой?",
                    "Да. Скажу — и ты остановишься.",
                    "Пока не знаю.",
                    "Нет. Мне нравится, как есть.",
                    "Договорились. Твоё «Харви, хватит» — такое же правило, как моё «стоп».$l#$b#Для меня это нечестно лёгкое обещание. Но я его сдержу.",
                    "Хорошо. Подумай.$0#$b#Я спрошу ещё раз — или ты сама скажешь, когда я снова начну считать твои шаги.",
                    "*смеётся* Правда? Даже крыльцо в полночь?$h#$b#…Ладно. Тогда буду стараться сам. Иногда."),

                HarveyCareConsents.SearchInStorm => new(
                    "В грозу ты иногда уходишь. В лес, подальше от людей.$s#$b#Я понимаю зачем. Но не знаю, хочешь ли ты, чтобы тебя там нашли.",
                    "Можно Харви искать тебя, если ты уйдёшь в грозу?",
                    "Да. Ищи меня.",
                    "Пока нет. Мне нужно побыть одной.",
                    "Нет. Не ищи меня.",
                    "Тогда я буду искать.$l#$b#Не чтобы тащить обратно. Чтобы ты не оставалась там одна.",
                    "Хорошо. Я не пойду следом.$s#$b#Но если передумаешь — я буду ждать в клинике. С чаем.",
                    "...Хорошо. Не буду.$s#$b#Мне трудно это обещать. Но я обещаю. Возвращайся сама — я буду ждать."),

                _ => new(
                    "Ты знаешь, какой я, когда боюсь за тебя. Строгий. Иногда — слишком.$a#$b#Поэтому хочу спросить, а не решать за тебя.",
                    "Можно Харви сказать «стоп» и увести тебя отдыхать, если ты себя загоняешь?",
                    "Да. Если увидишь — останавливай.",
                    "Пока нет. Я сама справлюсь.",
                    "Нет. Решаю я сама.",
                    "Договорились. Я скажу «стоп» — и ты остановишься.$a#$b#А если перегну я — «стоп» скажешь ты. Это работает в обе стороны.",
                    "Хорошо. Я буду только напоминать. Решать — тебе.$0",
                    "Понял. Решаешь ты.$0#$b#Я врач, а не командир. *улыбается краешком губ* Хотя иногда очень хочется."),
            };
        }
    }
}
