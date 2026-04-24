using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Images;

public sealed class JpgAstFormatAdapter : IAstFormatAdapter
{
    public string Format => "image:jpg";

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new AstDocument(Format, new AstNode("$jpg", new Dictionary<string, string> { ["$type"] = "text" }, sourceText), sourcePath, sourceText);
    }

    public string RenderDocument(AstDocument document)
    {
        return RenderNode(document.Root);
    }

    public string RenderNode(AstNode node)
    {
        return node.Conflict is null
            ? node.Value ?? string.Empty
            : ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);
    }
}
