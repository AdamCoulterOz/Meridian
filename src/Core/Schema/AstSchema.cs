using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Meridian.Core.Schema;

public sealed record AstSchema
{
    public IReadOnlyList<string> GlobalDiscriminatorFields { get; init; } = Array.Empty<string>();

    public IReadOnlyList<NodeIdentityRule> IdentityRules { get; init; } = Array.Empty<NodeIdentityRule>();

    public IReadOnlyList<PathSelector> OrderedChildren { get; init; } = Array.Empty<PathSelector>();

    public IReadOnlyList<ContentRule> ContentRules { get; init; } = Array.Empty<ContentRule>();

    public IReadOnlyList<CompanionRule> CompanionRules { get; init; } = Array.Empty<CompanionRule>();

    public IReadOnlyDictionary<string, AstSchema> NestedSchemas { get; init; } =
        new Dictionary<string, AstSchema>(StringComparer.OrdinalIgnoreCase);

    public static AstSchema Empty { get; } = new();
}

public sealed record NodeIdentityRule(PathSelector Path, DiscriminatorKey Key, string? Note = null);

public sealed record ContentRule(PathSelector Path, string Format, string? SchemaRef = null, string? Note = null);

public sealed record CompanionRule(
    string? Path = null,
    string? PathTemplate = null,
    string? PathFrom = null,
    PathFromMatchedPathRule? PathFromMatchedPath = null,
    string? Format = null,
    FormatFromRule? FormatFrom = null,
    string? DefaultFormat = null,
    string? SchemaRef = null,
    string? Note = null)
{
    public string ResolveFormat(Ast.AstNode metadataRoot)
    {
        ArgumentNullException.ThrowIfNull(metadataRoot);

        if (FormatFrom is not null)
        {
            var rawValue = AstPath.ReadValue(metadataRoot, FormatFrom.Path);
            if (rawValue is not null && FormatFrom.TryResolve(rawValue, out var mappedFormat))
            {
                return mappedFormat;
            }
        }

        if (!string.IsNullOrWhiteSpace(Format))
        {
            return Format;
        }

        if (!string.IsNullOrWhiteSpace(DefaultFormat))
        {
            return DefaultFormat;
        }

        throw new InvalidOperationException("Companion rule does not define a format, formatFrom mapping, or defaultFormat.");
    }

    public string? ResolvePath(Ast.AstNode metadataRoot, string? matchedPath = null)
    {
        ArgumentNullException.ThrowIfNull(metadataRoot);

        if (!string.IsNullOrWhiteSpace(Path))
        {
            return ResolveStaticPath(Path, matchedPath);
        }

        if (PathFromMatchedPath is not null && !string.IsNullOrWhiteSpace(matchedPath))
        {
            var resolvedFromMatchedPath = PathFromMatchedPath.Resolve(matchedPath);
            if (!string.IsNullOrWhiteSpace(PathTemplate))
            {
                var resolvedFromMetadata = ExpandTemplate(metadataRoot, PathTemplate);
                if (!string.Equals(resolvedFromMatchedPath, resolvedFromMetadata, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Companion path mismatch: matched path rule resolved '{resolvedFromMatchedPath}' but metadata template resolved '{resolvedFromMetadata}'.");
                }
            }

            return resolvedFromMatchedPath;
        }

        if (!string.IsNullOrWhiteSpace(PathTemplate))
        {
            return ExpandTemplate(metadataRoot, PathTemplate);
        }

        return string.IsNullOrWhiteSpace(PathFrom)
            ? null
            : AstPath.ReadValue(metadataRoot, PathFrom);
    }

    private static string ResolveStaticPath(string path, string? matchedPath)
    {
        if (string.IsNullOrWhiteSpace(matchedPath) ||
            System.IO.Path.IsPathRooted(path) ||
            path.Contains('/', StringComparison.Ordinal) ||
            path.Contains('\\', StringComparison.Ordinal))
        {
            return path.Replace('\\', '/');
        }

        var directory = System.IO.Path.GetDirectoryName(matchedPath.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(directory)
            ? path
            : directory + "/" + path;
    }

    private static string ExpandTemplate(Ast.AstNode metadataRoot, string template)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            template,
            "\\{(?<path>[^}]+)\\}",
            match =>
            {
                var path = match.Groups["path"].Value;
                return AstPath.ReadValue(metadataRoot, path) ??
                    throw new InvalidOperationException($"Companion path template references missing metadata path '{path}'.");
            });
    }
}

