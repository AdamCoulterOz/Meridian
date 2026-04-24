using System.Security.Cryptography;
using System.Text;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats.Mapped;

public sealed class MappedFormatAdapter : IFormatAdapter
{
    private readonly IMappedSourceAdapter _mappedSource;
    private readonly IMappedHost _host;

    public MappedFormatAdapter(
        IMappedSourceAdapter mappedSource,
        IMappedHost host)
    {
        _mappedSource = mappedSource;
        _host = host;
    }

    public string Format => _mappedSource.SourceName + ":" + _host.HostFormat;

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var mappedDocument = _mappedSource.Parse(sourceText, sourcePath, schema);
        var stitched = Stitch(mappedDocument.Root.Children, CreateCollisionFreeMarkerNonce(sourceText));
        if (!stitched.IsSafe)
            return CreateUnsafeDocument(
                sourceText,
                sourcePath,
                mappedDocument,
                stitched.UnsafeReason ?? "mapped token cannot be represented safely in the host format.");

        try
        {
            var hostDocument = _host.ParseHostWithMappedTokens(stitched.Source, sourcePath, schema);
            return new DocumentTree(
                Format,
                CreateRoot("safe",
                [
                    new TreeNode("$host", NodeMetadata.Create("host"), children: [hostDocument.Root]),
                    CreateMappedCollection(stitched.Tokens)
                ]),
                sourcePath,
                sourceText);
        }
        catch (Exception exception)
        {
            return CreateUnsafeDocument(
                sourceText,
                sourcePath,
                mappedDocument,
                "Stitched host content could not be parsed: " + exception.Message);
        }
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        var mode = node.Fields.TryGetValue(MappedTokenFields.Mode, out var modeValue)
                            ? modeValue
                            : "safe";

        if (string.Equals(mode, "unsafe", StringComparison.Ordinal))
        {
            var mapped = node.Children.Single(child => string.Equals(child.Kind, "$mapped", StringComparison.Ordinal));
            return _mappedSource.RenderNode(new TreeNode(
                mapped.Kind,
                mapped.Fields,
                mapped.Value,
                mapped.Children));
        }

        var hostRoot = node.Children.Single(child => string.Equals(child.Kind, "$host", StringComparison.Ordinal)).Children.Single();
        var mappedSources = node.Children
            .Single(child => string.Equals(child.Kind, "$mappedTokens", StringComparison.Ordinal))
            .Children
            .ToDictionary(
                child => child.Fields[MappedTokenFields.TokenId],
                child => child.Value ?? string.Empty,
                StringComparer.Ordinal);

