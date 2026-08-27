#!/usr/bin/env python3
"""Fail the build if the page uses a keel class the resolved keel version does not define.

keel ships no aliases: a renamed class stops existing, and a CSS class that does not exist
is not an error, it is simply no rule. So a rename reaches a CSS-only consumer as an
unstyled page in a browser, with nothing failing anywhere. Blazor consumers get the same
break at compile time with the symbol named. This check is the missing half of that: it
turns a silent visual regression into a red build.

Source of truth, in order of preference:
  1. dist/classes.json shipped by keel - a flat list of every class the bundle defines.
  2. Parsing the vendored bundle. Works, but it is a grep against a format that was never
     a contract, so it is the fallback rather than the design.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.normpath(os.path.join(HERE, ".."))
PAGE = os.path.join(DOCS, "index.html")
BUNDLE = os.path.join(DOCS, "keel.bundle.css")
INVENTORY = os.path.join(DOCS, "node_modules", "@adamcoulteroz", "keel", "dist", "classes.json")

# Classes the page layer defines itself. `.keel-cb{min-width:0}` is host-side grid
# containment, not a component redeclaration, so keel is not expected to define it.
PAGE_OWNED = set()


def defined_classes():
    if os.path.exists(INVENTORY):
        with open(INVENTORY, encoding="utf-8") as handle:
            return set(json.load(handle)), "keel's classes.json"

    with open(BUNDLE, encoding="utf-8") as handle:
        css = handle.read()
    # Strip comments so a class named only in prose does not count as defined.
    css = re.sub(r"/\*.*?\*/", "", css, flags=re.S)
    return set(re.findall(r"\.(keel-[A-Za-z0-9_-]+)", css)), "the vendored bundle"


def used_classes():
    with open(PAGE, encoding="utf-8") as handle:
        html = handle.read()
    used = set()
    for attr in re.findall(r'class="([^"]*)"', html):
        used.update(token for token in attr.split() if token.startswith("keel-"))
    return used


def main():
    for path in (PAGE, BUNDLE):
        if not os.path.exists(path):
            print(f"error: {path} is missing; run update-keel.sh and build.py first", file=sys.stderr)
            return 2

    defined, source = defined_classes()
    missing = sorted(used_classes() - defined - PAGE_OWNED)

    print(f"checked against {source}: {len(defined)} classes defined")
    if not missing:
        print("every keel class the page uses resolves")
        return 0

    print(f"\n{len(missing)} keel class(es) used by the page are not defined by this keel version:", file=sys.stderr)
    for name in missing:
        print(f"  {name}", file=sys.stderr)
    print("\nkeel ships no aliases, so these render unstyled. Check its CHANGELOG for the "
          "rename and update build.py in the same commit as the version bump.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
