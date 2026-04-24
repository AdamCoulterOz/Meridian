using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Structured;

public abstract class TextAstFormatAdapter : IAstFormatAdapter
{
    protected TextAstFormatAdapter(string format, string rootKind)
    {
        Format = format;
        RootKind = rootKind;
    }

    public string Format { get; }

    protected string RootKind { get; }

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new AstDocument(Format, new AstNode(RootKind, FormatAstUtilities.HiddenFields("text"), sourceText), sourcePath, sourceText);
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

public sealed class TemplateTextAstFormatAdapter : TextAstFormatAdapter
{
    public TemplateTextAstFormatAdapter()
        : base("template-text", "$template")
    {
    }
}

public sealed class RawAstFormatAdapter : TextAstFormatAdapter
{
    public RawAstFormatAdapter()
        : base("raw", "$raw")
    {
    }
}

public sealed class CssAstFormatAdapter : TextAstFormatAdapter
{
    public CssAstFormatAdapter()
        : base("css", "$css")
    {
    }
}

public sealed class PngAstFormatAdapter : TextAstFormatAdapter
{
    public PngAstFormatAdapter()
        : base("image:png", "$png")
    {
    }
}

public sealed class JpgAstFormatAdapter : TextAstFormatAdapter
{
    public JpgAstFormatAdapter()
        : base("image:jpg", "$jpg")
    {
    }
}

public sealed class GifAstFormatAdapter : TextAstFormatAdapter
{
    public GifAstFormatAdapter()
        : base("image:gif", "$gif")
    {
    }
}

public sealed class IcoAstFormatAdapter : TextAstFormatAdapter
{
    public IcoAstFormatAdapter()
        : base("image:ico", "$ico")
    {
    }
}

public sealed class XapAstFormatAdapter : TextAstFormatAdapter
{
    public XapAstFormatAdapter()
        : base("xap", "$xap")
    {
    }
}
