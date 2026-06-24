using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Tools.GitMerge;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("meridian");
    config.PropagateExceptions();
    config.AddCommand<MergeCommand>("merge")
        .WithDescription("Merge one file using Meridian structural three-way merge.");
    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare two files using Meridian structural two-way diff.");
});

try
{
    return await app.RunAsync(args);
}
catch (CommandRuntimeException error)
{
    Console.Error.WriteLine("Error: " + error.Message);
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine("Error: " + error.Message);
    return 2;
}

internal sealed class MergeSettings : CommandSettings
{
    [CommandOption("--base <PATH>")]
    public string? BasePath { get; init; }

    [CommandOption("--ours <PATH>")]
    public string? OursPath { get; init; }

    [CommandOption("--theirs <PATH>")]
    public string? TheirsPath { get; init; }

    [CommandOption("--path <REPO_PATH>")]
    public string? RepoPath { get; init; }

    [CommandOption("--schema <SCHEMA_YAML>")]
    public string? SchemaPath { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
            return ValidationResult.Error("Missing required --base argument.");

        if (string.IsNullOrWhiteSpace(OursPath))
            return ValidationResult.Error("Missing required --ours argument.");

        if (string.IsNullOrWhiteSpace(TheirsPath))
            return ValidationResult.Error("Missing required --theirs argument.");

        return ValidationResult.Success();
    }
}

internal sealed class DiffSettings : CommandSettings
{
    [CommandArgument(0, "[repo-path]")]
    public string? PositionalRepoPath { get; init; }

    [CommandArgument(1, "[old-file]")]
    public string? PositionalOldPath { get; init; }

    [CommandArgument(2, "[old-hex]")]
    public string? OldHex { get; init; }

    [CommandArgument(3, "[old-mode]")]
    public string? OldMode { get; init; }

    [CommandArgument(4, "[new-file]")]
    public string? PositionalNewPath { get; init; }

    [CommandArgument(5, "[new-hex]")]
    public string? NewHex { get; init; }

    [CommandArgument(6, "[new-mode]")]
    public string? NewMode { get; init; }

    [CommandOption("--old <PATH>")]
    public string? OldPath { get; init; }

    [CommandOption("--new <PATH>")]
    public string? NewPath { get; init; }

    [CommandOption("--path <REPO_PATH>")]
    public string? RepoPath { get; init; }

    [CommandOption("--schema <SCHEMA_YAML>")]
    public string? SchemaPath { get; init; }

    public override ValidationResult Validate()
    {
        var explicitPaths = !string.IsNullOrWhiteSpace(OldPath) && !string.IsNullOrWhiteSpace(NewPath);
        var gitExternalDiffPaths =
            !string.IsNullOrWhiteSpace(PositionalRepoPath) &&
            !string.IsNullOrWhiteSpace(PositionalOldPath) &&
            !string.IsNullOrWhiteSpace(PositionalNewPath);

        if (!explicitPaths && !gitExternalDiffPaths)
            return ValidationResult.Error("Missing required diff arguments. Use --old <path> --new <path> --path <repo-path>, or Git external-diff positional arguments.");

        return ValidationResult.Success();
    }
}

