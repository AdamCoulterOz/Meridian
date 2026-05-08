# History

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
