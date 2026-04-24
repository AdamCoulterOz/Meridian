using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats.Mapped;

public enum MappedTokenContext
{
    ChildNode,
    FieldValue
}

public sealed record MappedToken(
    string Id,
    string SemanticKey,
    string Source,
    string Kind,
    string SourceText,
    string PhysicalMarker);

public sealed record MappedTokenShape(string SourceText, MappedTokenContext Context);

public static class MappedTokenFields
{
    public const string Source = "$mappedSource";
    public const string HostFormat = "$hostFormat";
    public const string Mode = "$mode";
    public const string UnsafeReason = "$unsafeReason";
    public const string TokenId = "tokenId";
    public const string SemanticKey = "$semanticKey";
    public const string Context = "context";
    public const string MappedKind = "mappedKind";
    public const string MarkerPrefix = "__MERIDIAN_MAPPED__";
    public const string MarkerSuffix = "__";
}

public interface IMappedSourceAdapter : IFormatAdapter
{
    string SourceName { get; }
    bool IsLiteralNode(TreeNode node);
    string GetMappedKind(TreeNode node);
    string RenderMappedNode(TreeNode node);
}

public interface IMappedTokenContextTracker
{
    bool TryGetPossibleContexts(out IReadOnlyList<MappedTokenContext> contexts, out string? unsupportedReason);
    string CreateSemanticKey(MappedTokenContext context);
    void Feed(string literalText);
}

public interface IMappedHost
{
    string HostFormat { get; }
    IMappedTokenContextTracker CreateTokenContextTracker();
    bool CanRepresent(MappedToken token, MappedTokenContext context, out string? unsupportedReason);
    bool TryCreateToken(MappedToken token, MappedTokenContext context, out MappedTokenShape shape);
    DocumentTree ParseHostWithMappedTokens(string sourceText, string? sourcePath, MergeSchema schema);
    string RenderHostWithMappedTokens(DocumentTree document, IReadOnlyDictionary<string, string> mappedSourceByTokenId);
}
