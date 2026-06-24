#!/usr/bin/env python3
"""Transform Meridian.dc.html (a Keel/.dc.html design prototype) into a
standalone, runtime-free docs/index.html for GitHub Pages.

It expands every <x-import>/<sc-if>, ports the Keel components to plain
HTML/CSS, and inlines the full token system (light + dark) driven by the OS
via prefers-color-scheme. Copy is preserved verbatim (no fact reconciliation).

Usage:  python3 docs/redesigned/build.py   (requires: pip install beautifulsoup4)
Reads Meridian.dc.html alongside this script and writes ../index.html."""

import os
import re
from bs4 import BeautifulSoup

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "Meridian.dc.html")
OUT = os.path.normpath(os.path.join(HERE, "..", "index.html"))

GH_PATH = "M9 19c-5 1.5-5-2.5-7-3m14 6v-3.9a3.4 3.4 0 0 0-.9-2.6c3-.3 6.2-1.5 6.2-6.7a5.2 5.2 0 0 0-1.5-3.6 4.9 4.9 0 0 0-.1-3.6s-1.2-.4-3.9 1.5a13.4 13.4 0 0 0-7 0C5.6 1.7 4.4 2.1 4.4 2.1a4.9 4.9 0 0 0-.1 3.6A5.2 5.2 0 0 0 2.8 9.3c0 5.2 3.2 6.4 6.2 6.7a3.4 3.4 0 0 0-.9 2.5V22"
LANG_DOT = {"XML": "#e37933", "YAML": "#cb4b16", "JSON": "#bcae3b", "INI": "var(--accent)",
            "Bash": "#4eaa25", "JavaScript": "#e6b800", "CSS": "#563d7c", "HTML": "#e34c26"}

# leading corner glyphs: IDE (code) for editor chrome, prompt (>_) for terminal chrome
IDE_ICON = ('<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
            '<path d="m16 18 6-6-6-6"/><path d="m8 6-6 6 6 6"/></svg>')
TERMINAL_ICON = ('<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                 'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
                 '<path d="m4 17 6-6-6-6"/><path d="M12 19h8"/></svg>')

# inline-code chip style, matching the rest of the page
INLINE_CODE = ("font-family:'Fira Code',monospace;font-size:.86em;background:var(--surface-sunken);"
               "padding:1px 6px;border-radius:6px;color:var(--text-primary)")

# value -> generated hover class name
hover_classes = {}


def hover_class_for(value):
    if value not in hover_classes:
        hover_classes[value] = f"hv{len(hover_classes)}"
    return hover_classes[value]


def add_class(tag, cls):
    existing = tag.get("class", [])
    if isinstance(existing, str):
        existing = existing.split()
    existing.append(cls)
    tag["class"] = existing


def merge_style(base, extra):
    if not extra:
        return base
    base = base.rstrip()
    if base and not base.endswith(";"):
        base += ";"
    return base + extra


# ---- component builders (return single-rooted HTML strings, no surrounding ws) ----

def build_codeblock(tag):
    chrome = tag.get("chrome", "editor")
    theme = "auto"  # all code blocks follow the page theme (light/dark)
    filename = tag.get("filename", "")
    lang = tag.get("lang")
    inner = tag.decode_contents()
    if chrome == "terminal":
        header = (
            '<div class="keel-cb__bar">'
            f'<span class="keel-cb__lead">{TERMINAL_ICON}</span>'
            + (f'<span class="keel-cb__title">{filename}</span>' if filename else "")
            + "</div>"
        )
    else:
        dot = LANG_DOT.get(lang, "var(--accent)") if lang else None
        dot_html = f'<span class="keel-cb__dot" style="background:{dot}"></span>' if dot else ""
        lang_html = f'<span class="keel-cb__lang">{lang}</span>' if lang else ""
        header = (
            '<div class="keel-cb__tabs">'
            f'<span class="keel-cb__lead">{IDE_ICON}</span>'
            f'<span class="keel-cb__tab keel-cb__tab--active">{dot_html}{filename}</span>'
            f"{lang_html}</div>"
        )
    return (
        f'<div class="keel-cb keel-cb--{theme}">{header}'
        f'<div class="keel-cb__scroll"><pre class="keel-cb__pre"><code>{inner}</code></pre></div></div>'
    )


def build_githubstars(tag):
    repo = tag.get("repo", "")
    stars = tag.get("stars", "")
    variant = tag.get("variant", "minimal")
    size = tag.get("size", "md")
    href = tag.get("href") or (f"https://github.com/{repo}" if repo else "#")
    aria = f"{repo} on GitHub — {stars} stars" if repo else f"{stars} GitHub stars"
    octo = (
        f'<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
        f'stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
        f'<path d="{GH_PATH}"/></svg>'
    )
    return (
        f'<a class="keel-ghb keel-ghb--{variant} keel-ghb--{size}" href="{href}" aria-label="{aria}">'
        f'<span class="keel-ghb__ico">{octo}</span><span class="keel-ghb__count">{stars}</span></a>'
    )


def build_badge(tag):
    tone = tag.get("tone", "accent")
    dot = tag.get("dot") in ("true", "", "dot")
    style = merge_style("", tag.get("style", ""))
    inner = tag.decode_contents()
    dot_html = '<span class="keel-badge__dot"></span>' if dot else ""
    style_attr = f' style="{style}"' if style else ""
    return f'<span class="keel-badge keel-badge--{tone}"{style_attr}>{dot_html}{inner}</span>'


def build_button(tag):
    variant = tag.get("variant", "primary")
    size = tag.get("size", "lg")
    pill = tag.get("pill") in ("true", "")
    href = tag.get("href", "#")
    style = tag.get("style", "")
    inner = tag.decode_contents()
    cls = f"keel-btn keel-btn--{variant} keel-btn--{size}" + (" keel-btn--pill" if pill else "")
    style_attr = f' style="{style}"' if style else ""
    return f'<a class="{cls}" href="{href}"{style_attr}>{inner}</a>'


def build_callout(tag):
    tone = tag.get("tone", "info")
    title = tag.get("title")
    inner = tag.decode_contents()
    title_html = f'<div class="keel-callout__title">{title}</div>' if title else ""
    return f'<div class="keel-callout keel-callout--{tone}">{title_html}<div class="keel-callout__body">{inner}</div></div>'


def build_card(tag):
    variant = tag.get("variant", "outlined")
    interactive = tag.get("interactive") in ("true", "")
    padding = tag.get("padding")
    style = tag.get("style", "")
    inner = tag.decode_contents()
    cls = f"keel-card keel-card--{variant}"
    if interactive:
        cls += " keel-card--interactive"
    if padding == "lg":
        cls += " keel-card--lg"
    style_attr = f' style="{style}"' if style else ""
    return f'<div class="{cls}"{style_attr}>{inner}</div>'


def build_tooltip(tag):
    label = tag.get("label", "")
    inner = tag.decode_contents()
    return f'<span class="keel-tip">{inner}<span class="keel-tip__pop">{label}</span></span>'