        return _host.RenderHostWithMappedTokens(new DocumentTree(_host.HostFormat, hostRoot), mappedSources);
    }

    private StitchedHost Stitch(IReadOnlyList<TreeNode> mappedNodes, string markerNonce)
    {
        var tracker = _host.CreateTokenContextTracker();
        var stitched = new StringBuilder();
        var tokens = new List<MappedTokenReference>();
        var ordinal = 0;

        foreach (var node in mappedNodes)
        {
            if (_mappedSource.IsLiteralNode(node))
            {
                var text = node.Value ?? string.Empty;
                stitched.Append(text);
                tracker.Feed(text);
                continue;
            }

            var id = "mtk" + ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            if (!tracker.TryGetPossibleContexts(out var contexts, out var contextReason))
                return new StitchedHost(
                    stitched.ToString(),
                    tokens,
                    IsSafe: false,
                    UnsafeReason: $"mapped token '{id}' has no valid {_host.HostFormat} tree token context. {contextReason}");

            var context = default(MappedTokenContext);
            string? unsupportedReason = null;
            var canRepresent = false;
            foreach (var candidate in contexts)
                if (_host.CanRepresent(
                    new MappedToken(
                        id,
                        string.Empty,
                        _mappedSource.SourceName,
                        _mappedSource.GetMappedKind(node),
                        _mappedSource.RenderMappedNode(node),
                        CreatePhysicalMarker(markerNonce, id)),
                    candidate,
                    out unsupportedReason))
                {
                    context = candidate;
                    canRepresent = true;
                    break;
                }

            if (!canRepresent)
                return new StitchedHost(
                    stitched.ToString(),
                    tokens,
                    IsSafe: false,
                    UnsafeReason: $"mapped token '{id}' cannot be safely represented in {_host.HostFormat} contexts '{string.Join(", ", contexts)}'. {unsupportedReason}");

            var token = new MappedToken(
                                        id,
                                        tracker.CreateSemanticKey(context),
                                        _mappedSource.SourceName,
                                        _mappedSource.GetMappedKind(node),
                                        _mappedSource.RenderMappedNode(node),
                                        CreatePhysicalMarker(markerNonce, id));

            if (!_host.TryCreateToken(token, context, out var shape))
                return new StitchedHost(
                    stitched.ToString(),
                    tokens,
                    IsSafe: false,
                    UnsafeReason: $"mapped token '{id}' cannot be safely inserted in {_host.HostFormat} context '{context}'.");

            stitched.Append(shape.SourceText);
            tokens.Add(new MappedTokenReference(token, shape.Context));
            ordinal++;
        }

        return new StitchedHost(stitched.ToString(), tokens, IsSafe: true, UnsafeReason: null);
    }

    private static string CreateCollisionFreeMarkerNonce(string sourceText)
    {
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var nonce = CreateMarkerNonce(sourceText, attempt);
            if (!sourceText.Contains(MappedTokenFields.MarkerPrefix + nonce, StringComparison.Ordinal))
                return nonce;
        }

        throw new InvalidOperationException("Could not create a mapped token marker nonce that is absent from the source text.");
    }

    private static string CreateMarkerNonce(string sourceText, int attempt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceText + "\n" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string CreatePhysicalMarker(string markerNonce, string tokenId) => MappedTokenFields.MarkerPrefix + markerNonce + "__" + tokenId + MappedTokenFields.MarkerSuffix;

    private DocumentTree CreateUnsafeDocument(
        string sourceText,
        string? sourcePath,
        DocumentTree mappedDocument,
        string unsafeReason) => new(
            Format,
            CreateRoot("unsafe", unsafeReason,
            [
                new TreeNode(
                    "$mapped",
                    mappedDocument.Root.Fields,
                    sourceText,
                    mappedDocument.Root.Children)
            ]),
            sourcePath,
            sourceText);

    private TreeNode CreateRoot(string mode, IReadOnlyList<TreeNode> children) => CreateRoot(mode, null, children);

    private TreeNode CreateRoot(string mode, string? unsafeReason, IReadOnlyList<TreeNode> children)
    {
        var fields = NodeMetadata.Create("mappedFormat");
        fields[MappedTokenFields.Source] = _mappedSource.SourceName;
        fields[MappedTokenFields.HostFormat] = _host.HostFormat;
        fields[MappedTokenFields.Mode] = mode;
        if (!string.IsNullOrWhiteSpace(unsafeReason))
            fields[MappedTokenFields.UnsafeReason] = unsafeReason;

        return new TreeNode("$mapped", fields, children: children);
    }

    private static TreeNode CreateMappedCollection(IReadOnlyList<MappedTokenReference> tokens) => new(
            "$mappedTokens",
            NodeMetadata.Create("mappedCollection"),
            children: tokens.Select(CreateMappedNode).ToArray());

    private static TreeNode CreateMappedNode(MappedTokenReference token)
    {
        var fields = NodeMetadata.Create("mappedToken");
        fields[MappedTokenFields.TokenId] = token.Token.Id;
        fields[MappedTokenFields.SemanticKey] = token.Token.SemanticKey;
        fields[MappedTokenFields.Source] = token.Token.Source;
        fields[MappedTokenFields.MappedKind] = token.Token.Kind;
        fields[MappedTokenFields.Context] = token.Context.ToString();
        return new TreeNode("$mapped" + token.Token.Id, fields, token.Token.SourceText);
    }

    private sealed record StitchedHost(
        string Source,
        IReadOnlyList<MappedTokenReference> Tokens,
        bool IsSafe,
        string? UnsafeReason);

    private sealed record MappedTokenReference(
        MappedToken Token,
        MappedTokenContext Context);
}
