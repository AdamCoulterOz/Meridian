using MeridianGit.Abstractions;
using MeridianGit.Formats.Json;
using MeridianGit.Formats.Json5;
using MeridianGit.Formats.Xml;
using MeridianGit.Formats.Yaml;

namespace MeridianGit.Formats.Markup;

public sealed class MarkupMeridianGitProvider : IMeridianGitProvider
{
    public IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations()
    {
        yield return new MeridianGitFormatRegistration(".xml", () => new XmlAdapter());
        yield return new MeridianGitFormatRegistration(".json", () => new JsonAdapter());
        yield return new MeridianGitFormatRegistration(".json5", () => new Json5Adapter());
        yield return new MeridianGitFormatRegistration(".yaml", () => new YamlAdapter());
        yield return new MeridianGitFormatRegistration(".yml", () => new YamlAdapter());
    }
}