def transform_import(tag):
    comp = tag.get("component")
    gcomp = tag.get("component-from-global-scope", "")
    if comp == "CodeBlock":
        html = build_codeblock(tag)
    elif comp == "GitHubStars":
        html = build_githubstars(tag)
    elif gcomp.endswith(".Badge"):
        html = build_badge(tag)
    elif gcomp.endswith(".Button"):
        html = build_button(tag)
    elif gcomp.endswith(".Callout"):
        html = build_callout(tag)
    elif gcomp.endswith(".Card"):
        html = build_card(tag)
    elif gcomp.endswith(".Tooltip"):
        html = build_tooltip(tag)
    else:
        # unknown -> unwrap children
        tag.unwrap()
        return
    frag = BeautifulSoup(html, "html.parser")
    root = frag.find()
    tag.replace_with(root)


def main():
    with open(SRC, encoding="utf-8") as f:
        raw = f.read()
    soup = BeautifulSoup(raw, "html.parser")
    xdc = soup.find("x-dc")

    # drop prototype scaffolding inside x-dc
    for sel in ("helmet", "template"):
        for el in xdc.find_all(sel):
            el.decompose()

    # sc-if: keep content; special-case theme-icon conditionals
    for scif in xdc.find_all("sc-if"):
        val = scif.get("value", "")
        if "isLight" in val:
            span = soup.new_tag("span")
            span["class"] = ["theme-icon", "theme-icon--light"]
            for child in list(scif.contents):
                span.append(child.extract())
            scif.replace_with(span)
        elif "isDark" in val:
            span = soup.new_tag("span")
            span["class"] = ["theme-icon", "theme-icon--dark"]
            for child in list(scif.contents):
                span.append(child.extract())
            scif.replace_with(span)
        else:
            scif.unwrap()

    # x-import: innermost-first
    while True:
        imports = xdc.find_all("x-import")
        leaves = [i for i in imports if i.find("x-import") is None]
        if not leaves:
            break
        for tag in leaves:
            transform_import(tag)

    # theme toggle removed — the site follows the OS theme (prefers-color-scheme) only
    for el in xdc.find_all(attrs={"onclick": True}):
        if "toggleTheme" in el.get("onclick", ""):
            el.decompose()

    # style-hover -> generated hover classes
    for el in xdc.find_all(attrs={"style-hover": True}):
        value = el["style-hover"]
        add_class(el, hover_class_for(value))
        del el["style-hover"]

    # strip leftover prototype attrs
    for el in xdc.find_all(True):
        for attr in ("data-screen-label", "hint-size", "hint-placeholder-val"):
            if attr in el.attrs:
                del el[attr]

    # responsive nav: add a hamburger + a frosted flyout drawer mirroring the section links.
    # The drawer is appended at body level (not inside <nav>) because the nav's backdrop-filter
    # would otherwise become the containing block for its position:fixed children.
    nav = xdc.find("nav")
    if nav:
        inner = nav.find("div")
        child_divs = inner.find_all("div", recursive=False)
        if len(child_divs) >= 2:
            links_div, right_div = child_divs[0], child_divs[1]
            add_class(links_div, "nav-links")
            burger = BeautifulSoup(
                '<button id="nav-burger" class="nav-burger" aria-label="Open navigation menu"'
                ' aria-expanded="false" aria-controls="nav-drawer" onclick="toggleNav()">'
                '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                ' stroke-width="2" stroke-linecap="round" aria-hidden="true">'
                '<path d="M3 6h18M3 12h18M3 18h18"/></svg></button>', "html.parser")
            right_div.append(burger.find())
            # the delegated click handler closes the drawer, so no per-link onclick needed
            drawer_links = "".join(
                f'<a href="{a.get("href", "#")}">{a.decode_contents()}</a>'
                for a in links_div.find_all("a"))
            # short single-word doc names for the menu + a desktop "Docs" dropdown
            short = {"Read the README": "README", "Architecture notes": "Architecture",
                     "Schema contract": "Schema"}
            doc_html = ""
            start_sec = xdc.find("section", id="start")
            if start_sec is not None:
                docs = [a for a in start_sec.find_all("a") if a.get("href", "").startswith("http")]
                if docs:
                    items = "".join(
                        f'<a href="{a.get("href")}" target="_blank" rel="noopener">'
                        f'{short.get(a.get_text(strip=True), a.get_text(strip=True))}</a>'
                        for a in docs)
                    doc_html = '<div class="nav-drawer__sep"></div>' + items
                    dropdown = (
                        '<div class="nav-docs"><button class="nav-docs__btn" aria-haspopup="true">Docs'
                        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                        'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
                        '<path d="m6 9 6 6 6-6"/></svg></button>'
                        f'<div class="nav-docs__menu">{items}</div></div>')
                    links_div.append(BeautifulSoup(dropdown, "html.parser").find())
            overlay = (
                '<div id="nav-scrim" class="nav-scrim" onclick="closeNav()" aria-hidden="true"></div>'
                '<aside id="nav-drawer" class="nav-drawer" aria-hidden="true" aria-label="Site navigation">'
                '<div class="nav-drawer__head">'
                '<span style="font-weight:700;font-size:16px;letter-spacing:-.01em">Meridian</span>'
                '<button class="nav-close" aria-label="Close navigation menu" onclick="closeNav()">'
                '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                ' stroke-width="2" stroke-linecap="round" aria-hidden="true">'
                '<path d="M18 6 6 18M6 6l12 12"/></svg></button></div>'
                f'{drawer_links}{doc_html}</aside>')
            for node in list(BeautifulSoup(overlay, "html.parser").children):
                xdc.append(node)

    # quick start: caption each command block with a numbered step heading + one-liner
    start = xdc.find("section", id="start")
    if start:
        # widen the breathing room between the stacked step groups in the left column
        qs_grid = start.find("div", style=lambda s: s and "grid-template-columns:1fr .82fr" in s)
        if qs_grid is not None:
            left_col = qs_grid.find("div", recursive=False)
            if left_col is not None and left_col.get("style"):
                left_col["style"] = left_col["style"].replace("gap:18px", "gap:36px")
        steps = {
            "install · global tool": ("1", "Install the CLI",
                "Add Meridian as a global .NET tool — the "
                f'<code style="{INLINE_CODE}">meridian</code> command lands on your PATH.'),
            "meridian merge · three-way": ("2", "Run a merge by hand",
                "Invoke a three-way structural merge directly — handy for trying a schema "
                "before wiring it into Git."),
            ".gitattributes": ("3", "Wire it into Git",
                "Register the merge and diff driver, then opt files in so every clone "
                "merges and diffs structurally."),
        }
        for cb in start.select(".keel-cb"):
            title_el = cb.select_one(".keel-cb__title") or cb.select_one(".keel-cb__tab")
            if title_el is None:
                continue
            key = title_el.get_text(strip=True)
            if key not in steps:
                continue
            num, head, desc = steps[key]
            group = soup.new_tag("div")
            cb.wrap(group)
            heading = BeautifulSoup(
                '<div style="margin:0 0 11px">'
                '<h3 style="margin:0 0 3px;font-size:15px;font-weight:700;letter-spacing:-.01em;'
                f'color:var(--text-primary)"><span style="color:var(--accent)">{num}</span> · {head}</h3>'
                '<p style="margin:0;font-size:14px;line-height:1.5;color:var(--text-secondary)">'
                f'{desc}</p></div>', "html.parser").find()
            cb.insert_before(heading)

        # "Minimal schema shape" becomes step 4: unbox it, renumber the heading, and move it
        # inline below step 3 in a single column with the same step spacing
        aside = start.find("aside")
        if aside is not None and left_col is not None:
            h3 = aside.find("h3")
            p = aside.find("p")
            box_cb = aside.find(class_="keel-cb")
            aside.name = "div"
            aside.attrs = {}
            if box_cb is not None and box_cb.parent is not aside:
                box_cb.parent.unwrap()
            if h3 is not None:
                h3["style"] = ("margin:0 0 3px;font-size:15px;font-weight:700;"
                               "letter-spacing:-.01em;color:var(--text-primary)")
                h3.clear()
                for node in list(BeautifulSoup(
                        '<span style="color:var(--accent)">4</span> · Minimal schema shape',
                        "html.parser").contents):
                    h3.append(node)
            if p is not None:
                p["style"] = "margin:0;font-size:14px;line-height:1.5;color:var(--text-secondary)"
            if h3 is not None and p is not None:
                grp = soup.new_tag("div")
                grp["style"] = "margin:0 0 11px"
                h3.wrap(grp)
                grp.append(p.extract())
            left_col.append(aside)                 # move schema in as step 4
            if qs_grid is not None:
                qs_grid["style"] = "display:block"  # collapse the two-column layout to one

    # card groups that become swipeable carousels on narrow screens
    def make_carousel(sec_id, match, extra_class=None):
        sec = xdc.find("section", id=sec_id)
        if sec is None:
            return
        grid = sec.find("div", style=match)
        if grid is not None:
            add_class(grid, "carousel")
            if extra_class:
                add_class(grid, extra_class)

    make_carousel("constraints", lambda s: s and "repeat(2,1fr)" in s)
    make_carousel("formats", lambda s: s and "repeat(3,1fr)" in s)

    # nested formats: stronger contrast between the XML / JSON / JSON nesting levels
    nested = xdc.find("section", id="nested")
    if nested is not None:
        mid = nested.find("div", style=lambda s: s and "border-radius:11px" in s)
        if mid is not None:
            mid["style"] = (mid["style"].replace("var(--surface-subtle)", "var(--surface-sunken)")
                            .replace("var(--border-subtle)", "var(--border-default)"))
        # inner box: neutralize the blue so all three nesting levels read as equally important
        inner = nested.find("div", style=lambda s: s and "border-radius:9px" in s)
        if inner is not None:
            html = (str(inner)
                    .replace("border:1px solid rgba(11,95,255,.28)", "border:1px solid var(--border-default)")
                    .replace("background:var(--accent-subtle)", "background:var(--surface-card)")
                    .replace("rgba(11,95,255,.16)", "var(--border-subtle)")
                    .replace("background:var(--accent);color:#fff", "background:var(--accent-subtle);color:var(--accent)")
                    .replace("rgba(11,95,255,.22)", "var(--border-subtle)")
                    .replace("var(--accent-hover)", "var(--text-secondary)"))
            inner.replace_with(BeautifulSoup(html, "html.parser").find())
    # schema: turn the annotation cards into a step-through walkthrough that highlights the
    # matching config line(s) as you move prev/next (annotation N <-> config line group N)
    schema = xdc.find("section", id="schema")
    if schema is not None:
        code = schema.select_one(".keel-cb__pre code")
        if code is not None:
            line_group = {}
            for g, idxs in {0: [5, 6, 7], 1: [13, 14, 15], 2: [16, 17, 18], 3: [19, 20]}.items():
                for ix in idxs:
                    line_group[ix] = g
            for ix, span in enumerate(code.find_all("span", recursive=False)):
                if ix in line_group:
                    add_class(span, "anno-line")
                    span["data-anno"] = str(line_group[ix])
        anno = schema.find("div", style=lambda s: s and "display:grid" in s and "gap:12px" in s)
        grid = schema.find("div", style=lambda s: s and "1.05fr" in s)
        code_cb = grid.find(class_="keel-cb") if grid is not None else None
        if anno is not None and grid is not None and code_cb is not None:
            articles = anno.find_all("article", recursive=False)
            # code wrapper holds the code block plus the tooltip overlays (positioned by JS)
            codewrap = soup.new_tag("div")
            codewrap["class"] = ["schema-figure__code"]
            codewrap.append(code_cb.extract())
            figure = soup.new_tag("div")
            figure["class"] = ["schema-figure"]
            figure.append(codewrap)
            for i, art in enumerate(articles):
                art["style"] = ""
                # make every annotation label the same accent colour (orderedChildren was signal/green)
                for el in art.find_all(style=lambda v: v and ("signal" in v or "#8aa61f" in v)):
                    el["style"] = (el["style"].replace("var(--signal-text)", "var(--accent)")
                                   .replace("var(--signal-700)", "var(--accent)").replace("#8aa61f", "var(--accent)"))
                art["class"] = ["walk-panel"]
                art["data-anno"] = str(i)
                figure.append(art.extract())   # panels are figure children -> right rail on wide
                band = soup.new_tag("div")      # one focus band per group, overlaying its lines
                band["class"] = ["schema-band"]
                band["data-anno"] = str(i)
                codewrap.append(band)
            grid.replace_with(figure)

    # merge model: pair each pipeline step card with its explanation panel into one slide,
    # shown as a carousel on narrow; the two original grids stay as-is for desktop
    model = xdc.find("section", id="model")
    if model is not None:
        pipeline = model.find("div", style=lambda s: s and "repeat(4,1fr)" in s)
        explain = model.find("div", style=lambda s: s and "repeat(2,1fr)" in s)
        if pipeline is not None and explain is not None:
            pipe = pipeline.find_all("article", recursive=False)
            exp = explain.find_all("article", recursive=False)
            combo = soup.new_tag("div")
            combo["class"] = ["carousel", "model-steps"]
            combo["style"] = "gap:18px"   # horizontal space between steps
            for i in range(min(len(pipe), len(exp))):
                p_art = BeautifulSoup(str(pipe[i]), "html.parser").find()
                e_art = BeautifulSoup(str(exp[i]), "html.parser").find()
                # lift the big number tile up into the pipeline header, left of the step label
                e_divs = e_art.find_all("div", recursive=False)
                num_tile = e_divs[0].extract() if e_divs else None
                e_art["style"] = "padding:20px 24px 24px"   # explanation: heading + text, full width
                header = p_art.find("header")
                if header is not None and num_tile is not None:
                    htext = soup.new_tag("div")
                    for c in list(header.contents):
                        htext.append(c.extract())
                    # the number tile already shows the step number; drop the "01 · " from the label
                    eyebrow = htext.find("span")
                    if eyebrow is not None:
                        eyebrow.string = re.sub(r"^\s*\d+\W+", "", eyebrow.get_text())
                    header["style"] = "display:flex;align-items:center;gap:13px;padding:16px 16px 8px;border-bottom:0"
                    header.append(num_tile)
                    header.append(htext)
                slide = soup.new_tag("div")
                slide["class"] = ["model-slide"]
                slide.append(p_art)
                slide.append(e_art)
                combo.append(slide)
            add_class(pipeline, "wide-only")
            add_class(explain, "wide-only")
            explain.insert_after(combo)
            # number tiles: soft accent tile instead of a dark navy block (too dark in light mode)
            for tile in model.find_all("div", style=lambda s: s and "var(--surface-midnight)" in s):
                add_class(tile, "step-num")
                tile["style"] = (tile["style"].replace("background:var(--surface-midnight);", "")
                                 .replace("color:#9fb6ff;", "").replace("color:#9fb6ff", ""))

    # hero worked-example: lift the two editors out of the card and swipe between them on narrow
    top = xdc.find("header", id="top")
    if top is not None:
        box = top.find("div", style=lambda s: s and "margin-top:40px" in s and "border-radius:18px" in s)
        if box is not None:
            add_class(box, "hero-ex")
            head = box.find("div", recursive=False)
            if head is not None:
                add_class(head, "hero-ex__head")
            media = box.find("div", style=lambda s: s and "1fr auto 1fr" in s)
            if media is not None:
                add_class(media, "carousel")
                add_class(media, "hero-ex__media")
                for d in media.find_all("div", recursive=False):
                    if d.get_text(strip=True) == "resolves to":
                        add_class(d, "hero-ex__arrow")

        # hero stat grid -> CSS-only horizontal accordion on narrow (stays a 4-up grid on desktop)
        dl = top.find("dl")
        if dl is not None:
            items = []
            for cell in dl.find_all("div", recursive=False):
                dt, dd = cell.find("dt"), cell.find("dd")
                if dt is not None and dd is not None:
                    items.append((dt.decode_contents(), dd.decode_contents()))
            if items:
                parts = ['<div class="stat-accordion">']
                for i, (k, v) in enumerate(items):
                    checked = " checked" if i == 0 else ""
                    parts.append(f'<input type="radio" name="stat" id="stat{i}" class="stat-radio"{checked}>')
                    parts.append(f'<label for="stat{i}" class="stat-item">'
                                 f'<span class="stat-k">{k}</span><span class="stat-v">{v}</span></label>')
                parts.append("</div>")
                dl.replace_with(BeautifulSoup("".join(parts), "html.parser").find())

        # hero install command -> a proper terminal CodeBlock (was an inline chip)
        chip = None
        for sp in top.find_all("span"):
            if "dotnet tool install" in sp.get_text() and sp.find("span") is not None:
                chip = sp
                break
        if chip is not None:
            term = (
                '<div class="keel-cb keel-cb--auto" style="flex:0 0 100%;max-width:520px;margin-top:2px">'
                '<div class="keel-cb__bar">'
                f'<span class="keel-cb__lead">{TERMINAL_ICON}</span>'
                '<span class="keel-cb__title">install</span></div>'
                '<div class="keel-cb__scroll"><pre class="keel-cb__pre"><code>'
                '<span style="display:block"><span style="color:var(--code-keyword)">dotnet</span>'
                ' tool install --global MeridianGit</span>'
                '</code></pre></div></div>')
            chip.replace_with(BeautifulSoup(term, "html.parser").find())

    content = xdc.decode_contents()
    # footer keeps the midnight band in both themes (tiles/tooltip use neutral --surface-midnight)
    content = content.replace(
        'background:var(--surface-midnight);color:rgba(255,255,255,.7)',
        'background:var(--surface-footer);color:rgba(255,255,255,.7)')
    # GitHub stars: honest static fallback (the repo's real count); a runtime fetch keeps it live
    content = content.replace('keel-ghb__count">128<', 'keel-ghb__count">0<')
    content = content.replace('128 stars', '0 stars')

    hover_css = "\n".join(f".{cls}:hover{{{val}}}" for val, cls in hover_classes.items())

    head = HEAD.replace("/*__HOVER__*/", hover_css)
    final = head + content + "\n" + SCRIPT + TAIL
    # bs4/html.parser lowercases attribute names; restore camelCase SVG attrs
    for lc, cc in (("viewbox=", "viewBox="), ("preserveaspectratio=", "preserveAspectRatio="),
                   ("gradientunits=", "gradientUnits=")):
        final = final.replace(lc, cc)

    with open(OUT, "w", encoding="utf-8") as f:
        f.write(final)
    print(f"wrote {OUT} ({len(final)} bytes)")


