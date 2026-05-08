# Interface

## Purpose

Meridian is a domain-neutral structural merge and compare toolkit for source-controlled files whose semantics are richer than line-oriented text.

It owns format-agnostic document-tree merge behavior, schema-driven identity and ordering rules, reusable format adapter contracts, and a Git-facing merge/diff command.

## Responsibilities

Current ownership:

- Structural three-way merge over document trees.
- Structural two-way comparison over document trees.
- Schema loading, schema discovery, schema composition, and schema compilation for a target file.
- Format adapter contracts and built-in adapters for supported text-oriented formats.
- Git merge-driver and external-diff command behavior.
- Conservative conflict and identity diagnostics.

Potential future ownership:

- Packaged CLI and library distribution.
- Source-preserving patch projection.
- Additional safe format adapters.
- First-class package adapters for composite document formats.

Responsibilities Meridian should not own:

- Product-specific merge semantics.
- Consumer repository workflow orchestration.
- Domain schema generation for a specific platform.
- Persistent remote schema caching or schema registry hosting.
- Git repository policy beyond the command behavior it exposes.

## Domain Model

- A document is parsed into a document tree.
- A tree node has kind, optional value, fields, children, generated identity, and metadata.
- A schema set compiles into a merge schema for a target file.
- Identity rules assign stable sibling identities.
- Ordered-child rules declare where child sequence is semantically meaningful.
- Content rules describe nested scalar content that can be parsed as another format.
- Companion rules describe metadata-to-payload relationships for consumer tooling.
- Format aliases preserve logical format names while resolving to concrete adapters.
- Format adapters translate between physical source text and document trees.

## Public Interfaces

- `Meridian.Core` exposes the document tree, schema, identity, merge, diff, nested-content, mapped-format, and format adapter contracts.
- Format projects under `source/Formats` expose concrete adapters for data, web, Liquid, image-placeholder, and XAP-style formats.
- `source/Tools/GitMerge` exposes the `meridian` command surface:
  - `merge-file --base <PATH> --ours <PATH> --theirs <PATH> --path <REPO_PATH> [--schema <SCHEMA_YAML>]`
  - `diff-file --old <PATH> --new <PATH> --path <REPO_PATH> [--schema <SCHEMA_YAML>]`
  - `diff-file <repo-path> <old-file> <old-hex> <old-mode> <new-file> <new-hex> <new-mode>`
- `schemas/meridian.schema.json` is the public authoring contract for Meridian schema YAML.
- `*.meridian.yaml` files are discovered by the Git command from repository root to target file directory when `--schema` is omitted.

## Invariants

- Meridian core must remain domain-neutral.
- Ambiguous generated identity must fail loudly.
- Semantic merge must not silently fall back to positional matching for ambiguous repeated nodes.
- Child order is meaningful only where declared by schema.
- Git integration must stay outside `Meridian.Core`.
- Format adapters own physical parsing, escaping, and rendering.
- Schema content rules describe decoded logical content, not container escaping.
- Schema discovery and schema composition are separate mechanisms.
- Include resolution must fail loudly for missing local files, unavailable remote URLs, invalid include values, unsupported schemes, rooted local paths, and cycles.
- Remote schema loading must be explicit, diagnostic, and non-persistent.

## Side Effects

- `merge-file` writes the merged result to the `--ours` path.
- `diff-file` writes structural diff output to stdout.
- Identity, schema, adapter, conflict, and remote-schema diagnostics are written to stderr.
- Schema loading may read local files referenced by `--schema`, discovered `*.meridian.yaml` files, `includes`, or `references`.
- Schema loading may perform HTTP/HTTPS GET requests for remote includes.
- Remote schema fetches use a per-load in-memory cache keyed by exact URL and send `Cache-Control: no-cache`.
- The library does not persist remote schemas.

## Dependency Boundaries

- Upstream dependencies include .NET, YamlDotNet, Json5, and Spectre.Console.Cli.
- Downstream consumers may use the CLI, schema contract, or library APIs.
- Consumers should depend on documented schema and command behavior rather than internal implementation details.
- Product-specific schemas may target Meridian's public schema contract, but must not require Meridian core to know their domain.
- Adapter internals, tree metadata shape, and command implementation details are internal unless documented here or in `README.md`.

## Lifecycle / Execution Model

- CLI execution is one command invocation at a time.
- `merge-file` parses base, ours, and theirs, loads schema, merges, writes `ours`, and exits with Git-compatible status.
- `diff-file` parses old and new, loads schema, writes a semantic diff when differences exist, and exits with Git-compatible status.
- Schema discovery is evaluated at command runtime from the current Git repository root.
- Schema include resolution is evaluated during schema load.
- Remote schema cache lifetime is one schema load operation.
- Library callers own adapter registration, file IO, persistence, and any higher-level orchestration.

## Anti-Goals

- Do not encode Power Platform, Power Pages, Dataverse, or other product semantics in Meridian core.
- Do not hide merge uncertainty through silent fallback.
- Do not make ordering globally meaningful by node type alone.
- Do not expose storage or transport implementation details as semantic contracts.
- Do not treat branch-based remote schemas as reproducible without caller awareness.
- Do not make companion-file traversal implicit in the current Git single-file merge command.

## Agent Guidance

- Read `CONTEXT.md` before changing implementation.
- Update this file when changing public command behavior, schema authoring semantics, side effects, lifecycle, invariants, or dependency boundaries.
- Update `HISTORY.md` when a change materially changes architecture, lifecycle boundaries, invariants, migration state, or public semantic contracts.
- Preserve domain neutrality and fail-loud behavior.
- Prefer schema-owned semantics over adapter or core heuristics.
- Test boundary changes through focused unit tests and, where command behavior changes, Git integration command tests.
