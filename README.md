# Meridian

Language-aware structural merge tooling for source formats that Git can only see as text.

Meridian is for repositories where normal line-based merging is too fragile: unpacked solution XML, JSON config, YAML manifests, generated-ish metadata, templated files, and nested formats such as JSON inside XML.

Instead of asking Git to guess from lines, Meridian parses each side of a three-way merge into a document tree, matches nodes by schema-defined identity, merges the structure, and writes normal Git conflict markers only where the structural merge cannot be resolved safely.

## Why Use It

Use Meridian when a plain text merge turns independent structured edits into a conflict:

```xml
<<<<<<< ours
<label description="Near Miss" languagecode="1033" />
<label description="Incident" languagecode="3081" />
||||||| base
<label description="Near Miss" languagecode="1033" />
<label description="Near Miss" languagecode="3081" />
=======
<label description="Safety Event" languagecode="1033" />
<label description="Near Miss" languagecode="3081" />
>>>>>>> theirs
```

Those edits are not really competing. With a schema that says `languagecode` identifies sibling labels, Meridian can merge the structure instead:

```xml
<label description="Safety Event" languagecode="1033" />
<label description="Incident" languagecode="3081" />
```

Deterministically sorting the labels would also avoid this particular conflict. That trick is useful when order is provably irrelevant, but it is not a general solution: many structured files mix unordered lookup-style collections with ordered UI layout, execution steps, display sequence, or fallback precedence. Sorting everything is clunky at best and corrupting at worst.

Meridian handles that distinction in the schema. Children are matched by identity, and order is treated as meaningful only for parent paths listed in `orderedChildren`. If both sides change an ordered collection differently, Meridian reports an ordered-child conflict instead of pretending a sort can make the decision safely.

That is the core idea:

- `languagecode="1033"` and `languagecode="3081"` identify different sibling labels.
- sibling order may or may not matter depending on the parent node.
- a child XML/JSON/YAML payload should be merged as its own structure, not as escaped text.
- ambiguous repeated nodes should fail loudly instead of being guessed into corruption.

Meridian lets the repository define those facts once in a schema, then uses them during every merge.

## Current Status

Meridian is early, usable source-first tooling. It is not packaged yet as a NuGet package or global `dotnet` tool.

Today it includes:

- a `merge-file` command suitable for Git merge-driver integration;
- a `diff-file` command suitable for Git external-diff integration;
- structural adapters for XML, JSON, JSON5, YAML, HTML fragment, JavaScript, CSS, and Liquid in source;
- byte-safe adapters for binary image (PNG, JPEG, GIF, ICO) and XAP payloads;
- schema-driven identity and ordered-child rules in the Git merge path;
- schema-driven identity and ordered-child rules in the Git diff path;
- schema models and library utilities for nested content formats, companion file rules, and format aliases;
- two-sided Git conflict marker output for unresolved conflicts.

The current CLI auto-selects adapters by file extension for XML, JSON, JSON5, JavaScript, YAML, HTML, and CSS files, and for binary `.png`, `.jpg`/`.jpeg`, `.gif`, `.ico`, and `.xap` files. Additional adapters are available to consumers embedding Meridian directly.

JavaScript merges by top-level declaration: Esprima parses the source and each top-level statement is kept as its verbatim slice, keyed by the declared function/class/variable name (or module specifier for imports), so editing one declaration and adding another merge independently. CSS merges by rule and by declaration: rules are matched by selector and declarations within a block by property name, so independent edits land cleanly while two edits to the same property conflict. Binary formats are compared by exact byte content; when both sides change a binary file differently the merge reports a conflict and leaves `--ours` untouched, because binary content cannot carry text conflict markers.

## Build

Prerequisite: .NET 11 SDK.

```bash
git clone https://github.com/AdamCoulterOz/Meridian.git
cd Meridian
dotnet build Meridian.slnx
dotnet test Meridian.slnx
```

Run the merge command from source:

```bash
dotnet run --project source/Tools/GitMerge/GitMerge.csproj -- \
  merge-file \
  --base path/to/base.xml \
  --ours path/to/ours.xml \
  --theirs path/to/theirs.xml \
  --path catalog.xml
```

