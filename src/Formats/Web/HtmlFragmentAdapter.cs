using System.Net;
using AngleSharp.Html.Parser;
using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Web;

public sealed class HtmlFragmentAdapter : IAstFormatAdapter
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    public string Format => "html:fragment";

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<body>{sourceText}</body>");
        var children = document.Body?.ChildNodes
            .Select((node, index) => ParseNode(node, index))
            .ToArray() ?? [];

        return new AstDocument(Format, new AstNode("$fragment", AstNodeMetadata.Create("fragment"), children: children), sourcePath, sourceText);
    }

    public string RenderDocument(AstDocument document) => RenderNode(document.Root);

    public string RenderNode(AstNode node)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);


        return RenderHtmlNode(node);
    }

    private static AstNode ParseNode(AngleSharp.Dom.INode node, int index) => node switch
    {
        AngleSharp.Dom.IElement element => ParseElement(element, index),
        AngleSharp.Dom.IText text => new AstNode(
            $"$text{index:D6}",
            AstNodeMetadata.Create("text"),
            text.Data),
        AngleSharp.Dom.IComment comment => new AstNode(
            $"$comment{index:D6}",
            AstNodeMetadata.Create("comment"),
            comment.Data),
        _ => new AstNode(
            $"$node{index:D6}",
            AstNodeMetadata.Create("raw"),
            node.TextContent)
    };

    private static AstNode ParseElement(AngleSharp.Dom.IElement element, int index)
    {
        var fields = AstNodeMetadata.Create("element", element.LocalName);
        foreach (var attribute in element.Attributes)
            fields[attribute.Name] = attribute.Value;


        var children = element.ChildNodes
                            .Select((child, childIndex) => ParseNode(child, childIndex))
                            .ToArray();

        return new AstNode($"{AstNodeMetadata.EncodeKind(element.LocalName)}{index:D6}", fields, children: children);
    }

    private static string RenderHtmlNode(AstNode node)
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

    private static string RenderElement(AstNode node)
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
