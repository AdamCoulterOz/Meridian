using MeridianGit.Abstractions;

namespace MeridianGit.Formats.Binary;

public sealed class BinaryMeridianGitProvider : IMeridianGitProvider
{
    public IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations()
    {
        yield return new MeridianGitFormatRegistration(".bin", () => new BinAdapter());
    }
}
