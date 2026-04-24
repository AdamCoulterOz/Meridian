using Meridian.Core.Ast;

namespace Meridian.Formats.Json;

internal static class FormatAstUtilities
{
    public const string NameField = "$name";
    public const string TypeField = "$type";

    public static string EncodeKind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    public static string GetName(AstNode node)
    {
        return node.Fields.TryGetValue(NameField, out var name) ? name : node.Kind;
    }

    public static Dictionary<string, string> HiddenFields(string type)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TypeField] = type
        };
    }
}
