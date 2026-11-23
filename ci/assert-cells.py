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
    "scalex0-stuck": ("stuck", "Cannot set 'scaleX' to Float.NaN"),
    "scalex0-uncaught": ("uncaught", "Cannot set 'scaleX' to Float.NaN"),
}

MIN_THROWN = 10

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
        expected = f"'{message}' in logcat, at least {MIN_THROWN}/20 iterations throw"
        thrown = int(done.group(1)) if done else 0
        observed = ("found" if saw_message else "not found") + (f"; {done.group(1)}/{done.group(2)} threw, reachedLastTarget={done.group(3)}" if done else "; the process died before DONE")
        ok = saw_message and thrown >= MIN_THROWN
    elif role == "stuck":
        final = re.search(r'C18 STUCK step="after ScaleXToAsync\(0\.8, 250\)" maui=(\S+) native=(\S+)', text)
        expected = "the throw, then ScaleX stays NaN after ScaleX = 1 and ScaleXToAsync(0.8, 250), native stays 1"
        observed = ("message found" if saw_message else "message not found") + (f"; final maui={final.group(1)} native={final.group(2)}" if final else "; no final STUCK line")
        ok = saw_message and bool(final) and final.group(1) == "NaN" and float(final.group(2)) == 1.0
    elif role == "uncaught":
        died = "FATAL EXCEPTION" in text
        expected = "the process dies with the message and never logs DONE"
        observed = ("FATAL EXCEPTION found" if died else "no FATAL EXCEPTION") + ("; message found" if saw_message else "; message not found") + ("; DONE logged, the process survived" if done else "")
        ok = died and saw_message and not done
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