internal sealed class MergeCommand : AsyncCommand<MergeSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, MergeSettings settings, CancellationToken cancellationToken)
    {
        var basePath = settings.BasePath!;
        var oursPath = settings.OursPath!;
        var theirsPath = settings.TheirsPath!;
        var repoPath = settings.RepoPath ?? oursPath;
        var adapter = await GitIntegration.CreateAdapterAsync(repoPath, cancellationToken);
        if (adapter is null)
        {
            Console.Error.WriteLine($"No Meridian adapter is registered for '{repoPath}'.");
            return 2;
        }

        var schema = GitIntegration.LoadSchema(settings.SchemaPath, repoPath);

        if (adapter is IBinaryFormatAdapter binaryAdapter)
            return await MergeBinaryAsync(binaryAdapter, basePath, oursPath, theirsPath, schema, cancellationToken);

        var baseDocument = adapter.Parse(await File.ReadAllTextAsync(basePath, cancellationToken), basePath, schema);
        var oursDocument = adapter.Parse(await File.ReadAllTextAsync(oursPath, cancellationToken), oursPath, schema);
        var theirsDocument = adapter.Parse(await File.ReadAllTextAsync(theirsPath, cancellationToken), theirsPath, schema);

        var result = new Merger().Merge(baseDocument, oursDocument, theirsDocument, schema, adapter);
        await File.WriteAllTextAsync(oursPath, adapter.RenderDocument(result.Document), cancellationToken);

        GitIntegration.WriteDiagnostics(result.IdentityDiagnostics);

        foreach (var conflict in result.Conflicts)
            Console.Error.WriteLine($"Conflict: {conflict.Path}: {conflict.Message}");

        return result.HasConflicts ? 1 : 0;
    }

    private static async Task<int> MergeBinaryAsync(
        IBinaryFormatAdapter adapter,
        string basePath,
        string oursPath,
        string theirsPath,
        MergeSchema schema,
        CancellationToken cancellationToken)
    {
        var baseDocument = adapter.ParseBytes(await File.ReadAllBytesAsync(basePath, cancellationToken), basePath, schema);
        var oursDocument = adapter.ParseBytes(await File.ReadAllBytesAsync(oursPath, cancellationToken), oursPath, schema);
        var theirsDocument = adapter.ParseBytes(await File.ReadAllBytesAsync(theirsPath, cancellationToken), theirsPath, schema);

        var result = new Merger().Merge(baseDocument, oursDocument, theirsDocument, schema, adapter);
        GitIntegration.WriteDiagnostics(result.IdentityDiagnostics);

        if (result.HasConflicts)
        {
            // Binary content cannot carry text conflict markers. Leave ours on disk untouched and report the conflict.
            foreach (var conflict in result.Conflicts)
                Console.Error.WriteLine($"Conflict: {conflict.Path}: binary content changed on both sides; left as ours.");

            return 1;
        }

        await File.WriteAllBytesAsync(oursPath, adapter.RenderDocumentBytes(result.Document), cancellationToken);
        return 0;
    }
}

internal sealed class DiffCommand : AsyncCommand<DiffSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DiffSettings settings, CancellationToken cancellationToken)
    {
        var oldPath = settings.OldPath ?? settings.PositionalOldPath!;
        var newPath = settings.NewPath ?? settings.PositionalNewPath!;
        var repoPath = settings.RepoPath ?? settings.PositionalRepoPath ?? newPath;
        var adapter = await GitIntegration.CreateAdapterAsync(repoPath, cancellationToken);
        if (adapter is null)
        {
            Console.Error.WriteLine($"No Meridian adapter is registered for '{repoPath}'.");
            return 2;
        }

        var schema = GitIntegration.LoadSchema(settings.SchemaPath, repoPath);

        if (adapter is IBinaryFormatAdapter)
        {
            var oldBytes = await File.ReadAllBytesAsync(oldPath, cancellationToken);
            var newBytes = await File.ReadAllBytesAsync(newPath, cancellationToken);
            if (!oldBytes.AsSpan().SequenceEqual(newBytes))
                Console.WriteLine($"Binary files a/{repoPath} and b/{repoPath} differ");

            return 0;
        }

        var oldDocument = adapter.Parse(await File.ReadAllTextAsync(oldPath, cancellationToken), oldPath, schema);
        var newDocument = adapter.Parse(await File.ReadAllTextAsync(newPath, cancellationToken), newPath, schema);
        var result = new StructuralDiffer().Diff(oldDocument, newDocument, schema, adapter);

        GitIntegration.WriteDiagnostics(result.IdentityDiagnostics);
        if (result.HasIdentityErrors)
            return 2;

        if (result.HasDifferences)
            GitIntegration.WriteDiff(repoPath, result.Entries);

        return 0;
    }
}

internal static class GitIntegration
{
    public static Task<IFormatAdapter?> CreateAdapterAsync(string repoPath, CancellationToken cancellationToken) =>
        MeridianGitFormatProviders.CreateAdapterAsync(repoPath, cancellationToken);

    public static MergeSchema LoadSchema(string? schemaPath, string repoPath)
    {
        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            var loadResult = MergeSchemaYamlLoader.LoadFileWithDiagnostics(schemaPath);
            WriteRemoteSchemaDiagnostics(loadResult.RemoteSchemas);
            return loadResult.SchemaSet.CompileForFile(repoPath);
        }

