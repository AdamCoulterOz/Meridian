using System.Text;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Core.Text;
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

    // Git appends two extra arguments (rename/copy destination path and a transform message)
    // to the external-diff invocation when it detects a rename or copy.
    [CommandArgument(7, "[rename-path]")]
    public string? RenamePath { get; init; }

    [CommandArgument(8, "[rename-message]")]
    public string? RenameMessage { get; init; }

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
        // Git calls the external diff with a single argument (the path) for unmerged entries.
        var gitUnmergedPath =
            !string.IsNullOrWhiteSpace(PositionalRepoPath) &&
            string.IsNullOrWhiteSpace(PositionalOldPath) &&
            string.IsNullOrWhiteSpace(PositionalNewPath);

        if (!explicitPaths && !gitExternalDiffPaths && !gitUnmergedPath)
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

        // Read raw bytes so we can preserve the ours-side encoding/BOM on write instead of
        // silently transcoding to BOM-less UTF-8 (which corrupts UTF-16 and Power Platform exports).
        var baseBytes = await File.ReadAllBytesAsync(basePath, cancellationToken);
        var oursBytes = await File.ReadAllBytesAsync(oursPath, cancellationToken);
        var theirsBytes = await File.ReadAllBytesAsync(theirsPath, cancellationToken);

        var oursEncoding = TextEncoding.Detect(oursBytes);
        var baseText = TextEncoding.Decode(baseBytes);
        var oursText = TextEncoding.Decode(oursBytes);
        var theirsText = TextEncoding.Decode(theirsBytes);

        // Add/add: Git supplies an empty base when a file was created on both branches. There is no
        // common ancestor to merge against, so resolve at the text level: keep ours when the sides
        // agree, otherwise emit a whole-file conflict rather than crashing the structured parser.
        if (baseBytes.Length == 0)
        {
            if (string.Equals(oursText, theirsText, StringComparison.Ordinal))
                return 0;

            var addConflict = ConflictMarkers.Create(oursText, baseText, theirsText) + Environment.NewLine;
            await TextEncoding.WriteAsync(oursPath, LineEndings.MatchStyleOf(addConflict, oursText), oursEncoding, cancellationToken);
            Console.Error.WriteLine($"Conflict: {repoPath}: file added on both sides with differing content.");
            return 1;
        }

        MergeResult result;
        string rendered;
        try
        {
            var baseDocument = adapter.Parse(baseText, basePath, schema);
            var oursDocument = adapter.Parse(oursText, oursPath, schema);
            var theirsDocument = adapter.Parse(theirsText, theirsPath, schema);

            result = new Merger().Merge(baseDocument, oursDocument, theirsDocument, schema, adapter);
            rendered = adapter.RenderDocument(result.Document);

            // Safety net: a conflicted merge must never write a resolved-looking file. If the
            // structural render did not embed conflict markers (some adapters cannot project a
            // nested conflict inline), fall back to a whole-file three-way conflict so no side's
            // content is silently discarded.
            if (result.HasConflicts && !ConflictMarkers.ProjectedConflict(rendered, oursText, baseText, theirsText))
                rendered = ConflictMarkers.CreateDiff3(oursText, baseText, theirsText) + Environment.NewLine;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A file the structured adapter cannot parse (empty, comment-only, duplicate keys,
            // syntactically invalid mid-edit) must degrade to a plain text three-way merge rather
            // than crash the driver and leave Git with a half-merged tree.
            Console.Error.WriteLine($"Warning: structural merge unavailable for '{repoPath}' ({error.Message}); using text merge.");
            var textMerge = TextMerge(baseText, oursText, theirsText);
            await TextEncoding.WriteAsync(oursPath, LineEndings.MatchStyleOf(textMerge.Text, oursText), oursEncoding, cancellationToken);
            if (textMerge.HasConflict)
                Console.Error.WriteLine($"Conflict: {repoPath}: content changed on both sides.");
            return textMerge.HasConflict ? 1 : 0;
        }

        // Most structured adapters cannot round-trip CRLF (XML normalises per spec; the JSON/
        // YAML/HTML renderers emit their own layout), so restore the ours-side convention here
        // rather than rewriting every line of a file the user changed one line of.
        rendered = LineEndings.MatchStyleOf(rendered, oursText);

        await TextEncoding.WriteAsync(oursPath, rendered, oursEncoding, cancellationToken);

        GitIntegration.WriteDiagnostics(result.IdentityDiagnostics);

        foreach (var conflict in result.Conflicts)
            Console.Error.WriteLine($"Conflict: {conflict.Path}: {conflict.Message}");

        return result.HasConflicts ? 1 : 0;
    }

    // Plain text three-way resolution used as a fallback when structural parsing is not possible.
    private static (string Text, bool HasConflict) TextMerge(string baseText, string oursText, string theirsText)
    {
        if (string.Equals(oursText, theirsText, StringComparison.Ordinal))
            return (oursText, false);
        if (string.Equals(baseText, oursText, StringComparison.Ordinal))
            return (theirsText, false);
        if (string.Equals(baseText, theirsText, StringComparison.Ordinal))
            return (oursText, false);

        return (ConflictMarkers.CreateDiff3(oursText, baseText, theirsText) + Environment.NewLine, true);
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
        // Was this invoked by Git as an external diff (positional args) or by a human (--old/--new)?
        // Git treats ANY non-zero exit from an external diff as fatal and aborts the whole
        // diff/log/show, so the positional form must always exit 0 and degrade gracefully.
        var gitInvocation = string.IsNullOrWhiteSpace(settings.OldPath) && string.IsNullOrWhiteSpace(settings.NewPath);

        // Unmerged path: Git calls the external diff with only the path.
        if (gitInvocation
            && !string.IsNullOrWhiteSpace(settings.PositionalRepoPath)
            && string.IsNullOrWhiteSpace(settings.PositionalOldPath)
            && string.IsNullOrWhiteSpace(settings.PositionalNewPath))
        {
            Console.WriteLine($"* Unmerged path {settings.PositionalRepoPath}");
            return 0;
        }

        var oldPath = settings.OldPath ?? settings.PositionalOldPath!;
        var newPath = settings.NewPath ?? settings.PositionalNewPath!;
        // For a rename/copy, Git passes the destination path as the 8th argument; use it as the
        // repo-relative path (and hence for adapter/extension selection) when present.
        var repoPath = settings.RepoPath ?? settings.RenamePath ?? settings.PositionalRepoPath ?? newPath;

        try
        {
            var adapter = await GitIntegration.CreateAdapterAsync(repoPath, cancellationToken);
            if (adapter is null)
            {
                Console.Error.WriteLine($"No Meridian adapter is registered for '{repoPath}'.");
                return gitInvocation ? 0 : 2;
            }

            var schema = GitIntegration.LoadSchema(settings.SchemaPath, repoPath);

            // Added/deleted files: Git passes /dev/null for the missing side. Emit a one-side
            // pseudo-diff instead of parsing an empty file (which would throw and abort Git).
            var oldMissing = IsMissingSide(oldPath);
            var newMissing = IsMissingSide(newPath);
            if (oldMissing || newMissing)
            {
                if (oldMissing && newMissing)
                    return 0;

                Console.WriteLine($"diff --meridian a/{repoPath} b/{repoPath}");
                Console.WriteLine(oldMissing ? $"new file a/{repoPath}" : $"deleted file a/{repoPath}");
                return 0;
            }

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
            {
                // Ambiguous identity is not a reason to abort the user's entire git diff/log.
                if (gitInvocation)
                {
                    Console.WriteLine($"diff --meridian a/{repoPath} b/{repoPath}");
                    Console.WriteLine($"* {repoPath}: files differ (structural comparison unavailable: ambiguous node identity)");
                    return 0;
                }

                return 2;
            }

            if (result.HasDifferences)
                GitIntegration.WriteDiff(repoPath, result.Entries);

            return 0;
        }
        catch (Exception error) when (gitInvocation)
        {
            // Never let a Git external-diff invocation exit non-zero: that aborts the whole
            // git diff/log/show. Degrade to a plain "files differ" notice.
            Console.WriteLine($"diff --meridian a/{repoPath} b/{repoPath}");
            Console.WriteLine($"* {repoPath}: files differ (structural comparison unavailable: {error.Message})");
            return 0;
        }
    }

    private static bool IsMissingSide(string? path) =>
        string.IsNullOrEmpty(path)
        || string.Equals(path, "/dev/null", StringComparison.Ordinal)
        || string.Equals(Path.GetFileName(path), "nul", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Preserves the byte-level encoding of the file being written back. Git-tracked files (notably
/// Power Platform / Visual Studio XML exports) are commonly UTF-8-with-BOM or UTF-16; writing plain
/// BOM-less UTF-8 would silently corrupt them, so the merge writer reuses the ours-side encoding.
/// </summary>
internal static class TextEncoding
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: false);

    public static Encoding Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Utf8Bom;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;              // UTF-16 LE (writes the BOM back)
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;     // UTF-16 BE (writes the BOM back)
        return Utf8NoBom;
    }

    public static string Decode(byte[] bytes)
    {
        // detectEncodingFromByteOrderMarks strips a UTF-8/UTF-16 BOM and decodes with that encoding,
        // falling back to UTF-8 when none is present.
        using var reader = new StreamReader(new MemoryStream(bytes), Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static Task WriteAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, text, encoding, cancellationToken);
}

