#!/usr/bin/env python3
"""Remove dead CP topics/mail and apply CP-only fixes per audit-dead-content.md."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

TOPIC_DELETE_EXACT = {
    "topicEatSomething",
    "topicSpeakToHarvey",
    "topicSpeakToSomebody",
    "topicHealthDamage",
    "topicConcussionPhaseObservation",
    "topicFracturedBonePhaseCast",
    "topicShrapnelWoundsPhaseSurgery",
    "topicInfectedWoundPhaseTreatment",
    "topicTornMusclesPhaseRehab",
    "topicColdPhase1Ready",
    "topicColdRecoveryReady",
}

TOPIC_DELETE_SUFFIXES = (
    "Phase1Ready",
    "Phase2Ready",
    "RecoveryReady",
)

MAIL_DELETE = {
    # mailCure.json — legacy phased + unconnected cure mail
    "HarveyMod_TreatmentProgress",
    "HarveyMod_RecoveryUpdate",
    "HarveyMod_MorningCheckup",
    "HarveyMod_NutritionPlan",
    "HarveyMod_RecoveryMilestone",
    "HarveyMod_TreatmentComplete",
    "HarveyMod_FullRecovery",
    "HarveyMod_PreventiveCare",
    "HarveyMod_SafetyGuidelines",
    "HarveyMod_BackStrain_Phase2",
    "HarveyMod_BruisedRibs_Phase2",
    "HarveyMod_Burns_Phase2",
    "HarveyMod_Concussion_Phase2",
    "HarveyMod_Concussion_Phase3",
    "HarveyMod_DeepCuts_Phase2",
    "HarveyMod_FracturedBone_Phase2",
    "HarveyMod_FracturedBone_Phase3",
    "HarveyMod_Infection_Phase2",
    "HarveyMod_Shrapnel_Phase2",
    "HarveyMod_Shrapnel_Phase3",
    "HarveyMod_SprainedAnkle_Phase2",
    "HarveyMod_TornMuscles_Phase2",
    "HarveyMod_TornMuscles_Phase3",
    # mailInjury.json — dead duplicates / no trigger
    "HarveyMod_HurtCare",
    "HarveyMod_CriticalCareNotice",
    "HarveyMod_PostSurgeryInstructions",
    "HarveyMod_HospitalAdmission",
    "HarveyMod_HospitalDischarge",
    "HarveyMod_TooColdCare",
    "HarveyMod_HungerAdvice",
    "mailHarveyOverprotectiveNotice",
    "HarveyMod_OverprotectiveNotice",
    # mail.json — dead / duplicate
    "HarveyMod_HealingMilestone",
    "HarveyMod_HomeTreatmentOffer",
    "HarveyMod_LongTermCareSchedule",
    "HarveyMod_SkullCaveEmergency",
    "mailHarveyGentleCare",
}

ENTRY_LINE = re.compile(r'^(\s*)"(?P<key>[^"]+)":\s*".*",?\s*$')
FOREST_RESCUE_BLOCK = re.compile(
    r'\n\s*//После грозы\s*\n\s*\{\s*\n\s*"Action":\s*"EditData",\s*\n'
    r'\s*"Target":\s*"Characters/Dialogue/Harvey",\s*\n'
    r'\s*"Priority":\s*"Late",\s*\n'
    r'\s*"Entries":\s*\{[\s\S]*?\n\s*\},\s*\n'
    r'\s*"When":\s*\{\s*\n\s*"HasConversationTopic":\s*"topicForestRescue"\s*\n\s*\}\s*\n\s*\},',
    re.MULTILINE,
)


def should_delete_topic(key: str) -> bool:
    if key in TOPIC_DELETE_EXACT:
        return True
    if key.startswith("topic") and any(key.endswith(s) for s in TOPIC_DELETE_SUFFIXES):
        return True
    return False


def remove_entries(text: str, delete_keys: set[str], *, topic_mode: bool = False) -> tuple[str, int]:
    out_lines: list[str] = []
    removed = 0
    for line in text.splitlines(keepends=True):
        m = ENTRY_LINE.match(line.rstrip("\n"))
        if m:
            key = m.group("key")
            if topic_mode and should_delete_topic(key):
                removed += 1
                continue
            if not topic_mode and key in delete_keys:
                removed += 1
                continue
        out_lines.append(line)
    return "".join(out_lines), removed


def rename_topic(text: str, old: str, new: str) -> tuple[str, int]:
    count = text.count(f'"{old}"')
    return text.replace(f'"{old}"', f'"{new}"'), count


def connect_care_topics(text: str) -> tuple[str, bool, bool]:
    old_mod = '"AddMail Current mailHarveyModerateCare now"'
    new_mod = (
        '"AddMail Current mailHarveyModerateCare now",\n'
        '                        "AddConversationTopic topicHarveyModerateCare 7"'
    )
    old_int = '"AddMail Current mailHarveyIntensiveCare now"'
    new_int = (
        '"AddMail Current mailHarveyIntensiveCare now",\n'
        '                        "AddConversationTopic topicHarveyIntensiveCare 7"'
    )
    changed_mod = old_mod in text and new_mod not in text
    changed_int = old_int in text and new_int not in text
    if changed_mod:
        text = text.replace(old_mod, new_mod)
    if changed_int:
        text = text.replace(old_int, new_int)
    return text, changed_mod, changed_int


def main() -> None:
    stats: dict[str, int | bool] = {}

    path = CP / "dialoguesHarveyCure.json"
    text = path.read_text(encoding="utf-8")
    text, n = remove_entries(text, set(), topic_mode=True)
    stats["topics_cure_removed"] = n
    path.write_text(text, encoding="utf-8")

    path = CP / "dialoguesHarveyInjury.json"
    text = path.read_text(encoding="utf-8")
    text, n1 = remove_entries(text, set(), topic_mode=True)
    text, n2 = rename_topic(text, "topicSurgicalWoundHealed", "topicSurgicalWoundCured")
    stats["topics_injury_removed"] = n1
    stats["surgical_cured_renamed"] = n2
    path.write_text(text, encoding="utf-8")

    path = CP / "dialoguesHarvey.json"
    text = path.read_text(encoding="utf-8")
    new_text, n = FOREST_RESCUE_BLOCK.subn("", text, count=1)
    if n:
        text = new_text
    stats["forest_rescue_block_removed"] = bool(n)
    path.write_text(text, encoding="utf-8")

    for mail_file in ("mailCure.json", "mailInjury.json", "mail.json"):
        path = CP / mail_file
        text = path.read_text(encoding="utf-8")
        text, n = remove_entries(text, MAIL_DELETE)
        stats[f"mail_{mail_file}_removed"] = n
        path.write_text(text, encoding="utf-8")

    path = CP / "triggersCare.json"
    text = path.read_text(encoding="utf-8")
    text, mod, intensive = connect_care_topics(text)
    stats["care_moderate_connected"] = mod
    stats["care_intensive_connected"] = intensive
    path.write_text(text, encoding="utf-8")

    print("Done:")
    for k, v in stats.items():
        print(f"  {k}: {v}")


if __name__ == "__main__":
    main()
