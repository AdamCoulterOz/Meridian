using MeridianGit.Abstractions;
using MeridianGit.Formats.Css;
using MeridianGit.Formats.Html;
using MeridianGit.Formats.JavaScript;

namespace MeridianGit.Formats.Web;

public sealed class WebMeridianGitProvider : IMeridianGitProvider
{
    public IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations()
    {
        // The fragment adapter is the entry point for both HTML shapes: it hands a full page
        // (doctype or an <html> root) to HtmlDocumentAdapter, so content decides how a file parses.
        yield return new MeridianGitFormatRegistration(".html", () => new HtmlFragmentAdapter());
        yield return new MeridianGitFormatRegistration(".htm", () => new HtmlFragmentAdapter());
        yield return new MeridianGitFormatRegistration(".css", () => new CssAdapter());
        yield return new MeridianGitFormatRegistration(".js", () => new JavaScriptAdapter());
    }
}
