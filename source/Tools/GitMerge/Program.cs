using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Formats.Data;
using Meridian.Formats.Web;

try
{
    var command = args.FirstOrDefault();
    if (string.Equals(command, "merge-file", StringComparison.Ordinal))
        return await MergeFile(args.Skip(1).ToArray());

    if (string.Equals(command, "diff-file", StringComparison.Ordinal))
        return await DiffFile(args.Skip(1).ToArray());

    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  meridian merge-file --base <path> --ours <path> --theirs <path> --path <repo-path> [--schema <schema-yaml>]");
    Console.Error.WriteLine("  meridian diff-file --old <path> --new <path> --path <repo-path> [--schema <schema-yaml>]");
    Console.Error.WriteLine("  meridian diff-file <repo-path> <old-file> <old-hex> <old-mode> <new-file> <new-hex> <new-mode>");
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine("Error: " + error.Message);
    return 2;
}

static async Task<int> MergeFile(IReadOnlyList<string> args)
{
    var options = ParseOptions(args);
    if (!options.TryGetValue("base", out var basePath) ||
        !options.TryGetValue("ours", out var oursPath) ||
        !options.TryGetValue("theirs", out var theirsPath))
    {
        Console.Error.WriteLine("Missing required --base, --ours, or --theirs argument.");
        return 2;
    }

    var repoPath = options.GetValueOrDefault("path", oursPath);
    var adapter = CreateAdapter(repoPath);
    if (adapter is null)
    {
        Console.Error.WriteLine($"No Meridian adapter is registered for '{repoPath}'.");
        return 2;
    }

    var schema = LoadSchema(options, repoPath);

    var baseDocument = adapter.Parse(await File.ReadAllTextAsync(basePath), basePath, schema);
    var oursDocument = adapter.Parse(await File.ReadAllTextAsync(oursPath), oursPath, schema);
    var theirsDocument = adapter.Parse(await File.ReadAllTextAsync(theirsPath), theirsPath, schema);

    var result = new Merger().Merge(baseDocument, oursDocument, theirsDocument, schema, adapter);
    await File.WriteAllTextAsync(oursPath, adapter.RenderDocument(result.Document));

    WriteDiagnostics(result.IdentityDiagnostics);

    foreach (var conflict in result.Conflicts)
        Console.Error.WriteLine($"Conflict: {conflict.Path}: {conflict.Message}");

    return result.HasConflicts ? 1 : 0;
}

static async Task<int> DiffFile(IReadOnlyList<string> args)
{
    var diffArgs = ParseDiffArguments(args);
    if (diffArgs is null)
    {
        Console.Error.WriteLine("Missing required diff arguments. Use --old <path> --new <path> --path <repo-path>, or Git external-diff positional arguments.");
        return 2;
    }

    var adapter = CreateAdapter(diffArgs.RepoPath);
    if (adapter is null)
    {
        Console.Error.WriteLine($"No Meridian adapter is registered for '{diffArgs.RepoPath}'.");
        return 2;
    }

    var schema = LoadSchema(diffArgs.Options, diffArgs.RepoPath);
    var oldDocument = adapter.Parse(await File.ReadAllTextAsync(diffArgs.OldPath), diffArgs.OldPath, schema);
    var newDocument = adapter.Parse(await File.ReadAllTextAsync(diffArgs.NewPath), diffArgs.NewPath, schema);
    var result = new StructuralDiffer().Diff(oldDocument, newDocument, schema, adapter);

    WriteDiagnostics(result.IdentityDiagnostics);
    if (result.HasIdentityErrors)
        return 2;

    if (result.HasDifferences)
        WriteDiff(diffArgs.RepoPath, result.Entries);

    return 0;
}

static Dictionary<string, string> ParseOptions(IReadOnlyList<string> values)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);

    for (var i = 0; i < values.Count; i++)
    {
        var item = values[i];
        if (!item.StartsWith("--", StringComparison.Ordinal))
            continue;

        var key = item[2..];
        if (i + 1 >= values.Count)
            throw new ArgumentException("Missing value for option " + item);

        result[key] = values[++i];
    }

    return result;
}

static DiffArguments? ParseDiffArguments(IReadOnlyList<string> args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    var index = 0;
    while (index < args.Count && args[index].StartsWith("--", StringComparison.Ordinal))
    {
        var key = args[index][2..];
        if (index + 1 >= args.Count)
            throw new ArgumentException("Missing value for option " + args[index]);

        options[key] = args[index + 1];
        index += 2;
    }

    if (index < args.Count)
    {
        if (args.Count - index < 7)
            return null;

        return new DiffArguments(args[index + 1], args[index + 4], args[index], options);
    }

    if (!options.TryGetValue("old", out var oldPath) ||
        !options.TryGetValue("new", out var newPath))
        return null;

    var repoPath = options.GetValueOrDefault("path", newPath);
    return new DiffArguments(oldPath, newPath, repoPath, options);
}

static IFormatAdapter? CreateAdapter(string repoPath)
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

static MergeSchema LoadSchema(IReadOnlyDictionary<string, string> options, string repoPath)
{
    if (options.TryGetValue("schema", out var schemaPath))
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

static void WriteDiagnostics(IEnumerable<Meridian.Core.Identity.IdentityDiagnostic> diagnostics)
{
    foreach (var diagnostic in diagnostics)
        Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Path}: {diagnostic.Message}");
}

static void WriteDiff(string repoPath, IReadOnlyList<StructuralDiffEntry> entries)
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

static void WritePrefixedBlock(string prefix, string? text)
{
    if (string.IsNullOrEmpty(text))
        return;

    foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        Console.WriteLine(prefix + line);
}

static string Quote(string? value)
{
    if (value is null)
        return "<missing>";

    return "\"" + value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

sealed record DiffArguments(
    string OldPath,
    string NewPath,
    string RepoPath,
    IReadOnlyDictionary<string, string> Options);
