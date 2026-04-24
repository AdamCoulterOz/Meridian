# Meridian Architecture Notes

This document is for people extending Meridian. If you only want to use Meridian as a merge tool, start with the [README](../README.md).

## Mental Model

Meridian treats a merge as:

```text
base source  -> adapter -> base tree
ours source  -> adapter -> ours tree
theirs source -> adapter -> theirs tree

schema + identity assignment
three-way structural merge
adapter render
```

Git still supplies the normal three inputs: base, ours, and theirs. Meridian changes what happens inside the merge by operating on document structure instead of lines.

## Core Responsibilities

`Meridian.Core` owns format-independent behavior:

- document tree and node contracts;
- schema model and YAML loading;
- identity assignment and ambiguity diagnostics;
- three-way merge mechanics;
- conflict marker construction;
- generic format registry and adapter contracts;
- nested content expansion/collapse under `Core/Formats/Nested`;
- mapped format composition for template-like sources under `Core/Formats/Mapped`.

It should not know about product-specific formats such as Power Platform solution files. Those belong in schemas or consumer tools.

## Format Adapter Responsibilities

A format adapter owns everything physical about a format:

- parsing source text into tree nodes;
- choosing node kinds, field names, and source metadata;
- escaping and unescaping scalar values;
- rendering tree nodes back into source text;
- deciding which mapped-token contexts are safe for that host language.

Adapters exist under `src/Formats` by cohesive format family:

- `Data`: XML, JSON, JSON5, YAML;
- `Web`: HTML fragment, CSS, JavaScript;
- `Liquid`: Liquid mapped source parsing;
- `Images`: image placeholder formats;
- `Xap`: XAP placeholder format.

## Identity Assignment

The merge engine only becomes reliable once every comparable node has a stable identity.

Identity is local to the parent scope. A discriminator does not mean “this is globally unique”; it means “inside this parent, siblings with this node name can be matched by this value.”

Precedence:

```text
explicit path rule > global discriminator field > structural rule > typed path fallback
```

If repeated siblings cannot be assigned unique identities, Meridian reports an identity diagnostic instead of silently falling back to positional matching.

## Ordered Children

Unordered children are merged by identity.

Ordered children are merged as sequences of identities. If both sides change the same ordered sequence differently, Meridian emits an ordered-child conflict.

This is deliberately schema-driven because order importance is not a property of a node type alone. It depends on the parent instance and the semantics of that collection.

## Nested Content

Nested content lets a scalar node become another document tree.

Example:

```xml
<Metadata>{"color":"orange"}</Metadata>
```

With a schema rule saying `Metadata` is `json`, Meridian expands that scalar into a nested JSON tree before merge. After merging, it collapses the nested tree back through the parent adapter so escaping remains valid for the parent format.

Conflict projection is intentionally conservative. If unresolved nested conflict markers cannot be safely represented inside the parent scalar, Meridian should fail rather than embed corrupt escaped content.

## Mapped Formats

Mapped formats are for template-like languages embedded around a host language, such as `liquid:xml`.

The mapped source adapter parses template tokens and literal regions. The host adapter tracks where the literal stream is in host-language terms and decides whether a mapped token can be represented safely.

Current mapped token contexts:

- `ChildNode`: token appears where a child node can exist.
- `FieldValue`: token appears inside a field/attribute value.

The mapped adapter stitches host-safe placeholders into the literal source, lets the host parser build a tree, then replaces placeholders with mapped-token tree nodes. If the token location is unsafe, the document falls back to opaque mapped-source behavior.

This avoids pretending the host language can parse invalid source such as conditionally split XML tags.

## Companion Files

Companion rules let one file provide metadata for another file.

The schema can derive:

- a companion path from metadata values or from the matched path;
- a companion format from metadata values;
- a default format if the metadata value is unknown.

The core model is generic. Power Platform WebResource metadata is one consumer use case, not a built-in assumption.

## Git Integration Boundary

`src/Tools/GitMerge` is intentionally thin:

- parse Git merge-driver arguments;
- select an adapter;
- load an optional schema;
- call `Merger`;
- write the result to Git’s `%A` file;
- return Git-compatible exit codes.

The merge core does not depend on Git.

## Design Biases

Meridian prefers:

- failing loudly over guessing;
- explicit schema rules over hidden heuristics;
- logical format names with aliases over throwing away domain meaning;
- safe opaque fallback over unsafe structural parsing;
- consumer-owned schemas over product-specific core code.

## Near-Term Technical Follow-Ups

- Source-preserving patch projection so clean merges avoid broad formatting rewrites.
- More complete CLI adapter registration and format selection.
- Explicit unresolved nested-conflict projection.
- Stronger semantic keys for mapped template tokens.
- Byte-safe binary handling.
- Package adapters for composite formats such as `docx` and `xlsx`.
