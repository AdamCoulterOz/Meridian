using Meridian.Core.Tree;
using Meridian.Core.Identity;
using Meridian.Core.Schema;

namespace Meridian.Core.Merging;

public sealed class Merger
{
    private readonly IdentityAssigner _identityAssigner = new();

    public MergeResult Merge(DocumentTree @base, DocumentTree ours, DocumentTree theirs, MergeSchema schema, ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(@base);
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(renderer);

        var baseAssigned = _identityAssigner.Assign(@base, schema);
        var oursAssigned = _identityAssigner.Assign(ours, schema);
        var theirsAssigned = _identityAssigner.Assign(theirs, schema);

        var diagnostics = baseAssigned.Diagnostics
            .Concat(oursAssigned.Diagnostics)
            .Concat(theirsAssigned.Diagnostics)
            .ToArray();

        if (diagnostics.Any(diagnostic => diagnostic.Severity == IdentityDiagnosticSeverity.Error))
        {
            var conflict = new MergeConflict(
                ConflictKind.AmbiguousIdentity,
                baseAssigned.Document.Root.Path ?? baseAssigned.Document.Root.Kind,
                renderer.RenderNode(baseAssigned.Document.Root),
                renderer.RenderNode(oursAssigned.Document.Root),
                renderer.RenderNode(theirsAssigned.Document.Root),
                "Cannot merge while one or more tree nodes have ambiguous generated identities.");

            var conflictRoot = TreeNode.ConflictNode(
                baseAssigned.Document.Root.Kind,
                baseAssigned.Document.Root.Path ?? baseAssigned.Document.Root.Kind,
                baseAssigned.Document.Root.Identity ?? "/" + baseAssigned.Document.Root.Kind,
                conflict);

            return new MergeResult(@base with { Root = conflictRoot }, diagnostics, [conflict]);
        }

        var conflicts = new List<MergeConflict>();
        var mergedRoot = MergeExistingNode(
            baseAssigned.Document.Root,
            oursAssigned.Document.Root,
            theirsAssigned.Document.Root,
            schema,
            renderer,
            conflicts);

        return new MergeResult(ours with { Root = mergedRoot }, diagnostics, conflicts);
    }

    private TreeNode MergeExistingNode(
        TreeNode @base,
        TreeNode ours,
        TreeNode theirs,
        MergeSchema schema,
        ITextRenderer renderer,
        List<MergeConflict> conflicts)
    {
        if (StructuralComparer.Equals(ours, theirs))
            return ours;

        if (StructuralComparer.Equals(@base, ours))
            return theirs;

        if (StructuralComparer.Equals(@base, theirs))
            return ours;

        var mergedFields = MergeFields(@base, ours, theirs, renderer, conflicts);
        if (mergedFields is null)
        {
            var conflict = CreateNodeConflict(@base, ours, theirs, renderer, "Both sides changed node fields differently.");
            conflicts.Add(conflict);
            return TreeNode.ConflictNode(ours.Kind, ours.Path ?? @base.Path ?? ours.Kind, ours.Identity ?? @base.Identity ?? ours.Kind, conflict);
        }

        var mergedValue = MergeScalar(@base.Value, ours.Value, theirs.Value);
        if (mergedValue.HasConflict)
        {
            var conflict = new MergeConflict(
                ConflictKind.Scalar,
                ours.Path ?? @base.Path ?? ours.Kind,
                @base.Value,
                ours.Value,
                theirs.Value,
                "Both sides changed scalar content differently.");
            conflicts.Add(conflict);
            return ours with { Value = null, Children = [], Conflict = conflict };
        }

        var mergedChildren = MergeChildren(@base, ours, theirs, schema, renderer, conflicts);
        if (mergedChildren.HasConflict)
        {
            var conflict = new MergeConflict(
                ConflictKind.OrderedChildren,
                ours.Path ?? @base.Path ?? ours.Kind,
                renderer.RenderNode(@base),
                renderer.RenderNode(ours),
                renderer.RenderNode(theirs),
                "Both sides changed ordered children differently.");
            conflicts.Add(conflict);
            return TreeNode.ConflictNode(ours.Kind, ours.Path ?? @base.Path ?? ours.Kind, ours.Identity ?? @base.Identity ?? ours.Kind, conflict);
        }

        return ours
            .WithFields(mergedFields)
            .WithValue(mergedValue.Value)
            .WithChildren(mergedChildren.Children);
    }