`merge-file` writes the merged result back to `--ours`, matching Git merge-driver expectations.

Run a two-way structural diff from source:

```bash
dotnet run --project source/Tools/GitMerge/GitMerge.csproj -- \
  diff-file \
  --old path/to/old.xml \
  --new path/to/new.xml \
  --path catalog.xml
```

`diff-file` writes a semantic diff to stdout. It matches children by Meridian identity rules, so schema-unordered sibling reorders are ignored while schema-ordered child reorders are reported.

When `--schema` is omitted, the Git command discovers schema files automatically. It starts at the directory containing `--path`, walks up to the Git repository root, finds `*.meridian.yaml` files in each directory, then applies them from root to leaf. Mapping keys are recursively merged; nearer schema files overwrite earlier values. Non-mapping values, including lists, replace the earlier value.

Schema discovery is for applying schemas by source-tree scope. Schema composition is separate: a schema document can use `includes` or `references` to load other schema documents first, then apply itself last.

```text
somefolder/.meridian.yaml
somefolder/.meridian/schema-1.yaml
somefolder/.meridian/schema-2.yaml
```

```yaml
schemaVersion: 0.1
name: my-solution

includes:
  - .meridian/schema-1.yaml
  - .meridian/schema-2.yaml
```

Relative include paths resolve from the YAML file that contains the include. Remote HTTP/HTTPS includes are also supported:

```yaml
includes:
  - https://raw.githubusercontent.com/AdamCoulterOz/PowerSource/main/docs/schemas/powerplatform-solution.rules.yaml
  - https://raw.githubusercontent.com/AdamCoulterOz/PowerSource/main/docs/schemas/powerpages/generated-components.meridian.yaml
```

Remote schemas fail loudly when unavailable. Meridian keeps only a per-command in-memory cache keyed by exact URL, sends `Cache-Control: no-cache`, and never uses a persistent remote schema cache. The Git command prints every remote schema URL loaded and whether the URL appears pinned to a Git commit SHA. Branch URLs such as `main` are convenient, but commit SHA URLs are reproducible.

### Security Considerations For Remote Schemas

Remote includes are convenient but they are an outbound network capability that runs during ordinary Git operations. When Meridian is wired in as a merge or diff driver, schema loading happens automatically every time Git merges or diffs an opted-in file. A `*.meridian.yaml` file in the repository — or any document it pulls in through `includes`/`references` — can therefore cause your machine to issue HTTP/HTTPS requests as a side effect of `git merge` or `git diff`.

Treat schema files, including their transitive includes, as trusted code:

- A schema committed by another contributor (or fetched from a remote URL) can direct Meridian to request arbitrary HTTP/HTTPS URLs. On a host with access to internal services or a cloud instance-metadata endpoint, this is a server-side request forgery (SSRF) surface. Meridian does not restrict remote includes to an allowlist and does not block private, loopback, or link-local addresses.
- Remote includes are fetched, not executed, but the fetched content becomes your merge schema and can change how files are merged. Review remote schemas the same way you would review a dependency.
- Prefer commit-SHA-pinned URLs over branch URLs so the schema you reviewed is the schema you load. Meridian flags whether each remote URL appears pinned.
- If you do not need remote composition, keep `includes`/`references` local (relative paths only). Local-only schemas perform no network I/O.

Exit codes:

- `0`: clean merge.
- `1`: merge completed with conflict markers.
- `2`: usage, adapter, or configuration error.

For `diff-file`, `0` means the comparison completed, including when differences are printed. `2` means usage, adapter, schema, or identity configuration error. This keeps the command compatible with Git external-diff invocation.

## Git Merge Driver

In the repository that contains the files you want to merge, add a Git merge driver:

```ini
[merge "meridian"]
    name = Meridian structural merge
    driver = dotnet run --project ../Meridian/source/Tools/GitMerge/GitMerge.csproj -- merge-file --base %O --ours %A --theirs %B --path %P
```

Adjust the `../Meridian/...` path to wherever Meridian lives relative to the consuming repo.

Then opt files into the driver with `.gitattributes`:

```gitattributes
*.xml merge=meridian
*.json merge=meridian
*.json5 merge=meridian
*.yml merge=meridian
*.yaml merge=meridian
*.html merge=meridian
*.htm merge=meridian
*.js merge=meridian
```

