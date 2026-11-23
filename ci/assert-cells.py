import re
import sys
from pathlib import Path

runtime, cells = sys.argv[1], Path(sys.argv[2])

CELLS = {
    "scalex0": ("bug", "Cannot set 'scaleX' to Float.NaN"),
    "scaley0": ("bug", "Cannot set 'scaleY' to Float.NaN"),
    "scale0": ("control", "Float.NaN"),
    "scalex250": ("control", "Float.NaN"),
    "scalex0-workaround": ("workaround", "Float.NaN"),
}

rows, failures = [], 0
for scenario, (role, message) in CELLS.items():
    folder = cells / scenario
    text = ""
    for name in ("logcat.txt", "crash.txt"):
        path = folder / name
        if path.exists():
            text += path.read_text(errors="replace")

    started = re.search(r"C18 START scenario=%s api=(\d+) animatorScale=([\d.]+)" % re.escape(scenario), text)
    done = re.search(r"C18 DONE scenario=%s thrown=(\d+)/(\d+) reachedLastTarget=(\w+)" % re.escape(scenario), text)
    saw_message = message in text

    if not started:
        expected, observed, ok = "the app starts", "no 'C18 START' line: the app did not run", False
    elif float(started.group(2)) == 0:
        expected, observed, ok = "animations on", "animator duration scale is 0, so MAUI skips every animation", False
    elif role == "bug":
        expected = f"'{message}' in logcat"
        observed = ("found" if saw_message else "not found") + (f"; {done.group(1)}/{done.group(2)} iterations threw" if done else "; the process died before DONE")
        ok = saw_message
    else:
        expected = "no NaN message, every iteration clean, last target reached"
        observed = (f"{done.group(1)}/{done.group(2)} threw, reachedLastTarget={done.group(3)}" if done else "no 'C18 DONE' line") + ("; NaN message found" if saw_message else "")
        ok = bool(done) and done.group(1) == "0" and done.group(3) == "True" and not saw_message

    failures += not ok
    rows.append(f"| {runtime} | `{scenario}` | {role} | {expected} | {observed} | {'PASS' if ok else 'FAIL'} |")

print("| Runtime | Cell | Role | Expected | Observed | Result |")
print("|---|---|---|---|---|---|")
print("\n".join(rows))
sys.exit(1 if failures else 0)
