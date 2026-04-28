using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Formats.Data;
using Meridian.Formats.Web;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("meridian");
    config.PropagateExceptions();
    config.AddCommand<MergeFileCommand>("merge-file")
        .WithDescription("Merge one file using Meridian structural three-way merge.");
    config.AddCommand<DiffFileCommand>("diff-file")
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

internal sealed class MergeFileSettings : CommandSettings
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

internal sealed class DiffFileSettings : CommandSettings
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

internal sealed class MergeFileCommand : AsyncCommand<MergeFileSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, MergeFileSettings settings, CancellationToken cancellationToken)
    {
        var basePath = settings.BasePath!;
        var oursPath = settings.OursPath!;
        var theirsPath = settings.TheirsPath!;
        var repoPath = settings.RepoPath ?? oursPath;
        var adapter = GitIntegration.CreateAdapter(repoPath);
        if (adapter is null)
        {
            Console.Error.WriteLine($"No Meridian adapter is registered for '{repoPath}'.");
            return 2;
        }

        var schema = GitIntegration.LoadSchema(settings.SchemaPath, repoPath);
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
}

internal sealed class DiffFileCommand : AsyncCommand<DiffFileSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DiffFileSettings settings, CancellationToken cancellationToken)
    {
        var oldPath = settings.OldPath ?? settings.PositionalOldPath!;
        var newPath = settings.NewPath ?? settings.PositionalNewPath!;
        var repoPath = settings.RepoPath ?? settings.PositionalRepoPath ?? newPath;
        var adapter = GitIntegration.CreateAdapter(repoPath);
        if (adapter is null)
        {
            Console.Error.WriteLine($"No Meridian adapter is registered for '{repoPath}'.");
            return 2;
        }

        var schema = GitIntegration.LoadSchema(settings.SchemaPath, repoPath);
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
    public static IFormatAdapter? CreateAdapter(string repoPath)
    {
        var extension = Path.GetExtension(repoPath).ToLowerInvariant();
        return extension switch
        {
            ".xml" => new XmlAdapter(),
            ".json" => new JsonAdapter(),
            ".json5" => new Json5Adapter(),
            ".js" => new JavaScriptAdapter(),
            ".yaml" => new YamlAdapter(),
            ".yml" => new YamlAdapter(),
            ".html" => new HtmlFragmentAdapter(),
            ".htm" => new HtmlFragmentAdapter(),
            _ => null
        };
    }

    public static MergeSchema LoadSchema(string? schemaPath, string repoPath)
    {
        if (!string.IsNullOrWhiteSpace(schemaPath))
            return MergeSchemaYamlLoader.LoadFile(schemaPath).CompileForFile(repoPath);

        var discovery = MergeSchemaDiscovery.DiscoverForFile(repoPath, Environment.CurrentDirectory);
        if (discovery.SchemaFiles.Count > 0)
            return MergeSchemaYamlLoader.LoadFiles(discovery.SchemaFiles).CompileForFile(repoPath);

        Console.Error.WriteLine(
            $"Warning: No Meridian schema files matching '{MergeSchemaDiscovery.SchemaFilePattern}' were found from '{discovery.TargetDirectory}' up to '{discovery.RepositoryRoot}'. Using built-in default discriminator fields.");

        return new MergeSchema
        {
            GlobalDiscriminatorFields = ["id", "Id", "languagecode"]
        };
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