        var discovery = MergeSchemaDiscovery.DiscoverForFile(repoPath, Environment.CurrentDirectory);
        if (discovery.SchemaFiles.Count > 0)
        {
            var loadResult = MergeSchemaYamlLoader.LoadFilesWithDiagnostics(discovery.SchemaFiles);
            WriteRemoteSchemaDiagnostics(loadResult.RemoteSchemas);
            return loadResult.SchemaSet.CompileForFile(repoPath);
        }

        Console.Error.WriteLine(
            $"Warning: No Meridian schema files matching '{MergeSchemaDiscovery.SchemaFilePattern}' were found from '{discovery.TargetDirectory}' up to '{discovery.RepositoryRoot}'. Using built-in default discriminator fields.");

        return new MergeSchema
        {
            GlobalDiscriminatorFields = ["id", "Id", "languagecode"]
        };
    }

    private static void WriteRemoteSchemaDiagnostics(IReadOnlyList<RemoteSchemaLoad> remoteSchemas)
    {
        if (remoteSchemas.Count == 0)
            return;

        Console.Error.WriteLine("Meridian remote schemas loaded:");
        foreach (var remoteSchema in remoteSchemas)
        {
            var pinStatus = remoteSchema.IsPinnedToGitCommitSha
                ? "pinned to commit SHA"
                : "not pinned to a detected commit SHA";
            Console.Error.WriteLine($"  - {remoteSchema.Uri} ({pinStatus})");
        }

        Console.Error.WriteLine($"Meridian remote schema cache policy: {MergeSchemaYamlLoader.RemoteSchemaCachePolicy}.");
    }

    public static void WriteDiagnostics(IEnumerable<Meridian.Core.Identity.IdentityDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Path}: {diagnostic.Message}");
    }

    public static void WriteDiff(string repoPath, IReadOnlyList<StructuralDiffEntry> entries)
    {
        Console.WriteLine($"diff --meridian a/{repoPath} b/{repoPath}");
        Console.WriteLine($"--- a/{repoPath}");
        Console.WriteLine($"+++ b/{repoPath}");

        foreach (var entry in entries)
        {
            Console.WriteLine($"@@ {entry.Path} @@");
            switch (entry.Kind)
            {
                case StructuralDiffKind.NodeAdded:
                    Console.WriteLine("+ node added");
                    WritePrefixedBlock("+ ", entry.NewText);
                    break;
                case StructuralDiffKind.NodeRemoved:
                    Console.WriteLine("- node removed");
                    WritePrefixedBlock("- ", entry.OldText);
                    break;
                case StructuralDiffKind.NodeChanged:
                    Console.WriteLine($"~ node kind: {Quote(entry.OldValue)} -> {Quote(entry.NewValue)}");
                    WritePrefixedBlock("- ", entry.OldText);
                    WritePrefixedBlock("+ ", entry.NewText);
                    break;
                case StructuralDiffKind.FieldAdded:
                    Console.WriteLine($"+ @{entry.Field}: {Quote(entry.NewValue)}");
                    break;
                case StructuralDiffKind.FieldRemoved:
                    Console.WriteLine($"- @{entry.Field}: {Quote(entry.OldValue)}");
                    break;
                case StructuralDiffKind.FieldChanged:
                    Console.WriteLine($"~ @{entry.Field}: {Quote(entry.OldValue)} -> {Quote(entry.NewValue)}");
                    break;
                case StructuralDiffKind.ValueAdded:
                    Console.WriteLine($"+ value: {Quote(entry.NewValue)}");
                    break;
                case StructuralDiffKind.ValueRemoved:
                    Console.WriteLine($"- value: {Quote(entry.OldValue)}");
                    break;
                case StructuralDiffKind.ValueChanged:
                    Console.WriteLine($"~ value: {Quote(entry.OldValue)} -> {Quote(entry.NewValue)}");
                    break;
                case StructuralDiffKind.OrderedChildrenChanged:
                    Console.WriteLine($"~ child order: [{entry.OldValue}] -> [{entry.NewValue}]");
                    break;
                default:
                    throw new InvalidOperationException("Unknown diff entry kind: " + entry.Kind);
            }
        }
    }

    private static void WritePrefixedBlock(string prefix, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            Console.WriteLine(prefix + line);
    }

    private static string Quote(string? value)
    {
        if (value is null)
            return "<missing>";

        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
