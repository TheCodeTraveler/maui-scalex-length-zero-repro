"""Merges the per-leg tables into one Markdown summary. usage: summarize.py <results-dir>"""
import sys
from pathlib import Path

print("## C18 results: bug legs must show the bug, control and fix legs must be clean\n")
for path in sorted(Path(sys.argv[1]).rglob("*.md")):
    print(f"### {path.stem}\n")
    print(path.read_text())
