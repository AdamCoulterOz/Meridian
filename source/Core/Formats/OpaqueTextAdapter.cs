using Meridian.Core.Tree;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

/// <summary>
/// Base class for formats that are merged as a single opaque text scalar.
/// The whole content is one node value, so the three-way merge resolves to the
/// changed side when only one side changed and emits Git conflict markers when
/// both sides change it differently. Use this for genuinely unstructured text.
/// </summary>
public abstract class OpaqueTextAdapter : IFormatAdapter
{
    public abstract string Format { get; }

    protected abstract string RootKind { get; }

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new DocumentTree(
            Format,
            new TreeNode(RootKind, new Dictionary<string, string>(StringComparer.Ordinal) { ["$type"] = "text" }, sourceText),
            sourcePath,
            sourceText);
    }

    public string RenderDocument(DocumentTree document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RenderNode(document.Root);
    }

    public string RenderNode(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Conflict is null
            ? node.Value ?? string.Empty
            : ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);
    }
}
