using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Mapped;

public sealed class MappedTextAdapter : IAstFormatAdapter
{
    public string Format => "mapped-text";

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new AstDocument(Format, new AstNode("$mapped", new Dictionary<string, string> { ["$type"] = "text" }, sourceText), sourcePath, sourceText);
    }

    public string RenderDocument(AstDocument document) => RenderNode(document.Root);

    public string RenderNode(AstNode node) => node.Conflict is null
            ? node.Value ?? string.Empty
            : ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);
}
