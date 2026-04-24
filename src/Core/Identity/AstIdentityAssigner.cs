using Meridian.Core.Ast;
using Meridian.Core.Schema;
using Meridian.Core.Mapped;

namespace Meridian.Core.Identity;

public sealed class AstIdentityAssigner
{
    public IdentityAssignmentResult Assign(AstDocument document, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);

        var diagnostics = new List<IdentityDiagnostic>();
        var rootPath = document.Root.Kind;
        var rootIdentity = "/" + document.Root.Kind;
        var root = AssignNode(document.Root, schema, rootPath, rootIdentity, diagnostics);

        return new IdentityAssignmentResult(document with { Root = root }, diagnostics);
    }

    private AstNode AssignNode(
        AstNode node,
        AstSchema schema,
        string path,
        string identity,
        List<IdentityDiagnostic> diagnostics)
    {
        var assignedChildren = new List<AstNode>(node.Children.Count);
        var siblingIdentityCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var childPath = path + "/" + child.Kind;
            var childKey = ResolveKey(child, schema, childPath, index);
            var childIdentity = identity + "/" + child.Kind + "[" + childKey.Value + "]";

            siblingIdentityCounts.TryGetValue(childIdentity, out var count);
            siblingIdentityCounts[childIdentity] = count + 1;

            assignedChildren.Add(AssignNode(child, schema, childPath, childIdentity, diagnostics));
        }

        foreach (var duplicate in siblingIdentityCounts.Where(item => item.Value > 1))
            diagnostics.Add(new IdentityDiagnostic(
                IdentityDiagnosticSeverity.Error,
                path,
                duplicate.Key,
                $"Generated identity is ambiguous for {duplicate.Value} sibling nodes."));


        return node.WithPathAndIdentity(path, identity).WithChildren(assignedChildren);
    }

    private static ResolvedKey ResolveKey(AstNode node, AstSchema schema, string path, int ordinal)
    {
        var explicitRule = schema.IdentityRules.LastOrDefault(rule => rule.Path.IsMatch(path));
        if (explicitRule is not null)
            return ResolveExplicitKey(node, explicitRule.Key, ordinal);


        foreach (var field in schema.GlobalDiscriminatorFields)
            if (node.Fields.TryGetValue(field, out var value) && !string.IsNullOrEmpty(value))
                return new ResolvedKey(field + "=" + value);


        if (node.Fields.TryGetValue(MappedTokenFields.SemanticKey, out var semanticKey) &&
                            !string.IsNullOrEmpty(semanticKey))
            return new ResolvedKey(MappedTokenFields.SemanticKey + "=" + semanticKey);


        return new ResolvedKey("path");
    }

    private static ResolvedKey ResolveExplicitKey(AstNode node, DiscriminatorKey key, int ordinal) => key switch
    {
        DiscriminatorKey.Field field => ResolveField(node, field.Name),
        DiscriminatorKey.PathValue pathValue => ResolvePathValue(node, pathValue.Path),
        DiscriminatorKey.Text => new ResolvedKey("text=" + (node.Value ?? string.Empty)),
        DiscriminatorKey.Structural { Strategy: StructuralDiscriminator.OrderedSlot } => new ResolvedKey("slot=" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        DiscriminatorKey.Composite composite => ResolveComposite(node, composite),
        _ => new ResolvedKey("path")
    };

    private static ResolvedKey ResolveField(AstNode node, string field) => node.Fields.TryGetValue(field, out var value) && !string.IsNullOrEmpty(value)
            ? new ResolvedKey(field + "=" + value)
            : new ResolvedKey("missing:" + field);

    private static ResolvedKey ResolvePathValue(AstNode node, string path)
    {
        var value = AstPath.ReadValue(node, path);
        return !string.IsNullOrEmpty(value)
            ? new ResolvedKey(path + "=" + value)
            : new ResolvedKey("missing:" + path);
    }

    private static ResolvedKey ResolveComposite(AstNode node, DiscriminatorKey.Composite composite)
    {
        var parts = new List<string>(composite.Parts.Count);

        foreach (var part in composite.Parts)
        {
            var resolved = ResolveExplicitKey(node, part.Key, ordinal: 0).Value;
            if (!resolved.StartsWith("missing:", StringComparison.Ordinal) && !string.IsNullOrEmpty(resolved))
            {
                parts.Add(resolved);
                continue;
            }

            if (!part.Optional)
                parts.Add(resolved);

        }

        return new ResolvedKey(string.Join("+", parts));
    }

    private sealed record ResolvedKey(string Value);
}

public sealed record IdentityAssignmentResult(AstDocument Document, IReadOnlyList<IdentityDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == IdentityDiagnosticSeverity.Error);
}

public sealed record IdentityDiagnostic(
    IdentityDiagnosticSeverity Severity,
    string Path,
    string Identity,
    string Message);

public enum IdentityDiagnosticSeverity
{
    Warning,
    Error
}
