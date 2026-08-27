using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace MeridianGit.Formats.Html;

/// <summary>
/// Structural adapter for an HTML FRAGMENT: markup that lives in body context, such as a web
/// resource, an email body, or nested content inside another document.
/// </summary>
/// <remarks>
/// This is also the adapter registered for <c>.html</c> and <c>.htm</c> files, whose content decides
/// the shape: a complete page (doctype or an <c>&lt;html&gt;</c> root) is handed to
/// <see cref="HtmlDocumentAdapter"/>, which parses it as a real document. Parsing a full page in
/// body context foster-parents the head and drops the doctype and wrappers, so the two shapes must
/// never share a parser entry point.
/// </remarks>
public sealed class HtmlFragmentAdapter : IFormatAdapter
{
    private const string BodyTag = "<body>";

    private readonly HtmlDocumentAdapter _documentAdapter = new();

    public const string RootKind = "$fragment";

    public string Format => "html:fragment";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        if (HtmlDocumentAdapter.IsFullDocument(sourceText))
            return _documentAdapter.Parse(sourceText, sourcePath, schema);

        // Body context is what makes a snippet parse as a snippet. The wrapper only shifts offsets,
        // so the source the nodes are read back from is the wrapped text.
        var wrapped = $"<body>{sourceText}</body>";
        var document = HtmlNodes.CreateParser().ParseDocument(wrapped);
        var children = document.Body is { } body
            ? HtmlNodes.ParseChildren(body, wrapped, BodyTag.Length)
            : [];

        return new DocumentTree(Format, new TreeNode(RootKind, NodeMetadata.Create("fragment"), children: children), sourcePath, sourceText);
    }

    public string RenderDocument(DocumentTree document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RenderNode(document.Root);
    }

    public string RenderNode(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        // Fragment and document roots render through the same node renderer; it dispatches on the
        // node's $type, so this renders whichever shape Parse produced.
        return HtmlNodes.Render(node);
    }
}
