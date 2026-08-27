namespace Meridian.Core.Schema;

public static class MergeSchemaDiscovery
{
    public const string SchemaFilePattern = "*.meridian.yaml";

    public static SchemaDiscoveryResult DiscoverForFile(string repoPath, string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var current = Path.GetFullPath(currentDirectory);
        var targetPath = Path.IsPathRooted(repoPath)
            ? Path.GetFullPath(repoPath)
            : Path.GetFullPath(Path.Combine(current, repoPath));
        var targetDirectory = Directory.Exists(targetPath)
            ? targetPath
            : Path.GetDirectoryName(targetPath) ?? current;

        // Anchor discovery at the target file's directory rather than the process cwd, and treat
        // "no repository found" the same as "no schema files found": fall back to the target
        // directory as the search boundary instead of throwing. This keeps `meridian diff/merge`
        // usable outside a repo and stops an unrelated ancestor repo (e.g. a $HOME dotfiles repo)
        // from anchoring discovery on files it does not own.
        var repositoryRoot = FindRepositoryRoot(targetDirectory) ?? targetDirectory;
        targetDirectory = NearestExistingDirectory(targetDirectory, repositoryRoot);

        var directories = DirectoriesFromRoot(repositoryRoot, targetDirectory);
        var schemaFiles = directories
            .SelectMany(directory => Directory.EnumerateFiles(directory, SchemaFilePattern)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            .ToArray();

        return new SchemaDiscoveryResult(repositoryRoot, targetDirectory, schemaFiles);
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var current = Directory.Exists(startDirectory)
            ? Path.GetFullPath(startDirectory)
            : Path.GetDirectoryName(Path.GetFullPath(startDirectory));

        while (!string.IsNullOrWhiteSpace(current))
        {
            var marker = Path.Combine(current, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return current;

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static string NearestExistingDirectory(string directory, string repositoryRoot)
    {
        var current = Path.GetFullPath(directory);
        while (!Directory.Exists(current) && !PathEquals(current, repositoryRoot))
            current = Path.GetDirectoryName(current) ?? repositoryRoot;

        return current;
    }

    private static IReadOnlyList<string> DirectoriesFromRoot(string repositoryRoot, string targetDirectory)
    {
        var directories = new List<string>();
        var current = Path.GetFullPath(targetDirectory);

        while (true)
        {
            directories.Add(current);
            if (PathEquals(current, repositoryRoot))
                break;

            current = Path.GetDirectoryName(current) ??
                throw new InvalidOperationException($"Path '{targetDirectory}' is not under repository root '{repositoryRoot}'.");
        }

        directories.Reverse();
        return directories;
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

public sealed record SchemaDiscoveryResult(
    string RepositoryRoot,
    string TargetDirectory,
    IReadOnlyList<string> SchemaFiles);