internal static class GitIntegration
{
    public static Task<IFormatAdapter?> CreateAdapterAsync(string repoPath, CancellationToken cancellationToken) =>
        MeridianGitFormatProviders.CreateAdapterAsync(repoPath, cancellationToken);

    private static bool RemoteSchemasEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("MERIDIANGIT_ALLOW_REMOTE_SCHEMAS"), "1", StringComparison.Ordinal);

    public static MergeSchema LoadSchema(string? schemaPath, string repoPath)
    {
        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            // Containment applies here too: a repo can point the driver at a schema via git config,
            // so an explicitly named schema is no more trusted than a discovered one.
            var explicitOptions = new MergeSchemaLoadOptions
            {
                AllowRemoteSchemas = RemoteSchemasEnabled(),
                RepositoryRoot = MergeSchemaDiscovery.DiscoverForFile(repoPath, Environment.CurrentDirectory).RepositoryRoot
            };
            var loadResult = MergeSchemaYamlLoader.LoadFileWithDiagnostics(schemaPath, explicitOptions);
            WriteRemoteSchemaDiagnostics(loadResult.RemoteSchemas);
            return loadResult.SchemaSet.CompileForFile(repoPath);
        }

        var discovery = MergeSchemaDiscovery.DiscoverForFile(repoPath, Environment.CurrentDirectory);
        if (discovery.SchemaFiles.Count > 0)
        {
            var discoveryOptions = new MergeSchemaLoadOptions
            {
                AllowRemoteSchemas = RemoteSchemasEnabled(),
                RepositoryRoot = discovery.RepositoryRoot
            };
            var loadResult = MergeSchemaYamlLoader.LoadFilesWithDiagnostics(discovery.SchemaFiles, discoveryOptions);
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