    private static IReadOnlyDictionary<string, string>? MergeFields(
        TreeNode @base,
        TreeNode ours,
        TreeNode theirs,
        ITextRenderer renderer,
        List<MergeConflict> conflicts)
    {
        var fields = ours.Fields.Keys
            .Concat(theirs.Fields.Keys)
            .Concat(@base.Fields.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            @base.Fields.TryGetValue(field, out var baseValue);
            ours.Fields.TryGetValue(field, out var oursValue);
            theirs.Fields.TryGetValue(field, out var theirsValue);

            var scalar = MergeScalar(baseValue, oursValue, theirsValue);
            if (scalar.HasConflict)
                return null;

            if (scalar.Value is not null)
                merged[field] = scalar.Value;
        }

        return merged;
    }

    private ChildrenMergeResult MergeChildren(
        TreeNode @base,
        TreeNode ours,
        TreeNode theirs,
        MergeSchema schema,
        ITextRenderer renderer,
        List<MergeConflict> conflicts)
    {
        var baseById = ToIdentityMap(@base.Children);
        var oursById = ToIdentityMap(ours.Children);
        var theirsById = ToIdentityMap(theirs.Children);
        var mergedById = new Dictionary<string, TreeNode>(StringComparer.Ordinal);

        var identities = baseById.Keys
            .Concat(oursById.Keys)
            .Concat(theirsById.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var identity in identities)
        {
            baseById.TryGetValue(identity, out var baseNode);
            oursById.TryGetValue(identity, out var oursNode);
            theirsById.TryGetValue(identity, out var theirsNode);

            var merged = MergeChild(identity, baseNode, oursNode, theirsNode, schema, renderer, conflicts);
            if (merged is not null)
                mergedById[identity] = merged;
        }

        var baseOrder = @base.Children.Select(child => child.Identity!).ToArray();
        var oursOrder = ours.Children.Select(child => child.Identity!).Where(mergedById.ContainsKey).ToArray();
        var theirsOrder = theirs.Children.Select(child => child.Identity!).Where(mergedById.ContainsKey).ToArray();
        var mergedOrder = IsOrdered(@base, schema)
            ? MergeOrderedIdentities(baseOrder, oursOrder, theirsOrder)
            : MergeUnorderedIdentities(oursOrder, theirsOrder, identities, mergedById);

        if (mergedOrder.HasConflict)
            return new ChildrenMergeResult([], HasConflict: true);

        return new ChildrenMergeResult(mergedOrder.Identities.Select(identity => mergedById[identity]).ToArray(), HasConflict: false);
    }

    private TreeNode? MergeChild(
        string identity,
        TreeNode? @base,
        TreeNode? ours,
        TreeNode? theirs,
        MergeSchema schema,
        ITextRenderer renderer,
        List<MergeConflict> conflicts)
    {
        if (@base is null)
        {
            if (ours is null)
                return theirs;

            if (theirs is null || StructuralComparer.Equals(ours, theirs))
                return ours;

            var conflict = CreateNodeConflict(null, ours, theirs, renderer, "Both sides added the same identity differently.");
            conflicts.Add(conflict);
            return TreeNode.ConflictNode(ours.Kind, ours.Path ?? identity, identity, conflict);
        }

        if (ours is null && theirs is null)
            return null;

        if (ours is null)
        {
            if (StructuralComparer.Equals(@base, theirs))
                return null;

            var conflict = CreateNodeConflict(@base, null, theirs, renderer, "One side deleted a node while the other side changed it.");
            conflicts.Add(conflict);
            return TreeNode.ConflictNode(@base.Kind, @base.Path ?? identity, identity, conflict);
        }

        if (theirs is null)
        {
            if (StructuralComparer.Equals(@base, ours))
                return null;

            var conflict = CreateNodeConflict(@base, ours, null, renderer, "One side changed a node while the other side deleted it.");
            conflicts.Add(conflict);
            return TreeNode.ConflictNode(@base.Kind, @base.Path ?? identity, identity, conflict);
        }

        return MergeExistingNode(@base, ours, theirs, schema, renderer, conflicts);
    }

