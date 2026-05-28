import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury")

topic_keys = {}
mail_keys = {}


def scan_file(path: Path):
    text = path.read_text(encoding="utf-8")
    for m in re.finditer(r'"((?:topic|mail)[A-Za-z0-9_]*)"', text):
        k = m.group(1)
        if k.startswith("topic"):
            topic_keys.setdefault(k, set()).add(path.name)
        elif k.startswith("mail"):
            mail_keys.setdefault(k, set()).add(path.name)
    for m in re.finditer(r'"(HarveyMod_[A-Za-z0-9_]*)"', text):
        mail_keys.setdefault(m.group(1), set()).add(path.name)


for p in CP.rglob("*.json"):
    scan_file(p)

events_text = (CP / "events.json").read_text(encoding="utf-8")
event_add_topics = set(re.findall(r"addConversationTopic\s+([A-Za-z0-9_]+)", events_text))
event_remove_topics = set(re.findall(r"removeConversationTopic\s+([A-Za-z0-9_]+)", events_text))
event_has_topic = set(
    re.findall(r"!?PLAYER_HAS_CONVERSATION_TOPIC\s+Current\s+([A-Za-z0-9_]+)", events_text)
)
event_mail = set(re.findall(r"addMail\s+([A-Za-z0-9_]+)", events_text))
event_mail |= set(re.findall(r"addMailTomorrow\s+([A-Za-z0-9_]+)", events_text))

# dialogue topic transitions #$t topicX
dialogue_topic_transitions = set()
for p in CP.rglob("*.json"):
    text = p.read_text(encoding="utf-8")
    for m in re.finditer(r"#\$t\s+(topic[A-Za-z0-9_]+)", text):
        dialogue_topic_transitions.add(m.group(1))

cs_text = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in CS.rglob("*.cs"))
const_topics = dict(re.findall(r'public const string (\w+) = "(topic[^"]+)"', cs_text))
const_mails = dict(re.findall(r'public const string (\w+) = "(mail[^"]+|HarveyMod_[^"]+)"', cs_text))

csharp_add = set(re.findall(r'AddTopic\s*\(\s*"([^"]+)"', cs_text))
csharp_add |= set(const_topics.values())
csharp_add |= set(re.findall(r"AddTopic\s*\(\s*ConversationTopics\.(\w+)", cs_text))
csharp_remove = set(re.findall(r'RemoveTopic\s*\(\s*"([^"]+)"', cs_text))
csharp_remove |= set(const_topics.values())
csharp_has = set(re.findall(r'HasConversationTopic\s*\(\s*"([^"]+)"', cs_text))
csharp_has |= set(re.findall(r'HasTopic\s*\(\s*"([^"]+)"', cs_text))
csharp_tryadd = set(re.findall(r'activeDialogueEvents\.TryAdd\s*\(\s*"([^"]+)"', cs_text))
csharp_mail = set(re.findall(r'addMailForTomorrow\s*\(\s*"([^"]+)"', cs_text))
csharp_mail |= set(const_mails.values())

# ModEntry trauma/comp mappings
modentry_topics = set(re.findall(r'TopicId\s*=\s*"(topic[^"]+)"', cs_text))

all_topic_callers = (
    csharp_add
    | csharp_remove
    | csharp_has
    | csharp_tryadd
    | event_add_topics
    | event_has_topic
    | dialogue_topic_transitions
    | modentry_topics
)

all_mail_callers = csharp_mail | event_mail

DIALOGUE_PREFIXES = (
    "Treat_",
    "PhaseTransition_",
    "Proximity_",
    "Treatment_Phase_",
    "RemoveStitches_",
)


def topic_is_called(tid: str) -> tuple[bool, str]:
    if tid in all_topic_callers:
        return True, "exact"

    if any(tid.startswith(p) for p in DIALOGUE_PREFIXES):
        return True, "dialogue-key-not-topic"

    # C# dynamic: topic{Name}PhaseAcute|Healing|Recovery
    for phase in ("PhaseAcute", "PhaseHealing", "PhaseRecovery"):
        if tid.endswith(phase):
            return True, "csharp-GetPhaseTopicId"

    # C# dynamic: topicTreatment{Name}
    if tid.startswith("topicTreatment") and "topicTreatment" in cs_text:
        return True, "csharp-dynamic-treatment"

    # C# dynamic: topic{Name}Cured via completionTopic
    if tid.endswith("Cured") and "completionTopic" in cs_text:
        return True, "csharp-completionTopic"

    # PhaseTransition dialogue keys stored as topic-like in cure file
    if tid.startswith("PhaseTransition_"):
        return True, "dialogue-key"

    # Phase1Ready etc - legacy CP
    if "Phase1Ready" in tid or "Phase2Ready" in tid or "RecoveryReady" in tid:
        return False, "legacy-phase-ready"

    # Hurt phased topics - C# doesn't use phases for Hurt
    if tid.startswith("topicHurtPhase") or tid.startswith("topicBadlyHurtPhase"):
        return False, "legacy-hurt-phases"

    # memory topics - CP event memory system
    if "_memory_" in tid:
        base = re.sub(r"_memory_(oneday|oneweek)$", "", tid)
        if base in event_add_topics or base in csharp_add:
            return True, "memory-followup"
        return False, "memory-orphan"

    # stress file - check if included
    return False, "no-caller"


def mail_is_called(mid: str) -> tuple[bool, str]:
    if mid in all_mail_callers:
        return True, "exact"
    if mid in cs_text:
        return True, "csharp-string"
    return False, "no-caller"


print("TOPICS", len(topic_keys), "MAIL", len(mail_keys))
print("EVENT add topics", len(event_add_topics))
print("DIALOGUE #$t topics", len(dialogue_topic_transitions))

dead_t = [(t, f, *topic_is_called(t)) for t, f in sorted(topic_keys.items()) if not topic_is_called(t)[0]]
dead_m = [(m, f, *mail_is_called(m)) for m, f in sorted(mail_keys.items()) if not mail_is_called(m)[0]]

print("\nDEAD TOPICS", len(dead_t))
for row in dead_t:
    print(row[0], "|", ",".join(sorted(row[1])))

print("\nDEAD MAIL", len(dead_m))
for row in dead_m:
    print(row[0], "|", ",".join(sorted(row[1])))

print("\nEVENT ADD TOPICS:")
for t in sorted(event_add_topics):
    print(" ", t)

print("\nC# ADD TOPICS (string literals):")
for t in sorted(csharp_add):
    print(" ", t)

print("\nC# MAIL:")
for m in sorted(csharp_mail):
    print(" ", m)