FAVICON = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Crect width='32' height='32' rx='7' fill='%23ffffff'/%3E%3Ccircle cx='16' cy='16' r='10' fill='none' stroke='%23111820' stroke-width='2'/%3E%3Cpath d='M16 5v22M7 16h18' stroke='%23335fd0' stroke-width='2' stroke-linecap='round'/%3E%3Cellipse cx='16' cy='16' rx='5' ry='10' fill='none' stroke='%2319736a' stroke-width='1.7'/%3E%3Ccircle cx='16' cy='16' r='2.4' fill='%23335fd0'/%3E%3C/svg%3E"

LIGHT_VARS = """
  --font-sans:'Hanken Grotesk',-apple-system,BlinkMacSystemFont,'Segoe UI',system-ui,sans-serif;
  --font-mono:'Fira Code',ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;
  --fw-medium:500;--fw-semibold:600;
  --dur-fast:.14s;--ease-out:cubic-bezier(.22,1,.36,1);
  --radius-md:10px;--radius-lg:12px;--radius-xl:18px;
  --ring:0 0 0 3px var(--accent-subtle);
  --hairline-top:inset 0 1px 0 rgba(255,255,255,.06);
  --shadow-sm:0 1px 2px rgba(11,11,12,.05);
  --shadow-md:0 8px 24px rgba(11,11,12,.10),0 2px 6px rgba(11,11,12,.04);
  --surface-base:#FFFFFF;--surface-subtle:#F5F5F7;--surface-sunken:#EBEBEF;--surface-card:#FFFFFF;
  --surface-overlay:rgba(255,255,255,.72);--surface-midnight:#0A1430;--surface-footer:#0A1430;
  --text-primary:#1D1D1F;--text-secondary:#6E6E73;--text-tertiary:#86868B;
  --border-subtle:#DEDEDF;--border-default:#C8C8CE;--border-strong:#B0B0B8;
  --accent:#0B5FFF;--accent-hover:#094AD1;--accent-subtle:#ECF2FF;
  --success:#1FA855;--success-subtle:#E9F9EE;
  --signal:#C6F23C;--signal-500:#C6F23C;--signal-300:rgba(198,242,60,.55);--signal-subtle:rgba(198,242,60,.18);
  --signal-text:#7FAE00;--signal-700:#5E8A00;--signal-on:#0A1430;
  --warning:#C0863B;
  --code-bg:#0B0B0C;--code-fg:#E6E6EA;
  --code-keyword:#1D4ED8;--code-string:#15803D;--code-type:#0E7490;--code-number:#B45309;--code-comment:#6E7781;
  --gray-400:#9CA3AF;--blue-300:#9DBBFF;--blue-400:#7AA2FF;--blue-700:#1D4ED8;--blue-800:#1E40AF;
  --green-600:#1FA855;--green-700:#15803D;--amber-700:#B45309;
"""

