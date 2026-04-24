using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Meridian.Core.Ast;
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

    public AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var node = ParseJsonNode(sourceText);
        return new AstDocument(Format, ParseNode(node, "$root"), sourcePath, sourceText);
    }

    public string RenderDocument(AstDocument document) => RenderNode(document.Root) + Environment.NewLine;

    public string RenderNode(AstNode node)
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

    private static AstNode ParseNode(JsonNode? node, string kind) => node switch
    {
        JsonObject obj => ParseObject(obj, kind),
        JsonArray array => ParseArray(array, kind),
        JsonValue value => ParseValue(value, kind),
        null => ParseNull(kind),
        _ => throw new NotSupportedException($"Unsupported JSON node type '{node.GetType().Name}'.")
    };

    private static AstNode ParseObject(JsonObject obj, string kind)
    {
        var children = obj
            .Select(property =>
            {
                var child = ParseNode(property.Value, AstNodeMetadata.EncodeKind(property.Key));
                return child with { Fields = AddName(child.Fields, property.Key) };
            })
            .ToArray();

        return new AstNode(kind, AstNodeMetadata.Create("object"), children: children);
    }

    private static AstNode ParseArray(JsonArray array, string kind)
    {
        var children = array
            .Select((item, index) => ParseNode(item, $"$item{index:D6}"))
            .ToArray();

        return new AstNode(kind, AstNodeMetadata.Create("array"), children: children);
    }

    private static AstNode ParseNull(string kind)
    {
        var fields = AstNodeMetadata.Create("null");
        fields[ValueKindField] = nameof(JsonValueKind.Null);
        return new AstNode(kind, fields);
    }

    private static AstNode ParseValue(JsonValue value, string kind)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        var element = document.RootElement;
        var fields = AstNodeMetadata.Create("value");
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

        return new AstNode(kind, fields, scalar);
    }

    private static IReadOnlyDictionary<string, string> AddName(IReadOnlyDictionary<string, string> fields, string name)
    {
        var copy = fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
        copy[AstNodeMetadata.NameField] = name;
        return copy;
    }

    private static JsonNode? RenderJsonNode(AstNode node)
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

    private static JsonObject RenderObject(AstNode node)
    {
        var obj = new JsonObject();
        foreach (var child in node.Children)
            obj[child.GetMetadataName()] = RenderJsonNode(child);

        return obj;
    }

    private static JsonArray RenderArray(AstNode node)
    {
        var array = new JsonArray();
        foreach (var child in node.Children)
            array.Add(RenderJsonNode(child));

        return array;
    }

    private static JsonNode? RenderValue(AstNode node)
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

    private static string InferType(AstNode node) => node.Children.Count > 0 ? "object" : "value";
}
