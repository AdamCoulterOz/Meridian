using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;

namespace Meridian.Core.Schema;

public static class AstSchemaYamlLoader
{
    public static AstSchemaSet Load(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Schema YAML must contain a root mapping.");
        }

        var defaults = ParseDefaults(root);
        var formatAliases = ParseFormatAliases(root);
        var nestedSchemas = ParseNestedSchemas(root);
        var files = ParseFiles(root);

        return new AstSchemaSet(
            Scalar(root, "schemaVersion"),
            Scalar(root, "name"),
            defaults with { NestedSchemas = nestedSchemas },
            nestedSchemas,
            files)
        {
            FormatAliases = formatAliases
        };
    }

    public static AstSchemaSet LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static AstSchema ParseDefaults(YamlMappingNode root)
    {
        var globalFields = new List<string>();
        var defaults = Mapping(root, "defaults");
        var xml = Mapping(defaults, "xml");
        var discriminators = Sequence(xml, "discriminators");
        if (discriminators is not null)
        {
            foreach (var discriminator in discriminators.OfType<YamlMappingNode>())
            {
                var attribute = Scalar(discriminator, "attribute");
                if (!string.IsNullOrWhiteSpace(attribute))
                {
                    globalFields.Add(attribute);
                }
            }
        }

        return new AstSchema
        {
            GlobalDiscriminatorFields = globalFields
        };
    }

    private static IReadOnlyDictionary<string, string> ParseFormatAliases(YamlMappingNode root)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliasNode = Mapping(root, "formatAliases");
        if (aliasNode is null)
        {
            return aliases;
        }

        foreach (var pair in aliasNode.Children)
        {
            if (pair.Key is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
            {
                continue;
            }

            aliases[key.Value] = ScalarValue(pair.Value);
        }

        return aliases;
    }

    private static IReadOnlyDictionary<string, AstSchema> ParseNestedSchemas(YamlMappingNode root)
    {
        var nestedSchemas = new Dictionary<string, AstSchema>(StringComparer.OrdinalIgnoreCase);
        var node = Mapping(root, "nestedSchemas");
        if (node is null)
        {
            return nestedSchemas;
        }

        foreach (var pair in node.Children)
        {
            var name = ((YamlScalarNode)pair.Key).Value;
            if (string.IsNullOrWhiteSpace(name) || pair.Value is not YamlMappingNode schemaNode)
            {
                continue;
            }

            nestedSchemas[name] = ParseNestedSchema(schemaNode);
        }

        return nestedSchemas;
    }

    private static AstSchema ParseNestedSchema(YamlMappingNode schemaNode)
    {
        var contentRules = new List<ContentRule>();
        var orderedChildren = new List<PathSelector>();
        var root = Mapping(schemaNode, "root");
        if (root is not null)
        {
            ParsePropertyContentRules(root, "$root", contentRules);
            ParseOrderedChildren(root, orderedChildren);

            var item = Mapping(root, "item");
            if (item is not null)
            {
                ParsePropertyContentRules(item, @"^\$root/\$item\d{6}", contentRules, useRegex: true);
            }
        }

        return new AstSchema
        {
            ContentRules = contentRules,
            OrderedChildren = orderedChildren
        };
    }

    private static void ParsePropertyContentRules(
        YamlMappingNode node,
        string parentPath,
        List<ContentRule> contentRules,
        bool useRegex = false)
    {
        var properties = Mapping(node, "properties");
        if (properties is null)
        {
            return;
        }

        foreach (var pair in properties.Children)
        {
            if (pair.Key is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value) || pair.Value is not YamlMappingNode property)
            {
                continue;
            }

            var format = Scalar(property, "format");
            if (string.IsNullOrWhiteSpace(format))
            {
                continue;
            }

            var path = parentPath + "/" + key.Value;
            contentRules.Add(new ContentRule(
                useRegex ? PathSelector.Regex(path + "$") : PathSelector.Exact(path),
                format,
                Scalar(property, "schemaRef"),
                Scalar(property, "note")));
        }
    }

    private static IReadOnlyList<FileSchemaRule> ParseFiles(YamlMappingNode root)
    {
        var files = new List<FileSchemaRule>();
        var fileNodes = Sequence(root, "files");
        if (fileNodes is null)
        {
            return files;
        }

        foreach (var fileNode in fileNodes.OfType<YamlMappingNode>())
        {
            files.Add(new FileSchemaRule(
                Scalar(fileNode, "match") ?? throw new InvalidOperationException("File schema rule is missing match."),
                Scalar(fileNode, "root"),
                ParseIdentityRules(fileNode),
                ParseOrderedChildren(fileNode),
                ParseContentRules(fileNode),
                ParseCompanionRules(fileNode)));
        }

        return files;
    }

    private static IReadOnlyList<NodeIdentityRule> ParseIdentityRules(YamlMappingNode fileNode)
    {
        var rules = new List<NodeIdentityRule>();
        var discriminators = Sequence(fileNode, "discriminators");
        if (discriminators is null)
        {
            return rules;
        }

        foreach (var discriminator in discriminators.OfType<YamlMappingNode>())
        {
            var path = Scalar(discriminator, "path");
            var key = Mapping(discriminator, "key");
            if (string.IsNullOrWhiteSpace(path) || key is null)
            {
                continue;
            }

            rules.Add(new NodeIdentityRule(PathSelector.Exact(path), ParseDiscriminatorKey(key), Scalar(discriminator, "note")));
        }

        return rules;
    }

    private static DiscriminatorKey ParseDiscriminatorKey(YamlMappingNode key)
    {
        var attribute = Scalar(key, "attribute");
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            return new DiscriminatorKey.Field(attribute);
        }

        var element = Scalar(key, "element");
        if (!string.IsNullOrWhiteSpace(element))
        {
            return new DiscriminatorKey.PathValue(element);
        }

        if (key.Children.TryGetValue(new YamlScalarNode("text"), out var textNode) &&
            bool.TryParse(((YamlScalarNode)textNode).Value, out var isText) &&
            isText)
        {
            return new DiscriminatorKey.Text();
        }

        var structural = Scalar(key, "structural");
        if (string.Equals(structural, "orderedSlot", StringComparison.Ordinal))
        {
            return new DiscriminatorKey.Structural(StructuralDiscriminator.OrderedSlot);
        }

        var composite = Sequence(key, "composite");
        if (composite is not null)
        {
            return new DiscriminatorKey.Composite(composite.OfType<YamlMappingNode>().Select(ParseCompositePart).ToArray());
        }

        throw new InvalidOperationException("Unsupported discriminator key shape.");
    }

    private static CompositePart ParseCompositePart(YamlMappingNode part)
    {
        var optional = bool.TryParse(Scalar(part, "optional"), out var parsedOptional) && parsedOptional;
        var attribute = Scalar(part, "attribute");
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            return new CompositePart(new DiscriminatorKey.Field(attribute), optional);
        }

        var path = Scalar(part, "path");
        if (!string.IsNullOrWhiteSpace(path))
        {
            return new CompositePart(new DiscriminatorKey.PathValue(path), optional);
        }

        var element = Scalar(part, "element");
        if (!string.IsNullOrWhiteSpace(element))
        {
            return new CompositePart(new DiscriminatorKey.PathValue(element), optional);
        }

        throw new InvalidOperationException("Composite discriminator part must define attribute, element, or path.");
    }

    private static IReadOnlyList<PathSelector> ParseOrderedChildren(YamlMappingNode node)
    {
        var selectors = new List<PathSelector>();
        ParseOrderedChildren(node, selectors);
        return selectors;
    }

    private static void ParseOrderedChildren(YamlMappingNode node, List<PathSelector> selectors)
    {
        var orderedChildren = Sequence(node, "orderedChildren");
        if (orderedChildren is null)
        {
            return;
        }

        foreach (var item in orderedChildren)
        {
            if (item is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value))
            {
                selectors.Add(ParsePathSelector(scalar.Value));
            }
            else if (item is YamlMappingNode mapping)
            {
                var regex = Scalar(mapping, "regex");
                if (!string.IsNullOrWhiteSpace(regex))
                {
                    selectors.Add(PathSelector.Regex(regex));
                }
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

    private static IReadOnlyList<ContentRule> ParseContentRules(YamlMappingNode node)
    {
        var rules = new List<ContentRule>();
        var content = Sequence(node, "content");
        if (content is null)
        {
            return rules;
        }

        foreach (var item in content.OfType<YamlMappingNode>())
        {
            var path = Scalar(item, "path");
            var format = Scalar(item, "format");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(format))
            {
                continue;
            }

            rules.Add(new ContentRule(
                ParsePathSelector(path),
                format,
                Scalar(item, "schemaRef"),
                Scalar(item, "note")));
        }

        return rules;
    }

    private static IReadOnlyList<CompanionRule> ParseCompanionRules(YamlMappingNode node)
    {
        var rules = new List<CompanionRule>();
        var companions = Sequence(node, "companions");
        if (companions is null)
        {
            return rules;
        }

        foreach (var companion in companions.OfType<YamlMappingNode>())
        {
            rules.Add(new CompanionRule(
                Scalar(companion, "path"),
                Scalar(companion, "pathTemplate"),
                Scalar(companion, "pathFrom"),
                ParsePathFromMatchedPath(companion),
                Scalar(companion, "format"),
                ParseFormatFrom(companion),
                Scalar(companion, "defaultFormat"),
                Scalar(companion, "schemaRef"),
                Scalar(companion, "note")));
        }

        return rules;
    }

    private static PathFromMatchedPathRule? ParsePathFromMatchedPath(YamlMappingNode node)
    {
        var rule = Mapping(node, "pathFromMatchedPath");
        if (rule is null)
        {
            return null;
        }

        return new PathFromMatchedPathRule(
            Scalar(rule, "removeSuffix"),
            Scalar(rule, "regex"),
            Scalar(rule, "replace"));
    }

    private static FormatFromRule? ParseFormatFrom(YamlMappingNode node)
    {
        var formatFrom = Mapping(node, "formatFrom");
        if (formatFrom is null)
        {
            return null;
        }

        var path = Scalar(formatFrom, "path") ?? throw new InvalidOperationException("formatFrom requires path.");
        var enumMap = Mapping(formatFrom, "enum")?.Children.Select(pair =>
            new FormatMapEntry(ParseSchemaScalarValue(pair.Key), ScalarValue(pair.Value))).ToArray() ??
            Array.Empty<FormatMapEntry>();

        return new FormatFromRule(path, enumMap);
    }

    private static SchemaScalarValue ParseSchemaScalarValue(YamlNode node)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new InvalidOperationException("Expected scalar YAML enum key.");
        }

        var value = scalar.Value ?? string.Empty;
        return scalar.Style == ScalarStyle.Plain &&
            long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer)
            ? new SchemaScalarValue.Integer(integer)
            : new SchemaScalarValue.String(value);
    }

    private static string ScalarValue(YamlNode node)
    {
        return node is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : throw new InvalidOperationException("Expected scalar YAML value.");
    }

    private static YamlMappingNode? Mapping(YamlMappingNode? node, string key)
    {
        return node is not null &&
            node.Children.TryGetValue(new YamlScalarNode(key), out var value) &&
            value is YamlMappingNode mapping
            ? mapping
            : null;
    }

    private static YamlSequenceNode? Sequence(YamlMappingNode? node, string key)
    {
        return node is not null &&
            node.Children.TryGetValue(new YamlScalarNode(key), out var value) &&
            value is YamlSequenceNode sequence
            ? sequence
            : null;
    }

    private static string? Scalar(YamlMappingNode? node, string key)
    {
        return node is not null &&
            node.Children.TryGetValue(new YamlScalarNode(key), out var value) &&
            value is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }
}
