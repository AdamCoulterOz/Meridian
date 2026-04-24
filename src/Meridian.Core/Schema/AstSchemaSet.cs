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

        var matchingFiles = Files
            .Where(file => file.IsMatch(path) && (root is null || file.Root is null || string.Equals(file.Root, root, StringComparison.Ordinal)))
            .ToArray();

        var identityRules = Defaults.IdentityRules.Concat(matchingFiles.SelectMany(file => file.IdentityRules)).ToArray();
        var orderedChildren = Defaults.OrderedChildren.Concat(matchingFiles.SelectMany(file => file.OrderedChildren)).ToArray();
        var contentRules = Defaults.ContentRules.Concat(matchingFiles.SelectMany(file => file.ContentRules)).ToArray();
        var companionRules = Defaults.CompanionRules.Concat(matchingFiles.SelectMany(file => file.CompanionRules)).ToArray();

        return Defaults with
        {
            IdentityRules = identityRules,
            OrderedChildren = orderedChildren,
            ContentRules = contentRules,
            CompanionRules = companionRules,
            NestedSchemas = NestedSchemas
        };
    }
}

public sealed record FileSchemaRule(
    string Match,
    string? Root,
    IReadOnlyList<NodeIdentityRule> IdentityRules,
    IReadOnlyList<PathSelector> OrderedChildren,
    IReadOnlyList<ContentRule> ContentRules,
    IReadOnlyList<CompanionRule> CompanionRules)
{
    private readonly Lazy<System.Text.RegularExpressions.Regex> _matchRegex = new(() =>
        new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(Match)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public bool IsMatch(string path)
    {
        return _matchRegex.Value.IsMatch(path.Replace('\\', '/'));
    }
}
