from pathlib import Path

p = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json")
text = p.read_text(encoding="utf-8")

replacements = [
    (
        r'\\friendship Harvey 45(break)speak Harvey \"Принимается.',
        r'\\friendship Harvey 45\\addConversationTopic topicHarveyStorm_Clinic 7(break)speak Harvey \"Принимается.',
    ),
    (
        r'\\friendship Harvey 40(break)speak Harvey \"Записка',
        r'\\friendship Harvey 40\\addConversationTopic topicHarveyStorm_Home 7(break)speak Harvey \"Записка',
    ),
    (
        r'\\friendship Harvey 35(break)speak Harvey \"Конечно',
        r'\\friendship Harvey 35\\addConversationTopic topicHarveyStorm_Note 7(break)speak Harvey \"Конечно',
    ),
    (
        r'\\friendship Harvey 50/\n        pause 400/\n        speak Harvey \"Вот теперь хорошо. Не потому что страх исчез',
        r'\\friendship Harvey 50\\addConversationTopic topicHarveyStorm_Escort 7/\n        pause 400/\n        speak Harvey \"Вот теперь хорошо. Не потому что страх исчез',
    ),
    (
        r'\\speak Harvey \"Запомните это чувство. Не меня — ритм.$0\"(break)message \"Ты сжимаешь',
        r'\\speak Harvey \"Запомните это чувство. Не меня — ритм.$0\"\\addConversationTopic topicHarveyTrust_TouchOk 7(break)message \"Ты сжимаешь',
    ),
    (
        r'\\speak Harvey \"Иногда организм сначала спорит. Ничего. Я умею быть терпеливым.$u\"(break)message \"Ты стоишь',
        r'\\speak Harvey \"Иногда организм сначала спорит. Ничего. Я умею быть терпеливым.$u\"\\addConversationTopic topicHarveyTrust_BreathHard 7(break)message \"Ты стоишь',
    ),
    (
        r'\\speak Harvey \"Я останусь рядом и помолчу. Редкий медицинский метод, но действенный.$l\"(break)message \"Ты медленно',
        r'\\speak Harvey \"Я останусь рядом и помолчу. Редкий медицинский метод, но действенный.$l\"\\addConversationTopic topicHarveyTrust_NeedsSpace 7(break)message \"Ты медленно',
    ),
    (
        r'\\speak Harvey \"Счёт останется. Рука — только если понадобится.$0\"/\n        pause 400/\n        speak Harvey \"У вас получается',
        r'\\speak Harvey \"Счёт останется. Рука — только если понадобится.$0\"\\addConversationTopic topicHarveyTrust_NeedsSpace 7/\n        pause 400/\n        speak Harvey \"У вас получается',
    ),
    (
        r'\\move Harvey -2 0 3\\friendship Harvey 30(break)speak Harvey \"Хорошо. Я рядом',
        r'\\move Harvey -2 0 3\\friendship Harvey 30\\addConversationTopic topicHarveyHelp_Asks 7(break)speak Harvey \"Хорошо. Я рядом',
    ),
    (
        r'\\message \"Ты тянешься к коробке — Харви стоит в полушаге, готовый подхватить.\"\\friendship Harvey 28(break)speak Harvey \"Ладно',
        r'\\message \"Ты тянешься к коробке — Харви стоит в полушаге, готовый подхватить.\"\\friendship Harvey 28\\addConversationTopic topicHarveyHelp_Spotter 7(break)speak Harvey \"Ладно',
    ),
    (
        r'\\speak Harvey \"Отлично. Медленно вниз. Да. Теперь я снова могу дышать.$l\"\\friendship Harvey 25(break)message \"Ты указываешь',
        r'\\speak Harvey \"Отлично. Медленно вниз. Да. Теперь я снова могу дышать.$l\"\\friendship Harvey 25\\addConversationTopic topicHarveyHelp_Independent 7(break)message \"Ты указываешь',
    ),
]

for old, new in replacements:
    if old not in text:
        print("MISS:", old[:70])
    else:
        text = text.replace(old, new, 1)
        print("OK:", old[:50])

p.write_text(text, encoding="utf-8")
print("written")
