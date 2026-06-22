# History

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
