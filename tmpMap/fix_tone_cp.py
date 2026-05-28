# -*- coding: utf-8 -*-
import json
from pathlib import Path

injury_path = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\dialoguesHarveyInjury.json")
with injury_path.open(encoding="utf-8") as f:
    injury = json.load(f)

block1 = injury["Changes"][0]["Entries"]
orig = dict(block1)

clinical = {
    "topicHurt": "Покажите, где болит. Обработаю в клинике — быстро и аккуратно.$0",
    "topicBadlyHurt": "Травма серьёзная. Немедленно начнём лечение — не откладывайте.$a",
    "topicFarmerExhausted": "Вы явно переутомлены.$a#$b#Сегодня — только отдых. Завтра проведу полный осмотр.$0",
    "topicPassedOutInTown": "Обморок на улице — тревожный симптом.$8#$b#Приходите в клинику завтра на обследование. До выяснения причин — щадящий режим, без тяжёлых нагрузок.$a",
    "topicEatSomething": "Когда вы в последний раз нормально ели?$a#$b#Недостаток питания ослабляет организм. Сейчас принесу лёгкий перекус.$0",
    "topicSpeakToSomebody": "Если хотите поговорить — в клинике всегда можно найти тихий уголок.$0#$b#Иногда это помогает не меньше лекарств.$h",
    "topicSpeakToHarvey": "Расскажите, что беспокоит. Я выслушаю и дам медицинские рекомендации.$0",
    "topicInfectedWound": "Признаки воспаления раны.$a#$b#Температура 39 — это серьёзно. Начинаем курс антибиотиков и наблюдение в клинике.$a",
    "topicHealthDamage": "Покажите, где болит.$0#$b#Такие травмы нельзя игнорировать. Обработаю в клинике.$a",
    "topicHealthDamageCritical": "Что случилось?$8#$b#Пульс слабый, кожа бледная. Сейчас окажу помощь — дышите ровно.$a",
    "topicHealthDamageSevere": "Ваши показатели меня беспокоят.$8#$b#Нужен покой и восстановление. Останусь рядом до стабилизации — это протокол.$u",
    "topicOverprotectiveMode": "После тяжёлой травмы рекомендую избегать шахт и опасной работы до полного восстановления.$a#$b#Это стандартный протокол наблюдения.$0",
    "topicBruisedRibs": "Ушиб рёбер. Сяду, осмотрю — переломов нет, но нужен покой и фиксирующая повязка.$0#$b#Без резких движений несколько дней.$a",
    "topicSprainedAnkle": "Растяжение связок.$8#$b#Не наступайте на эту ногу. Выдам костыли и лёд.$a",
    "topicBackStrain": "Спазм в спине.$s#$b#Ложитесь, проверю позвоночник. Мышцы напряжены, но без перелома.$0",
    "topicDeepCuts": "Глубокие порезы.$8#$b#Обработаю раны и наложу швы. Сегодня — только покой.$a",
    "topicBurnWounds": "Ожоги требуют обработки.$a#$b#Нанесу мазь и повязку. Следите за признаками инфекции.$0",
    "topicTornMuscles": "Надрыв мышцы.$8#$b#Зафиксирую конечность. Никаких нагрузок до следующего осмотра.$a",
    "topicConcussion": "Подозрение на сотрясение.$8#$b#Проверю зрачки и рефлексы. Полный покой: без яркого света и резких звуков.$a#$b#Буду проверять состояние регулярно.$u",
    "topicFracturedBone": "Признаки перелома.$8#$b#Сейчас зафиксирую конечность. Не двигайтесь.$a#$b#Гипс и полный покой на несколько недель.$u",
    "topicShrapnelWounds": "Несколько осколочных ран.$8#$b#Подготовлю инструменты для обработки. Это займёт время, но всё под контролем.$a",
    "topicSurgicalWound": "Послеоперационный шов требует осмотра.$0#$b#Проверю на покраснение и признаки инфекции. Без нагрузок до заживления.$a",
    "topicPostOperativeCare": "Важно соблюдать все рекомендации после операции.$a#$b#Буду менять повязки по протоколу, чтобы избежать инфекции.$u",
    "topicSurgicalWoundHealed": "Шов зажил.$h#$b#Можно постепенно возвращаться к обычной активности — без резких нагрузок.$0",
    "topicCold": "Признаки простуды.$s#$b#Температура повышена. Жаропонижающее, покой и тёплый чай.$0#$b#Без прогулок под дождём до выздоровления.$a",
    "topicTooCold": "Признаки переохлаждения.$s#$b#Зайдите в клинику — согрею и дам горячий чай. Одевайтесь теплее.$0",
    "topicMineInjuryRescue": "Пришлось вытащить вас из шахты после потери сознания.$8#$b#Это могло закончиться гораздо хуже. Пожалуйста, будьте осторожнее.$a",
}

