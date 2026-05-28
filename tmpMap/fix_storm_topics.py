from pathlib import Path

p = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json")
text = p.read_text(encoding="utf-8")

# Remove mistaken Note topic from non-E5 storm event
wrong = (
    r'\\friendship Harvey 35\\addConversationTopic topicHarveyStorm_Note 7(break)speak Harvey \"Конечно! Я проверю все окна'
)
right = r'\\friendship Harvey 35(break)speak Harvey \"Конечно! Я проверю все окна'
if wrong in text:
    text = text.replace(wrong, right, 1)
    print("reverted wrong Note topic")
else:
    print("wrong Note not found")

# Add Escort to E5 only
escort_old = (
    r'Попросить сопровождение в грозу разумно.$a\"\\speak Harvey \"Я даже не буду делать вид, что мне это не спокойнее.$u\"\\friendship Harvey 50/'
)
escort_new = (
    r'Попросить сопровождение в грозу разумно.$a\"\\speak Harvey \"Я даже не буду делать вид, что мне это не спокойнее.$u\"\\friendship Harvey 50\\addConversationTopic topicHarveyStorm_Escort 7/'
)
if escort_old in text:
    text = text.replace(escort_old, escort_new, 1)
    print("added Escort")
else:
    print("Escort anchor not found")

p.write_text(text, encoding="utf-8")
