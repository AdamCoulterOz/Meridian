# Context

## Project Purpose And Current State

Meridian is a general-purpose structure-aware three-way merge and two-way compare toolkit.

It provides a format-agnostic merge/diff core, pluggable format adapters, schema-driven node identity rules, nested content traversal, and a Git integration command. Domain-specific repositories, such as PowerSource, should use Meridian by supplying schemas and workflow tooling rather than embedding domain rules in Meridian itself.

Current state:

- The repository targets .NET 11.
- The core document tree merge model and schema loader are implemented.
- Consumer usage is documented in `README.md`; deeper extension notes live in `docs/architecture.md`.
- Repository cognition is split across `CONTEXT.md` for current operational state, `HISTORY.md` for architectural and operational change history, and `INTERFACE.md` for the stable public boundary.
- The Meridian schema authoring contract is documented as JSON Schema in `schemas/meridian.schema.json`, including descriptions for editor and LLM-assisted generation.
- XML, JSON, JSON5, YAML, HTML fragment, JavaScript, CSS, and Liquid adapters parse their formats structurally. JavaScript is structural by top-level declaration (Esprima boundaries, verbatim slices) and CSS is structural by selector and declaration property; both are source-preserving on a clean round-trip. The mapped-text and raw adapters share an `OpaqueTextAdapter` base for genuinely unstructured text. PNG, JPEG, GIF, ICO, and `xap` adapters share a byte-safe `BinaryFormatAdapter` base (lossless base64 scalar, conflict on divergence) and implement `IBinaryFormatAdapter`. Closely related external adapters are grouped into format-family projects under `source/Formats`.
- The Git integration command can merge supported files and produce two-way semantic diffs with an optional schema.
- The Git integration command can automatically discover `*.meridian.yaml` schema files from the target file directory up to the Git repository root.
- Schema documents can compose other schema documents with `includes` or `references`; local relative includes resolve from the containing YAML file and remote HTTP/HTTPS includes are fetched with explicit diagnostics.
- Mapped format composition exists for formats such as `liquid:xml`.
- A generic catalog fixture exists to exercise schema-driven XML identity and clean three-way merge behavior without domain-specific assumptions.

## Architecture And Structure

- `source/Core` contains document tree contracts, schema model/loading, identity assignment, three-way merge mechanics, structural diff mechanics, conflict marker helpers, and generic format infrastructure.
- `source/Core/Formats/Nested` contains nested content expansion/collapse.
- `source/Core/Formats/Mapped` contains mapped-text fallback, mapped format composition, and mapped token contracts.
- `source/Formats/Data` contains XML, JSON, JSON5, and YAML adapters.
- `source/Formats/Web` contains HTML fragment, CSS, and JavaScript adapters.
- `source/Formats/Images` contains image placeholder adapters for PNG, JPEG, GIF, and ICO payloads.
- `source/Formats` also contains dedicated projects for Liquid and `xap`.
- `source/Tools/GitMerge` contains a thin Git merge-driver and external-diff style command using Spectre.Console.Cli command/settings classes with attribute-based options and arguments.
- `tests/Tests` contains coverage for identity generation, ambiguity detection, schema loading, unordered merge, ordered child conflicts, nested content traversal, format adapters, mapped format composition, Git conflict marker rendering, file-based generic fixtures, and Git merge/diff integration command behavior.

## Key Decisions And Invariants

