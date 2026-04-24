using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats.Nested;

public sealed class NestedContentExpander
{
    public static DocumentTree Expand(DocumentTree document, MergeSchema schema, IFormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(registry);

        return Expand(document, schema, registry, schema.NestedSchemas);
    }

    private static DocumentTree Expand(
        DocumentTree document,
        MergeSchema schema,
        IFormatRegistry registry,
        IReadOnlyDictionary<string, MergeSchema> schemaCatalog) => document with
        {
            Root = ExpandNode(document.Root, schema, registry, schemaCatalog, document.Root.Path ?? document.Root.Kind)
        };

    private static TreeNode ExpandNode(
        TreeNode node,
        MergeSchema schema,
        IFormatRegistry registry,
        IReadOnlyDictionary<string, MergeSchema> schemaCatalog,
        string path)
    {
        var children = node.Children
            .Select(child => ExpandNode(child, schema, registry, schemaCatalog, path + "/" + child.Kind))
            .ToList();
        var contentRule = schema.ContentRules.LastOrDefault(rule => rule.Path.IsMatch(path));

        if (contentRule is not null &&
            node.Value is not null &&
            registry.TryParse(contentRule.Format, node.Value, path, ResolveNestedSchema(schema, schemaCatalog, contentRule), out var nestedDocument))
        {
            var nestedSchema = ResolveNestedSchema(schema, schemaCatalog, contentRule);
            var expandedNestedDocument = Expand(nestedDocument, nestedSchema, registry, schemaCatalog);
            var contentFields = new Dictionary<string, string> { ["format"] = contentRule.Format };
            if (!string.IsNullOrWhiteSpace(contentRule.SchemaRef))
                contentFields["schemaRef"] = contentRule.SchemaRef;

            children.Add(new TreeNode(
                                        "$content",
                                        contentFields,
                                        children: [expandedNestedDocument.Root]));

            return node.WithValue(null).WithChildren(children);
        }

        return node.WithChildren(children);
    }

    private static MergeSchema ResolveNestedSchema(
        MergeSchema schema,
        IReadOnlyDictionary<string, MergeSchema> schemaCatalog,
        ContentRule contentRule)
    {
        if (string.IsNullOrWhiteSpace(contentRule.SchemaRef))
            return schema;

        if (schema.NestedSchemas.TryGetValue(contentRule.SchemaRef, out var nestedSchema))
            return nestedSchema;

        if (schemaCatalog.TryGetValue(contentRule.SchemaRef, out nestedSchema))
            return nestedSchema;

        throw new InvalidOperationException(
                            $"Content rule for path '{contentRule.Path.Pattern}' references unknown nested schema '{contentRule.SchemaRef}'.");
    }
}
