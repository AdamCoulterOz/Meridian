using Meridian.Core.Identity;
using Meridian.Core.Schema;
using Meridian.Core.Tree;

namespace Meridian.Core.Merging;

public sealed class StructuralDiffer
{
    private readonly IdentityAssigner _identityAssigner = new();

    public StructuralDiffResult Diff(DocumentTree oldDocument, DocumentTree newDocument, MergeSchema schema, ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(oldDocument);
        ArgumentNullException.ThrowIfNull(newDocument);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(renderer);

        var oldAssigned = _identityAssigner.Assign(oldDocument, schema);
        var newAssigned = _identityAssigner.Assign(newDocument, schema);
        var diagnostics = oldAssigned.Diagnostics
            .Concat(newAssigned.Diagnostics)
            .ToArray();

        if (diagnostics.Any(diagnostic => diagnostic.Severity == IdentityDiagnosticSeverity.Error))
            return new StructuralDiffResult(diagnostics, []);

        var entries = new List<StructuralDiffEntry>();
        DiffExistingNode(oldAssigned.Document.Root, newAssigned.Document.Root, schema, renderer, entries);

        return new StructuralDiffResult(diagnostics, entries);
    }

    private void DiffExistingNode(
        TreeNode oldNode,
        TreeNode newNode,
        MergeSchema schema,
        ITextRenderer renderer,
        List<StructuralDiffEntry> entries)
    {
        if (StructuralComparer.Equals(oldNode, newNode))
            return;

        if (!string.Equals(oldNode.Kind, newNode.Kind, StringComparison.Ordinal))
        {
            entries.Add(new StructuralDiffEntry(
                StructuralDiffKind.NodeChanged,
                DescribeNode(newNode),
                null,
                oldNode.Kind,
                newNode.Kind,
                renderer.RenderNode(oldNode),
                renderer.RenderNode(newNode)));
            return;
        }

        DiffFields(oldNode, newNode, entries);
        DiffValue(oldNode, newNode, entries);
        DiffChildren(oldNode, newNode, schema, renderer, entries);
    }

    private static void DiffFields(TreeNode oldNode, TreeNode newNode, List<StructuralDiffEntry> entries)
    {
        var fields = oldNode.Fields.Keys
            .Concat(newNode.Fields.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var field in fields)
        {
            oldNode.Fields.TryGetValue(field, out var oldValue);
            newNode.Fields.TryGetValue(field, out var newValue);

            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                continue;

            var kind = (oldValue, newValue) switch
            {
                (null, not null) => StructuralDiffKind.FieldAdded,
                (not null, null) => StructuralDiffKind.FieldRemoved,
                _ => StructuralDiffKind.FieldChanged
            };

            entries.Add(new StructuralDiffEntry(kind, DescribeNode(newNode), field, oldValue, newValue));
        }
    }

    private static void DiffValue(TreeNode oldNode, TreeNode newNode, List<StructuralDiffEntry> entries)
    {
        if (string.Equals(oldNode.Value, newNode.Value, StringComparison.Ordinal))
            return;

        var kind = (oldNode.Value, newNode.Value) switch
        {
            (null, not null) => StructuralDiffKind.ValueAdded,
            (not null, null) => StructuralDiffKind.ValueRemoved,
            _ => StructuralDiffKind.ValueChanged
        };

        entries.Add(new StructuralDiffEntry(kind, DescribeNode(newNode), null, oldNode.Value, newNode.Value));
    }

    private void DiffChildren(
        TreeNode oldNode,
        TreeNode newNode,
        MergeSchema schema,
        ITextRenderer renderer,
        List<StructuralDiffEntry> entries)
    {
        var oldById = ToIdentityMap(oldNode.Children);
        var newById = ToIdentityMap(newNode.Children);
        var identities = oldById.Keys
            .Concat(newById.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var identity in identities)
        {
            oldById.TryGetValue(identity, out var oldChild);
            newById.TryGetValue(identity, out var newChild);

            if (oldChild is null && newChild is not null)
            {
                entries.Add(new StructuralDiffEntry(
                    StructuralDiffKind.NodeAdded,
                    DescribeNode(newChild),
                    null,
                    null,
                    null,
                    null,
                    renderer.RenderNode(newChild)));
                continue;
            }

            if (oldChild is not null && newChild is null)
            {
                entries.Add(new StructuralDiffEntry(
                    StructuralDiffKind.NodeRemoved,
                    DescribeNode(oldChild),
                    null,
                    null,
                    null,
                    renderer.RenderNode(oldChild),
                    null));
                continue;
            }

            DiffExistingNode(oldChild!, newChild!, schema, renderer, entries);
        }

        if (!IsOrdered(oldNode, schema))
            return;

        var oldOrder = oldNode.Children.Select(DescribeIdentity).ToArray();
        var newOrder = newNode.Children.Select(DescribeIdentity).ToArray();
        if (oldOrder.SequenceEqual(newOrder, StringComparer.Ordinal))
            return;

        entries.Add(new StructuralDiffEntry(
            StructuralDiffKind.OrderedChildrenChanged,
            DescribeNode(newNode),
            null,
            string.Join(", ", oldOrder),
            string.Join(", ", newOrder)));
    }

    private static Dictionary<string, TreeNode> ToIdentityMap(IEnumerable<TreeNode> nodes) => nodes.ToDictionary(node => node.Identity ?? throw new InvalidOperationException("Tree node has no generated identity."), StringComparer.Ordinal);

    private static bool IsOrdered(TreeNode parent, MergeSchema schema)
    {
        var path = parent.Path ?? parent.Kind;
        return schema.OrderedChildren.Any(selector => selector.IsMatch(path));
    }

    private static string DescribeNode(TreeNode node)
    {
        var path = node.Path ?? node.Kind;
        var key = DescribeIdentity(node);

        return key == node.Kind || key == "path"
            ? path
            : path + "[" + key + "]";
    }

    private static string DescribeIdentity(TreeNode node)
    {
        if (node.Identity is null)
            return node.Kind;

        var open = node.Identity.LastIndexOf('[', StringComparison.Ordinal);
        if (open < 0 || !node.Identity.EndsWith(']'))
            return node.Kind;

        return node.Identity[(open + 1)..^1];
    }
}

public sealed record StructuralDiffResult(
    IReadOnlyList<IdentityDiagnostic> IdentityDiagnostics,
    IReadOnlyList<StructuralDiffEntry> Entries)
{
    public bool HasIdentityErrors => IdentityDiagnostics.Any(diagnostic => diagnostic.Severity == IdentityDiagnosticSeverity.Error);

    public bool HasDifferences => Entries.Count > 0;
}

public sealed record StructuralDiffEntry(
    StructuralDiffKind Kind,
    string Path,
    string? Field,
    string? OldValue,
    string? NewValue,
    string? OldText = null,
    string? NewText = null);

public enum StructuralDiffKind
{
    NodeAdded,
    NodeRemoved,
    NodeChanged,
    FieldAdded,
    FieldRemoved,
    FieldChanged,
    ValueAdded,
    ValueRemoved,
    ValueChanged,
    OrderedChildrenChanged
}
