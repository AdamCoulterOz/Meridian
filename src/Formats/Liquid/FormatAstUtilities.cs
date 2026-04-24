namespace Meridian.Formats.Liquid;

internal static class FormatAstUtilities
{
    public const string TypeField = "$type";

    public static Dictionary<string, string> HiddenFields(string type)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TypeField] = type
        };
    }
}
