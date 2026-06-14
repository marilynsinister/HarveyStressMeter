import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")


def append_action(text: str, key: str, action: str) -> str:
    pattern = rf'("{re.escape(key)}": "[^"]*?)(")'
    def repl(m):
        body = m.group(1)
        if "HarveyStress_" in body:
            return m.group(0)
        return body + f"#$action {action}" + m.group(2)
    return re.sub(pattern, repl, text)


def main():
    stress_path = CP / "dialoguesHarveyStress.json"
    stress = stress_path.read_text(encoding="utf-8")
    for key, buff in {
        "topicStressTired": "buffStressTired",
        "topicStressLonely": "buffStressLonely",
        "topicStressThunder": "buffStressThunder",
        "topicStressHunger": "buffStressHunger",
        "topicStressOverwork": "buffStressOverwork",
        "topicStressNoSleep": "buffStressNoSleep",
        "topicStressTooCold": "buffStressTooCold",
        "topicStressDarkness": "buffStressDarkness",
    }.items():
        stress = append_action(stress, key, f"HarveyStress_StartTreatment {buff}")
    stress = append_action(stress, "topicStressSocial", "HarveyStress_SocialAnxiety_Start")
    stress_path.write_text(stress, encoding="utf-8")
    print("dialoguesHarveyStress.json updated")

    cure_path = CP / "dialoguesHarveyCureStress.json"
    cure = cure_path.read_text(encoding="utf-8")
    for key, buff in {
        "topicStressTreatmentTiredReadyForReview": "buffStressTired",
        "topicStressTreatmentLonelyReadyForReview": "buffStressLonely",
        "topicStressTreatmentThunderReadyForReview": "buffStressThunder",
        "topicStressTreatmentHungerReadyForReview": "buffStressHunger",
        "topicStressTreatmentOverworkReadyForReview": "buffStressOverwork",
        "topicStressTreatmentNoSleepReadyForReview": "buffStressNoSleep",
        "topicStressTreatmentTooColdReadyForReview": "buffStressTooCold",
        "topicStressTreatmentDarknessReadyForReview": "buffStressDarkness",
    }.items():
        cure = append_action(cure, key, f"HarveyStress_CompleteReview {buff}")
    cure = append_action(cure, "topicStressTreatmentSocialReadyForReview", "HarveyStress_SocialAnxiety_Complete")
    cure_path.write_text(cure, encoding="utf-8")
    print("dialoguesHarveyCureStress.json updated")


if __name__ == "__main__":
    main()
