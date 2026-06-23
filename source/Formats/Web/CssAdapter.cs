using System.Globalization;
using System.Text.RegularExpressions;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Web;

/// <summary>
/// Structural, source-preserving CSS adapter. The stylesheet is split into an
/// ordered sequence of rules, at-rules, declarations, comments, and whitespace,
/// where every byte of the source is captured by some node, so a clean
/// round-trip reproduces the input exactly (even for non-CSS text). Top-level
/// rules are identified by selector and declarations within a block by property
/// name, so independent edits to different rules or different properties merge
/// cleanly while two edits to the same property produce a conflict. The scanner
/// respects comments, strings, parentheses, and nested braces (so at-rule blocks
/// such as <c>@media</c> are parsed recursively).
/// </summary>
public sealed class CssAdapter : IFormatAdapter
{
    private const string TextType = "text";
    private const string BlockType = "block";
    private const string DeclarationType = "declaration";
    private const string AtRuleType = "atRule";
    private const string StatementType = "statement";
    private const string OpenField = "$open";
    private const string CloseField = "$close";

    private static readonly Regex DeclarationHead = new(
        @"^\s*(--[\w-]+|[A-Za-z_][\w-]*)\s*:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AtRuleHead = new(
        @"^\s*@[\w-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRun = new(
        @"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Format => "css";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var children = ParseItems(sourceText, 0, sourceText.Length, inBlock: false);
        return new DocumentTree(
            Format,
            new TreeNode("$stylesheet", NodeMetadata.Create("stylesheet"), children: children, sourceText: sourceText),
            sourcePath,
            sourceText);
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        var type = node.TryGetMetadataType(out var nodeType) ? nodeType : "stylesheet";
        return type switch
        {
            "stylesheet" => string.Concat(node.Children.Select(RenderNode)),
            BlockType => Field(node, OpenField) + string.Concat(node.Children.Select(RenderNode)) + Field(node, CloseField),
            _ => node.Value ?? string.Empty
        };
    }

    private static string Field(TreeNode node, string name) => node.Fields.TryGetValue(name, out var value) ? value : string.Empty;

    private static List<TreeNode> ParseItems(string src, int start, int end, bool inBlock)
    {
        var items = new List<TreeNode>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var textOrdinal = 0;
        var pos = start;
        var triviaStart = start;

        while (pos < end)
        {
            if (char.IsWhiteSpace(src[pos]))
            {
                pos++;
                continue;
            }

            if (IsCommentStart(src, pos, end))
            {
                pos = SkipComment(src, pos, end);
                continue;
            }

            FlushText(items, src, triviaStart, pos, ref textOrdinal);

            if (src[pos] == '}')
            {
                // Stray closing brace at this level: keep it verbatim so the source round-trips.
                items.Add(TextNode("}", ref textOrdinal));
                pos++;
                triviaStart = pos;
                continue;
            }

            var construct = ScanConstruct(src, pos, end);
            if (construct.Kind == ConstructKind.Block)
            {
                var open = src[pos..(construct.BraceOpen + 1)];
                var body = ParseItems(src, construct.BraceOpen + 1, construct.BraceClose, inBlock: true);
                var close = src[construct.BraceClose..(construct.BraceClose + 1)];
                items.Add(CreateBlockNode(open, body, close, occurrences));
                pos = construct.BraceClose + 1;
            }
            else if (construct.Kind == ConstructKind.Statement)
            {
                items.Add(CreateStatementNode(src[pos..construct.End], inBlock, isStatement: true, occurrences, ref textOrdinal));
                pos = construct.End;
            }
            else
            {
                items.Add(CreateStatementNode(src[pos..end], inBlock, isStatement: false, occurrences, ref textOrdinal));
                pos = end;
            }

            triviaStart = pos;
        }

        FlushText(items, src, triviaStart, pos, ref textOrdinal);
        return items;
    }

