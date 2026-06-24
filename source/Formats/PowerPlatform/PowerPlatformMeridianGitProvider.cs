using MeridianGit.Abstractions;
using MeridianGit.Formats.Liquid;
using MeridianGit.Formats.Xap;

namespace MeridianGit.Formats.PowerPlatform;

public sealed class PowerPlatformMeridianGitProvider : IMeridianGitProvider
{
    public IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations()
    {
        yield return new MeridianGitFormatRegistration(".xap", () => new XapAdapter());
        yield return new MeridianGitFormatRegistration(".liquid", () => new LiquidAdapter());
    }
}
