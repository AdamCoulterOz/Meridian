using Meridian.Core.Tree;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public sealed class RawAdapter : IFormatAdapter
{
    public string Format => "raw";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new DocumentTree(Format, new TreeNode("$raw", new Dictionary<string, string> { ["$type"] = "text" }, sourceText), sourcePath, sourceText);
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node) => node.Conflict is null
            ? node.Value ?? string.Empty
            : ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);
}
