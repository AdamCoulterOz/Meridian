using System.Net;
using AngleSharp.Html.Parser;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace MeridianGit.Formats.Html;

public sealed class HtmlFragmentAdapter : IFormatAdapter
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

    public string Format => "html:fragment";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        // A full HTML document (doctype / <html> / <head>) is not a body fragment; parsing it
        // in body context would foster-parent the head, drop the doctype, and destroy structure.
        // Preserve it as an exact-round-trip opaque document instead of silently mangling it.
        if (IsFullDocument(sourceText))
            return new DocumentTree(
                Format,
                new TreeNode("$document", NodeMetadata.Create("raw"), sourceText),
                sourcePath,
                sourceText);

        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<body>{sourceText}</body>");
        var children = document.Body?.ChildNodes
            .Select((node, index) => ParseNode(node, index))
            .ToArray() ?? [];

        return new DocumentTree(Format, new TreeNode("$fragment", NodeMetadata.Create("fragment"), children: children), sourcePath, sourceText);
    }

    private static bool IsFullDocument(string sourceText)
    {
        var trimmed = sourceText.TrimStart();
        // Match a real <html> root or a doctype only — not fragments that merely start with a tag
        // whose name shares a prefix (e.g. <header>, which must still merge structurally).
        return trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
            || StartsWithTag(trimmed, "html");
    }

    private static bool StartsWithTag(string text, string tag)
    {
        if (!text.StartsWith("<" + tag, StringComparison.OrdinalIgnoreCase))
            return false;

        var afterIndex = tag.Length + 1;
        if (afterIndex >= text.Length)
            return true;

        var after = text[afterIndex];
        return after == '>' || after == '/' || char.IsWhiteSpace(after);
    }

    // HTML attribute names may begin with '$', which would collide with the $type/$name metadata
    // sentinels. Escape a name starting with '$' (or with the '=' escape char itself, so the
    // mapping stays reversible) by prefixing '='; decoding strips exactly that one prefix. Ordinary
    // names (id, class, ...) are untouched so identity rules keep matching them.
    private static string EncodeAttributeName(string name) =>
        name.StartsWith('$') || name.StartsWith('=') ? "=" + name : name;

    private static string DecodeAttributeName(string key) => key.StartsWith('=') ? key[1..] : key;

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        return RenderHtmlNode(node);
    }

    private static TreeNode ParseNode(AngleSharp.Dom.INode node, int index) => node switch
    {
        AngleSharp.Dom.IElement element => ParseElement(element, index),
        AngleSharp.Dom.IText text => new TreeNode(
            $"$text{index:D6}",
            NodeMetadata.Create("text"),
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

    private static TreeNode ParseElement(AngleSharp.Dom.IElement element, int index)
    {
        var fields = NodeMetadata.Create("element", element.LocalName);
        foreach (var attribute in element.Attributes)
            fields[EncodeAttributeName(attribute.Name)] = attribute.Value;

        var children = element.ChildNodes
                            .Select((child, childIndex) => ParseNode(child, childIndex))
                            .ToArray();

        return new TreeNode($"{NodeMetadata.EncodeKind(element.LocalName)}{index:D6}", fields, children: children);
    }

    private static string RenderHtmlNode(TreeNode node) => RenderHtmlNode(node, rawTextParent: false);

    private static string RenderHtmlNode(TreeNode node, bool rawTextParent)
    {
        var type = node.TryGetMetadataType(out var nodeType)
            ? nodeType
            : "fragment";

        return type switch
        {
            "fragment" => string.Concat(node.Children.Select(child => RenderHtmlNode(child, rawTextParent: false))),
            "text" => rawTextParent ? node.Value ?? string.Empty : WebUtility.HtmlEncode(node.Value ?? string.Empty),
            "comment" => $"<!--{node.Value ?? string.Empty}-->",
            "element" => RenderElement(node),
            "raw" => node.Value ?? string.Empty,
            _ => string.Concat(node.Children.Select(child => RenderHtmlNode(child, rawTextParent: false)))
        };
    }

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
}
