using System.Globalization;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace MeridianGit.Formats.Yaml;

public sealed class YamlAdapter : IFormatAdapter
{
    private const string ScalarStyleField = "$scalarStyle";

    public string Format => "yaml";

    private const string RawType = "raw";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var stream = new YamlStream();
        stream.Load(new StringReader(sourceText));

        // An empty stream, or a multi-document stream (e.g. Kubernetes manifests), cannot be
        // represented as a single structural tree without losing documents. Round-trip the whole
        // source exactly so no document is silently dropped; conflicts still surface via markers.
        if (stream.Documents.Count != 1)
            return new DocumentTree(
                Format,
                new TreeNode("$rawYaml", NodeMetadata.Create(RawType), sourceText),
                sourcePath,
                sourceText);

        return new DocumentTree(Format, ParseNode(stream.Documents[0].RootNode, "$root"), sourcePath, sourceText);
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        if (node.TryGetMetadataType(out var rootType) && string.Equals(rootType, RawType, StringComparison.Ordinal))
            return node.Value ?? string.Empty;

        var stream = new YamlStream(new YamlDocument(RenderYamlNode(node)));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        return StripDocumentEndMarker(writer.ToString());
    }

    // YamlStream.Save appends an explicit document-end marker ("...") for a single document.
    // Strip that trailing marker line so a round-trip does not gain a spurious "...".
    private static string StripDocumentEndMarker(string text)
    {
        var trimmed = text.TrimEnd('\n', '\r');
        if (!trimmed.EndsWith("...", StringComparison.Ordinal))
            return text;

        var lastLineStart = trimmed.LastIndexOf('\n') + 1;
        if (trimmed[lastLineStart..] == "...")
            return trimmed[..lastLineStart].TrimEnd('\n', '\r') + Environment.NewLine;

        return text;
    }

    private static TreeNode ParseNode(YamlNode node, string kind) => node switch
    {
        YamlMappingNode mapping => ParseMapping(mapping, kind),
        YamlSequenceNode sequence => ParseSequence(sequence, kind),
        YamlScalarNode scalar => ParseScalar(scalar, kind),
        _ => throw new NotSupportedException($"Unsupported YAML node type '{node.GetType().Name}'.")
    };

    private static TreeNode ParseMapping(YamlMappingNode mapping, string kind)
    {
        var children = mapping.Children.Select(pair =>
        {
            if (pair.Key is not YamlScalarNode key)
                throw new NotSupportedException("Only scalar YAML mapping keys are supported.");

            var name = key.Value ?? string.Empty;
            var child = ParseNode(pair.Value, NodeMetadata.EncodeKind(name));
            return child with { Fields = AddName(child.Fields, name) };
        }).ToArray();

        return new TreeNode(kind, NodeMetadata.Create("mapping"), children: children);
    }

    private static TreeNode ParseSequence(YamlSequenceNode sequence, string kind)
    {
        var children = sequence.Children
            .Select((item, index) => ParseNode(item, $"$item{index:D6}"))
            .ToArray();

        return new TreeNode(kind, NodeMetadata.Create("sequence"), children: children);
    }

    private static TreeNode ParseScalar(YamlScalarNode scalar, string kind)
    {
        var fields = NodeMetadata.Create("scalar");
        fields[ScalarStyleField] = scalar.Style.ToString();
        return new TreeNode(kind, fields, scalar.Value);
    }

    private static IReadOnlyDictionary<string, string> AddName(IReadOnlyDictionary<string, string> fields, string name)
    {
        var copy = fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
        copy[NodeMetadata.NameField] = name;
        return copy;
    }

    private static YamlNode RenderYamlNode(TreeNode node)
    {
        var type = node.TryGetMetadataType(out var nodeType)
            ? nodeType
            : node.Children.Count > 0 ? "mapping" : "scalar";

        return type switch
        {
            "mapping" => RenderMapping(node),
            "sequence" => RenderSequence(node),
            _ => RenderScalar(node)
        };
    }

    private static YamlScalarNode RenderScalar(TreeNode node)
    {
        var style = node.Fields.TryGetValue(ScalarStyleField, out var styleName)
            && Enum.TryParse<ScalarStyle>(styleName, ignoreCase: true, out var parsedStyle)
                ? parsedStyle
                : ScalarStyle.Any;

        // A null value — or an empty plain scalar, which YAML reads as null — must render as a
        // bare plain scalar (key:), not the quoted empty string '' (which would change its type).
        if (node.Value is null || (node.Value.Length == 0 && style == ScalarStyle.Plain))
            return new YamlScalarNode((string?)null) { Style = ScalarStyle.Plain };

        var scalar = new YamlScalarNode(node.Value);
        if (style != ScalarStyle.Any)
            scalar.Style = style;

        return scalar;
    }

    private static YamlMappingNode RenderMapping(TreeNode node)
    {
        var mapping = new YamlMappingNode();
        foreach (var child in node.Children)
            mapping.Add(child.GetMetadataName(), RenderYamlNode(child));

        return mapping;
    }

    private static YamlSequenceNode RenderSequence(TreeNode node)
    {
        var sequence = new YamlSequenceNode();
        foreach (var child in node.Children)
            sequence.Add(RenderYamlNode(child));

        return sequence;
    }
}
