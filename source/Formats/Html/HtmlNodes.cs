using System.Net;
using Meridian.Core.Tree;

namespace MeridianGit.Formats.Html;

// Node-level parsing and rendering shared by the two HTML shapes: html:fragment (a body-context
// snippet, rooted at $fragment) and html:document (a complete page, rooted at $document). Only the
// roots differ; every node beneath them is parsed and rendered identically.
internal static class HtmlNodes
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    // Elements whose content the HTML spec treats as raw text (or escapable raw text):
    // their character data must be emitted verbatim, never HTML-entity-encoded, or the
    // embedded script/style/text is corrupted.
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes", "noscript", "plaintext"
    };

    public const string TextType = "text";

    public static TreeNode ParseNode(AngleSharp.Dom.INode node, int index) => node switch
    {
        AngleSharp.Dom.IElement element => ParseElement(element, index),
        AngleSharp.Dom.IText text => new TreeNode(
            $"$text{index:D6}",
            NodeMetadata.Create(TextType),
            text.Data),
        AngleSharp.Dom.IComment comment => new TreeNode(
            $"$comment{index:D6}",
            NodeMetadata.Create("comment"),
            comment.Data),
        _ => new TreeNode(
            $"$node{index:D6}",
            NodeMetadata.Create("raw"),
            node.TextContent)
    };

    public static TreeNode ParseElement(AngleSharp.Dom.IElement element, int index)
    {
        var fields = NodeMetadata.Create("element", element.LocalName);
        foreach (var attribute in element.Attributes)
            fields[EncodeAttributeName(attribute.Name)] = attribute.Value;

        var children = element.ChildNodes
                            .Select((child, childIndex) => ParseNode(child, childIndex))
                            .ToArray();

        return new TreeNode($"{NodeMetadata.EncodeKind(element.LocalName)}{index:D6}", fields, children: children);
    }

    public static string Render(TreeNode node) => RenderHtmlNode(node, rawTextParent: false);

    private static string RenderHtmlNode(TreeNode node, bool rawTextParent)
    {
        var type = node.TryGetMetadataType(out var nodeType)
            ? nodeType
            : "fragment";

        return type switch
        {
            "fragment" or "document" => RenderChildren(node),
            TextType => rawTextParent ? node.Value ?? string.Empty : WebUtility.HtmlEncode(node.Value ?? string.Empty),
            "comment" => $"<!--{node.Value ?? string.Empty}-->",
            "element" => RenderElement(node),
            "raw" => node.Value ?? string.Empty,
            _ => RenderChildren(node)
        };
    }

    private static string RenderChildren(TreeNode node) =>
        string.Concat(node.Children.Select(child => RenderHtmlNode(child, rawTextParent: false)));

    private static string RenderElement(TreeNode node)
    {
        var tag = node.GetMetadataName();
        var attributes = node.VisibleFields()
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => $" {DecodeAttributeName(field.Key)}=\"{WebUtility.HtmlEncode(field.Value)}\"");
        var start = $"<{tag}{string.Concat(attributes)}>";

        if (VoidElements.Contains(tag))
            return start;

        var rawText = RawTextElements.Contains(tag);
        return start + string.Concat(node.Children.Select(child => RenderHtmlNode(child, rawText))) + $"</{tag}>";
    }

    // HTML attribute names may begin with '$', which would collide with the $type/$name metadata
    // sentinels. Escape a name starting with '$' (or with the '=' escape char itself, so the
    // mapping stays reversible) by prefixing '='; decoding strips exactly that one prefix. Ordinary
    // names (id, class, ...) are untouched so identity rules keep matching them.
    private static string EncodeAttributeName(string name) =>
        name.StartsWith('$') || name.StartsWith('=') ? "=" + name : name;

    private static string DecodeAttributeName(string key) => key.StartsWith('=') ? key[1..] : key;
}
