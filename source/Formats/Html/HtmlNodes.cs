using System.Net;
using AngleSharp.Html.Parser;
using Meridian.Core.Tree;

namespace MeridianGit.Formats.Html;

// Node-level parsing and rendering shared by the two HTML shapes: html:fragment (a body-context
// snippet, rooted at $fragment) and html:document (a complete page, rooted at $document). Only the
// roots differ; every node beneath them is parsed and rendered identically.
//
// Rendering is source-faithful. The tree the parser hands back has already normalised away how the
// markup was written — attribute order and quoting, valueless attributes, name casing, whether a
// void element closed itself, which characters were written as entities — so re-serialising from it
// rewrites the whole file on a merge that changed one line. Each node therefore carries the source
// text it came from in TreeNode.SourceText (a field that is neither merged nor compared, so it can
// never cause a conflict), and rendering emits that verbatim as long as it still matches what the
// node now holds. A node the merge changed falls back to canonical serialisation, so the rewriting
// is confined to the parts that actually changed.
internal static class HtmlNodes
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    // Elements whose content the HTML spec treats as RAW text: their character data must be emitted
    // verbatim, never HTML-entity-encoded, or the embedded script/style/text is corrupted.
    //
    // ESCAPABLE raw text (title, textarea) is deliberately not here, and neither is noscript, whose
    // content is ordinary markup while scripting is off. The parser decodes character references in
    // all three, so emitting their text verbatim would re-corrupt it: a title written
    // "&amp;copy;" comes back as "&copy;", which reads as © the next time the page is parsed.
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "xmp", "iframe", "noembed", "noframes", "plaintext"
    };

    public const string TextType = "text";

    // Separates the two halves of an element's fidelity hint (see BuildHint). The tokenizer replaces
    // a NUL in the source with U+FFFD, so it can never turn up inside parsed content.
    private const char HintSeparator = '\0';

    // Source references are what make verbatim rendering possible: they give each element the offset
    // of its start tag, which anchors the walk that recovers everything else.
    public static HtmlParser CreateParser() => new(new HtmlParserOptions { IsKeepingSourceReferences = true });

    /// <summary>Parses an element and its subtree, taking its source offset from the parser.</summary>
    public static TreeNode ParseElement(AngleSharp.Dom.IElement element, int index, string source)
    {
        var start = StartOfElement(element, source);
        var cursor = start >= 0 ? start : 0;
        return ParseElement(element, index, source, start, ref cursor);
    }

    /// <summary>Parses the children of <paramref name="parent"/>, reading source from <paramref name="cursor"/>.</summary>
    public static IReadOnlyList<TreeNode> ParseChildren(AngleSharp.Dom.INode parent, string source, int cursor) =>
        ParseChildren(parent, source, ref cursor);

    private static TreeNode[] ParseChildren(AngleSharp.Dom.INode parent, string source, ref int cursor)
    {
        var rawTextParent = parent is AngleSharp.Dom.IElement element && RawTextElements.Contains(element.LocalName);
        var nodes = new List<TreeNode>();
        var index = 0;

        foreach (var child in parent.ChildNodes)
        {
            switch (child)
            {
                case AngleSharp.Dom.IElement childElement:
                    var start = StartOfElement(childElement, source);
                    // The tree-construction algorithm discards whitespace between the wrappers (the
                    // newline before <head>, for one). An element's offset is authoritative, so a
                    // whitespace-only gap ahead of it is text the parser dropped: put it back where
                    // the source had it, and let the offset resynchronise the walk either way.
                    if (start > cursor && IsWhitespaceRange(source, cursor, start))
                        nodes.Add(TextNode(index++, source[cursor..start], source[cursor..start]));

                    if (start >= 0)
                        cursor = start;

                    nodes.Add(ParseElement(childElement, index++, source, start, ref cursor));
                    break;

                case AngleSharp.Dom.IText text:
                    nodes.Add(ParseText(text, index++, source, rawTextParent, ref cursor));
                    break;

                case AngleSharp.Dom.IComment comment:
                    nodes.Add(new TreeNode($"$comment{index++:D6}", NodeMetadata.Create("comment"), comment.Data));
                    cursor = SkipComment(source, cursor, comment.Data);
                    break;

                default:
                    nodes.Add(new TreeNode($"$node{index++:D6}", NodeMetadata.Create("raw"), child.TextContent));
                    break;
            }
        }

        return [.. nodes];
    }

    private static TreeNode ParseElement(
        AngleSharp.Dom.IElement element,
        int index,
        string source,
        int start,
        ref int cursor)
    {
        var fields = NodeMetadata.Create("element", element.LocalName);
        foreach (var attribute in element.Attributes)
            fields[EncodeAttributeName(attribute.Name)] = attribute.Value;

        string? rawStartTag = null;
        if (start >= 0 && EndOfStartTag(source, start) is var tagEnd and > 0)
        {
            rawStartTag = source[start..tagEnd];
            cursor = tagEnd;
        }

        var children = ParseChildren(element, source, ref cursor);
        cursor = SkipEndTag(source, cursor, element.LocalName);

        return new TreeNode(
            $"{NodeMetadata.EncodeKind(element.LocalName)}{index:D6}",
            fields,
            children: children,
            sourceText: BuildHint(rawStartTag, element.LocalName, fields));
    }

    private static TreeNode ParseText(
        AngleSharp.Dom.IText text,
        int index,
        string source,
        bool rawTextParent,
        ref int cursor)
    {
        var data = text.Data;
        if (cursor < 0 || cursor > source.Length)
            return TextNode(index, data, null);

        // Text cannot contain '<', so the run ends at the next tag. Character references make the
        // source form differ from the parsed data, so the slice only counts when it decodes back to
        // exactly what the node holds — which also rejects a cursor that has drifted.
        var end = source.IndexOf('<', cursor);
        if (end < 0)
            end = source.Length;

        var slice = source[cursor..end];
        if (!rawTextParent && string.Equals(WebUtility.HtmlDecode(slice), data, StringComparison.Ordinal))
        {
            cursor = end;
            return TextNode(index, data, slice);
        }

        // Raw text (a script or stylesheet body) is not decoded and may itself contain '<', so match
        // it directly to keep the walk in step. It renders verbatim already and needs no hint.
        if (data.Length > 0 &&
            cursor + data.Length <= source.Length &&
            source.AsSpan(cursor, data.Length).Equals(data, StringComparison.Ordinal))
            cursor += data.Length;

        return TextNode(index, data, null);
    }

    private static TreeNode TextNode(int index, string data, string? source) =>
        new($"$text{index:D6}", NodeMetadata.Create(TextType), data, sourceText: source);

    // An element's fidelity hint is its verbatim start tag and the canonical rendering of the
    // attributes it was parsed with, so rendering can tell whether the node still says what the
    // source said. It is only kept where emitting it back is safe: a self-closing tag on a non-void
    // element means something different in foreign content (an <svg> subtree) than in HTML, and
    // guessing wrong would move the elements that follow it into the subtree.
    private static string? BuildHint(string? rawStartTag, string tag, IReadOnlyDictionary<string, string> fields)
    {
        if (rawStartTag is null)
            return null;

        if (!VoidElements.Contains(tag) && rawStartTag.EndsWith("/>", StringComparison.Ordinal))
            return null;

        return rawStartTag + HintSeparator + RenderStartTag(tag, fields);
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
            TextType => RenderText(node, rawTextParent),
            "comment" => $"<!--{node.Value ?? string.Empty}-->",
            "element" => RenderElement(node),
            "raw" => node.Value ?? string.Empty,
            _ => RenderChildren(node)
        };
    }

    private static string RenderChildren(TreeNode node) =>
        string.Concat(node.Children.Select(child => RenderHtmlNode(child, rawTextParent: false)));

    private static string RenderText(TreeNode node, bool rawTextParent)
    {
        var value = node.Value ?? string.Empty;
        if (rawTextParent)
            return value;

        // Unchanged text goes back exactly as written, keeping the character references the author
        // chose. Text a merge changed is encoded minimally: only the three characters that cannot
        // stand for themselves, so a page of accented or dashed prose does not turn into entities.
        return node.SourceText is { } source && string.Equals(WebUtility.HtmlDecode(source), value, StringComparison.Ordinal)
            ? source
            : EncodeText(value);
    }

    private static string RenderElement(TreeNode node)
    {
        var tag = node.GetMetadataName();
        var canonical = RenderStartTag(tag, node.Fields);
        var start = TryReadVerbatimStartTag(node, canonical, out var verbatim) ? verbatim : canonical;

        if (VoidElements.Contains(tag))
            return start;

        var rawText = RawTextElements.Contains(tag);
        return start + string.Concat(node.Children.Select(child => RenderHtmlNode(child, rawText))) + $"</{tag}>";
    }

    private static bool TryReadVerbatimStartTag(TreeNode node, string canonical, out string verbatim)
    {
        verbatim = string.Empty;
        if (node.SourceText is not { } hint)
            return false;

        var separator = hint.IndexOf(HintSeparator, StringComparison.Ordinal);
        if (separator < 0 || !string.Equals(hint[(separator + 1)..], canonical, StringComparison.Ordinal))
            return false;

        verbatim = hint[..separator];
        return true;
    }

    private static string RenderStartTag(string tag, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var attributes = fields
            .Where(field => !field.Key.StartsWith('$'))
            .Select(field => $" {DecodeAttributeName(field.Key)}=\"{EncodeAttributeValue(field.Value)}\"");

        return $"<{tag}{string.Concat(attributes)}>";
    }

    private static string EncodeText(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EncodeAttributeValue(string value) => EncodeText(value)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    // 0-based offset of the element's '<', or -1 when the parser gave no offset or the source does
    // not actually start that element there. Every use of an offset is verified, so a hint is only
    // ever built from source that really is the element's own start tag.
    private static int StartOfElement(AngleSharp.Dom.IElement element, string source)
    {
        if (element.SourceReference?.Position.Position is not > 0)
            return -1;

        var start = element.SourceReference.Position.Position - 1;
        var name = element.LocalName;
        if (start >= source.Length || source[start] != '<' || start + 1 + name.Length > source.Length)
            return -1;

        if (!source.AsSpan(start + 1, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase))
            return -1;

        var after = start + 1 + name.Length;
        return after >= source.Length || source[after] is '>' or '/' || char.IsWhiteSpace(source[after])
            ? start
            : -1;
    }

    // End of the start tag that begins at <paramref name="start"/>, or -1 if it does not end.
    private static int EndOfStartTag(string source, int start)
    {
        var quote = '\0';

        for (var index = start + 1; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
            }
            else if (character is '"' or '\'')
                quote = character;
            else if (character == '>')
                return index + 1;
            else if (character == '<')
                return -1;
        }

        return -1;
    }

    // Past the element's end tag when the source wrote one; unchanged when it was left out, which
    // the spec allows for several elements and which the walk has to tolerate.
    private static int SkipEndTag(string source, int cursor, string name)
    {
        if (cursor < 0 || cursor + 2 + name.Length > source.Length)
            return cursor;

        if (source[cursor] != '<' || source[cursor + 1] != '/' ||
            !source.AsSpan(cursor + 2, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase))
            return cursor;

        var index = cursor + 2 + name.Length;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
            index++;

        return index < source.Length && source[index] == '>' ? index + 1 : cursor;
    }

    private static int SkipComment(string source, int cursor, string data)
    {
        var comment = $"<!--{data}-->";
        if (cursor < 0 || cursor + comment.Length > source.Length)
            return cursor;

        return source.AsSpan(cursor, comment.Length).Equals(comment, StringComparison.Ordinal)
            ? cursor + comment.Length
            : cursor;
    }

    private static bool IsWhitespaceRange(string source, int start, int end)
    {
        if (start < 0 || end > source.Length || start >= end)
            return false;

        for (var index = start; index < end; index++)
            if (!char.IsWhiteSpace(source[index]))
                return false;

        return true;
    }

    // HTML attribute names may begin with '$', which would collide with the $type/$name metadata
    // sentinels. Escape a name starting with '$' (or with the '=' escape char itself, so the
    // mapping stays reversible) by prefixing '='; decoding strips exactly that one prefix. Ordinary
    // names (id, class, ...) are untouched so identity rules keep matching them.
    private static string EncodeAttributeName(string name) =>
        name.StartsWith('$') || name.StartsWith('=') ? "=" + name : name;

    private static string DecodeAttributeName(string key) => key.StartsWith('=') ? key[1..] : key;
}
