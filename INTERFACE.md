# Interface

## Purpose

Meridian is a domain-neutral structural merge and compare toolkit for source-controlled files whose semantics are richer than line-oriented text.

It owns format-agnostic document-tree merge behavior, schema-driven identity and ordering rules, reusable format adapter contracts, provider registration contracts, and a Git-facing merge/diff command.

## Responsibilities

Current ownership:

- Structural three-way merge over document trees.
- Structural two-way comparison over document trees.
- Schema loading, schema discovery, schema composition, and schema compilation for a target file.
- Format adapter contracts and built-in adapters for supported text-oriented formats.
- Provider registration contracts and trusted provider package resolution for known grouped `MeridianGit.Formats.*` packages.
- Packaged .NET tool distribution as `MeridianGit`, installing the `meridian` command.
- Git merge-driver and external-diff command behavior.
- Conservative conflict and identity diagnostics.

Potential future ownership:

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

- `Meridian.Core` exposes the document tree, schema, identity, merge, diff, nested-content, mapped-format, and format adapter contracts, including `IFormatAdapter`, the `OpaqueTextAdapter` base, and the byte-safe `IBinaryFormatAdapter`/`BinaryFormatAdapter` contract.
- `MeridianGit.Abstractions` exposes `IMeridianGitProvider` and `MeridianGitFormatRegistration` for provider packages.
- Format projects under `source/Formats` expose grouped NuGet packages: `MeridianGit.Formats.Markup` for XML/JSON/JSON5/YAML, `MeridianGit.Formats.Web` for HTML/CSS/JavaScript, `MeridianGit.Formats.Images` for PNG/JPEG/GIF/ICO, `MeridianGit.Formats.PowerPlatform` for XAP/Liquid, and `MeridianGit.Formats.Binary` for generic `.bin` payloads.
- `source/Tools/GitMerge` packs as the `MeridianGit` .NET tool package and exposes the installed `meridian` command surface:
  - `meridian merge --base <PATH> --ours <PATH> --theirs <PATH> --path <REPO_PATH> [--schema <SCHEMA_YAML>]`
  - `meridian diff --old <PATH> --new <PATH> --path <REPO_PATH> [--schema <SCHEMA_YAML>]`
  - `meridian diff <repo-path> <old-file> <old-hex> <old-mode> <new-file> <new-hex> <new-mode>`
- `schemas/meridian.schema.json` is the public authoring contract for Meridian schema YAML.
- `*.meridian.yaml` files are discovered by the Git command from repository root to target file directory when `--schema` is omitted.

## Invariants

- Meridian core must remain domain-neutral.
- Ambiguous generated identity must fail loudly.
- Semantic merge must not silently fall back to positional matching for ambiguous repeated nodes.
- Child order is meaningful only where declared by schema.
- Git integration must stay outside `Meridian.Core`.
- Format adapters own physical parsing, escaping, and rendering.
- Provider packages register extension-to-adapter behavior; the CLI owns provider trust, restore, and invocation.
- Automatic provider restore must be limited to the trusted exact-package catalog.
- Schema content rules describe decoded logical content, not container escaping.
- Schema discovery and schema composition are separate mechanisms.
- Include resolution must fail loudly for missing local files, unavailable remote URLs, invalid include values, unsupported schemes, rooted local paths, and cycles.
- Remote schema loading must be explicit, diagnostic, and non-persistent.

## Side Effects

- `meridian merge` writes the merged result to the `--ours` path. For binary adapters it reads and writes file content as bytes; when both sides change a binary file differently it leaves `--ours` untouched and exits with conflict status, because binary content cannot carry text conflict markers.
- `meridian diff` writes structural diff output to stdout, or a single "binary files differ" line for binary adapters.
- Identity, schema, adapter, conflict, and remote-schema diagnostics are written to stderr.
- When a known provider assembly is unavailable, provider resolution may run `dotnet restore` for the exact trusted grouped `MeridianGit.Formats.*` package/version into a user cache under `.cache/meridiangit/providers`, unless `MERIDIANGIT_DISABLE_PROVIDER_DOWNLOAD=1`.
- Schema loading may read local files referenced by `--schema`, discovered `*.meridian.yaml` files, `includes`, or `references`.
- Schema loading may perform HTTP/HTTPS GET requests for remote includes.
- Remote schema fetches use a per-load in-memory cache keyed by exact URL and send `Cache-Control: no-cache`.
- The library does not persist remote schemas.

## Dependency Boundaries

- Upstream dependencies include .NET, YamlDotNet, Json5, AngleSharp, Esprima, and Spectre.Console.Cli.
- Downstream consumers may use the CLI, schema contract, or library APIs.
- Consumers should depend on documented schema and command behavior rather than internal implementation details.
- Provider authors should depend on `MeridianGit.Abstractions` and the relevant Meridian core adapter contracts rather than CLI internals.
- Product-specific schemas may target Meridian's public schema contract, but must not require Meridian core to know their domain.
- Adapter internals, tree metadata shape, trusted provider catalog implementation details, and command implementation details are internal unless documented here or in `README.md`.

## Lifecycle / Execution Model

- CLI execution is one command invocation at a time.
- `meridian merge` resolves a format provider, parses base, ours, and theirs, loads schema, merges, writes `ours`, and exits with Git-compatible status.
- `meridian diff` resolves a format provider, parses old and new, loads schema, writes a semantic diff when differences exist, and exits with Git-compatible status.
- Provider resolution checks bundled provider registrations before attempting trusted package restore.
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
- Do not auto-load arbitrary third-party provider packages outside the trusted catalog.
- Do not make companion-file traversal implicit in the current Git single-file merge command.

## Agent Guidance

- Read `CONTEXT.md` before changing implementation.
- Update this file when changing public command behavior, schema authoring semantics, side effects, lifecycle, invariants, or dependency boundaries.
- Update `HISTORY.md` when a change materially changes architecture, lifecycle boundaries, invariants, migration state, or public semantic contracts.
- Preserve domain neutrality and fail-loud behavior.
- Prefer schema-owned semantics over adapter or core heuristics.
- Test boundary changes through focused unit tests and, where command behavior changes, Git integration command tests.
