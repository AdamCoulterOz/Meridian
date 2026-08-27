using System.Diagnostics;

namespace Meridian.Tests;

public sealed class GitIntegrationCommandTests : IDisposable
{
    private readonly List<string> _createdRepositories = [];

    public void Dispose()
    {
        foreach (var repository in _createdRepositories)
        {
            try
            {
                if (!Directory.Exists(repository))
                    continue;

                // Git marks everything under .git/objects read-only. On Windows that makes
                // Directory.Delete throw UnauthorizedAccessException (which is NOT an
                // IOException, so it escaped the catch below and failed the whole test class).
                foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(repository, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; a locked temp file must not fail the test run.
            }
        }
    }

    [Fact]
    public async Task ConflictingTextMergeWritesMarkersAndExitsOne()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var basePath = Path.Combine(repository, "base.xml");
        var oursPath = Path.Combine(repository, "ours.xml");
        var theirsPath = Path.Combine(repository, "theirs.xml");

        await File.WriteAllTextAsync(basePath, """<root><item id="1">Base</item></root>""");
        await File.WriteAllTextAsync(oursPath, """<root><item id="1">Ours</item></root>""");
        await File.WriteAllTextAsync(theirsPath, """<root><item id="1">Theirs</item></root>""");

        var result = await RunGitMergeAsync(
            repository, "merge", "--base", basePath, "--ours", oursPath, "--theirs", theirsPath, "--path", "catalog.xml");

        Assert.Equal(1, result.ExitCode);
        var merged = await File.ReadAllTextAsync(oursPath);
        Assert.Contains("<<<<<<< ours", merged);
        Assert.Contains(">>>>>>> theirs", merged);
    }

    [Fact]
    public async Task AddAddMergeWithEmptyBaseDoesNotCrash()
    {
        var repository = CreateTemporaryRepository();
        var basePath = Path.Combine(repository, "base.json");
        var oursPath = Path.Combine(repository, "ours.json");
        var theirsPath = Path.Combine(repository, "theirs.json");

        await File.WriteAllTextAsync(basePath, string.Empty);
        await File.WriteAllTextAsync(oursPath, "{\"x\":1}\n");
        await File.WriteAllTextAsync(theirsPath, "{\"y\":2}\n");

        var result = await RunGitMergeAsync(
            repository, "merge", "--base", basePath, "--ours", oursPath, "--theirs", theirsPath, "--path", "config.json");

        Assert.Equal(1, result.ExitCode);
        var merged = await File.ReadAllTextAsync(oursPath);
        Assert.Contains("<<<<<<< ours", merged);
    }

    [Fact]
    public async Task ExternalDiffOfAddedFileExitsZero()
    {
        var repository = CreateTemporaryRepository();
        var newPath = Path.Combine(repository, "new.json");
        await File.WriteAllTextAsync(newPath, "{\"a\":1}\n");

        // Git passes /dev/null for the missing side of an added file.
        var result = await RunGitMergeAsync(
            repository, "diff", "config.json", "/dev/null", "0000000", "100644", newPath, "1111111", "100644");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task UnknownExtensionExitsTwo()
    {
        var repository = CreateTemporaryRepository();
        var basePath = Path.Combine(repository, "base.txt");
        var oursPath = Path.Combine(repository, "ours.txt");
        var theirsPath = Path.Combine(repository, "theirs.txt");
        await File.WriteAllTextAsync(basePath, "a");
        await File.WriteAllTextAsync(oursPath, "b");
        await File.WriteAllTextAsync(theirsPath, "c");

        var result = await RunGitMergeAsync(
            repository, "merge", "--base", basePath, "--ours", oursPath, "--theirs", theirsPath, "--path", "notes.txt");

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task MergeCommandUsesGitMergeDriverArgumentsAndWritesOurs()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var basePath = Path.Combine(repository, "base.xml");
        var oursPath = Path.Combine(repository, "ours.xml");
        var theirsPath = Path.Combine(repository, "theirs.xml");

        await File.WriteAllTextAsync(basePath, """<root><item id="1">Base</item></root>""");
        await File.WriteAllTextAsync(oursPath, """<root><item id="1">Local</item></root>""");
        await File.WriteAllTextAsync(theirsPath, """<root><item id="1">Base</item><item id="2">Remote</item></root>""");

        var result = await RunGitMergeAsync(
            repository,
            "merge",
            "--base", basePath,
            "--ours", oursPath,
            "--theirs", theirsPath,
            "--path", "catalog.xml");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);

        var merged = await File.ReadAllTextAsync(oursPath);
        Assert.Contains("""<item id="1">Local</item>""", merged);
        Assert.Contains("""<item id="2">Remote</item>""", merged);
    }

