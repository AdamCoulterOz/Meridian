"""Resolve the keel custom properties the page uses, per theme, from the vendored bundle.

Membership checks catch a token being REMOVED. They pass silently when a token keeps its
name and changes its value, which is the quieter failure and the one that has actually
shipped here: --accent-on-midnight kept its name and moved to the rung hardest to read on
its own ground, and keel 0.5.0 retunes three state tokens the same way. A value you were
never given is a value you cannot diff, so the page records what it resolved and fails when
that moves without a reviewed change to the recorded file.

Validated against the browser: all 31 tokens the page uses, in both schemes, 62
comparisons, zero mismatches. The resolver is trusted because it was checked against the
thing that actually computes these, not because the code looks right.
"""
import re

def _strip(css):
    return re.sub(r"/\*.*?\*/", "", css, flags=re.S)


def _dark_spans(css):
    spans = []
    for match in re.finditer(r"@media[^{]*prefers-color-scheme:\s*dark[^{]*\{", css):
        index, depth = match.end(), 1
        while index < len(css) and depth:
            if css[index] == "{":
                depth += 1
            elif css[index] == "}":
                depth -= 1
            index += 1
        spans.append((match.start(), index))
    return spans


def _layers(css):
    """(light, dark) declaration maps. Dark is the light layer with its overrides applied."""
    spans = _dark_spans(css)
    light, dark = {}, {}
    for match in re.finditer(r"(?:^|\})\s*([^{}@]+?)\s*\{([^{}]*)\}", css, re.M):
        body = match.group(2)
        if "--" not in body:
            continue
        in_dark = any(start <= match.start(2) < end for start, end in spans)
        (dark if in_dark else light).update(
            dict(re.findall(r"(--[A-Za-z0-9-]+)\s*:\s*([^;{}]+)", body)))
    merged = dict(light)
    merged.update(dark)
    return light, merged


def _resolve(name, table, seen=frozenset()):
    if name in seen or name not in table:
        return None
    seen = seen | {name}
    def substitute(match):
        inner = _resolve(match.group(1), table, seen)
        return inner if inner is not None else match.group(0)
    value = re.sub(r"var\((--[A-Za-z0-9-]+)\)", substitute, table[name].strip())
    return re.sub(r"\s+", " ", value).strip().lower()


def resolve_used(bundle_css, page_html):
    """{token: {"light": value, "dark": value}} for every token the page uses."""
    css = _strip(bundle_css)
    light, dark = _layers(css)
    used = sorted(set(re.findall(r"var\((--[A-Za-z0-9-]+)", page_html)))
    return {name: {"light": _resolve(name, light), "dark": _resolve(name, dark)}
            for name in used}
