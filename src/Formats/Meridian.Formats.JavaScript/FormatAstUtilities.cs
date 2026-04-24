namespace Meridian.Formats.JavaScript;

internal static class FormatAstUtilities
{
    public static Dictionary<string, string> HiddenFields(string type)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$type"] = type
        };
    }
}
