using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Core.Templates;

namespace Meridian.Formats.Liquid;

public class LiquidAdapter : ITemplateEngineAstFormatAdapter
{
    private const string OpenField = "open";
    private const string CloseField = "close";
    private const string StartTagField = "startTag";
    private const string EndTagField = "endTag";
    private const string TagNameField = "tagName";

    public string Format => "liquid:multi";

    public string EngineName => "liquid";

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        return new AstDocument(
            Format,
            new AstNode("$liquid", AstNodeMetadata.Create("template"), children: ParseTokens(sourceText)),
            sourcePath,
            sourceText);
    }

    public string RenderDocument(AstDocument document)
    {
        return RenderNode(document.Root);
    }

    public string RenderNode(AstNode node)
    {
        if (node.Conflict is not null)
        {
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);
        }

        var type = node.TryGetMetadataType(out var nodeType)
            ? nodeType
            : "template";

        return type switch
        {
            "template" => string.Concat(node.Children.Select(RenderNode)),
            "text" => node.Value ?? string.Empty,
            "output" or "tag" => RenderInlineLiquidToken(node),
            "rawBlock" or "commentBlock" => RenderBlockLiquidToken(node),
            _ => node.Value ?? string.Concat(node.Children.Select(RenderNode))
        };
    }

    public bool IsLiteralNode(AstNode node)
    {
        return node.TryGetMetadataType(out var nodeType) &&
            string.Equals(nodeType, "text", StringComparison.Ordinal);
    }

    public string GetTemplateKind(AstNode node)
    {
        return node.TryGetMetadataType(out var nodeType)
            ? nodeType switch
            {
                "output" => "output",
                "tag" => "tag",
                "rawBlock" => "raw",
                "commentBlock" => "comment",
                _ => "unknown"
            }
            : "unknown";
    }

    public string RenderTemplateNode(AstNode node)
    {
        return RenderNode(node);
    }

    private static IReadOnlyList<AstNode> ParseTokens(string source)
    {
        var tokens = new List<AstNode>();
        var position = 0;
        var ordinal = 0;

        while (position < source.Length)
        {
            var nextOutput = source.IndexOf("{{", position, StringComparison.Ordinal);
            var nextTag = source.IndexOf("{%", position, StringComparison.Ordinal);
            var next = FirstNonNegative(nextOutput, nextTag);

            if (next < 0)
            {
                AddText(tokens, source[position..], ref ordinal);
                break;
            }

            if (next > position)
            {
                AddText(tokens, source[position..next], ref ordinal);
            }

            if (next == nextOutput)
            {
                var token = ReadDelimitedToken(source, next, "{{", "}}");
                tokens.Add(CreateToken("$output", "output", token, ref ordinal));
                position = token.After;
                continue;
            }

            var tag = ReadDelimitedToken(source, next, "{%", "%}");
            var tagName = ReadTagName(tag.Inner);
            if (IsBlockToken(tagName))
            {
                var endTag = FindEndTag(source, tag.After, "end" + tagName);
                var body = source[tag.After..endTag.Start];
                tokens.Add(CreateBlockToken(tagName, tag, body, endTag, ref ordinal));
                position = endTag.After;
                continue;
            }

            tokens.Add(CreateToken("$tag", "tag", tag, ref ordinal, tagName));
            position = tag.After;
        }

        return tokens;
    }

    private static void AddText(List<AstNode> tokens, string text, ref int ordinal)
    {
        if (text.Length == 0)
        {
            return;
        }

        tokens.Add(new AstNode(
            $"$text{ordinal++:D6}",
            AstNodeMetadata.Create("text"),
            text));
    }

    private static AstNode CreateToken(string kindPrefix, string type, DelimitedToken token, ref int ordinal, string? tagName = null)
    {
        var fields = AstNodeMetadata.Create(type);
        fields[OpenField] = token.Open;
        fields[CloseField] = token.Close;
        if (!string.IsNullOrWhiteSpace(tagName))
        {
            fields[TagNameField] = tagName;
        }

        return new AstNode($"{kindPrefix}{ordinal++:D6}", fields, token.Inner);
    }

    private static AstNode CreateBlockToken(string tagName, DelimitedToken startTag, string body, DelimitedToken endTag, ref int ordinal)
    {
        var type = string.Equals(tagName, "raw", StringComparison.OrdinalIgnoreCase)
            ? "rawBlock"
            : "commentBlock";
        var fields = AstNodeMetadata.Create(type);
        fields[TagNameField] = tagName;
        fields[StartTagField] = startTag.FullText;
        fields[EndTagField] = endTag.FullText;

        return new AstNode($"${tagName}{ordinal++:D6}", fields, body);
    }

    private static string RenderInlineLiquidToken(AstNode node)
    {
        var open = node.Fields.TryGetValue(OpenField, out var openValue) ? openValue : "{%";
        var close = node.Fields.TryGetValue(CloseField, out var closeValue) ? closeValue : "%}";
        return open + (node.Value ?? string.Empty) + close;
    }

    private static string RenderBlockLiquidToken(AstNode node)
    {
        var startTag = node.Fields.TryGetValue(StartTagField, out var startValue) ? startValue : string.Empty;
        var endTag = node.Fields.TryGetValue(EndTagField, out var endValue) ? endValue : string.Empty;
        return startTag + (node.Value ?? string.Empty) + endTag;
    }

    private static DelimitedToken FindEndTag(string source, int position, string endTagName)
    {
        var current = position;
        while (current < source.Length)
        {
            var next = source.IndexOf("{%", current, StringComparison.Ordinal);
            if (next < 0)
            {
                break;
            }

            var candidate = ReadDelimitedToken(source, next, "{%", "%}");
            if (string.Equals(ReadTagName(candidate.Inner), endTagName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            current = candidate.After;
        }

        throw new InvalidOperationException($"Liquid block is missing required '{endTagName}' tag.");
    }

    private static DelimitedToken ReadDelimitedToken(string source, int start, string openMarker, string closeMarker)
    {
        var openEnd = start + openMarker.Length;
        if (openEnd < source.Length && source[openEnd] == '-')
        {
            openEnd++;
        }

        var closeStart = source.IndexOf(closeMarker, openEnd, StringComparison.Ordinal);
        if (closeStart < 0)
        {
            throw new InvalidOperationException($"Liquid token starting at offset {start} is missing '{closeMarker}'.");
        }

        var contentEnd = closeStart;
        if (contentEnd > openEnd && source[contentEnd - 1] == '-')
        {
            contentEnd--;
        }

        var after = closeStart + closeMarker.Length;
        var open = source[start..openEnd];
        var close = source[contentEnd..after];

        return new DelimitedToken(
            start,
            after,
            open,
            source[openEnd..contentEnd],
            close,
            source[start..after]);
    }

    private static string ReadTagName(string markup)
    {
        var trimmed = markup.TrimStart();
        var end = trimmed.TakeWhile(character => !char.IsWhiteSpace(character)).Count();
        return end == 0 ? string.Empty : trimmed[..end];
    }

    private static bool IsBlockToken(string tagName)
    {
        return string.Equals(tagName, "raw", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tagName, "comment", StringComparison.OrdinalIgnoreCase);
    }

    private static int FirstNonNegative(int left, int right)
    {
        return (left, right) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => right,
            (_, < 0) => left,
            _ => Math.Min(left, right)
        };
    }

    private sealed record DelimitedToken(
        int Start,
        int After,
        string Open,
        string Inner,
        string Close,
        string FullText);
}