moderate = {
    "topicHurt": "Порез? Покажи, где болит — перевяжу здесь.$0",
    "topicBadlyHurt": "Травма серьёзнее, чем кажется. Сейчас займусь лечением — не спорь.$a",
    "topicFarmerExhausted": "Ты еле стоишь на ногах.$a#$b#Сегодня — только отдых. Завтра осмотр.$0",
    "topicPassedOutInTown": "Ты потеряла сознание в городе.$8#$b#Это тревожно. Завтра — обязательный осмотр. Сегодня без тяжёлой работы.$a",
    "topicEatSomething": "Когда ты последний раз нормально ела?$a#$b#Сейчас принесу что-нибудь питательное. Работа на голодный желудок — плохая идея.$0",
    "topicSpeakToHarvey": "Рад, что пришла. Расскажи, что тревожит — помогу разобраться.$0#$b#Я и врач, и человек, которому не всё равно.$h",
    "topicOverprotectiveMode": "После всего случившегося прошу избегать шахт и опасных мест.$a#$b#Пока не восстановишься — это необходимо.$u",
    "topicHealthDamage": "Поранилась? Покажи, где болит — обработаю в клинике.$0",
    "topicHealthDamageCritical": "Что случилось?$8#$b#Ты бледная, пульс слабый. Дыши ровно — я здесь.$a",
    "topicHealthDamageSevere": "Твой вид меня беспокоит.$8#$b#Нужен покой. Не спорь — останусь рядом до стабилизации.$u",
    "topicConcussion": "Подозрение на сотрясение.$8#$b#Проверю зрачки. Полный покой — никакого яркого света.$a#$b#Буду следить за состоянием.$u",
    "topicInfectedWound": orig["topicInfectedWound"],
    "topicBruisedRibs": orig["topicBruisedRibs"],
    "topicSprainedAnkle": orig["topicSprainedAnkle"],
    "topicBackStrain": orig["topicBackStrain"],
    "topicDeepCuts": orig["topicDeepCuts"],
    "topicBurnWounds": orig["topicBurnWounds"],
    "topicTornMuscles": orig["topicTornMuscles"],
    "topicFracturedBone": orig["topicFracturedBone"],
    "topicShrapnelWounds": orig["topicShrapnelWounds"],
    "topicSurgicalWound": orig["topicSurgicalWound"],
    "topicPostOperativeCare": orig["topicPostOperativeCare"],
    "topicSurgicalWoundHealed": orig["topicSurgicalWoundHealed"],
    "topicCold": orig["topicCold"],
    "topicTooCold": orig["topicTooCold"],
    "topicMineInjuryRescue": orig["topicMineInjuryRescue"],
}

firm = {k: orig[k] for k in clinical if k in orig}
firm["topicOverprotectiveMode"] = orig["topicOverprotectiveMode"]

for k, v in clinical.items():
    block1[k] = v

hospital_early = {
    "Hospital_Mon": "Перевязка по расписанию. Покажите рану — обработаю.$0",
    "Hospital_Tue": "Мазь нанесена. Не перенапрягайтесь сегодня.$0",
    "Hospital_Wed": "Если усилится боль или слабость — сразу сообщите.$0",
    "Hospital_Thu": "Проверю, как идёт заживление.$0",
    "Hospital_Fri": "При необходимости дам обезболивание. Не терпите боль.$0",
    "Hospital_Sat": "Свежие перевязочные материалы на месте. Зовите, если понадобится.$0",
    "Hospital_Sun": "Сегодня — отдых. Травяной чай поможет восстановиться.$0",
}

for ch in injury["Changes"]:
    when = ch.get("When", {})
    if when.get("Hearts:Harvey") == "0,1,2,3,4,5" and when.get("HasConversationTopic") == "topicHurt":
        ch["Entries"]["Hospital_Mon"] = (
            "Перевязка. Покажи рану — обработаю.$0#$b#Старайся быть осторожнее.$0"
        )
        ch["Entries"]["Hospital_Wed"] = "Если усилится боль — сразу скажи.$0"
        ch["Entries"]["Hospital_Thu"] = "Проверю заживление сегодня.$0"
        when["Hearts:Harvey"] = "2,3,4,5"
        break

new_blocks = [
    {
        "Action": "EditData",
        "Target": "Characters/Dialogue/Harvey",
        "Priority": "Late",
        "When": {"Hearts:Harvey": "0,1", "HasConversationTopic": "topicHurt"},
        "Entries": hospital_early,
    },
    {
        "Action": "EditData",
        "Target": "Characters/Dialogue/Harvey",
        "Priority": "Late",
        "When": {"Hearts:Harvey": "3,4,5"},
        "Entries": moderate,
    },
    {
        "Action": "EditData",
        "Target": "Characters/Dialogue/Harvey",
        "Priority": "Late",
        "When": {"Hearts:Harvey": "6,7,8,9,10"},
        "Entries": firm,
    },
]

injury["Changes"][1:1] = new_blocks

with injury_path.open("w", encoding="utf-8") as f:
    json.dump(injury, f, ensure_ascii=False, indent=4)
    f.write("\n")
print("injury OK")

cure_path = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\dialoguesHarveyCure.json"
)
text = cure_path.read_text(encoding="utf-8")
marker = '\n        {\n            "Action": "EditData",\n            "Target": "Characters/Dialogue/Harvey",\n            "When":'
idx = text.find(marker)
if idx == -1:
    raise SystemExit("cure marker not found")
head, tail = text[:idx], text[idx:]

replacements = [
    ("не отпущу тебя без контроля", "завершу осмотр и дам чёткие рекомендации"),
    ("не отпущу", "не завершу осмотр"),
    ("не позволю", "не рекомендую"),
    ("Ты под моей защитой", "Вы под медицинским наблюдением"),
    ("ты под моей защитой", "вы под наблюдением"),
    ("Ты под моим присмотром", "Вы под наблюдением"),
    ("ты под моим присмотром", "вы под наблюдением"),
    ("слишком хрупкая", "организм ослаблен"),
    ("слишком худая", "недостаточно питания"),
    ("никуда без меня не пойдёшь", "сегодня — только покой"),
    ("я всё контролирую", "я слежу за показателями"),
    ("отпущу только если", "отпущу после осмотра, если"),
]
for old, new in replacements:
    head = head.replace(old, new)

cure_path.write_text(head + tail, encoding="utf-8")
print("cure OK")
