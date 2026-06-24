using MeridianGit.Abstractions;
using MeridianGit.Formats.Css;
using MeridianGit.Formats.Html;
using MeridianGit.Formats.JavaScript;

namespace MeridianGit.Formats.Web;

public sealed class WebMeridianGitProvider : IMeridianGitProvider
{
    public IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations()
    {
        yield return new MeridianGitFormatRegistration(".html", () => new HtmlFragmentAdapter());
        yield return new MeridianGitFormatRegistration(".htm", () => new HtmlFragmentAdapter());
        yield return new MeridianGitFormatRegistration(".css", () => new CssAdapter());
        yield return new MeridianGitFormatRegistration(".js", () => new JavaScriptAdapter());
    }
}