Commit `.gitattributes` and the relevant `*.meridian.yaml` schema files in the consuming repo so every clone gets the same merge behavior.

## Git Diff Driver

In the repository that contains the files you want to compare, add a Git diff driver:

```ini
[diff "meridian"]
    command = dotnet run --project ../Meridian/source/Tools/GitMerge/GitMerge.csproj -- diff-file
```

Git appends its external-diff arguments after the configured command:

```text
<repo-path> <old-file> <old-hex> <old-mode> <new-file> <new-hex> <new-mode>
```

Meridian consumes the repo path and temporary file paths directly.

Pass `--schema some-file.yaml` before Git's appended arguments only when you want to bypass automatic discovery and use one explicit schema file.

Then opt files into the driver with `.gitattributes`:

```gitattributes
*.xml diff=meridian
*.json diff=meridian
*.json5 diff=meridian
*.yml diff=meridian
*.yaml diff=meridian
*.html diff=meridian
*.htm diff=meridian
*.js diff=meridian
```

You can combine merge and diff attributes on the same files:

```gitattributes
*.xml merge=meridian diff=meridian
```

## Schema Quickstart

Meridian schemas describe only merge-relevant facts:

- which fields identify sibling nodes;
- which child collections have meaningful order;
- which scalar values contain another parseable format;
- which companion payloads can be resolved from metadata files.

Example:

```yaml
$schema: https://raw.githubusercontent.com/AdamCoulterOz/Meridian/main/schemas/meridian.schema.json
schemaVersion: 0.1
name: catalog

defaults:
  globalDiscriminatorFields:
    - id
    - Id
    - languagecode

nestedSchemas:
  productMetadata:
    contentRules:
      - path: $root/color
        format: plain
      - path: $root/dimensions
        format: json

files:
  - match: catalog.xml
    root: Catalog
    discriminators:
      - path: Catalog/Products/Product
        key:
          attribute: sku
      - path: Catalog/DisplayOrder/ProductRef
        key:
          attribute: sku
    orderedChildren:
      - Catalog/DisplayOrder
    content:
      - path: Catalog/Products/Product/Metadata
        format: json
        schemaRef: productMetadata
```

What this tells Meridian:

- any sibling XML nodes with `id`, `Id`, or `languagecode` can use that attribute as their local identity;
- `Product` nodes under `Catalog/Products` are matched by `sku`;
- `ProductRef` nodes under `Catalog/DisplayOrder` are matched by `sku`;
- the order of `Catalog/DisplayOrder` matters;
- `Metadata` text contains JSON and should be merged as JSON using the `productMetadata` nested schema.

The schema for Meridian schema files lives at [schemas/meridian.schema.json](schemas/meridian.schema.json). It includes descriptions intended for editor tooling and LLMs that generate schema files.

## Identity Rules

Identity is local to a parent, not global to the whole document.

For XML-like data, this means:

```text
parent identity + node name + discriminator value
```

If a parent has repeated children and Meridian cannot produce unique identities for them, the merge fails loudly. That is intentional: a failed merge is safer than silently aligning the wrong nodes.

Supported discriminator styles:

```yaml
key:
  attribute: sku
```

```yaml
key:
  element: Name
```

```yaml
key:
  composite:
    - attribute: name
    - element: type
      optional: true
```

```yaml
key:
  structural: orderedSlot
```

Use `orderedSlot` only when the position is genuinely the identity. It compares slot `0` with slot `0`, slot `1` with slot `1`, and so on.

## Ordered Children

By default, Meridian treats sibling order as unimportant once nodes have stable identities.

Declare order only where the order itself is semantically meaningful:

```yaml
files:
  - match: form.xml
    orderedChildren:
      - forms/systemform/form/tabs
      - forms/systemform/form/tabs/tab/columns
      - forms/systemform/form/events
```

If both sides change an ordered collection differently, Meridian reports an ordered-child conflict instead of guessing.

## Nested Content

Some formats carry other formats inside scalar values. Common examples:

- JSON inside XML text;
- HTML inside JSON strings;
- YAML values that contain plain multi-line text;
- templating languages wrapped around XML or HTML.

Declare nested content with `content` rules:

