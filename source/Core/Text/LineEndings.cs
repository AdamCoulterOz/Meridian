namespace Meridian.Core.Text;

/// <summary>
/// Preserves a file's line-ending convention across a merge.
/// </summary>
/// <remarks>
/// Most structured adapters cannot round-trip CRLF: XML parsing normalises line endings to LF
/// per the XML spec, and the JSON/JSON5/YAML/HTML renderers emit their own layout rather than
/// replaying source bytes. Rendering a CRLF file therefore returns LF throughout, and writing
/// that back rewrites every line in a file the user may have changed one line of. For a
/// structural merge tool that is the exact damage it exists to prevent, and it lands hardest on
/// Windows checkouts.
///
/// Fixing it per adapter would mean six changes today and a seventh forgotten tomorrow, so the
/// restore happens once in the driver's write path — the same place, and for the same reason,
/// that the ours-side encoding and BOM are preserved.
/// </remarks>
public static class LineEndings
{
    public const string Crlf = "\r\n";
    public const string Lf = "\n";

    /// <summary>
    /// The dominant line ending in <paramref name="text"/>, or null when it has no line breaks
    /// and therefore no convention to preserve.
    /// </summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var crlf = 0;
        var bare = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
                continue;

            if (index > 0 && text[index - 1] == '\r')
                crlf++;
            else
                bare++;
        }

        if (crlf == 0 && bare == 0)
            return null;

        // A tie goes to CRLF: the only way to get one is a mixed file, and on the platform where
        // mixed files occur the surrounding convention is CRLF.
        return crlf >= bare ? Crlf : Lf;
    }

    /// <summary>
    /// Rewrites every line ending in <paramref name="text"/> to <paramref name="newline"/>.
    /// A null <paramref name="newline"/> leaves the text untouched.
    /// </summary>
    public static string Normalize(string text, string? newline)
    {
        if (string.IsNullOrEmpty(text) || newline is null)
            return text;

        // Collapse to LF first (handling CRLF and lone CR) so mixed input converges, then expand.
        var collapsed = text.Replace(Crlf, Lf, StringComparison.Ordinal).Replace('\r', '\n');
        return newline == Lf ? collapsed : collapsed.Replace(Lf, newline, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites <paramref name="text"/> to match the convention of <paramref name="reference"/>,
    /// which is normally the ours side of the merge.
    /// </summary>
    public static string MatchStyleOf(string text, string? reference) =>
        Normalize(text, Detect(reference));
}
