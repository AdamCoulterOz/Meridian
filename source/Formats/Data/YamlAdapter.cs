using System.Globalization;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using YamlDotNet.RepresentationModel;

namespace Meridian.Formats.Data;

public sealed class YamlAdapter : IFormatAdapter
{
    private const string ScalarStyleField = "$scalarStyle";

    public string Format => "yaml";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var stream = new YamlStream();
        stream.Load(new StringReader(sourceText));
        if (stream.Documents.Count == 0)
            throw new InvalidOperationException("YAML document is empty.");

        return new DocumentTree(Format, ParseNode(stream.Documents[0].RootNode, "$root"), sourcePath, sourceText);
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root);

    public string RenderNode(TreeNode node)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        var stream = new YamlStream(new YamlDocument(RenderYamlNode(node)));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
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
            _ => new YamlScalarNode(node.Value ?? string.Empty)
        };
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