    private static Construct ScanConstruct(string src, int start, int end)
    {
        var i = start;
        var parenDepth = 0;

        while (i < end)
        {
            if (IsCommentStart(src, i, end))
            {
                i = SkipComment(src, i, end);
                continue;
            }

            var ch = src[i];
            if (ch is '"' or '\'')
            {
                i = SkipString(src, i, end);
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
            }
            else if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;
            }
            else if (parenDepth == 0)
            {
                if (ch == '{')
                {
                    var close = FindMatchingBrace(src, i, end);
                    return close < 0
                        ? new Construct(ConstructKind.Leftover, end, -1, -1)
                        : new Construct(ConstructKind.Block, close + 1, i, close);
                }

                if (ch == '}')
                    return new Construct(ConstructKind.Leftover, i, -1, -1);

                if (ch == ';')
                    return new Construct(ConstructKind.Statement, i + 1, -1, -1);
            }

            i++;
        }

        return new Construct(ConstructKind.Leftover, end, -1, -1);
    }

    private static int FindMatchingBrace(string src, int openIndex, int end)
    {
        var depth = 0;
        var i = openIndex;

        while (i < end)
        {
            if (IsCommentStart(src, i, end))
            {
                i = SkipComment(src, i, end);
                continue;
            }

            var ch = src[i];
            if (ch is '"' or '\'')
            {
                i = SkipString(src, i, end);
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }

            i++;
        }

        return -1;
    }

    private static bool IsCommentStart(string src, int i, int end) => i + 1 < end && src[i] == '/' && src[i + 1] == '*';

    private static int SkipComment(string src, int i, int end)
    {
        var close = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
        return close < 0 ? end : Math.Min(close + 2, end);
    }

    private static int SkipString(string src, int i, int end)
    {
        var quote = src[i];
        i++;
        while (i < end)
        {
            if (src[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (src[i] == quote)
                return i + 1;

            i++;
        }

        return end;
    }

    private static TreeNode CreateBlockNode(string open, List<TreeNode> body, string close, Dictionary<string, int> occurrences)
    {
        var prelude = open.Length > 0 && open[^1] == '{' ? open[..^1] : open;
        var key = Disambiguate("rule:" + Normalize(prelude), occurrences);
        var fields = NodeMetadata.Create(BlockType);
        fields[OpenField] = open;
        fields[CloseField] = close;
        return new TreeNode("$css:" + NodeMetadata.EncodeKind(key), fields, children: body);
    }

    private static TreeNode CreateStatementNode(
        string text,
        bool inBlock,
        bool isStatement,
        Dictionary<string, int> occurrences,
        ref int textOrdinal)
    {
        if (inBlock && DeclarationHead.Match(text) is { Success: true } declaration)
        {
            var property = declaration.Groups[1].Value;
            var key = Disambiguate("decl:" + property.ToLowerInvariant(), occurrences);
            return new TreeNode("$css:" + NodeMetadata.EncodeKind(key), NodeMetadata.Create(DeclarationType, property), text);
        }

        if (AtRuleHead.IsMatch(text))
        {
            var key = Disambiguate("at:" + Normalize(text), occurrences);
            return new TreeNode("$css:" + NodeMetadata.EncodeKind(key), NodeMetadata.Create(AtRuleType), text);
        }

        if (isStatement)
        {
            var key = Disambiguate("stmt:" + Normalize(text), occurrences);
            return new TreeNode("$css:" + NodeMetadata.EncodeKind(key), NodeMetadata.Create(StatementType), text);
        }

        return TextNode(text, ref textOrdinal);
    }

    private static void FlushText(List<TreeNode> items, string src, int start, int endPos, ref int textOrdinal)
    {
        if (endPos > start)
            items.Add(TextNode(src[start..endPos], ref textOrdinal));
    }

    private static TreeNode TextNode(string text, ref int textOrdinal) =>
        new($"$cssText{textOrdinal++:D6}", NodeMetadata.Create(TextType), text);

    private static string Normalize(string text) => WhitespaceRun.Replace(text, " ").Trim();

    private static string Disambiguate(string key, Dictionary<string, int> occurrences)
    {
        occurrences.TryGetValue(key, out var count);
        occurrences[key] = count + 1;
        return count == 0 ? key : key + "~" + count.ToString(CultureInfo.InvariantCulture);
    }

    private enum ConstructKind
    {
        Block,
        Statement,
        Leftover
    }

    private readonly record struct Construct(ConstructKind Kind, int End, int BraceOpen, int BraceClose);
}
