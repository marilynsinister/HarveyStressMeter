import re
from pathlib import Path
from inject_topic_blocks import EVENT_HEADER_RE

md = Path(r"C:\Users\Admin\HarveyOverhaulInjury\docs\events-inventory\08-events-as-book.md").read_text(encoding="utf-8")
start_m = re.search(r"\n## Часть I", md)
work = md[start_m.start() :]
for m in EVENT_HEADER_RE.finditer(work):
    line = work[: m.start()].count("\n") + 1
    print(line, m.group(1))
