using Meridian.Core.Ast;
using Meridian.Core.Formats;

namespace Meridian.Core.Formats.Nested;

public sealed class NestedContentCollapser
{
    public static AstDocument Collapse(AstDocument document, IFormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);

        return document with { Root = CollapseNode(document.Root, registry) };
    }

    private static AstNode CollapseNode(AstNode node, IFormatRegistry registry)
    {
        var collapsedChildren = node.Children
            .Select(child => CollapseNode(child, registry))
            .ToArray();
        var contentChildren = collapsedChildren
            .Where(child => string.Equals(child.Kind, "$content", StringComparison.Ordinal))
            .ToArray();

        if (contentChildren.Length == 0)
            return node.WithChildren(collapsedChildren);

        if (contentChildren.Length > 1)
            throw new InvalidOperationException($"Node '{node.Path ?? node.Kind}' has multiple nested content children.");

        var content = contentChildren[0];
        if (!content.Fields.TryGetValue("format", out var format) || string.IsNullOrWhiteSpace(format))
            throw new InvalidOperationException($"Nested content under '{node.Path ?? node.Kind}' does not declare a format.");

        if (content.Children.Count != 1)
            throw new InvalidOperationException($"Nested content under '{node.Path ?? node.Kind}' must contain exactly one AST root.");

        var nestedRoot = content.Children[0];
        if (HasConflict(nestedRoot))
            throw new InvalidOperationException(
                $"Cannot collapse unresolved nested content conflicts under '{node.Path ?? node.Kind}'. " +
                "Project the conflict at the owning encoded scalar boundary before rendering.");

        var nestedDocument = new AstDocument(format, nestedRoot);
        if (!registry.TryRender(format, nestedDocument, out var renderedContent))
            throw new InvalidOperationException($"No AST renderer is registered for nested content format '{format}'.");

        var remainingChildren = collapsedChildren
                            .Where(child => !string.Equals(child.Kind, "$content", StringComparison.Ordinal))
                            .ToArray();
        return node
            .WithValue(renderedContent)
            .WithChildren(remainingChildren);
    }

    private static bool HasConflict(AstNode node) => node.IsConflict || node.Children.Any(HasConflict);
}