```yaml
content:
  - path: WebResource/Content
    format: json
    schemaRef: webResourceContent
```

Meridian parses the nested content, merges it using the selected adapter and nested schema, then re-embeds it into the parent format. If unresolved conflict markers cannot be safely embedded back into the parent scalar, Meridian fails instead of corrupting escaped content.

Today this is exposed as library functionality through the nested content expander/collapser. The Git `merge-file` command does not automatically expand and collapse nested content yet.

## Companion Files

Some repositories store metadata and payload separately. A schema can derive the companion payload path and format from the metadata file.

```yaml
files:
  - match: WebResources/*.data.xml
    root: WebResource
    discriminators:
      - path: WebResource
        key:
          element: Name
    companions:
      - pathTemplate: WebResources/{WebResource/Name}
        formatFrom:
          path: WebResource/WebResourceType
          enum:
            1: html:fragment
            2: css
            3: javascript
            4: xml
            11: svg
            12: resx
        defaultFormat: raw
```

The logical formats can then resolve through aliases:

```yaml
formatAliases:
  svg: xml
  resx: xml
```

This keeps useful domain meaning in the schema without forcing every logical type to have a dedicated parser on day one.

Companion rules are available in the schema model for consumer tooling. The current Git `merge-file` command merges the one file Git passes to it; it does not automatically chase companion payloads yet.

## Library Usage

The CLI is the easiest way to use Meridian from Git. You can also embed the merge engine directly:

```csharp
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Formats.Data;

var schema = MergeSchemaYamlLoader
    .LoadFile("repo.meridian.yaml")
    .CompileForFile("catalog.xml", "Catalog");

var xml = new XmlAdapter();

var result = new Merger().Merge(
    xml.Parse(File.ReadAllText("base.xml"), "catalog.xml", schema),
    xml.Parse(File.ReadAllText("ours.xml"), "catalog.xml", schema),
    xml.Parse(File.ReadAllText("theirs.xml"), "catalog.xml", schema),
    schema,
    xml);

File.WriteAllText("ours.xml", xml.RenderDocument(result.Document));
```

Check `result.HasConflicts`, `result.Conflicts`, and `result.IdentityDiagnostics` before accepting the merge.

## Format Names

Common schema format names:

| Format | Notes |
| --- | --- |
| `xml` | XML documents and XML nested content. |
| `json` | Strict JSON. |
| `json5` | JSON5 with comments/trailing commas. |
| `yaml` | YAML documents and nested content. |
| `html:fragment` | HTML fragments, not necessarily full documents. |
| `javascript` | JavaScript source, merged by top-level declaration. |
| `css` | CSS, merged by rule selector and declaration property. |
| `liquid:xml` | Liquid mapped over XML when using composed adapters. |
| `plain` | Plain scalar text. |
| `raw` | Opaque content; useful as a safe default. |
| `image:png` `image:jpg` `image:gif` `image:ico` `xap` | Byte-safe binary payloads, compared by exact content. |

Schema aliases let consumers keep precise logical names:

```yaml
formatAliases:
  svg: xml
  resx: xml
```

## Current Limitations

- Formatting preservation is not yet source-patch based; clean structural merges may rewrite formatting.
- The Git merge CLI does not automatically traverse nested content or companion files yet.
- Binary formats merge by whole-file byte content, not by internal structure; a both-sides change is a conflict rather than a structural merge.
- JavaScript merges top-level declarations by name and other statements positionally; it does not yet merge inside a function body.
- CSS merges top-level rules and block declarations; deeply nested structures beyond at-rule blocks are preserved verbatim rather than merged.
- Mapped templating support intentionally falls back to opaque behavior when tokens appear in unsafe host-language positions.
- Packaging is not done yet; use from source for now.

## More Detail

- [Architecture notes](docs/architecture.md)
- [Generic catalog fixture](tests/Tests/Fixtures/GenericCatalog/catalog.schema.yaml)

## License

Meridian is licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for attribution. The Apache-2.0 patent grant and patent-retaliation terms apply to all contributions.

## Contact

Questions, issues, and security reports: **adam.coulter@me.com**. For security-sensitive reports, please follow [SECURITY.md](SECURITY.md) rather than opening a public issue.
