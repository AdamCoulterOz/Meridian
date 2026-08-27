#!/usr/bin/env python3
"""Fail the build if the page uses a keel class or token the resolved keel version lacks.

keel ships no aliases: a renamed class stops existing, and a CSS class that does not exist
is not an error, it is simply no rule. So a rename reaches a CSS-only consumer as an
unstyled page in a browser, with nothing failing anywhere. Blazor consumers get the same
break at compile time with the symbol named. This check is the missing half of that: it
turns a silent visual regression into a red build.

Two descriptions of the same bundle, and neither is trusted alone:

  1. dist/classes.json shipped by keel - a flat, generated list of every class it defines.
  2. Parsing the vendored bundle here.

The stylesheet the browser loads is the only ground truth; both of these merely describe
it. A wrong description is worse than none, because it is believed: a phantom entry in the
inventory (keel's comments name classes, so a comment mentioning a removed one could add
it) makes this check pass while the page is broken in exactly the way it exists to catch.
A gap in the local parse does the opposite and fails a good build.

So when both are available they must AGREE about every class the page actually uses. If
they disagree, one of them is wrong and this cannot tell which, which is itself the
failure. Disagreement about classes the page does not use is keel's business, not a reason
to block a deploy.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from keel_tokens import resolve_used

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.normpath(os.path.join(HERE, ".."))
PAGE = os.path.join(DOCS, "index.html")
BUNDLE = os.path.join(DOCS, "keel.bundle.css")
LOCKFILE = os.path.join(DOCS, "package-lock.json")
SNAPSHOT = os.path.join(HERE, "keel-token-values.json")
INVENTORY = os.path.join(DOCS, "node_modules", "@adamcoulteroz", "keel", "dist", "classes.json")

# Classes the page layer defines itself. `.keel-cb{min-width:0}` is host-side grid
# containment, not a component redeclaration, so keel is not expected to define it.
PAGE_OWNED = set()


def classes_in_bundle():
    with open(BUNDLE, encoding="utf-8") as handle:
        css = handle.read()
    # Strip comments so a class named only in prose does not count as defined. keel's
    # stylesheets explain why rules exist and name classes routinely while doing it.
    css = re.sub(r"/\*.*?\*/", "", css, flags=re.S)
    # Quoted strings and url() payloads contain dots that are not selectors: the @import of
    # fonts.googleapis.com otherwise yields "com" and "googleapis" as phantom classes. This
    # is why the local parse is the second opinion and not the authority.
    css = re.sub(r"""(['"]).*?\1""", "", css, flags=re.S)
    css = re.sub(r"url\([^)]*\)", "", css)
    # Every class in this file is keel's, so do not pre-filter to the keel- prefix: that
    # would drop the syntax token classes and make them look absent from the stylesheet.
    return set(re.findall(r"\.([A-Za-z_][A-Za-z0-9_-]*)", css))


def classes_in_inventory():
    if not os.path.exists(INVENTORY):
        return None
    with open(INVENTORY, encoding="utf-8") as handle:
        return set(json.load(handle))


def used_classes(known):
    """Classes the page uses that keel is expected to provide.

    Not simply everything prefixed keel-: keel's namespace is not entirely prefixed. The
    syntax token classes it emits inside a code block are `c`, `k`, `s`, `n`, `t` and `f`,
    real selectors at `.keel-cb .c`. This page colours syntax with inline styles rather
    than those classes, so none are in use today, but a build.py change could start
    emitting them and a keel- filter would silently stop covering them.

    Membership of anything keel is known to define settles it exactly — the inventory when
    present, the stylesheet always. Scoping on the inventory alone would mean a class going
    MISSING from the inventory also drops out of scope, which is the one case that most
    needs catching. It cannot over-reach either:
    the page has classes defined nowhere in CSS at all (`anno-line` is a selector the
    scroll handler queries, never a styled rule), and a "must be defined somewhere" rule
    would wrongly flag those.
    """
    with open(PAGE, encoding="utf-8") as handle:
        html = handle.read()
    used = set()
    for attr in re.findall(r'class="([^"]*)"', html):
        used.update(attr.split())

    return {name for name in used if name.startswith("keel-")} | (used & known)


def tokens_in_bundle():
    with open(BUNDLE, encoding="utf-8") as handle:
        css = re.sub(r"/\*.*?\*/", "", handle.read(), flags=re.S)
    # Custom properties are declared, not selected, so a "--name:" is unambiguous in a way
    # a class selector is not. No corroborating inventory is needed for this half.
    return set(re.findall(r"(--[A-Za-z0-9-]+)\s*:", css))


def page_tokens():
    """(tokens the page uses, tokens the page defines for itself)."""
    with open(PAGE, encoding="utf-8") as handle:
        html = handle.read()
    used = set(re.findall(r"var\((--[A-Za-z0-9-]+)", html))
    style = re.search(r"<style>(.*?)</style>", html, re.S)
    declared = set()
    if style:
        declared = set(re.findall(r"(--[A-Za-z0-9-]+)\s*:",
                                  re.sub(r"/\*.*?\*/", "", style.group(1), flags=re.S)))
    return used, declared


def check_tokens():
    """A renamed TOKEN is as silent as a renamed class, and has bitten this page more often.

    --code-* meant something different from what the markup assumed, keel ramps were read
    where semantic aliases were needed, and --surface-footer was removed outright in 0.2.0.
    Each was found by hand. An unresolved var() falls back to nothing and the declaration is
    simply dropped, so the page renders with the property unset and nothing reports it.
    """
    used, declared = page_tokens()
    defined = tokens_in_bundle() | declared
    missing = sorted(used - defined)

    print(f"tokens: page uses {len(used)}, keel defines {len(tokens_in_bundle())}, "
          f"page defines {len(declared)}")
    if not missing:
        print("every custom property the page uses resolves")
        return check_token_values()

    print(f"\n{len(missing)} custom propert(ies) the page uses are defined nowhere:", file=sys.stderr)
    for name in missing:
        print(f"  var({name})", file=sys.stderr)
    print("\nAn unresolved var() drops the declaration silently. Check keel's CHANGELOG for "
          "a rename and update the markup in the same commit as the version bump.", file=sys.stderr)
    return 1


def check_token_values():
    """Membership is the easy half. A token that keeps its name and changes its value passes
    every check above while the page renders differently — which is what --accent-on-midnight
    did, and what keel 0.5.0 does to three state tokens. So the resolved values are recorded
    and a move has to arrive as a reviewed diff to that file rather than silently."""
    if not os.path.exists(SNAPSHOT):
        print("no recorded token values; run snapshot-keel-tokens.py", file=sys.stderr)
        return 1

    with open(SNAPSHOT, encoding="utf-8") as handle:
        recorded = json.load(handle)
    with open(BUNDLE, encoding="utf-8") as bundle_handle, open(PAGE, encoding="utf-8") as page_handle:
        current = resolve_used(bundle_handle.read(), page_handle.read())

    drifted = []
    for name in sorted(set(recorded) | set(current)):
        for scheme in ("light", "dark"):
            was = (recorded.get(name) or {}).get(scheme)
            now = (current.get(name) or {}).get(scheme)
            if was != now:
                drifted.append((name, scheme, was, now))

    if not drifted:
        print(f"all {len(current)} token values match what this repo recorded")
        return 0

    print(f"\n{len(drifted)} keel token value(s) moved without the recorded file changing:",
          file=sys.stderr)
    for name, scheme, was, now in drifted:
        print(f"  {name} ({scheme}): {was} -> {now}", file=sys.stderr)
    print("\nA token keeping its name while changing its value is silent everywhere else. If "
          "this is intended, run snapshot-keel-tokens.py in the SAME commit as the keel bump "
          "so the change is reviewed rather than absorbed.", file=sys.stderr)
    return 1


def check_bundle_version():
    """The bundle is a gitignored build output, so nothing keeps it current.

    Being ignored stops it being committed AND stops it being maintained: a stale copy is
    invisible to git status, survives every rebuild, and is read by every check here. Mine
    sat two minor versions behind for weeks in the main checkout while all the real work
    happened in worktrees, and it reported 42 contrast failures against a page that has 0.

    keel stamps the version into line one of every published bundle, so this costs one
    comparison. The deploy workflow already asserted it; nothing asserted it locally, which
    is precisely where the stale copy lived.
    """
    with open(LOCKFILE, encoding="utf-8") as handle:
        packages = json.load(handle).get("packages", {})
    pinned = next((meta.get("version") for name, meta in packages.items() if "keel" in name), None)

    with open(BUNDLE, encoding="utf-8") as handle:
        first = handle.readline().strip()
    # Anchor on the version characters themselves. Stamp formats have differed across
    # releases -- "keel v0.4.3 - generated" now, "keel v0.2.1. Refresh with" when the
    # bundle was fetched by script -- and a greedy match swallows the trailing period,
    # which would then never equal the lockfile's version.
    match = re.search(r"keel v([0-9][0-9A-Za-z.\-]*[0-9A-Za-z])", first)

    if pinned is None:
        print("no keel version pinned in package-lock.json", file=sys.stderr)
        return 1
    if match is None:
        print(f"\nThe stylesheet carries no version stamp. Its first line is:\n  {first}\n\n"
              "keel stamps every PUBLISHED bundle; a working copy taken from keel's source "
              "tree says 'unversioned working copy' instead. Run update-keel.sh, which "
              "installs the pinned release.", file=sys.stderr)
        return 1
    if match.group(1) != pinned:
        print(f"\nStale stylesheet: measuring against keel v{match.group(1)} while the "
              f"lockfile pins {pinned}.\n\nkeel.bundle.css is a gitignored build output, so "
              "nothing updates it for you. Run update-keel.sh.", file=sys.stderr)
        return 1

    print(f"stylesheet is keel v{pinned}, matching the lockfile")
    return 0


def main():
    for path in (PAGE, BUNDLE):
        if not os.path.exists(path):
            print(f"error: {path} is missing; run update-keel.sh and build.py first", file=sys.stderr)
            return 2

    stale = check_bundle_version()
    if stale:
        return stale

    bundle = classes_in_bundle()
    inventory = classes_in_inventory()
    known = bundle | (inventory or set())
    used = used_classes(known) - PAGE_OWNED

    if inventory is None:
        print(f"checked against the vendored bundle only: {len(bundle)} classes defined")
    else:
        # Compare like with like. The local parse only sees keel-* names, so counting the
        # whole inventory against it would show a difference that is a namespace artefact
        # rather than a disagreement.
        print(f"checked against keel's classes.json ({len(inventory)}) "
              f"corroborated by the bundle ({len(bundle)})")
        # Only the classes the page depends on matter here.
        disputed = sorted(name for name in used if (name in inventory) != (name in bundle))
        if disputed:
            print(f"\n{len(disputed)} class(es) the page uses are described inconsistently by "
                  f"keel's own artifacts:", file=sys.stderr)
            for name in disputed:
                where = "classes.json but NOT the stylesheet" if name in inventory \
                    else "the stylesheet but NOT classes.json"
                print(f"  {name}: in {where}", file=sys.stderr)
            print("\nOne of those two is wrong and this cannot tell which, so it is not safe to "
                  "deploy on either. Report it to keel.", file=sys.stderr)
            return 1

    # The stylesheet is what the browser loads, so it decides whether the page renders.
    missing = sorted(used - bundle)
    if not missing:
        print("every keel class the page uses resolves")
        return check_tokens()

    print(f"\n{len(missing)} keel class(es) used by the page are not defined by this keel version:", file=sys.stderr)
    for name in missing:
        print(f"  {name}", file=sys.stderr)
    print("\nkeel ships no aliases, so these render unstyled. Check its CHANGELOG for the "
          "rename and update build.py in the same commit as the version bump.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