- Meridian is domain-neutral. Product-specific knowledge must live in external schemas or wrapper tools.
- The merge engine operates on tree nodes, not plain text.
- Format adapters own parsing, physical representation, escaping/encoding, and write-back behavior.
- Format adapter projects are grouped by cohesive format family. Shared format helper assemblies should be avoided unless the shared contract is genuinely format-agnostic and belongs outside a concrete adapter.
- Git integration remains outside `Meridian.Core`.
- Schema rules define merge-relevant semantics such as discriminators, ordered children, companion files, content formats, nested schema references, and format aliases.
- Schema content rules should describe decoded logical content, not normal container escaping. The active adapter owns container-specific decoding and re-encoding.
- Logical format aliases should preserve domain meaning instead of erasing it. For example, a schema may name `svg`, `xsl`, or `resx` while the registry currently aliases them to `xml` until dedicated adapters exist.
- Ambiguous node identity must fail loudly; no silent positional fallback for semantic merge.
- Child order participates in merge only for schema-declared ordered collections.
- Clean nested content is rendered by the child adapter and re-encoded by the parent adapter.
- Unresolved nested content conflicts must not be silently embedded inside escaped child content.
- Conflict marker projection defaults to normal two-sided Git conflict markers.
- Conflict marker syntax is control syntax, not data. If unresolved nested conflicts cannot be represented safely inside an encoded scalar, Meridian should project the conflict at the nearest owning encoded scalar/property boundary or fail loudly.
- Scalar formats such as GUIDs, booleans, integers, and decimals should parse as typed values where possible while preserving source representation on emit unless an explicit normalization policy says otherwise.
- Composite/package formats such as future `docx` and `xlsx` should be first-class package adapters, not aliases to XML, because the package relationships are part of the merge surface.
- Source-preserving output matters. Clean merges should avoid rewriting formatting, declarations, namespace declarations, attribute order, and sibling order unless an explicit canonicalization policy is active.
- Git merge-driver placeholders should be consumed exactly as Git supplies them; callers should not shell-quote `%O`, `%A`, `%B`, or `%P` inside Git driver config.
- The Git merge command must continue to support explicit schema loading so domain repositories can provide file-specific discriminator and ordering rules.
- The Git diff command must use the same identity and ordered-child schema rules as merge; unordered sibling reorders should not be reported as semantic differences.
- Automatic schema discovery applies `*.meridian.yaml` files from repository root to file directory; mapping keys merge recursively and nearer non-mapping values replace earlier values.
- Schema discovery and schema composition are separate concerns: discovery applies source-tree scope, while `includes`/`references` compose schema documents before the including document is overlaid.
- Include resolution must fail loudly for missing documents, unavailable remote URLs, invalid include values, and cycles. Remote schema diagnostics must identify the exact URL and whether it appears pinned to a Git commit SHA.

## Mapped Format Model

- Mapped format adapters compose a mapped source and a host format as `source:host`, such as `liquid:xml`.
- Mapped sources parse mapped tokens; host adapters decide which tree-relative token contexts are safe.
- Current token contexts are `ChildNode` and `FieldValue`.
- Host context tracking can return multiple possible tree contexts. The composed adapter chooses the first context the host can represent.
- Raw host syntax is not a valid token context; unsupported locations should make the document unsafe.
- XML currently represents mapped tokens as child-node tokens and field-value tokens.
- XML field-value support is a flat sequence of text and tokens; richer expression trees need explicit host representation before being marked safe.
- Physical mapped-token markers are parse-time plumbing only.
- Mapped tokens carry hidden `$semanticKey` metadata for merge identity when available.
- Current XML semantic keys are host-slot scoped but still ordinal within that slot.

## Known Follow-Ups

- Add source-preserving patch projection so clean merges do not rewrite formatting unnecessarily.
- Add explicit unresolved nested-conflict projection instead of failing during collapse.
- Strengthen mapped token semantic keys so they remain stable under unrelated token insertion.
- Add more host adapters and safe token strategies for JSON, YAML, HTML, JavaScript, and other formats.
- Add lossless or source-preserving formatting preservation for JSON, YAML, JSON5, and HTML where practical (XML, JavaScript, and CSS already round-trip clean merges exactly).
- Deepen JavaScript merge below the top-level statement boundary and CSS merge below the rule/at-rule-block boundary.
- Add first-class package adapters for composite formats such as `docx` and `xlsx`, building on the byte-safe `BinaryFormatAdapter` base.
- Decide packaging and distribution shape before external consumers depend on source checkout paths.