DARK_VARS = """
  --surface-base:#181A1B;--surface-subtle:#1C1E1F;--surface-sunken:#232628;--surface-card:#1E2021;
  --surface-overlay:rgba(24,26,27,.72);--surface-midnight:#141617;
  --text-primary:#D5D1CC;--text-secondary:#A2998D;--text-tertiary:#7E776C;
  --border-subtle:#393E40;--border-default:#4A4F51;--border-strong:#5C6164;
  --accent:#3B78FF;--accent-hover:#5E92FF;--accent-subtle:rgba(59,120,255,.18);
  --success:#30D158;--success-subtle:rgba(48,209,88,.16);
  --signal-text:#C6F23C;--signal-700:#C6F23C;--signal-subtle:rgba(198,242,60,.16);--signal-300:rgba(198,242,60,.40);
  --code-bg:#121415;--code-fg:#E3DFD9;--code-keyword:#84A9FF;--code-string:#5FD49A;--code-type:#6FD4E0;--code-number:#E5A66A;--code-comment:#7E776B;
  --shadow-sm:0 1px 2px rgba(0,0,0,.4);
  --shadow-md:0 10px 30px rgba(0,0,0,.5),0 3px 8px rgba(0,0,0,.4);
"""

CB_DARK = """
  --cb-bg:var(--code-bg);--cb-fg:var(--code-fg);
  --cb-tabbar-bg:rgba(255,255,255,.035);--cb-line:rgba(255,255,255,.08);--cb-tab-active-bg:var(--code-bg);
  --cb-meta:#8A8279;--cb-ln:var(--code-comment);
  --cb-shadow:var(--hairline-top),var(--shadow-md),inset 0 0 0 1px rgba(255,255,255,.07);
  --cb-c:var(--code-comment);--cb-k:var(--code-keyword);--cb-s:var(--code-string);--cb-n:var(--code-number);--cb-t:var(--code-type);--cb-f:#d2a8ff;
"""

