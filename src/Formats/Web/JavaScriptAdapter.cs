using Esprima;
using Esprima.Utils;
using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Web;

public sealed class JavaScriptAdapter : IFormatAdapter
{
    public string Format => "javascript";

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var parser = new JavaScriptParser(new ParserOptions { Tolerant = false });
        var program = parser.ParseScript(sourceText, sourcePath);
        var fields = AstNodeMetadata.Create("script");
        fields["parser"] = "esprima";
        fields["sourceType"] = program.SourceType.ToString();
        fields["bodyCount"] = program.Body.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new AstDocument(
            Format,
            new AstNode("$javascript", fields, sourceText, sourceText: program.ToJsonString(indent: "  ")),
            sourcePath,
            sourceText);
    }

    public string RenderDocument(AstDocument document) => RenderNode(document.Root);

    public string RenderNode(AstNode node) => node.Conflict is null
            ? node.Value ?? string.Empty
            : ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);
}
