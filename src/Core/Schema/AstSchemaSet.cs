using System.Text.Json.Serialization;

namespace Meridian.Core.Schema;

public sealed record AstSchemaSet(
    string? SchemaVersion,
    string? Name,
    AstSchema Defaults,
    IReadOnlyDictionary<string, AstSchema> NestedSchemas,
    IReadOnlyList<FileSchemaRule> Files)
{
    public IReadOnlyDictionary<string, string> FormatAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public AstSchema CompileForFile(string path, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var defaults = Defaults ?? AstSchema.Empty;
        var nestedSchemas = NestedSchemas ?? new Dictionary<string, AstSchema>(StringComparer.OrdinalIgnoreCase);
        var files = Files ?? [];

        var matchingFiles = files
            .Where(file => file.IsMatch(path) && (root is null || file.Root is null || string.Equals(file.Root, root, StringComparison.Ordinal)))
            .ToArray();

        var identityRules = defaults.IdentityRules.Concat(matchingFiles.SelectMany(file => file.IdentityRules ?? [])).ToArray();
        var orderedChildren = defaults.OrderedChildren.Concat(matchingFiles.SelectMany(file => file.OrderedChildren ?? [])).ToArray();
        var contentRules = defaults.ContentRules.Concat(matchingFiles.SelectMany(file => file.ContentRules ?? [])).ToArray();
        var companionRules = defaults.CompanionRules.Concat(matchingFiles.SelectMany(file => file.CompanionRules ?? [])).ToArray();

        return defaults with
        {
            IdentityRules = identityRules,
            OrderedChildren = orderedChildren,
            ContentRules = contentRules,
            CompanionRules = companionRules,
            NestedSchemas = nestedSchemas
        };
    }
}

public sealed record FileSchemaRule(
    string Match,
    string? Root,
    [property: JsonPropertyName("discriminators")]
    IReadOnlyList<NodeIdentityRule> IdentityRules,
    IReadOnlyList<PathSelector> OrderedChildren,
    [property: JsonPropertyName("content")]
    IReadOnlyList<ContentRule> ContentRules,
    [property: JsonPropertyName("companions")]
    IReadOnlyList<CompanionRule> CompanionRules)
{
    private readonly Lazy<System.Text.RegularExpressions.Regex> _matchRegex = new(() =>
        new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(Match)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public bool IsMatch(string path) => _matchRegex.Value.IsMatch(path.Replace('\\', '/'));
}
