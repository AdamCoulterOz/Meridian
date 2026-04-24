using System.Security.Cryptography;
using System.Text;
using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Core.Templates;

namespace Meridian.Formats.TemplatedHost;

public sealed class TemplatedHostAstFormatAdapter : IAstFormatAdapter
{
    private readonly ITemplateEngineAstFormatAdapter _templateEngine;
    private readonly ITemplatePlaceholderHost _host;

    public TemplatedHostAstFormatAdapter(
        ITemplateEngineAstFormatAdapter templateEngine,
        ITemplatePlaceholderHost host)
    {
        _templateEngine = templateEngine;
        _host = host;
    }

    public string Format => _templateEngine.EngineName + ":" + _host.HostFormat;

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var templateDocument = _templateEngine.Parse(sourceText, sourcePath, schema);
        var stitched = Stitch(templateDocument.Root.Children, CreateCollisionFreeMarkerNonce(sourceText));
        if (!stitched.IsSafe)
        {
            return CreateUnsafeDocument(
                sourceText,
                sourcePath,
                templateDocument,
                stitched.UnsafeReason ?? "Template token cannot be represented safely in the host format.");
        }

        try
        {
            var hostDocument = _host.ParseHostWithPlaceholders(stitched.Source, sourcePath, schema);
            return new AstDocument(
                Format,
                CreateRoot("safe", new[]
                {
                    new AstNode("$host", FormatAstUtilities.HiddenFields("host"), children: new[] { hostDocument.Root }),
                    CreateTemplateCollection(stitched.Placeholders)
                }),
                sourcePath,
                sourceText);
        }
        catch (Exception exception)
        {
            return CreateUnsafeDocument(
                sourceText,
                sourcePath,
                templateDocument,
                "Stitched host content could not be parsed: " + exception.Message);
        }
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

        var mode = node.Fields.TryGetValue(TemplatePlaceholderFields.Mode, out var modeValue)
            ? modeValue
            : "safe";

        if (string.Equals(mode, "unsafe", StringComparison.Ordinal))
        {
            var template = node.Children.Single(child => string.Equals(child.Kind, "$template", StringComparison.Ordinal));
            return _templateEngine.RenderNode(new AstNode(
                template.Kind,
                template.Fields,
                template.Value,
                template.Children));
        }

        var hostRoot = node.Children.Single(child => string.Equals(child.Kind, "$host", StringComparison.Ordinal)).Children.Single();
        var templates = node.Children
            .Single(child => string.Equals(child.Kind, "$templates", StringComparison.Ordinal))
            .Children
            .ToDictionary(
                child => child.Fields[TemplatePlaceholderFields.PlaceholderId],
                child => child.Value ?? string.Empty,
                StringComparer.Ordinal);

