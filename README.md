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
=======
<label description="Safety Event" languagecode="1033" />
<label description="Near Miss" languagecode="3081" />
>>>>>>> theirs
```

Those edits are not really competing. One side changed the `3081` label; the other changed the `1033` label. With a schema that says `languagecode` identifies sibling labels, Meridian can merge the structure instead:

```xml
<label description="Safety Event" languagecode="1033" />
<label description="Incident" languagecode="3081" />
```

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
- XML, JSON, JSON5, YAML, HTML fragment, JavaScript, Liquid, CSS, raw, image-placeholder, and XAP adapters in source;
- schema-driven identity and ordered-child rules in the Git merge path;
- schema models and library utilities for nested content formats, companion file rules, and format aliases;
- two-sided Git conflict marker output for unresolved conflicts.

The current CLI auto-selects adapters by file extension for XML, JSON, JSON5, JavaScript, YAML, and HTML files. Additional adapters are available to consumers embedding Meridian directly.

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
dotnet run --project src/Tools/GitMerge/GitMerge.csproj -- \
  merge-file \
  --base path/to/base.xml \
  --ours path/to/ours.xml \
  --theirs path/to/theirs.xml \
  --path catalog.xml \
  --schema meridian.schema.yaml
```

`merge-file` writes the merged result back to `--ours`, matching Git merge-driver expectations.

Exit codes:

- `0`: clean merge.
- `1`: merge completed with conflict markers.
- `2`: usage, adapter, or configuration error.

## Git Merge Driver

In the repository that contains the files you want to merge, add a Git merge driver:

```ini
[merge "meridian"]
    name = Meridian structural merge
    driver = dotnet run --project ../Meridian/src/Tools/GitMerge/GitMerge.csproj -- merge-file --base %O --ours %A --theirs %B --path %P --schema meridian.schema.yaml
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

Commit both `.gitattributes` and `meridian.schema.yaml` in the consuming repo so every clone gets the same merge behavior.

## Schema Quickstart

Meridian schemas describe only merge-relevant facts:

- which fields identify sibling nodes;
- which child collections have meaningful order;
- which scalar values contain another parseable format;
- which companion payloads can be resolved from metadata files.

Example:

```yaml
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

var schema = AstSchemaYamlLoader
    .LoadFile("meridian.schema.yaml")
    .CompileForFile("catalog.xml", "Catalog");

var xml = new XmlAdapter();

var result = new AstMerger().Merge(
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
| `javascript` | JavaScript source. |
| `liquid:xml` | Liquid mapped over XML when using composed adapters. |
| `plain` | Plain scalar text. |
| `raw` | Opaque content; useful as a safe default. |

Schema aliases let consumers keep precise logical names:

```yaml
formatAliases:
  svg: xml
  resx: xml
  image:png: raw
```

## Current Limitations

- Formatting preservation is not yet source-patch based; clean structural merges may rewrite formatting.
- The Git merge CLI currently registers a practical subset of adapters by extension.
- The Git merge CLI does not automatically traverse nested content or companion files yet.
- Binary formats are placeholders unless a consumer supplies byte-safe handling.
- Mapped templating support intentionally falls back to opaque behavior when tokens appear in unsafe host-language positions.
- Packaging is not done yet; use from source for now.

## More Detail

- [Architecture notes](docs/architecture.md)
- [Generic catalog fixture](tests/Tests/Fixtures/GenericCatalog/catalog.schema.yaml)