    private static Dictionary<string, TreeNode> ToIdentityMap(IEnumerable<TreeNode> nodes) => nodes.ToDictionary(node => node.Identity ?? throw new InvalidOperationException("Tree node has no generated identity."), StringComparer.Ordinal);

    private static bool IsOrdered(TreeNode parent, MergeSchema schema)
    {
        var path = parent.Path ?? parent.Kind;
        return schema.OrderedChildren.Any(selector => selector.IsMatch(path));
    }

    private static ScalarMergeResult MergeScalar(string? @base, string? ours, string? theirs)
    {
        if (string.Equals(ours, theirs, StringComparison.Ordinal))
            return new ScalarMergeResult(ours, HasConflict: false);

        if (string.Equals(@base, ours, StringComparison.Ordinal))
            return new ScalarMergeResult(theirs, HasConflict: false);

        if (string.Equals(@base, theirs, StringComparison.Ordinal))
            return new ScalarMergeResult(ours, HasConflict: false);

        return new ScalarMergeResult(null, HasConflict: true);
    }

    private static SequenceMergeResult MergeOrderedIdentities(
        IReadOnlyList<string> @base,
        IReadOnlyList<string> ours,
        IReadOnlyList<string> theirs)
    {
        if (ours.SequenceEqual(theirs, StringComparer.Ordinal))
            return new SequenceMergeResult(ours, HasConflict: false);

        if (@base.SequenceEqual(ours, StringComparer.Ordinal))
            return new SequenceMergeResult(theirs, HasConflict: false);

        if (@base.SequenceEqual(theirs, StringComparer.Ordinal))
            return new SequenceMergeResult(ours, HasConflict: false);

        return new SequenceMergeResult([], HasConflict: true);
    }

    private static SequenceMergeResult MergeUnorderedIdentities(
        IReadOnlyList<string> ours,
        IReadOnlyList<string> theirs,
        IReadOnlyList<string> allIdentities,
        IReadOnlyDictionary<string, TreeNode> mergedById)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<string>();

        foreach (var identity in ours.Concat(theirs).Concat(allIdentities))
            if (mergedById.ContainsKey(identity) && emitted.Add(identity))
                merged.Add(identity);

        return new SequenceMergeResult(merged, HasConflict: false);
    }

    private static MergeConflict CreateNodeConflict(
        TreeNode? @base,
        TreeNode? ours,
        TreeNode? theirs,
        ITextRenderer renderer,
        string message) => new(
            ConflictKind.Node,
            ours?.Path ?? theirs?.Path ?? @base?.Path ?? "<unknown>",
            @base is null ? null : renderer.RenderNode(@base),
            ours is null ? null : renderer.RenderNode(ours),
            theirs is null ? null : renderer.RenderNode(theirs),
            message);

    private sealed record ScalarMergeResult(string? Value, bool HasConflict);

    private sealed record ChildrenMergeResult(IReadOnlyList<TreeNode> Children, bool HasConflict);

    private sealed record SequenceMergeResult(IReadOnlyList<string> Identities, bool HasConflict);
}

public sealed record MergeResult(
    DocumentTree Document,
    IReadOnlyList<IdentityDiagnostic> IdentityDiagnostics,
    IReadOnlyList<MergeConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}
