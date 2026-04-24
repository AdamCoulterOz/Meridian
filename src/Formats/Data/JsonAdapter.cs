using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Data;

public class JsonAdapter : IFormatAdapter
{
    private const string ValueKindField = "$valueKind";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public virtual string Format => "json";

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var node = ParseJsonNode(sourceText);
        return new DocumentTree(Format, ParseNode(node, "$root"), sourcePath, sourceText);
    }

    public string RenderDocument(DocumentTree document) => RenderNode(document.Root) + Environment.NewLine;

    public string RenderNode(TreeNode node)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        return RenderJsonNode(node)?.ToJsonString(JsonOptions) ?? "null";
    }

    protected virtual JsonNode? ParseJsonNode(string sourceText) => JsonNode.Parse(sourceText, documentOptions: new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    });

    private static TreeNode ParseNode(JsonNode? node, string kind) => node switch
    {
        JsonObject obj => ParseObject(obj, kind),
        JsonArray array => ParseArray(array, kind),
        JsonValue value => ParseValue(value, kind),
        null => ParseNull(kind),
        _ => throw new NotSupportedException($"Unsupported JSON node type '{node.GetType().Name}'.")
    };

    private static TreeNode ParseObject(JsonObject obj, string kind)
    {
        var children = obj
            .Select(property =>
            {
                var child = ParseNode(property.Value, NodeMetadata.EncodeKind(property.Key));
                return child with { Fields = AddName(child.Fields, property.Key) };
            })
            .ToArray();

        return new TreeNode(kind, NodeMetadata.Create("object"), children: children);
    }

    private static TreeNode ParseArray(JsonArray array, string kind)
    {
        var children = array
            .Select((item, index) => ParseNode(item, $"$item{index:D6}"))
            .ToArray();

        return new TreeNode(kind, NodeMetadata.Create("array"), children: children);
    }

    private static TreeNode ParseNull(string kind)
    {
        var fields = NodeMetadata.Create("null");
        fields[ValueKindField] = nameof(JsonValueKind.Null);
        return new TreeNode(kind, fields);
    }

    private static TreeNode ParseValue(JsonValue value, string kind)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        var element = document.RootElement;
        var fields = NodeMetadata.Create("value");
        fields[ValueKindField] = element.ValueKind.ToString();

        var scalar = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };

        return new TreeNode(kind, fields, scalar);
    }

    private static IReadOnlyDictionary<string, string> AddName(IReadOnlyDictionary<string, string> fields, string name)
    {
        var copy = fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
        copy[NodeMetadata.NameField] = name;
        return copy;
    }

    private static JsonNode? RenderJsonNode(TreeNode node)
    {
        var type = node.TryGetMetadataType(out var nodeType)
            ? nodeType
            : InferType(node);

        return type switch
        {
            "object" => RenderObject(node),
            "array" => RenderArray(node),
            "null" => null,
            _ => RenderValue(node)
        };
    }

    private static JsonObject RenderObject(TreeNode node)
    {
        var obj = new JsonObject();
        foreach (var child in node.Children)
            obj[child.GetMetadataName()] = RenderJsonNode(child);

        return obj;
    }

    private static JsonArray RenderArray(TreeNode node)
    {
        var array = new JsonArray();
        foreach (var child in node.Children)
            array.Add(RenderJsonNode(child));

        return array;
    }

    private static JsonNode? RenderValue(TreeNode node)
    {
        if (!node.Fields.TryGetValue(ValueKindField, out var valueKind))
            return node.Value is null ? null : JsonValue.Create(node.Value);

        return valueKind switch
        {
            nameof(JsonValueKind.String) => JsonValue.Create(node.Value ?? string.Empty),
            nameof(JsonValueKind.True) => JsonValue.Create(true),
            nameof(JsonValueKind.False) => JsonValue.Create(false),
            nameof(JsonValueKind.Number) => JsonNode.Parse(node.Value ?? "0"),
            nameof(JsonValueKind.Null) => null,
            _ => node.Value is null ? null : JsonValue.Create(node.Value)
        };
    }

    private static string InferType(TreeNode node) => node.Children.Count > 0 ? "object" : "value";
}