    [Fact]
    public async Task DiffCommandUsesGitExternalDiffArguments()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var oldPath = Path.Combine(repository, "old.xml");
        var newPath = Path.Combine(repository, "new.xml");

        await File.WriteAllTextAsync(oldPath, """<root><item id="1" name="Old">Before</item></root>""");
        await File.WriteAllTextAsync(newPath, """<root><item id="1" name="New">After</item><item id="2">Added</item></root>""");

        var result = await RunGitMergeAsync(
            repository,
            "diff",
            "catalog.xml",
            oldPath,
            "0000000",
            "100644",
            newPath,
            "1111111",
            "100644");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("diff --meridian a/catalog.xml b/catalog.xml", result.StandardOutput);
        Assert.Contains("~ @name: \"Old\" -> \"New\"", result.StandardOutput);
        Assert.Contains("~ value: \"Before\" -> \"After\"", result.StandardOutput);
        Assert.Contains("+ node added", result.StandardOutput);
        Assert.Contains("""+ <item id="2">Added</item>""", result.StandardOutput);
    }

    [Fact]
    public async Task DiffCommandUsesExplicitTwoWayArguments()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var oldPath = Path.Combine(repository, "old.xml");
        var newPath = Path.Combine(repository, "new.xml");

        await File.WriteAllTextAsync(oldPath, """<root><item id="1" /></root>""");
        await File.WriteAllTextAsync(newPath, """<root><item id="2" /></root>""");

        var result = await RunGitMergeAsync(
            repository,
            "diff",
            "--old", oldPath,
            "--new", newPath,
            "--path", "catalog.xml");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("@@ root/item[id=1] @@", result.StandardOutput);
        Assert.Contains("- node removed", result.StandardOutput);
        Assert.Contains("@@ root/item[id=2] @@", result.StandardOutput);
        Assert.Contains("+ node added", result.StandardOutput);
    }

    [Fact]
    public async Task GitDiffDriverUsesMeridianExternalDiffCommand()
    {
        var repository = CreateTemporaryRepository();
        await InitializeGitRepositoryAsync(repository);
        WriteCatalogSchema(repository);
        await File.WriteAllTextAsync(
            Path.Combine(repository, ".gitattributes"),
            "*.xml diff=meridian\n");
        await RunGitAsync(repository, "config", "diff.meridian.command", $"dotnet \"{GitMergeAssemblyPath}\" diff");

        var catalogPath = Path.Combine(repository, "catalog.xml");
        await File.WriteAllTextAsync(catalogPath, """<root><item id="1" name="Old">Before</item></root>""");
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "initial");

        await File.WriteAllTextAsync(catalogPath, """<root><item id="1" name="New">After</item></root>""");

        var result = await RunGitAsync(repository, "diff", "--", "catalog.xml");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("diff --meridian a/catalog.xml b/catalog.xml", result.StandardOutput);
        Assert.Contains("~ @name: \"Old\" -> \"New\"", result.StandardOutput);
        Assert.Contains("~ value: \"Before\" -> \"After\"", result.StandardOutput);
    }

    [Fact]
    public async Task GitMergeDriverUsesMeridianMergeCommand()
    {
        var repository = CreateTemporaryRepository();
        await InitializeGitRepositoryAsync(repository);
        WriteCatalogSchema(repository);
        await File.WriteAllTextAsync(
            Path.Combine(repository, ".gitattributes"),
            "*.xml merge=meridian\n");
        await RunGitAsync(repository, "config", "merge.meridian.driver", $"dotnet \"{GitMergeAssemblyPath}\" merge --base %O --ours %A --theirs %B --path %P");

        var catalogPath = Path.Combine(repository, "catalog.xml");
        await File.WriteAllTextAsync(catalogPath, """<root><item id="1">Base</item></root>""");
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "initial");

        await RunGitAsync(repository, "checkout", "-b", "topic");
        await File.WriteAllTextAsync(catalogPath, """<root><item id="1">Local</item></root>""");
        await RunGitAsync(repository, "commit", "-am", "local change");

        await RunGitAsync(repository, "checkout", "main");
        await File.WriteAllTextAsync(catalogPath, """<root><item id="1">Base</item><item id="2">Remote</item></root>""");
        await RunGitAsync(repository, "commit", "-am", "remote change");

        var result = await RunGitAsync(repository, "merge", "--no-ff", "topic");

        Assert.Equal(0, result.ExitCode);
        var merged = await File.ReadAllTextAsync(catalogPath);
        Assert.Contains("""<item id="1">Local</item>""", merged);
        Assert.Contains("""<item id="2">Remote</item>""", merged);
    }

    [Fact]
    public async Task MergeCommandMergesBinaryWhenOnlyOneSideChanged()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var basePath = Path.Combine(repository, "base.png");
        var oursPath = Path.Combine(repository, "ours.png");
        var theirsPath = Path.Combine(repository, "theirs.png");

        byte[] baseBytes = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xFE];
        byte[] theirsBytes = [0x89, 0x50, 0x4E, 0x47, 0x20, 0xFF, 0xFE];
        await File.WriteAllBytesAsync(basePath, baseBytes);
        await File.WriteAllBytesAsync(oursPath, baseBytes);
        await File.WriteAllBytesAsync(theirsPath, theirsBytes);

        var result = await RunGitMergeAsync(
            repository,
            "merge",
            "--base", basePath,
            "--ours", oursPath,
            "--theirs", theirsPath,
            "--path", "image.png");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(theirsBytes, await File.ReadAllBytesAsync(oursPath));
    }

    // Most structured adapters cannot round-trip CRLF, so a merged CRLF file used to come back
    // LF throughout: every line rewritten in a file the user changed one line of. Windows-heavy
    // consumers (Power Platform exports) feel this hardest.
    [Theory]
    [InlineData("catalog.json", "{\r\n  \"a\": 1,\r\n  \"b\": 1\r\n}\r\n", "{\r\n  \"a\": 2,\r\n  \"b\": 1\r\n}\r\n", "{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}\r\n")]
    [InlineData("catalog.yaml", "a: 1\r\nb: 1\r\n", "a: 2\r\nb: 1\r\n", "a: 1\r\nb: 2\r\n")]
    [InlineData("catalog.xml", "<r>\r\n  <a>1</a>\r\n  <b>1</b>\r\n</r>\r\n", "<r>\r\n  <a>2</a>\r\n  <b>1</b>\r\n</r>\r\n", "<r>\r\n  <a>1</a>\r\n  <b>2</b>\r\n</r>\r\n")]
    public async Task MergeCommandPreservesCrlf(string fileName, string baseText, string oursText, string theirsText)
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var extension = Path.GetExtension(fileName);
        var basePath = Path.Combine(repository, "base" + extension);
        var oursPath = Path.Combine(repository, "ours" + extension);
        var theirsPath = Path.Combine(repository, "theirs" + extension);

        await File.WriteAllTextAsync(basePath, baseText);
        await File.WriteAllTextAsync(oursPath, oursText);
        await File.WriteAllTextAsync(theirsPath, theirsText);

        var result = await RunGitMergeAsync(
            repository, "merge", "--base", basePath, "--ours", oursPath, "--theirs", theirsPath, "--path", fileName);

        Assert.Equal(0, result.ExitCode);
        var merged = await File.ReadAllTextAsync(oursPath);
        Assert.Contains("\r\n", merged);
        Assert.DoesNotMatch("(?<!\\r)\\n", merged);   // no bare LF: the file must not end up mixed
        Assert.Contains("2", merged);
    }

    // The other direction matters just as much: an LF file must not acquire CRLF.
    [Fact]
    public async Task MergeCommandLeavesLfFilesAlone()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var basePath = Path.Combine(repository, "base.json");
        var oursPath = Path.Combine(repository, "ours.json");
        var theirsPath = Path.Combine(repository, "theirs.json");

        await File.WriteAllTextAsync(basePath, "{\n  \"a\": 1,\n  \"b\": 1\n}\n");
        await File.WriteAllTextAsync(oursPath, "{\n  \"a\": 2,\n  \"b\": 1\n}\n");
        await File.WriteAllTextAsync(theirsPath, "{\n  \"a\": 1,\n  \"b\": 2\n}\n");

        var result = await RunGitMergeAsync(
            repository, "merge", "--base", basePath, "--ours", oursPath, "--theirs", theirsPath, "--path", "catalog.json");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("\r", await File.ReadAllTextAsync(oursPath));
    }

    [Fact]
    public async Task MergeCommandLeavesBinaryConflictAsOurs()
    {
        var repository = CreateTemporaryRepository();
        WriteCatalogSchema(repository);
        var basePath = Path.Combine(repository, "base.png");
        var oursPath = Path.Combine(repository, "ours.png");
        var theirsPath = Path.Combine(repository, "theirs.png");

        byte[] baseBytes = [0x00, 0x01, 0x02];
        byte[] oursBytes = [0x10, 0x01, 0x02];
        byte[] theirsBytes = [0x20, 0x01, 0x02];
        await File.WriteAllBytesAsync(basePath, baseBytes);
        await File.WriteAllBytesAsync(oursPath, oursBytes);
        await File.WriteAllBytesAsync(theirsPath, theirsBytes);

        var result = await RunGitMergeAsync(
            repository,
            "merge",
            "--base", basePath,
            "--ours", oursPath,
            "--theirs", theirsPath,
            "--path", "image.png");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("binary", result.StandardError);
        Assert.Equal(oursBytes, await File.ReadAllBytesAsync(oursPath));
    }

    private string CreateTemporaryRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridiangit-integration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        _createdRepositories.Add(root);
        return root;
    }

    private static void WriteCatalogSchema(string repository)
    {
        File.WriteAllText(
            Path.Combine(repository, "catalog.meridian.yaml"),
            """
schemaVersion: 0.1
name: catalog
defaults:
  globalDiscriminatorFields:
    - id
files:
  - match: catalog.xml
    discriminators:
      - path: root/item
        key:
          attribute: id
""");
    }

    private static async Task<CommandResult> RunGitMergeAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add(GitMergeAssemblyPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return await RunProcessAsync(startInfo);
    }

    private static async Task InitializeGitRepositoryAsync(string workingDirectory)
    {
        await RunGitAsync(workingDirectory, "init", "-b", "main");
        await RunGitAsync(workingDirectory, "config", "user.email", "meridian-tests@example.invalid");
        await RunGitAsync(workingDirectory, "config", "user.name", "Meridian Tests");
    }

    private static Task<CommandResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return RunProcessAsync(startInfo);
    }

    private static async Task<CommandResult> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new CommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string GitMergeAssemblyPath => Path.Combine(
        RepositoryRoot,
        "source",
        "Tools",
        "GitMerge",
        "bin",
        BuildConfiguration,
        "net11.0",
        "Meridian.Tools.GitMerge.dll");

    private static string BuildConfiguration => Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Name;

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
}
