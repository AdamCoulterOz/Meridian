namespace Meridian.Core.Ast;

public static class AstNodeMetadata
{
    public const string NameField = "$name";
    public const string TypeField = "$type";

    public static string EncodeKind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    public static Dictionary<string, string> Create(string type) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TypeField] = type
    };

    public static Dictionary<string, string> Create(string type, string name) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TypeField] = type,
        [NameField] = name
    };

    public static Dictionary<string, string> Create(string type, Dictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        fields[TypeField] = type;
        return fields;
    }

    public static string GetMetadataName(this AstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Fields.TryGetValue(NameField, out var name) ? name : node.Kind;
    }

    public static bool TryGetMetadataType(this AstNode node, out string? type)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Fields.TryGetValue(TypeField, out type);
    }

    public static IReadOnlyDictionary<string, string> VisibleFields(this AstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Fields
            .Where(field => !field.Key.StartsWith('$'))
            .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
    }
}
