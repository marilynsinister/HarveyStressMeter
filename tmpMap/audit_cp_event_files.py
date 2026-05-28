import re
from pathlib import Path

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]"
)


def strip_jsonc(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def is_main_event_key(key: str) -> bool:
    if "/" in key:
        return True
    return key.startswith("event") or key.startswith("MyMod_")


def analyze_file(fp: Path) -> tuple[list[str], dict]:
    text = strip_jsonc(fp.read_text(encoding="utf-8"))
    targets = re.findall(r'"Target"\s*:\s*"(Data/Events[^"]*)"', text)
    blocks = list(
        re.finditer(r'"Target"\s*:\s*"(Data/Events[^"]*)"[\s\S]*?"Entries"\s*:\s*\{', text)
    )
    per_target: dict = {}
    for m in blocks:
        target = m.group(1)
        start = m.end()
        depth = 1
        j = start
        while j < len(text) and depth > 0:
            if text[j] == "{":
                depth += 1
            elif text[j] == "}":
                depth -= 1
            j += 1
        block = text[start : j - 1]
        keys = re.findall(r'"((?:[^"\\]|\\.)+)"\s*:', block)
        main = [k for k in keys if is_main_event_key(k)]
        sub = [k for k in keys if k not in main]
        long_line = bool(
            re.search(
                r'"\s*:\s*"(?:none|continue)/[^"\n]{150,}/[^"\n]{20,}/[^"\n]{20,}/',
                block,
            )
        )
        multiline = bool(re.search(r'"\s*:\s*"\s*\n\s*(?:continue|none)/', block))
        array_fmt = bool(re.search(r'"\s*:\s*\[\s*\n', block))
        per_target[target] = {
            "main": len(main),
            "sub": len(sub),
            "long_line": long_line,
            "multiline": multiline or array_fmt,
            "main_keys": main,
        }
    return targets, per_target


def main() -> None:
    content = strip_jsonc((CP / "content.json").read_text(encoding="utf-8"))
    files = sorted((CP / "assets/Code").glob("*.json"))
    found_any = False

    for fp in files:
        text_raw = fp.read_text(encoding="utf-8")
        if "Data/Events" not in text_raw:
            continue
        found_any = True
        targets, per_target = analyze_file(fp)
        rel = fp.relative_to(CP)
        included = rel.as_posix() in content
        print(f"FILE: {rel}")
        print(f"  Included in content.json: {'yes' if included else 'no'}")
        total_main = 0
        total_sub = 0
        any_long = False
        any_multiline = False
        for t in sorted(set(targets)):
            info = per_target.get(t, {})
            total_main += info.get("main", 0)
            total_sub += info.get("sub", 0)
            any_long = any_long or info.get("long_line", False)
            any_multiline = any_multiline or info.get("multiline", False)
            print(
                f"  Target: {t} | main events: {info.get('main', 0)} | fork/sub keys: {info.get('sub', 0)}"
            )
        print(f"  TOTAL main events (approx): {total_main}")
        print(f"  TOTAL fork/sub keys: {total_sub}")
        print(f"  Single long line via /: {'yes' if any_long else 'no'}")
        print(f"  Multiline or JSON array format: {'yes' if any_multiline else 'no'}")
        print()

    if not found_any:
        print("No event files found.")


if __name__ == "__main__":
    main()
