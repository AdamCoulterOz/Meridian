using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace MeridianGit.Formats.Html;

/// <summary>
/// Structural adapter for a COMPLETE HTML page: a doctype plus an &lt;html&gt; root with its
/// &lt;head&gt; and &lt;body&gt;. The document element and everything below it are real tree nodes,
/// so edits to disjoint subtrees (a title in the head, a paragraph in the body) merge cleanly.
/// </summary>
/// <remarks>
/// A full document must NOT be parsed in body context (<c>ParseDocument($"&lt;body&gt;{source}&lt;/body&gt;")</c>,
/// which <see cref="HtmlFragmentAdapter"/> uses for snippets): that foster-parents the head, drops
/// the doctype and the html/head/body wrappers, and silently destroys the page. AngleSharp handles a
/// complete document correctly when it is handed the source unwrapped, which is what this does.
///
/// Two things the HTML tree-construction algorithm normalises away are recovered from the source
/// text, so a page whose framing is what the merge is about survives it intact:
/// <list type="bullet">
/// <item>Everything before the <c>&lt;html&gt;</c> start tag (the doctype and its exact casing, plus
/// any leading whitespace or comments) is kept verbatim as a <c>$prologue</c> node. The parser
/// exposes a doctype only as a normalised name, and discards whitespace in the "before html"
/// insertion mode entirely.</item>
/// <item>Everything after <c>&lt;/html&gt;</c> — usually just the file's trailing newline — becomes an
/// <c>$epilogue</c> node. The parser folds that whitespace into the end of the body and attaches
/// trailing comments to the document, so the folded part is lifted back out of the tree to keep the
/// merged file ending the way it started.</item>
/// </list>
/// Whitespace at the head/body boundaries is still relocated by the parser (moved between wrappers,
/// never dropped), and markup is re-serialised the way <see cref="HtmlNodes"/> renders every HTML
/// node: attributes sorted, void elements unclosed, non-ASCII text as numeric entities. Content is
/// preserved exactly; layout of a pretty-printed page is not.
/// </remarks>
public sealed class HtmlDocumentAdapter : IFormatAdapter
{
    public const string RootKind = "$document";
    private const string PrologueKind = "$prologue";
    private const string EpilogueKind = "$epilogue";

    // A comment after </html> is attached to the document, not folded into the body like the
    // whitespace around it, so it is not part of what has to be lifted back out of the tree.
    private static readonly Regex TrailingComment = new(
        "<!--.*?-->", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public string Format => "html:document";

    /// <summary>
    /// True when <paramref name="sourceText"/> is a complete page rather than a body fragment.
    /// </summary>
    public static bool IsFullDocument(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var trimmed = sourceText.TrimStart();
        // Match a real <html> root or a doctype only — not fragments that merely start with a tag
        // whose name shares a prefix (e.g. <header>, which must still merge structurally).
        return trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
            || IndexOfStartTag(trimmed, "html") == 0;
    }

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var parser = new HtmlParser();
        var parsed = parser.ParseDocument(sourceText);
        var documentElement = parsed.DocumentElement
            ?? throw new InvalidOperationException("HTML source has no document element.");

        // Index 0 keeps the document element's identity stable whether or not a prologue precedes
        // it, so adding a doctype on one side does not read as a replacement of the whole page.
        var root = HtmlNodes.ParseElement(documentElement, 0);
        var children = new List<TreeNode>(3);

        var prologue = ReadPrologue(sourceText);
        if (prologue.Length > 0)
            children.Add(new TreeNode(PrologueKind, NodeMetadata.Create("raw"), prologue));

        // The HTML input-stream preprocessor rewrites CRLF to LF, so the tree only ever holds LF.
        // The slice has to be normalised the same way or it will not match on a CRLF file — the
        // driver restores the ours-side convention across the whole render afterwards.
        var epilogue = ReadEpilogue(sourceText).Replace("\r\n", "\n", StringComparison.Ordinal);
        // Lift the folded part back out of the tree, then keep the slice itself verbatim. If it
        // cannot be lifted (the parser put it somewhere this does not look), emitting the slice
        // would duplicate content — unless it carries a comment, which lives nowhere else and
        // would otherwise be lost, so prefer duplicated whitespace over dropped content.
        var folded = TrailingComment.Replace(epilogue, string.Empty);
        if (folded.Length > 0)
        {
            if (TryTrimTrailingText(root, folded, out var trimmed))
                root = trimmed;
            else if (string.IsNullOrWhiteSpace(epilogue))
                epilogue = string.Empty;
        }

        children.Add(root);

        if (epilogue.Length > 0)
            children.Add(new TreeNode(EpilogueKind, NodeMetadata.Create("raw"), epilogue));

        return new DocumentTree(
            Format,
            new TreeNode(RootKind, NodeMetadata.Create("document"), children: children),
            sourcePath,
            sourceText);
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

        return HtmlNodes.Render(node);
    }

    // Everything ahead of the <html> start tag, kept verbatim. With no literal <html> tag (an
    // implied document element) only the doctype declaration itself is framing; the rest is content
    // the parser owns.
    private static string ReadPrologue(string sourceText)
    {
        var htmlStart = IndexOfStartTag(sourceText, "html");
        if (htmlStart >= 0)
            return sourceText[..htmlStart];

        var doctypeStart = sourceText.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase);
        if (doctypeStart < 0)
            return string.Empty;

        var doctypeEnd = sourceText.IndexOf('>', doctypeStart);
        return doctypeEnd < 0 ? sourceText : sourceText[..(doctypeEnd + 1)];
    }

    // Everything after the </html> end tag, which the parser relocates into the body.
    private static string ReadEpilogue(string sourceText)
    {
        var index = sourceText.LastIndexOf("</html", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return string.Empty;

        var close = sourceText.IndexOf('>', index);
        return close < 0 ? string.Empty : sourceText[(close + 1)..];
    }

    // Index of the tag's start tag, or -1. A name is only a match when the character after it ends
    // the name, so <header> never counts as <head> — a fragment starting with one must keep merging
    // structurally rather than being read as a page.
    private static int IndexOfStartTag(string text, string tag)
    {
        var index = 0;
        while ((index = text.IndexOf("<" + tag, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var after = index + tag.Length + 1;
            if (after >= text.Length)
                return index;

            if (text[after] is '>' or '/' || char.IsWhiteSpace(text[after]))
                return index;

            index = after;
        }

        return -1;
    }

    // Removes <paramref name="suffix"/> from the trailing text node of the subtree, rebuilding the
    // ancestors that lead to it. Returns false (leaving the tree untouched) when the subtree does
    // not end in that text, so nothing is ever trimmed that the source did not put there.
    private static bool TryTrimTrailingText(TreeNode node, string suffix, out TreeNode trimmed)
    {
        trimmed = node;

        if (node.Children.Count == 0)
        {
            if (!node.TryGetMetadataType(out var type) || type != HtmlNodes.TextType)
                return false;

            var value = node.Value ?? string.Empty;
            if (!value.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            trimmed = node.WithValue(value[..^suffix.Length]);
            return true;
        }

        if (!TryTrimTrailingText(node.Children[^1], suffix, out var trimmedLast))
            return false;

        // An emptied text node carries nothing and would only add a spurious child to compare. Test
        // for an empty string, not for null: every element node has a null Value, and dropping one
        // of those here would delete the whole subtree the trimmed text lives in.
        var children = trimmedLast.Value is { Length: 0 }
            ? node.Children.Take(node.Children.Count - 1).ToArray()
            : [.. node.Children.Take(node.Children.Count - 1), trimmedLast];

        trimmed = node.WithChildren(children);
        return true;
    }
}