public sealed record PathFromMatchedPathRule(
    string? RemoveSuffix = null,
    string? Regex = null,
    string? Replace = null)
{
    public string Resolve(string matchedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchedPath);
        var normalized = matchedPath.Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(RemoveSuffix))
        {
            if (!normalized.EndsWith(RemoveSuffix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Matched path '{normalized}' does not end with expected suffix '{RemoveSuffix}'.");
            }

            return normalized[..^RemoveSuffix.Length];
        }

        if (!string.IsNullOrWhiteSpace(Regex) && Replace is not null)
        {
            var regex = new Regex(Regex, RegexOptions.CultureInvariant);
            if (!regex.IsMatch(normalized))
            {
                throw new InvalidOperationException(
                    $"Matched path '{normalized}' does not match companion path regex '{Regex}'.");
            }

            return regex.Replace(normalized, Replace);
        }

        throw new InvalidOperationException("pathFromMatchedPath requires removeSuffix or regex plus replace.");
    }
}

public sealed record FormatFromRule(string Path, IReadOnlyList<FormatMapEntry> Enum)
{
    public bool TryResolve(string rawValue, out string format)
    {
        foreach (var entry in Enum)
        {
            if (entry.Value.Matches(rawValue))
            {
                format = entry.Format;
                return true;
            }
        }

        format = string.Empty;
        return false;
    }
}

public sealed record FormatMapEntry(SchemaScalarValue Value, string Format);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SchemaScalarValue.String), "string")]
[JsonDerivedType(typeof(SchemaScalarValue.Integer), "integer")]
public abstract record SchemaScalarValue
{
    private SchemaScalarValue()
    {
    }

    public abstract bool Matches(string rawValue);

    public sealed record String(string Value) : SchemaScalarValue
    {
        public override bool Matches(string rawValue) => string.Equals(Value, rawValue, StringComparison.Ordinal);
    }

    public sealed record Integer(long Value) : SchemaScalarValue
    {
        public override bool Matches(string rawValue)
        {
            return long.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                parsed == Value;
        }
    }
}

public sealed record PathSelector(string Pattern, bool IsRegex = false)
{
    private Regex? _regex;

    public bool IsMatch(string path)
    {
        if (!IsRegex)
        {
            return string.Equals(Pattern, path, StringComparison.Ordinal);
        }

        _regex ??= new Regex(Pattern, RegexOptions.CultureInvariant);
        return _regex.IsMatch(path);
    }

    public static PathSelector Exact(string path) => new(path);

    public static PathSelector Regex(string pattern) => new(pattern, IsRegex: true);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DiscriminatorKey.Field), "field")]
[JsonDerivedType(typeof(DiscriminatorKey.PathValue), "path")]
[JsonDerivedType(typeof(DiscriminatorKey.Composite), "composite")]
[JsonDerivedType(typeof(DiscriminatorKey.Text), "text")]
[JsonDerivedType(typeof(DiscriminatorKey.Structural), "structural")]
public abstract record DiscriminatorKey
{
    private DiscriminatorKey()
    {
    }

    public sealed record Field(string Name) : DiscriminatorKey;

    public sealed record PathValue(string Path) : DiscriminatorKey;

    public sealed record Composite(IReadOnlyList<CompositePart> Parts) : DiscriminatorKey;

    public sealed record Text : DiscriminatorKey;

    public sealed record Structural(StructuralDiscriminator Strategy) : DiscriminatorKey;
}

public sealed record CompositePart(DiscriminatorKey Key, bool Optional = false);

public enum StructuralDiscriminator
{
    OrderedSlot
}

public static class AstPath
{
    public static string? ReadValue(Ast.AstNode node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var current = node;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var startIndex = parts.Length > 0 && string.Equals(parts[0], node.Kind, StringComparison.Ordinal)
            ? 1
            : 0;

        for (var index = startIndex; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.StartsWith('@', StringComparison.Ordinal))
            {
                return index == parts.Length - 1 && current.Fields.TryGetValue(part[1..], out var fieldValue)
                    ? fieldValue
                    : null;
            }

            var child = current.Children.FirstOrDefault(candidate => string.Equals(candidate.Kind, part, StringComparison.Ordinal));
            if (child is null)
            {
                return null;
            }

            current = child;
        }

        return current.Value;
    }
}
