namespace Meridian.Core.Merging;

public static class ConflictMarkers
{
    public const string OursMarker = "<<<<<<< ours";
    public const string TheirsMarker = ">>>>>>> theirs";

    /// <summary>
    /// True when <paramref name="text"/> already carries Git conflict markers, i.e. an
    /// adapter projected an unresolved conflict inline. The merge driver uses this to
    /// detect the opposite case — a conflicted merge whose render produced no markers —
    /// and fall back to a whole-file conflict instead of writing a resolved-looking file.
    /// </summary>
    public static bool ContainsMarker(string? text) =>
        text is not null && text.Contains(OursMarker, StringComparison.Ordinal);

    public static string Create(string? ours, string? @base, string? theirs)
    {
        _ = @base;
        return string.Join(
            Environment.NewLine,
            "<<<<<<< ours",
            ours ?? string.Empty,
            "=======",
            theirs ?? string.Empty,
            ">>>>>>> theirs");
    }

    public static string CreateDiff3(string? ours, string? @base, string? theirs) => string.Join(
            Environment.NewLine,
            "<<<<<<< ours",
            ours ?? string.Empty,
            "||||||| base",
            @base ?? string.Empty,
            "=======",
            theirs ?? string.Empty,
            ">>>>>>> theirs");
}