BASE_CSS = """
  *{box-sizing:border-box}
  html{scroll-behavior:smooth}
  header[id],section[id]{scroll-margin-top:80px}
  body{margin:0;background:var(--surface-base);color:var(--text-primary);font-family:var(--font-sans);
    font-size:17px;-webkit-font-smoothing:antialiased;text-rendering:optimizeLegibility;
    transition:background-color .25s var(--ease-out)}
  a{color:var(--accent)}
  ::selection{background:rgba(11,95,255,.16)}
  ::-webkit-scrollbar{height:9px;width:9px}
  ::-webkit-scrollbar-thumb{background:rgba(127,127,140,.4);border-radius:9px}
  code{font-family:var(--font-mono)}
  svg{flex-shrink:0}
  @media (prefers-reduced-motion:reduce){
    *{animation:none!important;transition:none!important;scroll-behavior:auto!important}
  }
  /* the paired merge-model carousel is built for narrow only; hidden on desktop */
  .model-steps{display:none}
  /* --- responsive nav: hamburger + frosted flyout drawer --- */
  .nav-burger{display:none;align-items:center;justify-content:center;width:36px;height:36px;border-radius:10px;border:none;background:transparent;color:var(--text-secondary);cursor:pointer;transition:background var(--dur-fast) var(--ease-out),color var(--dur-fast) var(--ease-out)}
  .nav-burger:hover{background:var(--surface-sunken);color:var(--text-primary)}
  .nav-scrim{position:fixed;inset:0;z-index:50;background:rgba(0,0,0,.38);opacity:0;visibility:hidden;transition:opacity .22s var(--ease-out),visibility .22s var(--ease-out)}
  .nav-drawer{position:fixed;top:0;right:0;bottom:0;z-index:51;width:min(82vw,300px);display:flex;flex-direction:column;gap:2px;padding:16px;
    background:var(--surface-overlay);backdrop-filter:saturate(180%) blur(20px);-webkit-backdrop-filter:saturate(180%) blur(20px);
    border-left:1px solid var(--border-subtle);box-shadow:var(--shadow-md);
    transform:translateX(100%);transition:transform .26s var(--ease-out)}
  .nav-drawer__head{display:flex;align-items:center;justify-content:space-between;padding:2px 6px 12px;margin-bottom:6px;border-bottom:1px solid var(--border-subtle)}
  .nav-drawer a{display:block;padding:12px;border-radius:10px;font-size:16px;font-weight:var(--fw-medium);color:var(--text-secondary);text-decoration:none;transition:background var(--dur-fast) var(--ease-out),color var(--dur-fast) var(--ease-out)}
  .nav-drawer a:hover{background:var(--surface-sunken);color:var(--text-primary)}
  .nav-drawer__sep{height:1px;background:var(--border-subtle);margin:8px 6px}

  /* model number tile — soft accent chip (both themes) */
  .step-num{background:var(--accent-subtle);color:var(--accent)}

  /* desktop "Docs" dropdown in the top nav */
  .nav-docs{position:relative;display:inline-flex}
  .nav-docs__btn{display:inline-flex;align-items:center;gap:4px;font:500 15px/1 var(--font-sans);color:var(--text-secondary);background:none;border:none;padding:0;cursor:pointer;transition:color var(--dur-fast) var(--ease-out)}
  .nav-docs:hover .nav-docs__btn,.nav-docs:focus-within .nav-docs__btn{color:var(--text-primary)}
  .nav-docs__menu{position:absolute;top:calc(100% + 12px);left:0;min-width:180px;display:flex;flex-direction:column;gap:2px;padding:6px;
    background:var(--surface-overlay);backdrop-filter:saturate(180%) blur(20px);-webkit-backdrop-filter:saturate(180%) blur(20px);
    border:1px solid var(--border-subtle);border-radius:12px;box-shadow:var(--shadow-md);z-index:50;
    opacity:0;visibility:hidden;transform:translateY(-4px);transition:opacity .16s var(--ease-out),transform .16s var(--ease-out),visibility .16s}
  .nav-docs__menu::before{content:"";position:absolute;top:-12px;left:0;right:0;height:12px}
  .nav-docs:hover .nav-docs__menu,.nav-docs:focus-within .nav-docs__menu{opacity:1;visibility:visible;transform:translateY(0)}
  .nav-docs__menu a{display:block;padding:8px 12px;border-radius:8px;font:500 14px/1.3 var(--font-sans);color:var(--text-secondary);text-decoration:none;white-space:nowrap;transition:background var(--dur-fast) var(--ease-out),color var(--dur-fast) var(--ease-out)}
  .nav-docs__menu a:hover{background:var(--surface-sunken);color:var(--text-primary)}
  .nav-close{display:inline-flex;align-items:center;justify-content:center;width:34px;height:34px;border-radius:9px;border:none;background:transparent;color:var(--text-secondary);cursor:pointer}
  .nav-close:hover{background:var(--surface-sunken);color:var(--text-primary)}
  @media (max-width:880px){
    .nav-links{display:none!important}
    .nav-burger{display:inline-flex}
    :root[data-nav-open] .nav-scrim{opacity:1;visibility:visible}
    :root[data-nav-open] .nav-drawer{transform:translateX(0)}
    /* tighter vertical rhythm between sections on small screens */
    section[id]{padding-top:48px!important;padding-bottom:48px!important}
    header[id]{padding-top:48px!important}
    /* ~70% tighter horizontal gutters on small screens (32px -> 10px) */
    [style*="max-width:1200px"]{padding-left:10px!important;padding-right:10px!important}
    section [style*="display:grid"]:not(.carousel),header [style*="display:grid"]{
      grid-template-columns:minmax(0,1fr)!important}
    section [style*="display:grid"]>*,header [style*="display:grid"]>*{min-width:0}
    .keel-cb{min-width:0}
    /* swipeable card carousel (constraints) */
    .carousel{display:flex!important;overflow-x:auto;scroll-snap-type:x mandatory;scroll-padding-left:10px;
      margin:0 -10px;padding:4px 10px 16px;-webkit-overflow-scrolling:touch;scrollbar-width:none}
    .carousel::-webkit-scrollbar{display:none}
    .carousel>*{flex:0 0 min(86%,300px);scroll-snap-align:start}
    /* merge model: paired step slide (pipeline card + its explanation) */
    .wide-only{display:none!important}
    .model-slide{display:flex;flex-direction:column;border:1px solid var(--border-subtle);border-radius:14px;overflow:hidden;background:var(--surface-card)}
    .model-slide>article{border:0!important;border-radius:0!important;box-shadow:none!important;margin:0;background:transparent!important}
    .model-slide>article:first-child{flex:0 0 auto;min-height:248px}
    .model-slide>article+article{flex:1 1 auto;border-top:1px solid var(--border-subtle)!important}
    /* hero worked-example: drop the box, swipe between the two editors */
    .hero-ex{background:transparent!important;border:0!important;border-radius:0!important;box-shadow:none!important;overflow:visible!important;margin-top:24px!important}
    .hero-ex__head{background:transparent!important;border-bottom:0!important;padding:0 0 14px!important}
    .hero-ex__media{padding:4px 10px 16px!important;align-items:stretch!important}
    .hero-ex__arrow{display:none!important}
    /* hero stat grid -> horizontal accordion: tap to expand one, others collapse to label strips */
    .stat-accordion{display:flex!important;grid-template-columns:none!important;margin-top:32px!important}
    .stat-item{flex:0 1 52px;align-items:center;justify-content:center;padding:16px 0;overflow:hidden;cursor:pointer;
      transition:flex-grow .35s var(--ease-out),padding .35s var(--ease-out)}
    .stat-item .stat-k{writing-mode:vertical-rl;letter-spacing:.12em;white-space:nowrap;color:var(--text-secondary)}
    .stat-item .stat-v{display:none}
    .stat-item:hover .stat-k{color:var(--text-primary)}
    .stat-radio:checked + .stat-item{flex-grow:1;align-items:flex-start;justify-content:flex-start;padding:16px 18px}
    .stat-radio:checked + .stat-item .stat-k{writing-mode:horizontal-tb;color:var(--text-tertiary)}
    .stat-radio:checked + .stat-item .stat-v{display:block}
    .stat-radio:focus-visible + .stat-item{box-shadow:inset 0 0 0 2px var(--accent)}
  }
"""

