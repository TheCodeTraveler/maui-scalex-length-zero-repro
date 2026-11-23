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
        threw = re.search(r"C18 STUCKTHREW threwAt=(-?\d+)", text)
        steps = {m.group(1): (m.group(2), m.group(3)) for m in re.finditer(r'C18 STUCK step="([^"]+)" maui=(\S+) native=(\S+)', text)}
        expected = "a caught throw within 20 fresh boxes, then ScaleX stays NaN and native stays 1 after ScaleX = 0.3 and ScaleXToAsync(0.8, 250)"
        observed = f"threwAt={threw.group(1)}" if threw else "no STUCKTHREW line"
        ok = bool(threw) and int(threw.group(1)) >= 0 and saw_message
        for step in ("after ScaleX = 0.3", "after ScaleXToAsync(0.8, 250)"):
            values = steps.get(step)
            observed += f"; {step}: maui={values[0]} native={values[1]}" if values else f"; {step}: missing"
            ok = ok and values is not None and values[0] == "NaN" and float(values[1]) == 1.0
    elif role == "uncaught":
        process_file = folder / "process.txt"
        process = process_file.read_text().strip() if process_file.exists() else "unknown"
        died = any(marker in text for marker in ("FATAL EXCEPTION", "Fatal signal")) or bool(re.search(r"ActivityManager.*has died", text)) or process == "gone"
        iterations = len(re.findall(r"C18 ITER \d+ awaiting", text))
        expected = "a length-0 call throws and the process dies"
        if died and saw_message:
            observed, ok = f"threw and the process died after {iterations} call(s)", True
        elif saw_message:
            observed, ok = f"threw but the process survived (process={process}); describe it as a throw, not a crash", False
        else:
            observed, ok = f"no throw in {iterations} call(s)" + ("; DONE logged" if done or "C18 DONE scenario=scalex0-uncaught" in text else ""), False
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
