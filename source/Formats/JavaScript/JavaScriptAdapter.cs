using System.Text;
using System.Text.RegularExpressions;
using Esprima;
using Esprima.Ast;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace MeridianGit.Formats.JavaScript;

/// <summary>
/// Structural JavaScript adapter. Esprima parses the source (rejecting invalid
/// scripts) and supplies exact byte ranges for each top-level statement. Each
/// statement is kept as its verbatim source slice with its leading trivia, so a
/// clean round-trip reproduces the source exactly while top-level declarations
/// merge by identity: a function/class/variable is keyed by its declared name,
/// an import by its module specifier, and anything else by position. Editing one
/// declaration's body and adding another therefore merge independently instead
/// of conflicting on adjacent lines.
/// </summary>
public sealed class JavaScriptAdapter : IFormatAdapter
{
    private const string StatementType = "statement";
    private const string TriviaField = "$trivia";
    private const string TrailingTriviaField = "$trailingTrivia";

    private static readonly Regex FunctionName = new(
        @"^\s*(?:export\s+(?:default\s+)?)?(?:async\s+)?function\s*\*?\s*([A-Za-z_$][\w$]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ClassName = new(
        @"^\s*(?:export\s+(?:default\s+)?)?(?:abstract\s+)?class\s+([A-Za-z_$][\w$]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VariableName = new(
        @"^\s*(?:export\s+)?(?:var|let|const)\s+([A-Za-z_$][\w$]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ImportFromSource = new(
        @"^\s*(?:import|export)\b[^;]*?from\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BareImportSource = new(
        @"^\s*import\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Format => "javascript";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var parser = new JavaScriptParser(new ParserOptions { Tolerant = false });
        var program = ParseProgram(parser, sourceText, sourcePath);

        var children = new List<TreeNode>(program.Body.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var cursor = 0;
        var ordinal = 0;

        foreach (var statement in program.Body)
        {
            var start = statement.Range.Start;
            var end = statement.Range.End;
            var leadingTrivia = sourceText[cursor..start];
            var slice = sourceText[start..end];

            var key = DisambiguateKey(ComputeKey(slice, ordinal), occurrences);
            var fields = NodeMetadata.Create(StatementType);
            fields[TriviaField] = leadingTrivia;

            children.Add(new TreeNode("$statement:" + key, fields, slice));
            cursor = end;
            ordinal++;
        }

        var rootFields = NodeMetadata.Create("script");
        rootFields["parser"] = "esprima";
        rootFields["sourceType"] = program.SourceType.ToString();
        rootFields["bodyCount"] = program.Body.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        rootFields[TrailingTriviaField] = sourceText[cursor..];

        return new DocumentTree(
            Format,
            new TreeNode("$javascript", rootFields, children: children, sourceText: sourceText),
            sourcePath,
            sourceText);
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        if (!node.TryGetMetadataType(out var type) || !string.Equals(type, "script", StringComparison.Ordinal))
            return RenderStatement(node);

        var builder = new StringBuilder();
        foreach (var child in node.Children)
        {
            builder.Append(child.Fields.TryGetValue(TriviaField, out var trivia) ? trivia : string.Empty);
            builder.Append(RenderStatement(child));
        }

        builder.Append(node.Fields.TryGetValue(TrailingTriviaField, out var trailing) ? trailing : string.Empty);
        return builder.ToString();
    }

    // Module syntax (import/export) is only valid via ParseModule; sloppy-mode-only
    // scripts are only valid via ParseScript. Try the broader module grammar first.
    private static Program ParseProgram(JavaScriptParser parser, string source, string? path)
    {
        try
        {
            return parser.ParseModule(source, path);
        }
        catch (ParserException)
        {
            return parser.ParseScript(source, path);
        }
    }

    private static string RenderStatement(TreeNode node) => node.Conflict is not null
        ? ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText)
        : node.Value ?? string.Empty;

    private static string ComputeKey(string slice, int ordinal)
    {
        if (FunctionName.Match(slice) is { Success: true } function)
            return "function:" + function.Groups[1].Value;

        if (ClassName.Match(slice) is { Success: true } @class)
            return "class:" + @class.Groups[1].Value;

        if (VariableName.Match(slice) is { Success: true } variable)
            return "var:" + variable.Groups[1].Value;

        if (ImportFromSource.Match(slice) is { Success: true } importFrom)
            return "module:" + importFrom.Groups[1].Value;

        if (BareImportSource.Match(slice) is { Success: true } bareImport)
            return "module:" + bareImport.Groups[1].Value;

        return "statement#" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string DisambiguateKey(string key, Dictionary<string, int> occurrences)
    {
        occurrences.TryGetValue(key, out var count);
        occurrences[key] = count + 1;
        return count == 0 ? key : key + "~" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