COMPONENT_CSS = """
  /* ---- CodeBlock (ported from components/CodeBlock.jsx) ---- */
  .keel-cb{font-family:var(--font-mono);font-size:13px;line-height:1.65;border-radius:var(--radius-lg);overflow:hidden;font-feature-settings:"liga" 1,"calt" 1;
    --cb-bg:var(--surface-base);--cb-fg:var(--text-primary);
    --cb-tabbar-bg:var(--surface-sunken);--cb-line:var(--border-default);--cb-tab-active-bg:var(--surface-base);
    --cb-meta:var(--text-tertiary);--cb-ln:var(--gray-400);
    --cb-shadow:inset 0 0 0 1px var(--border-subtle),var(--shadow-sm);
    --cb-c:#6e7781;--cb-k:var(--blue-700);--cb-s:var(--green-700);--cb-n:var(--amber-700);--cb-t:var(--blue-800);--cb-f:#8250df;
    background:var(--cb-bg);color:var(--cb-fg);box-shadow:var(--cb-shadow)}
  .keel-cb--dark{__CBDARK__}
  @media (prefers-color-scheme:dark){.keel-cb--auto{__CBDARK__}}
  .keel-cb__tabs{display:flex;align-items:stretch;font-family:var(--font-sans);font-size:12.5px;font-weight:var(--fw-medium);background:var(--cb-tabbar-bg);border-bottom:1px solid var(--cb-line)}
  .keel-cb__lead{display:inline-flex;align-items:center;justify-content:center;flex:none;width:40px;align-self:stretch;color:var(--cb-meta);border-right:1px solid var(--cb-line)}
  .keel-cb__tab{display:flex;align-items:center;gap:8px;padding:9px 15px;border-right:1px solid var(--cb-line);color:inherit;opacity:.55;white-space:nowrap}
  .keel-cb__tab--active{opacity:1;background:var(--cb-tab-active-bg);box-shadow:inset 0 2px 0 var(--accent)}
  .keel-cb__dot{width:9px;height:9px;border-radius:2px;flex:none}
  .keel-cb__lang{margin-left:auto;display:flex;align-items:center;padding:0 15px;font-family:var(--font-sans);font-size:11px;font-weight:var(--fw-semibold);letter-spacing:.04em;text-transform:uppercase;color:var(--cb-meta)}
  .keel-cb__bar{display:flex;align-items:stretch;background:var(--cb-tabbar-bg);border-bottom:1px solid var(--cb-line)}
  .keel-cb__title{display:inline-flex;align-items:center;padding:9px 15px;font-family:var(--font-mono);font-size:11.5px;color:var(--cb-meta)}
  .keel-cb__scroll{overflow:auto}
  .keel-cb__pre{margin:0;padding:16px 18px;white-space:pre}
  .keel-cb__pre code{display:block;min-width:max-content}
  .keel-cb .c{color:var(--cb-c)}.keel-cb .k{color:var(--cb-k)}.keel-cb .s{color:var(--cb-s)}.keel-cb .n{color:var(--cb-n)}.keel-cb .t{color:var(--cb-t)}.keel-cb .f{color:var(--cb-f)}

  /* ---- GitHubStars ---- */
  .keel-ghb{font-family:var(--font-sans);font-weight:var(--fw-semibold);display:inline-flex;align-items:center;text-decoration:none;color:var(--text-primary);letter-spacing:-.01em;line-height:1;cursor:pointer;-webkit-tap-highlight-color:transparent;user-select:none}
  .keel-ghb:focus-visible{outline:none;box-shadow:var(--ring);border-radius:var(--radius-md)}
  .keel-ghb__ico{flex:none;display:inline-flex}
  .keel-ghb__count{font-variant-numeric:tabular-nums}
  .keel-ghb--md{font-size:14px}
  .keel-ghb--minimal{gap:7px;color:var(--text-secondary);transition:color var(--dur-fast) var(--ease-out)}
  .keel-ghb--minimal:hover{color:var(--text-primary)}

  /* ---- Badge ---- */
  .keel-badge{display:inline-flex;align-items:center;gap:7px;font-family:var(--font-sans);font-weight:var(--fw-semibold);font-size:12px;line-height:1;padding:6px 11px;border-radius:980px;letter-spacing:.01em}
  .keel-badge__dot{width:6px;height:6px;border-radius:50%;background:currentColor;flex:none}
  .keel-badge--accent{background:var(--accent-subtle);color:var(--accent)}
  .keel-badge--success{background:var(--success-subtle);color:var(--success)}
  .keel-badge--signal{background:var(--signal-subtle);color:var(--signal-text)}

  /* ---- Button ---- */
  .keel-btn{display:inline-flex;align-items:center;justify-content:center;gap:8px;font-family:var(--font-sans);font-weight:var(--fw-semibold);text-decoration:none;cursor:pointer;border:none;
    transition:background var(--dur-fast) var(--ease-out),box-shadow var(--dur-fast) var(--ease-out),color var(--dur-fast) var(--ease-out),transform var(--dur-fast) var(--ease-out)}
  .keel-btn:active{transform:scale(.97)}
  .keel-btn--lg{height:48px;padding:0 22px;font-size:16px}
  .keel-btn--pill{border-radius:980px}
  .keel-btn--primary{background:var(--accent);color:#fff}
  .keel-btn--primary:hover{background:var(--accent-hover)}
  .keel-btn--outline{background:transparent;color:var(--text-primary);box-shadow:inset 0 0 0 1px var(--border-default)}
  .keel-btn--outline:hover{background:var(--surface-sunken)}

  /* ---- Callout ---- */
  .keel-callout{border-radius:var(--radius-md);padding:16px 18px;font-size:15px;line-height:1.55;color:var(--text-secondary);background:var(--accent-subtle);box-shadow:inset 0 0 0 1px rgba(11,95,255,.14)}
  .keel-callout__title{font-weight:700;color:var(--text-primary);margin-bottom:5px}

  /* ---- Card ---- */
  .keel-card{background:var(--surface-card);border-radius:var(--radius-xl);padding:20px;transition:transform var(--dur-fast) var(--ease-out),box-shadow var(--dur-fast) var(--ease-out)}
  .keel-card--outlined{box-shadow:inset 0 0 0 1px var(--border-subtle)}
  .keel-card--flat{box-shadow:none}
  .keel-card--lg{padding:24px}
  .keel-card--interactive:hover{transform:translateY(-2px);box-shadow:var(--shadow-md)}
  .keel-card--outlined.keel-card--interactive:hover{box-shadow:inset 0 0 0 1px var(--border-default),var(--shadow-md)}

  /* ---- Tooltip ---- */
  .keel-tip{position:relative;display:inline-flex}
  .keel-tip__pop{position:absolute;bottom:calc(100% + 8px);left:50%;transform:translateX(-50%) translateY(4px);
    background:var(--surface-midnight);color:#fff;font-family:var(--font-sans);font-size:12px;font-weight:500;line-height:1.4;
    padding:7px 10px;border-radius:8px;box-shadow:var(--shadow-md);opacity:0;pointer-events:none;
    transition:opacity var(--dur-fast) var(--ease-out),transform var(--dur-fast) var(--ease-out);
    white-space:normal;width:max-content;max-width:215px;text-align:center;z-index:50}
  .keel-tip:hover .keel-tip__pop,.keel-tip:focus-within .keel-tip__pop{opacity:1;transform:translateX(-50%) translateY(0)}

  /* ---- Hero stat accordion (4-up grid on desktop; horizontal accordion on narrow) ---- */
  .stat-accordion{display:grid;grid-template-columns:repeat(4,1fr);gap:1px;background:var(--border-subtle);border:1px solid var(--border-subtle);border-radius:14px;overflow:hidden;margin:40px 0 0}
  .stat-radio{position:absolute;width:1px;height:1px;opacity:0;pointer-events:none}
  .stat-item{display:flex;flex-direction:column;gap:7px;padding:18px 20px;background:var(--surface-card);min-width:0;cursor:default}
  .stat-k{font-size:11px;font-weight:700;letter-spacing:.07em;text-transform:uppercase;color:var(--text-tertiary)}
  .stat-v{font-size:15px;font-weight:600;color:var(--text-primary);line-height:1.35}

  /* ---- Schema walkthrough: whole figure scrolls naturally; detents on each group ---- */
  .schema-figure{position:relative;max-width:720px}
  .schema-figure__code{position:relative}
  .schema-band{position:absolute;left:0;right:0;z-index:4;background:rgba(59,120,255,.18);border-radius:2px;opacity:0;pointer-events:none}
  .walk-panel{position:absolute;left:10px;right:10px;top:0;z-index:5;opacity:0;pointer-events:none;
    background:var(--surface-card);border:1px solid var(--border-default);border-radius:12px;padding:14px 16px;box-shadow:var(--shadow-md)}
  .walk-panel::before{content:"";position:absolute;top:-7px;left:24px;width:12px;height:12px;
    background:var(--surface-card);border-top:1px solid var(--border-default);border-left:1px solid var(--border-default);transform:rotate(45deg)}
  /* non-narrow: float each tooltip into a rail to the RIGHT of the code so it never covers lines below */
  @media (min-width:881px){
    .schema-figure{max-width:1064px}
    .schema-figure__code{max-width:calc(100% - 344px)}
    .walk-panel{left:auto;right:0;width:320px}
    .walk-panel::before{top:16px;left:-7px;border:none;
      border-bottom:1px solid var(--border-default);border-left:1px solid var(--border-default)}
  }
"""

