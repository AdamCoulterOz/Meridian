using System.Net;
using AngleSharp.Html.Parser;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Web;

public sealed class HtmlFragmentAdapter : IFormatAdapter
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    public string Format => "html:fragment";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<body>{sourceText}</body>");
        var children = document.Body?.ChildNodes
            .Select((node, index) => ParseNode(node, index))
            .ToArray() ?? [];

        return new DocumentTree(Format, new TreeNode("$fragment", NodeMetadata.Create("fragment"), children: children), sourcePath, sourceText);
    }

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
            fields[attribute.Name] = attribute.Value;

        var children = element.ChildNodes
                            .Select((child, childIndex) => ParseNode(child, childIndex))
                            .ToArray();

        return new TreeNode($"{NodeMetadata.EncodeKind(element.LocalName)}{index:D6}", fields, children: children);
    }

    private static string RenderHtmlNode(TreeNode node)
    {
        var type = node.TryGetMetadataType(out var nodeType)
            ? nodeType
            : "fragment";

        return type switch
        {
            "fragment" => string.Concat(node.Children.Select(RenderHtmlNode)),
            "text" => WebUtility.HtmlEncode(node.Value ?? string.Empty),
            "comment" => $"<!--{node.Value ?? string.Empty}-->",
            "element" => RenderElement(node),
            "raw" => node.Value ?? string.Empty,
            _ => string.Concat(node.Children.Select(RenderHtmlNode))
        };
    }

    private static string RenderElement(TreeNode node)
    {
        var tag = node.GetMetadataName();
        var attributes = node.VisibleFields()
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => $" {field.Key}=\"{WebUtility.HtmlEncode(field.Value)}\"");
        var start = $"<{tag}{string.Concat(attributes)}>";

        if (VoidElements.Contains(tag))
            return start;

        return start + string.Concat(node.Children.Select(RenderHtmlNode)) + $"</{tag}>";
    }
}
