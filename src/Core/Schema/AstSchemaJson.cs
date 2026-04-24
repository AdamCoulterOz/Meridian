using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meridian.Core.Schema;

public static class AstSchemaJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new NodeIdentityRuleJsonConverter());
        options.Converters.Add(new PathSelectorJsonConverter());
        options.Converters.Add(new FormatFromRuleJsonConverter());
        return options;
    }
}

internal sealed class NodeIdentityRuleJsonConverter : JsonConverter<NodeIdentityRule>
{
    public override NodeIdentityRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader) as JsonObject ??
            throw new JsonException("Node identity rule must be a JSON object.");

        return new NodeIdentityRule(
            node["path"]?.Deserialize<PathSelector>(options) ??
                throw new JsonException("Node identity rule requires path."),
            ReadDiscriminatorKey(node["key"], options),
            node.ReadString("note"));
    }

    public override void Write(Utf8JsonWriter writer, NodeIdentityRule value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("path");
        JsonSerializer.Serialize(writer, value.Path, options);
        writer.WritePropertyName("key");
        JsonSerializer.Serialize(writer, value.Key, options);
        writer.WriteString("note", value.Note);
        writer.WriteEndObject();
    }

    private static DiscriminatorKey ReadDiscriminatorKey(JsonNode? node, JsonSerializerOptions options)
    {
        if (node is not JsonObject key)
            throw new JsonException("Node identity rule requires key.");

        if (key.ReadString("$type") is { Length: > 0 })
            return key.Deserialize<DiscriminatorKey>(options) ??
                throw new JsonException("Discriminator key could not be read.");

        if (key.ReadString("attribute") is { Length: > 0 } attribute)
            return new DiscriminatorKey.Field(attribute);

        if (key.ReadString("element") is { Length: > 0 } element)
            return new DiscriminatorKey.PathValue(element);

        if (key.ReadBoolean("text") == true)
            return new DiscriminatorKey.Text();

        if (key.ReadString("structural") is { Length: > 0 } structural)
            return structural == "orderedSlot"
                ? new DiscriminatorKey.Structural(StructuralDiscriminator.OrderedSlot)
                : throw new JsonException($"Unsupported structural discriminator '{structural}'.");

        if (key["composite"] is JsonArray composite)
            return new DiscriminatorKey.Composite(composite.Select(part => ReadCompositePart(part, options)).ToArray());

        throw new JsonException("Unsupported discriminator key shape.");
    }

    private static CompositePart ReadCompositePart(JsonNode? node, JsonSerializerOptions options)
    {
        if (node is not JsonObject part)
            throw new JsonException("Composite discriminator part must be an object.");

        if (part["key"] is not null)
            return part.Deserialize<CompositePart>(options) ??
                throw new JsonException("Composite discriminator part could not be read.");

        DiscriminatorKey key;
        if (part.ReadString("attribute") is { Length: > 0 } attribute)
            key = new DiscriminatorKey.Field(attribute);
        else if (part.ReadString("path") is { Length: > 0 } path)
            key = new DiscriminatorKey.PathValue(path);
        else if (part.ReadString("element") is { Length: > 0 } element)
            key = new DiscriminatorKey.PathValue(element);
        else
            throw new JsonException("Composite discriminator part must define attribute, element, or path.");

        return new CompositePart(key, part.ReadBoolean("optional") ?? false);
    }
}

internal sealed class PathSelectorJsonConverter : JsonConverter<PathSelector>
{
    public override PathSelector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.String)
            return Parse(root.GetString() ?? string.Empty);

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("regex", out var regex))
                return PathSelector.Regex(regex.GetString() ?? string.Empty);

            var pattern = root.TryGetProperty("pattern", out var patternProperty)
                ? patternProperty.GetString() ?? string.Empty
                : string.Empty;
            var isRegex = root.TryGetProperty("isRegex", out var isRegexProperty) && isRegexProperty.GetBoolean();
            return new PathSelector(pattern, isRegex);
        }

        throw new JsonException("Path selector must be a string or object.");
    }

    public override void Write(Utf8JsonWriter writer, PathSelector value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new { value.Pattern, value.IsRegex }, options);

    private static PathSelector Parse(string value)
    {
        if (value.Contains('*', StringComparison.Ordinal))
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(value)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";
            return PathSelector.Regex(regex);
        }

        return PathSelector.Exact(value);
    }
}

internal sealed class FormatFromRuleJsonConverter : JsonConverter<FormatFromRule>
{
    public override FormatFromRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader) as JsonObject ??
            throw new JsonException("formatFrom must be an object.");
        var path = node.ReadString("path") ?? throw new JsonException("formatFrom requires path.");

        if (node["enum"] is JsonObject enumMap)
            return new FormatFromRule(
                path,
                enumMap.Select(pair => new FormatMapEntry(
                    ScalarFromString(pair.Key),
                    pair.Value?.GetValue<string>() ?? string.Empty)).ToArray());

        return new FormatFromRule(
            path,
            node["enum"]?.Deserialize<List<FormatMapEntry>>(options) ?? []);
    }

    public override void Write(Utf8JsonWriter writer, FormatFromRule value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WritePropertyName("enum");
        JsonSerializer.Serialize(writer, value.Enum, options);
        writer.WriteEndObject();
    }

    private static SchemaScalarValue ScalarFromString(string value)
    {
        return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer)
            ? new SchemaScalarValue.Integer(integer)
            : new SchemaScalarValue.String(value);
    }
}

internal static class JsonObjectExtensions
{
    public static string? ReadString(this JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    public static bool? ReadBoolean(this JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : null;
}
