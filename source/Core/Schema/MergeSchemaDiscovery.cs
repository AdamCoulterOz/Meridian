namespace Meridian.Core.Schema;

public static class MergeSchemaDiscovery
{
    public const string SchemaFilePattern = "*.meridian.yaml";

    public static SchemaDiscoveryResult DiscoverForFile(string repoPath, string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var current = Path.GetFullPath(currentDirectory);
        var repositoryRoot = FindRepositoryRoot(current) ??
            throw new InvalidOperationException($"No Git repository root was found from '{current}'.");

        var targetPath = Path.IsPathRooted(repoPath)
            ? Path.GetFullPath(repoPath)
            : Path.GetFullPath(Path.Combine(repositoryRoot, repoPath));
        var targetDirectory = Directory.Exists(targetPath)
            ? targetPath
            : Path.GetDirectoryName(targetPath) ?? repositoryRoot;
        targetDirectory = NearestExistingDirectory(targetDirectory, repositoryRoot);

        if (!IsSameOrChildPath(targetDirectory, repositoryRoot))
            throw new InvalidOperationException(
                $"Path '{repoPath}' resolved outside the Git repository root '{repositoryRoot}'.");

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

    private static bool IsSameOrChildPath(string path, string parent)
    {
        var fullPath = EnsureTrailingSeparator(Path.GetFullPath(path));
        var fullParent = EnsureTrailingSeparator(Path.GetFullPath(parent));
        return fullPath.StartsWith(fullParent, PathComparison);
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        PathComparison);

    private static string EnsureTrailingSeparator(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed + Path.DirectorySeparatorChar;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

public sealed record SchemaDiscoveryResult(
    string RepositoryRoot,
    string TargetDirectory,
    IReadOnlyList<string> SchemaFiles);
