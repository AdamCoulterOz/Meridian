using Meridian.Core.Ast;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public sealed class NestedContentExpander
{
    public AstDocument Expand(AstDocument document, AstSchema schema, IAstFormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(registry);

        return Expand(document, schema, registry, schema.NestedSchemas);
    }

    private static AstDocument Expand(
        AstDocument document,
        AstSchema schema,
        IAstFormatRegistry registry,
        IReadOnlyDictionary<string, AstSchema> schemaCatalog) => document with
        {
            Root = ExpandNode(document.Root, schema, registry, schemaCatalog, document.Root.Path ?? document.Root.Kind)
        };

    private static AstNode ExpandNode(
        AstNode node,
        AstSchema schema,
        IAstFormatRegistry registry,
        IReadOnlyDictionary<string, AstSchema> schemaCatalog,
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


            children.Add(new AstNode(
                                        "$content",
                                        contentFields,
                                        children: new[] { expandedNestedDocument.Root }));

            return node.WithValue(null).WithChildren(children);
        }

        return node.WithChildren(children);
    }

    private static AstSchema ResolveNestedSchema(
        AstSchema schema,
        IReadOnlyDictionary<string, AstSchema> schemaCatalog,
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
