using MeridianGit.Abstractions;
using MeridianGit.Formats.Gif;
using MeridianGit.Formats.Ico;
using MeridianGit.Formats.Jpeg;
using MeridianGit.Formats.Png;

namespace MeridianGit.Formats.Images;

public sealed class ImagesMeridianGitProvider : IMeridianGitProvider
{
    public IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations()
    {
        yield return new MeridianGitFormatRegistration(".png", () => new PngAdapter());
        yield return new MeridianGitFormatRegistration(".jpg", () => new JpgAdapter());
        yield return new MeridianGitFormatRegistration(".jpeg", () => new JpgAdapter());
        yield return new MeridianGitFormatRegistration(".gif", () => new GifAdapter());
        yield return new MeridianGitFormatRegistration(".ico", () => new IcoAdapter());
    }
}
