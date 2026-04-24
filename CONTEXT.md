# Context

## Project Purpose And Current State

Meridian is a general-purpose AST-aware three-way merge toolkit.

It provides a format-agnostic merge core, pluggable format adapters, schema-driven node identity rules, nested content traversal, and a Git merge-driver command. Domain-specific repositories, such as PowerSource, should use Meridian by supplying schemas and workflow tooling rather than embedding domain rules in Meridian itself.

Current state:

- The repository targets .NET 11.
- The core AST merge model and schema loader are implemented.
- Consumer usage is documented in `README.md`; deeper extension notes live in `docs/architecture.md`.
- The Meridian schema authoring contract is documented as JSON Schema in `schemas/meridian.schema.json`, including descriptions for editor and LLM-assisted generation.
- XML, JSON, JSON5, YAML, HTML fragment, JavaScript, Liquid, mapped-text fallback, CSS, image placeholder, `xap`, and raw adapters exist. Closely related external adapters are grouped into format-family projects under `src/Formats`.
- The Git merge-driver command can merge supported files with an optional schema.
- Mapped format composition exists for formats such as `liquid:xml`.
- A generic catalog fixture exists to exercise schema-driven XML identity and clean three-way merge behavior without domain-specific assumptions.

## Architecture And Structure

- `src/Core` contains AST contracts, schema model/loading, identity assignment, nested content expansion/collapse, three-way merge mechanics, raw opaque handling, mapped-text fallback, mapped format composition, mapped token contracts, and conflict marker helpers.
- `src/Formats/Data` contains XML, JSON, JSON5, and YAML adapters.
- `src/Formats/Web` contains HTML fragment, CSS, and JavaScript adapters.
- `src/Formats/Images` contains image placeholder adapters for PNG, JPEG, GIF, and ICO payloads.
- `src/Formats` also contains dedicated projects for Liquid and `xap`.
- `src/Tools/GitMerge` contains a thin Git merge-driver style command.
- `tests/Tests` contains coverage for identity generation, ambiguity detection, schema loading, unordered merge, ordered child conflicts, nested content traversal, format adapters, mapped format composition, Git conflict marker rendering, and file-based generic fixtures.

## Key Decisions And Invariants

- Meridian is domain-neutral. Product-specific knowledge must live in external schemas or wrapper tools.
- The merge engine operates on AST nodes, not plain text.
- Format adapters own parsing, physical representation, escaping/encoding, and write-back behavior.
- Format adapter projects are grouped by cohesive format family. Shared format helper assemblies should be avoided unless the shared contract is genuinely format-agnostic and belongs outside a concrete adapter.
- Git integration remains outside `Meridian.Core`.
- Schema rules define merge-relevant semantics such as discriminators, ordered children, companion files, content formats, nested schema references, and format aliases.
- Ambiguous node identity must fail loudly; no silent positional fallback for semantic merge.
- Child order participates in merge only for schema-declared ordered collections.
- Clean nested content is rendered by the child adapter and re-encoded by the parent adapter.
- Unresolved nested content conflicts must not be silently embedded inside escaped child content.
- Conflict marker projection defaults to normal two-sided Git conflict markers.

## Mapped Format Model

- Mapped format adapters compose a mapped source and a host format as `source:host`, such as `liquid:xml`.
- Mapped sources parse mapped tokens; host adapters decide which AST-relative token contexts are safe.
- Current token contexts are `ChildNode` and `FieldValue`.
- Host context tracking can return multiple possible AST contexts. The composed adapter chooses the first context the host can represent.
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
- Replace opaque adapters for binary formats with byte-safe handling.
- Add first-class package adapters for composite formats such as `docx` and `xlsx`.
- Add small integration fixtures for real Git merge-driver runs.
