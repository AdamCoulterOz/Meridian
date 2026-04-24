namespace Meridian.Core.Merging;

public static class ConflictMarkers
{
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

    public static string CreateDiff3(string? ours, string? @base, string? theirs)
    {
        return string.Join(
            Environment.NewLine,
            "<<<<<<< ours",
            ours ?? string.Empty,
            "||||||| base",
            @base ?? string.Empty,
            "=======",
            theirs ?? string.Empty,
            ">>>>>>> theirs");
    }
}
