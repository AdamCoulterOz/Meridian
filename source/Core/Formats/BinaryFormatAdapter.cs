using System.Text;
using Meridian.Core.Tree;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

/// <summary>
/// Base class for opaque binary formats. Content is represented losslessly as a
/// single base64 scalar node so the structural merge can compare byte content
/// exactly: it resolves to the changed side when only one side changed, and
/// reports a conflict when both sides diverge. Binary content cannot carry text
/// conflict markers, so callers must check <see cref="MergeResult.HasConflicts"/>
/// and avoid rendering bytes for an unresolved conflict.
/// </summary>
public abstract class BinaryFormatAdapter : IBinaryFormatAdapter
{
    public const string EncodingField = "$encoding";
    public const string Base64Encoding = "base64";

    public abstract string Format { get; }

    protected abstract string RootKind { get; }

    public DocumentTree ParseBytes(ReadOnlyMemory<byte> content, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var base64 = Convert.ToBase64String(content.Span);
        return new DocumentTree(Format, CreateNode(base64), sourcePath, base64);
    }

    public byte[] RenderDocumentBytes(DocumentTree document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Root.IsConflict)
            throw new InvalidOperationException(
                $"Cannot render '{Format}' bytes for an unresolved binary conflict; binary content cannot carry text conflict markers.");

        return Decode(document.Root.Value);
    }

    // The string contract treats the text as UTF-8 bytes so the string and byte
    // paths stay consistent: UTF8.GetString(UTF8.GetBytes(s)) == s for any string.
    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return ParseBytes(Encoding.UTF8.GetBytes(sourceText), sourcePath, schema);
    }

    public string RenderDocument(DocumentTree document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RenderNode(document.Root);
    }

    public string RenderNode(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Conflict is not null
            ? ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText)
            : Encoding.UTF8.GetString(Decode(node.Value));
    }

    private static byte[] Decode(string? value) =>
        string.IsNullOrEmpty(value) ? [] : Convert.FromBase64String(value);

    private TreeNode CreateNode(string base64) => new(
        RootKind,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$type"] = "binary",
            [EncodingField] = Base64Encoding
        },
        base64);
}
