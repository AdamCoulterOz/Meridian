# Context

## Project Purpose And Current State

Meridian is a general-purpose AST-aware three-way merge toolkit.

It provides a format-agnostic merge core, pluggable format adapters, schema-driven node identity rules, nested content traversal, and a Git merge-driver command. Domain-specific repositories, such as PowerSource, should use Meridian by supplying schemas and workflow tooling rather than embedding domain rules in Meridian itself.

Current state:

- The repository targets .NET 11.
- The core AST merge model and schema loader are implemented.
- XML, JSON, JSON5, YAML, HTML fragment, JavaScript, Liquid/template text, CSS, image placeholder, `xap`, and raw adapters exist as separate format projects under `src/Formats`.
- The Git merge-driver command can merge supported files with an optional schema.
- Template-host composition exists for formats such as `liquid:xml`.
- A generic catalog fixture exists to exercise schema-driven XML identity and clean three-way merge behavior without domain-specific assumptions.

## Architecture And Structure

- `src/Meridian.Core` contains AST contracts, schema model/loading, identity assignment, nested content expansion/collapse, three-way merge mechanics, template placeholder contracts, and conflict marker helpers.
- `src/Formats` contains one project per format adapter, including XML, JSON, JSON5, YAML, HTML fragments, JavaScript, Liquid, templated-host composition, template text, CSS, raw payloads, image placeholders, and `xap`.
- `src/Meridian.Tools.GitMerge` contains a thin Git merge-driver style command.
- `tests/Meridian.Tests` contains coverage for identity generation, ambiguity detection, schema loading, unordered merge, ordered child conflicts, nested content traversal, format adapters, template-host composition, Git conflict marker rendering, and file-based generic fixtures.

## Key Decisions And Invariants

- Meridian is domain-neutral. Product-specific knowledge must live in external schemas or wrapper tools.
- The merge engine operates on AST nodes, not plain text.
- Format adapters own parsing, physical representation, escaping/encoding, and write-back behavior.
- Format adapter projects are isolated by format. Shared format helper assemblies should be avoided unless the shared contract is genuinely format-agnostic and belongs outside a concrete adapter.
- Git integration remains outside `Meridian.Core`.
- Schema rules define merge-relevant semantics such as discriminators, ordered children, companion files, content formats, nested schema references, and format aliases.
- Ambiguous node identity must fail loudly; no silent positional fallback for semantic merge.
- Child order participates in merge only for schema-declared ordered collections.
- Clean nested content is rendered by the child adapter and re-encoded by the parent adapter.
- Unresolved nested content conflicts must not be silently embedded inside escaped child content.
- Conflict marker projection defaults to normal two-sided Git conflict markers.

## Template-Host Model

- Template-host formats are composed as `engine:host`, such as `liquid:xml`.
- Template engines parse template tokens; host adapters decide which AST-relative token contexts are safe.
- Current token contexts are `ChildNode` and `FieldValue`.
- Host context tracking can return multiple possible AST contexts. The composed adapter chooses the first context the host can represent.
- Raw host syntax is not a valid token context; unsupported locations should make the document unsafe.
- XML currently represents template tokens as child-node placeholders and field-value placeholders.
- XML field-value support is a flat sequence of text and placeholders; richer expression trees need explicit host representation before being marked safe.
- Physical template markers are parse-time plumbing only.
- Template placeholders carry hidden `$semanticKey` metadata for merge identity when available.
- Current XML semantic keys are host-slot scoped but still ordinal within that slot.

## Known Follow-Ups

- Add source-preserving patch projection so clean merges do not rewrite formatting unnecessarily.
- Add explicit unresolved nested-conflict projection instead of failing during collapse.
- Strengthen template placeholder semantic keys so they remain stable under unrelated token insertion.
- Add more host adapters and safe placeholder strategies for JSON, YAML, HTML, JavaScript, and other formats.
- Replace opaque adapters for binary formats with byte-safe handling.
- Add first-class package adapters for composite formats such as `docx` and `xlsx`.
- Add small integration fixtures for real Git merge-driver runs.
