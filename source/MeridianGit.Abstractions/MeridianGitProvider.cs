using Meridian.Core.Formats;

namespace MeridianGit.Abstractions;

/// <summary>
/// Implemented by provider assemblies that add file-format support to the
/// MeridianGit command-line tool.
/// </summary>
public interface IMeridianGitProvider
{
    IEnumerable<MeridianGitFormatRegistration> GetFormatRegistrations();
}

/// <summary>
/// Registers an adapter type for a repository file extension.
/// </summary>
public sealed class MeridianGitFormatRegistration
{
    private readonly Func<IFormatAdapter> _adapterFactory;

    public MeridianGitFormatRegistration(string extension, Func<IFormatAdapter> adapterFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(adapterFactory);

        if (!extension.StartsWith(".", StringComparison.Ordinal))
            throw new ArgumentException("File extensions must start with '.'.", nameof(extension));

        Extension = extension.ToLowerInvariant();
        _adapterFactory = adapterFactory;
    }

    public string Extension { get; }

    public IFormatAdapter CreateAdapter() => _adapterFactory();
}