HEAD = (
    "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n"
    "<meta charset=\"utf-8\" />\n"
    "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n"
    "<title>Meridian | structural merge for source files</title>\n"
    "<meta name=\"description\" content=\"Meridian is a domain-neutral structural merge and semantic diff toolkit for source formats Git can only see as text.\" />\n"
    "<link rel=\"canonical\" href=\"https://adamcoulteroz.github.io/Meridian/\" />\n"
    "<meta property=\"og:type\" content=\"website\" />\n"
    "<meta property=\"og:title\" content=\"Meridian | structural merge for source files\" />\n"
    "<meta property=\"og:description\" content=\"Merge by structure, not by line order. A domain-neutral structural merge and semantic diff toolkit, wired in as a Git driver.\" />\n"
    "<meta property=\"og:url\" content=\"https://adamcoulteroz.github.io/Meridian/\" />\n"
    "<meta name=\"twitter:card\" content=\"summary\" />\n"
    f"<link rel=\"icon\" href=\"{FAVICON}\" />\n"
    "<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\" />\n"
    "<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin />\n"
    "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Hanken+Grotesk:wght@400;500;600;700;800&family=Fira+Code:wght@400;500;600;700&display=swap\" />\n"
    "<style>\n"
    ":root{" + LIGHT_VARS + "}\n"
    "@media (prefers-color-scheme:dark){:root{" + DARK_VARS + "}}\n"
    + BASE_CSS
    + COMPONENT_CSS.replace("__CBDARK__", CB_DARK)
    + "\n/*__HOVER__*/\n</style>\n</head>\n<body>\n"
)

