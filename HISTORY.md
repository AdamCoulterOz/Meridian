# History

## 2026-08-28

- Made complete HTML documents structurally mergeable. `.html`/`.htm` content that is a full page (a doctype or an `<html>` root) was kept as a single opaque scalar, so any edit on both sides conflicted the whole file. It is now parsed by a new `html:document` adapter: the document element and everything below it are real tree nodes, so disjoint head and body edits merge and only genuine same-node edits conflict.
  - `HtmlDocumentAdapter` parses the source unwrapped, which is what AngleSharp needs to build a correct document. The earlier body-context parse (`ParseDocument($"<body>{source}</body>")`) foster-parented the head and dropped the doctype and wrappers, and is still the right thing for fragments only.
  - The doctype (with its exact casing and any legacy identifiers) and anything after `</html>` are recovered from the source text as `$prologue`/`$epilogue` nodes, because the tree-construction algorithm normalises the doctype and folds trailing whitespace into the body.
  - `HtmlFragmentAdapter` stays the registered adapter for `.html`/`.htm` and routes full documents to the document adapter; node parsing and rendering are shared by both shapes in `HtmlNodes`.
- Made HTML rendering source-faithful, so a structural merge produces a reviewable diff instead of rewriting the file. Rendering from the parsed tree alone had normalised away how the markup was written — attribute order and quoting, valueless attributes, name casing, self-closing void elements, character references, and every non-ASCII character (encoded as a numeric entity) — which was tolerable while fragments were small and invisible while whole pages were opaque blobs, but rewrote ~2.7KB of an untouched 113KB page once documents became structural.
  - Each node now carries the source text it was parsed from in `TreeNode.SourceText`, which is neither merged nor compared and so can never cause a conflict. An element keeps its verbatim start tag alongside the canonical rendering of the attributes it was parsed with; rendering emits the verbatim tag while re-rendering the node's current fields still produces that same canonical form, and falls back to canonical serialisation for a node the merge changed.
  - Element offsets come from the parser (`IsKeepingSourceReferences`) and anchor a cursor walk that recovers text runs, end tags, and the whitespace the tree-construction algorithm drops between the wrappers. Every use of an offset or slice is verified against what the parser produced, so anything unrecognised degrades to canonical rendering rather than guessing.
  - Text the merge did change is encoded minimally (`&`, `<`, `>` only) instead of through `WebUtility.HtmlEncode`, which encoded all non-ASCII.
  - A self-closing tag on a non-void element is not emitted verbatim: it is meaningful in foreign content (an `<svg>` subtree) and ignored in HTML, and guessing wrong would pull the following elements into the subtree.
  - Fixed a latent corruption in the process: `<title>` and `<textarea>` hold ESCAPABLE raw text, which the parser decodes, but the renderer treated them like `<script>` and emitted their text undecoded. A title written `&amp;copy;` came back as `&copy;`, which the next parse reads as ©. Both are now encoded on the way out; `noscript`, whose content is ordinary markup while scripting is off, was in that set for the same wrong reason.
  - A real 113KB page now round-trips byte for byte through the merge driver, and a two-sided merge of it produces a diff of exactly the two changed lines.

## 2026-08-02

- Added canonical keyword, social, crawler, and Schema.org metadata to the static Meridian site while preserving its complete JavaScript-independent documentation content.
- Added a footer route back to the parent Adam Coulter project index and published repository-local crawler files.

## 2026-06-24

- Adjusted the distributable provider package boundary from one package per concrete adapter to cohesive bundles:
  - `MeridianGit.Formats.Markup` carries XML, JSON, JSON5, and YAML.
  - `MeridianGit.Formats.Web` carries HTML, CSS, and JavaScript.
  - `MeridianGit.Formats.Images` carries PNG, JPEG, GIF, and ICO.
  - `MeridianGit.Formats.PowerPlatform` carries XAP and Liquid.
  - `MeridianGit.Formats.Binary` carries the generic `.bin` byte-safe provider and is the intended home for future generic binary types.
  - Adapter namespaces remain format-specific while package restore, provider registration, and CLI trust operate at the bundle level.

## 2026-06-23

