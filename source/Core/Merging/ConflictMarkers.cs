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
    /// <remarks>
    /// Only a marker at the start of a line counts. A bare substring search matches the
    /// marker text appearing as ordinary content (a JSON string documenting conflict
    /// syntax, a YAML block scalar, a test fixture), which would make a conflicted merge
    /// look projected and silently write a resolved file.
    /// </remarks>
    public static bool ContainsMarker(string? text) => CountMarkers(text) > 0;

    /// <summary>
    /// True when rendering actually PROJECTED a conflict, rather than merely carrying marker
    /// text that was already in the inputs. Content that legitimately contains a marker line
    /// appears in the sides too, so a render that adds none is not a projection.
    /// </summary>
    public static bool ProjectedConflict(string? rendered, string? ours, string? @base, string? theirs)
    {
        var inInputs = Math.Max(CountMarkers(ours), Math.Max(CountMarkers(@base), CountMarkers(theirs)));
        return CountMarkers(rendered) > inInputs;
    }

    /// <summary>Counts line-anchored occurrences of the ours marker.</summary>
    private static int CountMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(OursMarker, index, StringComparison.Ordinal)) >= 0)
        {
            if (index == 0 || text[index - 1] == '\n' || text[index - 1] == '\r')
                count++;
            index += OursMarker.Length;
        }

        return count;
    }

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
