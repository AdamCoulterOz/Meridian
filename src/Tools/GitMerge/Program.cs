using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Formats.Data;
using Meridian.Formats.Web;

var command = args.FirstOrDefault();
if (!string.Equals(command, "merge-file", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: meridian merge-file --base <path> --ours <path> --theirs <path> --path <repo-path> [--schema <schema-yaml>]");
    return 2;
}

var options = ParseOptions(args.Skip(1).ToArray());
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

var result = new AstMerger().Merge(baseDocument, oursDocument, theirsDocument, schema, adapter);
await File.WriteAllTextAsync(oursPath, adapter.RenderDocument(result.Document));

foreach (var diagnostic in result.IdentityDiagnostics)
    Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Path}: {diagnostic.Message}");

foreach (var conflict in result.Conflicts)
    Console.Error.WriteLine($"Conflict: {conflict.Path}: {conflict.Message}");

return result.HasConflicts ? 1 : 0;

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

static IAstFormatAdapter? CreateAdapter(string repoPath)
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

static AstSchema LoadSchema(IReadOnlyDictionary<string, string> options, string repoPath)
{
    if (options.TryGetValue("schema", out var schemaPath))
        return AstSchemaYamlLoader.LoadFile(schemaPath).CompileForFile(repoPath);

    return new AstSchema
    {
        GlobalDiscriminatorFields = new[] { "id", "Id", "languagecode" }
    };
}