SCRIPT = (
    "<script>\n"
    "function openNav(){\n"
    "  document.documentElement.setAttribute('data-nav-open','');\n"
    "  document.body.style.overflow='hidden';\n"
    "  var b=document.getElementById('nav-burger'); if(b)b.setAttribute('aria-expanded','true');\n"
    "  var d=document.getElementById('nav-drawer'); if(d)d.setAttribute('aria-hidden','false');\n"
    "}\n"
    "function closeNav(){\n"
    "  document.documentElement.removeAttribute('data-nav-open');\n"
    "  document.body.style.overflow='';\n"
    "  var b=document.getElementById('nav-burger'); if(b)b.setAttribute('aria-expanded','false');\n"
    "  var d=document.getElementById('nav-drawer'); if(d)d.setAttribute('aria-hidden','true');\n"
    "}\n"
    "function toggleNav(){ document.documentElement.hasAttribute('data-nav-open') ? closeNav() : openNav(); }\n"
    "document.addEventListener('keydown', function(e){ if(e.key==='Escape') closeNav(); });\n"
    "if(window.matchMedia){ window.matchMedia('(min-width:881px)').addEventListener('change', function(e){ if(e.matches) closeNav(); }); }\n"
    "// in-page links: close the drawer first, then scroll on the next frame so the\n"
    "// scroll-lock release and drawer transition can't cancel the smooth scroll.\n"
    "document.addEventListener('click', function(e){\n"
    "  var a = e.target && e.target.closest ? e.target.closest('a[href^=\"#\"]') : null;\n"
    "  if(!a) return;\n"
    "  var id = a.getAttribute('href');\n"
    "  if(!id || id.length < 2) return;\n"
    "  var el = document.querySelector(id);\n"
    "  if(!el) return;\n"
    "  e.preventDefault();\n"
    "  closeNav();\n"
    "  requestAnimationFrame(function(){\n"
    "    el.scrollIntoView({behavior:'smooth', block:'start'});\n"
    "    try{ history.pushState(null, '', id); }catch(_){}\n"
    "  });\n"
    "});\n"
    "// schema walkthrough: the whole figure scrolls naturally; each group's band + tooltip\n"
    "// fade in as it nears the focal line, and the page detents (scroll-snaps) on each one.\n"
    "(function(){\n"
    "  var wrap, fig, bands = [], panels = [], groups = [], ready = false, snapping = false, snapT;\n"
    "  function clamp(v,a,b){ return v<a?a:(v>b?b:v); }\n"
    "  function wide(){ return window.matchMedia('(min-width:881px)').matches; }\n"
    "  function init(){\n"
    "    wrap = document.querySelector('.schema-figure__code');\n"
    "    if(!wrap) return false;\n"
    "    fig = wrap.parentNode;\n"
    "    bands = [].slice.call(wrap.querySelectorAll('.schema-band'));\n"
    "    panels = [].slice.call(fig.querySelectorAll('.walk-panel'));\n"
    "    var lines = [].slice.call(wrap.querySelectorAll('.anno-line'));\n"
    "    if(!bands.length || !panels.length || !lines.length) return false;\n"
    "    var wrapTop = wrap.getBoundingClientRect().top;\n"
    "    var by = {};\n"
    "    lines.forEach(function(l){ var g=l.getAttribute('data-anno'); (by[g]=by[g]||[]).push(l); });\n"
    "    groups = Object.keys(by).sort(function(a,b){return a-b;}).map(function(g){\n"
    "      var ls=by[g];\n"
    "      var top=ls[0].getBoundingClientRect().top - wrapTop;\n"
    "      var bot=ls[ls.length-1].getBoundingClientRect().bottom - wrapTop;\n"
    "      return {top:top, height:bot-top, first:ls[0]};\n"
    "    });\n"
    "    var w = wide();\n"
    "    groups.forEach(function(g,i){\n"
    "      if(bands[i]){ bands[i].style.top=g.top+'px'; bands[i].style.height=g.height+'px'; }\n"
    "      if(panels[i]){ panels[i].style.top=(w ? g.top : (g.top+g.height+10))+'px'; }\n"
    "    });\n"
    "    ready = groups.length > 0;\n"
    "    return ready;\n"
    "  }\n"
    "  function sync(){\n"
    "    if(!ready && !init()) return;\n"
    "    var focal = window.innerHeight*0.38;\n"
    "    var fade = 34;\n"
    "    for(var i=0;i<groups.length;i++){\n"
    "      var top = groups[i].first.getBoundingClientRect().top;\n"
    "      var prox = clamp(1 - Math.abs(top - focal)/fade, 0, 1);\n"
    "      if(bands[i]) bands[i].style.opacity = prox;\n"
    "      if(panels[i]) panels[i].style.opacity = prox;\n"
    "    }\n"
    "  }\n"
    "  function maybeSnap(){\n"
    "    if(snapping || !ready) return;\n"
    "    var f = window.innerHeight*0.38, bi=-1, bd=1e9;\n"
    "    for(var i=0;i<groups.length;i++){ var d=groups[i].first.getBoundingClientRect().top - f; if(Math.abs(d)<Math.abs(bd)){ bd=d; bi=i; } }\n"
    "    if(bi>=0 && Math.abs(bd) > 1.5 && Math.abs(bd) <= 34){\n"
    "      snapping = true;\n"
    "      window.scrollTo({top: Math.round(window.scrollY + bd), behavior:'smooth'});\n"
    "      setTimeout(function(){ snapping = false; }, 450);\n"
    "    }\n"
    "  }\n"
    "  var tick=false;\n"
    "  function onScroll(){\n"
    "    if(!tick){ tick=true; requestAnimationFrame(function(){ sync(); tick=false; }); }\n"
    "    if(!snapping){ clearTimeout(snapT); snapT = setTimeout(maybeSnap, 110); }\n"
    "  }\n"
    "  window.addEventListener('scroll', onScroll, {passive:true});\n"
    "  window.addEventListener('resize', function(){ ready=false; sync(); });\n"
    "  window.addEventListener('load', function(){ ready=false; sync(); });\n"
    "  if(document.fonts && document.fonts.ready){ document.fonts.ready.then(function(){ ready=false; sync(); }); }\n"
    "  sync();\n"
    "})();\n"
    "// live GitHub star count: swap the static fallback for the repo's real count\n"
    "(function(){\n"
    "  function fmt(n){ if(typeof n!=='number'||!isFinite(n)) return null;\n"
    "    if(n>=1e6) return +(n/1e6).toFixed(1)+'M'; if(n>=1e3) return +(n/1e3).toFixed(1)+'k'; return String(n); }\n"
    "  function apply(n){ var t=fmt(n); if(t===null) return;\n"
    "    var c=document.querySelectorAll('.keel-ghb__count'); for(var i=0;i<c.length;i++) c[i].textContent=t;\n"
    "    var a=document.querySelectorAll('a.keel-ghb'); for(var j=0;j<a.length;j++){ var l=a[j].getAttribute('aria-label');\n"
    "      if(l) a[j].setAttribute('aria-label', l.replace(/[0-9.,kM]+ stars/, t+' stars')); } }\n"
    "  try{ fetch('https://api.github.com/repos/AdamCoulterOz/Meridian',{headers:{'Accept':'application/vnd.github+json'}})\n"
    "    .then(function(r){ return r.ok ? r.json() : null; })\n"
    "    .then(function(d){ if(d && typeof d.stargazers_count==='number') apply(d.stargazers_count); })\n"
    "    .catch(function(){}); }catch(e){}\n"
    "})();\n"
    "</script>\n"
)

TAIL = "</body>\n</html>\n"


if __name__ == "__main__":
    main()