- Implemented the format adapters ("providers") that were previously opaque placeholders.
  - JavaScript is now structural: Esprima parses the source and supplies exact statement byte ranges; each top-level statement is kept as its verbatim source slice with leading trivia, keyed by declared name (function/class/variable) or module specifier, with positional fallback. Clean round-trips are exact; independent top-level edits merge. Module syntax is parsed via `ParseModule` with a `ParseScript` fallback.
  - CSS is now structural and source-preserving: a brace/string/comment/paren-aware scanner splits the stylesheet into rules (by selector), declarations (by property), at-rules, comments, and whitespace, capturing every byte so any input round-trips exactly. Independent rule/declaration edits merge; same-property edits conflict.
  - PNG, JPEG, GIF, ICO, and XAP are now byte-safe via a shared `BinaryFormatAdapter` base and the new `IBinaryFormatAdapter` contract. Content is a lossless base64 scalar; the Git CLI reads/writes these as bytes, resolves to the changed side when only one side changed, and reports a conflict (leaving `--ours` untouched) when both diverge. Raw and mapped-text adapters were consolidated onto a shared `OpaqueTextAdapter` base.
  - The Git CLI registers `.css`, `.png`, `.jpg`/`.jpeg`, `.gif`, `.ico`, and `.xap` by extension, and `diff-file` reports binary differences without dumping content.
- Established the distributable MeridianGit package shape:
  - The Git CLI now packs as the `MeridianGit` .NET tool package while installing the user-facing `meridian` command.
  - Public Git verbs are `meridian merge` and `meridian diff`, replacing the earlier source-run `merge-file`/`diff-file` command names.
  - Provider registration moved behind `MeridianGit.Abstractions`, with a trusted exact-package restore catalog for known format types.

## 2026-06-22

- Fixed XML structural fidelity in the non-mapped `XmlAdapter` parse/render path. Clean merges previously dropped XML comments, discarded namespace prefixes on element and attribute names, and lost significant mixed-content text. The adapter now preserves comments, processing instructions, CDATA, and significant text as child nodes, and carries namespace prefixes through `Kind` and attribute keys. Leaf-text elements still parse to a scalar value and whitespace-only formatting is still normalized, so existing schema, discriminator, nested-content, and pretty-print behavior is unchanged.
- Added a GitHub Actions CI workflow (`.github/workflows/ci.yml`) that installs the .NET 11 preview SDK and runs `dotnet build` and `dotnet test` on the solution for pushes and pull requests to `main`.
- Documented the remote-schema include security posture (SSRF / supply-chain surface when Meridian runs as an automatic Git driver) in `README.md`.
- Adopted the Apache License 2.0. Added `LICENSE` (full Apache-2.0 text), a `NOTICE` attribution file, and a root `Directory.Build.props` setting `PackageLicenseExpression` to `Apache-2.0` and the project copyright for all current and future packable projects. Apache-2.0 was chosen over MIT for its explicit patent grant and patent-retaliation terms, and over copyleft options to keep Meridian freely embeddable by consumers.
- Published a public contact point (`adam.coulter@me.com`) via `NOTICE`, the README `Contact` section, and a `SECURITY.md` security policy that also documents the remote-schema SSRF surface. Added a static GitHub Pages landing site under `docs/` (`index.html` plus `.nojekyll`) explaining what Meridian is and how it works.

## 2026-05-08

- Established the repository-level cognition split required by current agent instructions: `CONTEXT.md` carries current operational state, `HISTORY.md` carries durable architectural history, and `INTERFACE.md` carries the public semantic boundary.
- Documented Meridian's current external boundary as a domain-neutral structural merge/diff toolkit with a source-first CLI, reusable .NET libraries, format adapters, and schema authoring contract.
- Kept the boundary explicitly domain-neutral: product-specific rules, such as Power Platform or Power Pages merge semantics, belong in consumer-owned schema files or wrapper tooling rather than Meridian core.

## Established Architecture Decisions

- Chose structural merge and compare over document trees rather than line-oriented text.
- Chose format adapters as the owners of physical parsing, source representation, escaping, and rendering.
- Chose schema rules as the owners of merge-relevant semantics such as identity, ordered children, nested content, companion payloads, format aliases, and file-specific overlays.
- Chose a thin Git command boundary over the core merge and diff services, keeping Git integration outside `Meridian.Core`.
- Chose to keep schema discovery and schema composition as separate lifecycle concerns: discovery applies source-tree scope, while `includes` and `references` compose schema documents.
- Chose explicit, diagnostic remote schema composition over persistent hidden caching.
- Chose conservative mapped-format handling, where template tokens become host-aware structure only when the host adapter can represent the token safely.
- Chose first-class package adapters as the future direction for composite formats such as `docx` and `xlsx`, rather than aliasing package internals to XML.