        return _host.RenderHostWithPlaceholders(new AstDocument(_host.HostFormat, hostRoot), templates);
    }

    private StitchedHost Stitch(IReadOnlyList<AstNode> templateNodes, string markerNonce)
    {
        var tracker = _host.CreatePlaceholderContextTracker();
        var stitched = new StringBuilder();
        var placeholders = new List<TemplatePlaceholder>();
        var ordinal = 0;

        foreach (var node in templateNodes)
        {
            if (_templateEngine.IsLiteralNode(node))
            {
                var text = node.Value ?? string.Empty;
                stitched.Append(text);
                tracker.Feed(text);
                continue;
            }

            var id = "tpl" + ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            if (!tracker.TryGetPossibleContexts(out var contexts, out var contextReason))
            {
                return new StitchedHost(
                    stitched.ToString(),
                    placeholders,
                    IsSafe: false,
                    UnsafeReason: $"Template placeholder '{id}' has no valid {_host.HostFormat} AST token context. {contextReason}");
            }

            var context = default(TemplateTokenContext);
            string? unsupportedReason = null;
            var canRepresent = false;
            foreach (var candidate in contexts)
            {
                if (_host.CanRepresent(
                    new TemplatePlaceholderToken(
                        id,
                        string.Empty,
                        _templateEngine.EngineName,
                        _templateEngine.GetTemplateKind(node),
                        _templateEngine.RenderTemplateNode(node),
                        CreatePhysicalMarker(markerNonce, id)),
                    candidate,
                    out unsupportedReason))
                {
                    context = candidate;
                    canRepresent = true;
                    break;
                }
            }

            if (!canRepresent)
            {
                return new StitchedHost(
                    stitched.ToString(),
                    placeholders,
                    IsSafe: false,
                    UnsafeReason: $"Template placeholder '{id}' cannot be safely represented in {_host.HostFormat} contexts '{string.Join(", ", contexts)}'. {unsupportedReason}");
            }

            var token = new TemplatePlaceholderToken(
                id,
                tracker.CreateSemanticKey(context),
                _templateEngine.EngineName,
                _templateEngine.GetTemplateKind(node),
                _templateEngine.RenderTemplateNode(node),
                CreatePhysicalMarker(markerNonce, id));

            if (!_host.TryCreatePlaceholder(token, context, out var shape))
            {
                return new StitchedHost(
                    stitched.ToString(),
                    placeholders,
                    IsSafe: false,
                    UnsafeReason: $"Template placeholder '{id}' cannot be safely inserted in {_host.HostFormat} context '{context}'.");
            }

            stitched.Append(shape.SourceText);
            placeholders.Add(new TemplatePlaceholder(token, shape.Context));
            ordinal++;
        }

        return new StitchedHost(stitched.ToString(), placeholders, IsSafe: true, UnsafeReason: null);
    }

    private static string CreateCollisionFreeMarkerNonce(string sourceText)
    {
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var nonce = CreateMarkerNonce(sourceText, attempt);
            if (!sourceText.Contains(TemplatePlaceholderFields.MarkerPrefix + nonce, StringComparison.Ordinal))
            {
                return nonce;
            }
        }

        throw new InvalidOperationException("Could not create a template placeholder marker nonce that is absent from the source text.");
    }

    private static string CreateMarkerNonce(string sourceText, int attempt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceText + "\n" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string CreatePhysicalMarker(string markerNonce, string placeholderId)
    {
        return TemplatePlaceholderFields.MarkerPrefix + markerNonce + "__" + placeholderId + TemplatePlaceholderFields.MarkerSuffix;
    }

    private AstDocument CreateUnsafeDocument(
        string sourceText,
        string? sourcePath,
        AstDocument templateDocument,
        string unsafeReason)
    {
        return new AstDocument(
            Format,
            CreateRoot("unsafe", unsafeReason, new[]
            {
                new AstNode(
                    "$template",
                    templateDocument.Root.Fields,
                    sourceText,
                    templateDocument.Root.Children)
            }),
            sourcePath,
            sourceText);
    }

    private AstNode CreateRoot(string mode, IReadOnlyList<AstNode> children)
    {
        return CreateRoot(mode, null, children);
    }

    private AstNode CreateRoot(string mode, string? unsafeReason, IReadOnlyList<AstNode> children)
    {
        var fields = FormatAstUtilities.HiddenFields("templatedHost");
        fields[TemplatePlaceholderFields.Engine] = _templateEngine.EngineName;
        fields[TemplatePlaceholderFields.HostFormat] = _host.HostFormat;
        fields[TemplatePlaceholderFields.Mode] = mode;
        if (!string.IsNullOrWhiteSpace(unsafeReason))
        {
            fields[TemplatePlaceholderFields.UnsafeReason] = unsafeReason;
        }

        return new AstNode("$templated", fields, children: children);
    }

    private static AstNode CreateTemplateCollection(IReadOnlyList<TemplatePlaceholder> placeholders)
    {
        return new AstNode(
            "$templates",
            FormatAstUtilities.HiddenFields("templateCollection"),
            children: placeholders.Select(CreateTemplateNode).ToArray());
    }

    private static AstNode CreateTemplateNode(TemplatePlaceholder placeholder)
    {
        var fields = FormatAstUtilities.HiddenFields("templatePlaceholder");
        fields[TemplatePlaceholderFields.PlaceholderId] = placeholder.Token.Id;
        fields[TemplatePlaceholderFields.SemanticKey] = placeholder.Token.SemanticKey;
        fields[TemplatePlaceholderFields.Engine] = placeholder.Token.Engine;
        fields[TemplatePlaceholderFields.TemplateKind] = placeholder.Token.Kind;
        fields[TemplatePlaceholderFields.Context] = placeholder.Context.ToString();
        return new AstNode("$template" + placeholder.Token.Id, fields, placeholder.Token.SourceText);
    }

    private sealed record StitchedHost(
        string Source,
        IReadOnlyList<TemplatePlaceholder> Placeholders,
        bool IsSafe,
        string? UnsafeReason);

    private sealed record TemplatePlaceholder(
        TemplatePlaceholderToken Token,
        TemplateTokenContext Context);
}
