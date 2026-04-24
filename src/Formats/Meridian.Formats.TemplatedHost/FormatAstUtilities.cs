namespace Meridian.Formats.TemplatedHost;

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
