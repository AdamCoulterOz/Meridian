#!/usr/bin/env python3
"""Record the resolved value of every keel token the page uses, per theme.

Run this ONLY when a value change is intended, in the same commit as the keel bump, so the
change shows up as a reviewable diff instead of arriving silently.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from keel_tokens import resolve_used

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.normpath(os.path.join(HERE, ".."))
SNAPSHOT = os.path.join(HERE, "keel-token-values.json")

values = resolve_used(open(os.path.join(DOCS, "keel.bundle.css"), encoding="utf-8").read(),
                      open(os.path.join(DOCS, "index.html"), encoding="utf-8").read())
with open(SNAPSHOT, "w", encoding="utf-8") as handle:
    json.dump(values, handle, indent=1, sort_keys=True)
    handle.write("\n")
print(f"recorded {len(values)} tokens -> {os.path.relpath(SNAPSHOT, DOCS)}")
