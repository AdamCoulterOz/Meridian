using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meridian.Core.Schema;

public static class AstSchemaYamlLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static AstSchemaSet Load(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var document = Deserializer.Deserialize<SchemaDocumentDto>(yaml) ??
            throw new InvalidOperationException("Schema YAML must contain a root mapping.");

        var defaults = ConvertDefaults(document.Defaults);
        var nestedSchemas = ConvertNestedSchemas(document.NestedSchemas);

        return new AstSchemaSet(
            document.SchemaVersion,
            document.Name,
            defaults with { NestedSchemas = nestedSchemas },
            nestedSchemas,
            ConvertFiles(document.Files))
        {
            FormatAliases = ToOrdinalIgnoreCaseDictionary(document.FormatAliases)
        };
    }

    public static AstSchemaSet LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static AstSchema ConvertDefaults(DefaultsDto? defaults)
    {
        return new AstSchema
        {
            GlobalDiscriminatorFields = defaults?.Xml?.Discriminators?
                .Select(discriminator => discriminator.Attribute)
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
                .Cast<string>()
                .ToArray() ?? Array.Empty<string>()
        };
    }

    private static IReadOnlyDictionary<string, AstSchema> ConvertNestedSchemas(IDictionary<string, NestedSchemaDto>? nestedSchemas)
    {
        if (nestedSchemas is null)
        {
            return new Dictionary<string, AstSchema>(StringComparer.OrdinalIgnoreCase);
        }

        return nestedSchemas.ToDictionary(
            pair => pair.Key,
            pair => ConvertNestedSchema(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static AstSchema ConvertNestedSchema(NestedSchemaDto nestedSchema)
    {
        var contentRules = new List<ContentRule>();
        var orderedChildren = new List<PathSelector>();

        if (nestedSchema.Root is not null)
        {
            AddPropertyContentRules(nestedSchema.Root, "$root", contentRules);
            AddOrderedChildren(nestedSchema.Root.OrderedChildren, orderedChildren);

            if (nestedSchema.Root.Item is not null)
            {
                AddPropertyContentRules(nestedSchema.Root.Item, @"^\$root/\$item\d{6}", contentRules, useRegex: true);
            }
        }

        return new AstSchema
        {
            ContentRules = contentRules,
            OrderedChildren = orderedChildren
        };
    }

    private static void AddPropertyContentRules(
        SchemaNodeDto node,
        string parentPath,
        List<ContentRule> contentRules,
        bool useRegex = false)
    {
        if (node.Properties is null)
        {
            return;
        }

        foreach (var pair in node.Properties)
        {
            if (string.IsNullOrWhiteSpace(pair.Value.Format))
            {
                continue;
            }

            var path = parentPath + "/" + pair.Key;
            contentRules.Add(new ContentRule(
                useRegex ? PathSelector.Regex(path + "$") : PathSelector.Exact(path),
                pair.Value.Format,
                pair.Value.SchemaRef,
                pair.Value.Note));
        }
    }

    private static IReadOnlyList<FileSchemaRule> ConvertFiles(IReadOnlyList<FileSchemaRuleDto>? files)
    {
        if (files is null)
        {
            return Array.Empty<FileSchemaRule>();
        }

        return files.Select(file => new FileSchemaRule(
            file.Match ?? throw new InvalidOperationException("File schema rule is missing match."),
            file.Root,
            ConvertIdentityRules(file.Discriminators),
            ConvertOrderedChildren(file.OrderedChildren),
            ConvertContentRules(file.Content),
            ConvertCompanionRules(file.Companions))).ToArray();
    }

    private static IReadOnlyList<NodeIdentityRule> ConvertIdentityRules(IReadOnlyList<DiscriminatorRuleDto>? discriminators)
    {
        if (discriminators is null)
        {
            return Array.Empty<NodeIdentityRule>();
        }

        return discriminators
            .Where(discriminator => !string.IsNullOrWhiteSpace(discriminator.Path) && discriminator.Key is not null)
            .Select(discriminator => new NodeIdentityRule(
                PathSelector.Exact(discriminator.Path!),
                ConvertDiscriminatorKey(discriminator.Key!),
                discriminator.Note))
            .ToArray();
    }

    private static DiscriminatorKey ConvertDiscriminatorKey(DiscriminatorKeyDto key)
    {
        if (!string.IsNullOrWhiteSpace(key.Attribute))
        {
            return new DiscriminatorKey.Field(key.Attribute);
        }

        if (!string.IsNullOrWhiteSpace(key.Element))
        {
            return new DiscriminatorKey.PathValue(key.Element);
        }

        if (key.Text == true)
        {
            return new DiscriminatorKey.Text();
        }

        if (string.Equals(key.Structural, "orderedSlot", StringComparison.Ordinal))
        {
            return new DiscriminatorKey.Structural(StructuralDiscriminator.OrderedSlot);
        }

        if (key.Composite is { Count: > 0 })
        {
            return new DiscriminatorKey.Composite(key.Composite.Select(ConvertCompositePart).ToArray());
        }

        throw new InvalidOperationException("Unsupported discriminator key shape.");
    }

    private static CompositePart ConvertCompositePart(CompositePartDto part)
    {
        if (!string.IsNullOrWhiteSpace(part.Attribute))
        {
            return new CompositePart(new DiscriminatorKey.Field(part.Attribute), part.Optional);
        }

        if (!string.IsNullOrWhiteSpace(part.Path))
        {
            return new CompositePart(new DiscriminatorKey.PathValue(part.Path), part.Optional);
        }

        if (!string.IsNullOrWhiteSpace(part.Element))
        {
            return new CompositePart(new DiscriminatorKey.PathValue(part.Element), part.Optional);
        }

        throw new InvalidOperationException("Composite discriminator part must define attribute, element, or path.");
    }

    private static IReadOnlyList<PathSelector> ConvertOrderedChildren(IReadOnlyList<object>? orderedChildren)
    {
        var selectors = new List<PathSelector>();
        AddOrderedChildren(orderedChildren, selectors);
        return selectors;
    }

    private static void AddOrderedChildren(IReadOnlyList<object>? orderedChildren, List<PathSelector> selectors)
    {
        if (orderedChildren is null)
        {
            return;
        }

        foreach (var item in orderedChildren)
        {
            if (item is string path && !string.IsNullOrWhiteSpace(path))
            {
                selectors.Add(ParsePathSelector(path));
                continue;
            }

            if (TryReadString(item, "regex", out var regex) && !string.IsNullOrWhiteSpace(regex))
            {
                selectors.Add(PathSelector.Regex(regex));
            }
        }
    }

    private static PathSelector ParsePathSelector(string value)
    {
        if (value.Contains('*', StringComparison.Ordinal))
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(value)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";
            return PathSelector.Regex(regex);
        }

        return PathSelector.Exact(value);
    }

    private static IReadOnlyList<ContentRule> ConvertContentRules(IReadOnlyList<ContentRuleDto>? content)
    {
        if (content is null)
        {
            return Array.Empty<ContentRule>();
        }

        return content
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Path) && !string.IsNullOrWhiteSpace(rule.Format))
            .Select(rule => new ContentRule(
                ParsePathSelector(rule.Path!),
                rule.Format!,
                rule.SchemaRef,
                rule.Note))
            .ToArray();
    }

    private static IReadOnlyList<CompanionRule> ConvertCompanionRules(IReadOnlyList<CompanionRuleDto>? companions)
    {
        if (companions is null)
        {
            return Array.Empty<CompanionRule>();
        }

        return companions.Select(companion => new CompanionRule(
            companion.Path,
            companion.PathTemplate,
            companion.PathFrom,
            companion.PathFromMatchedPath is null
                ? null
                : new PathFromMatchedPathRule(
                    companion.PathFromMatchedPath.RemoveSuffix,
                    companion.PathFromMatchedPath.Regex,
                    companion.PathFromMatchedPath.Replace),
            companion.Format,
            companion.FormatFrom is null
                ? null
                : new FormatFromRule(
                    companion.FormatFrom.Path ?? throw new InvalidOperationException("formatFrom requires path."),
                    ConvertFormatMap(companion.FormatFrom.Enum)),
            companion.DefaultFormat,
            companion.SchemaRef,
            companion.Note)).ToArray();
    }

    private static IReadOnlyList<FormatMapEntry> ConvertFormatMap(IDictionary<object, string>? enumMap)
    {
        if (enumMap is null)
        {
            return Array.Empty<FormatMapEntry>();
        }

        return enumMap.Select(pair => new FormatMapEntry(ConvertSchemaScalarValue(pair.Key), pair.Value)).ToArray();
    }

    private static SchemaScalarValue ConvertSchemaScalarValue(object value)
    {
        return value switch
        {
            byte integer => new SchemaScalarValue.Integer(integer),
            short integer => new SchemaScalarValue.Integer(integer),
            int integer => new SchemaScalarValue.Integer(integer),
            long integer => new SchemaScalarValue.Integer(integer),
            sbyte integer => new SchemaScalarValue.Integer(integer),
            ushort integer => new SchemaScalarValue.Integer(integer),
            uint integer => new SchemaScalarValue.Integer(integer),
            ulong integer when integer <= long.MaxValue => new SchemaScalarValue.Integer((long)integer),
            string text when long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer) =>
                new SchemaScalarValue.Integer(integer),
            string text => new SchemaScalarValue.String(text),
            _ => new SchemaScalarValue.String(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    private static IReadOnlyDictionary<string, string> ToOrdinalIgnoreCaseDictionary(IDictionary<string, string>? values)
    {
        return values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryReadString(object value, string key, out string? text)
    {
        if (value is IDictionary<object, object> objectMap &&
            objectMap.TryGetValue(key, out var objectValue))
        {
            text = Convert.ToString(objectValue, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        if (value is IDictionary<string, object> stringMap &&
            stringMap.TryGetValue(key, out var stringValue))
        {
            text = Convert.ToString(stringValue, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        text = null;
        return false;
    }

    private sealed record SchemaDocumentDto
    {
        public string? SchemaVersion { get; init; }

        public string? Name { get; init; }

        public DefaultsDto? Defaults { get; init; }

        public Dictionary<string, string>? FormatAliases { get; init; }

        public Dictionary<string, NestedSchemaDto>? NestedSchemas { get; init; }

        public List<FileSchemaRuleDto>? Files { get; init; }
    }

    private sealed record DefaultsDto
    {
        public XmlDefaultsDto? Xml { get; init; }
    }

    private sealed record XmlDefaultsDto
    {
        public List<XmlDiscriminatorDto>? Discriminators { get; init; }
    }

    private sealed record XmlDiscriminatorDto
    {
        public string? Attribute { get; init; }
    }

    private sealed record NestedSchemaDto
    {
        public SchemaNodeDto? Root { get; init; }
    }

    private sealed record SchemaNodeDto
    {
        public Dictionary<string, ContentRuleDto>? Properties { get; init; }

        public List<object>? OrderedChildren { get; init; }

        public SchemaNodeDto? Item { get; init; }
    }

    private sealed record FileSchemaRuleDto
    {
        public string? Match { get; init; }

        public string? Root { get; init; }

        public List<DiscriminatorRuleDto>? Discriminators { get; init; }

        public List<object>? OrderedChildren { get; init; }

        public List<ContentRuleDto>? Content { get; init; }

        public List<CompanionRuleDto>? Companions { get; init; }
    }

    private sealed record DiscriminatorRuleDto
    {
        public string? Path { get; init; }

        public DiscriminatorKeyDto? Key { get; init; }

        public string? Note { get; init; }
    }

    private sealed record DiscriminatorKeyDto
    {
        public string? Attribute { get; init; }

        public string? Element { get; init; }

        public bool? Text { get; init; }

        public string? Structural { get; init; }

        public List<CompositePartDto>? Composite { get; init; }
    }

    private sealed record CompositePartDto
    {
        public string? Attribute { get; init; }

        public string? Path { get; init; }

        public string? Element { get; init; }

        public bool Optional { get; init; }
    }

    private sealed record ContentRuleDto
    {
        public string? Path { get; init; }

        public string? Format { get; init; }

        public string? SchemaRef { get; init; }

        public string? Note { get; init; }
    }

    private sealed record CompanionRuleDto
    {
        public string? Path { get; init; }

        public string? PathTemplate { get; init; }

        public string? PathFrom { get; init; }

        public PathFromMatchedPathRuleDto? PathFromMatchedPath { get; init; }

        public string? Format { get; init; }

        public FormatFromRuleDto? FormatFrom { get; init; }

        public string? DefaultFormat { get; init; }

        public string? SchemaRef { get; init; }

        public string? Note { get; init; }
    }

    private sealed record PathFromMatchedPathRuleDto
    {
        public string? RemoveSuffix { get; init; }

        public string? Regex { get; init; }

        public string? Replace { get; init; }
    }

    private sealed record FormatFromRuleDto
    {
        public string? Path { get; init; }

        public Dictionary<object, string>? Enum { get; init; }
    }
}
