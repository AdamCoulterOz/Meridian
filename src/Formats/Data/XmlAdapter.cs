using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meridian.Core.Tree;
using Meridian.Core.Formats;
using Meridian.Core.Formats.Mapped;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Formats.Data;

public sealed class XmlAdapter : IFormatAdapter, IMappedHost
{
    private const string TokenElementName = "__meridian_mapped";
    private const string FieldOrderField = "$fieldOrder";
    private static readonly Regex AttributeTokenMarker = new(
        Regex.Escape(MappedTokenFields.MarkerPrefix) + "[0-9a-f]{16}__(?<id>mtk[0-9]{6})" + Regex.Escape(MappedTokenFields.MarkerSuffix),
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Format => "xml";

    public string HostFormat => Format;

    public DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var document = XDocument.Parse(sourceText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (document.Root is null)
            throw new InvalidOperationException("XML document has no root element.");

        var root = ParseElement(document.Root);
        if (document.Declaration is not null)
        {
            var fields = new Dictionary<string, string>(root.Fields, StringComparer.Ordinal)
            {
                ["$xmlDeclaration"] = document.Declaration.ToString()
            };
            root = root.WithFields(fields);
        }

        return new DocumentTree(Format, root, sourcePath, sourceText);
    }

    public string RenderDocument(DocumentTree document)
    {
        var builder = new StringBuilder();
        if (document.Root.Fields.TryGetValue("$xmlDeclaration", out var declaration))
            builder.AppendLine(declaration);

        builder.Append(RenderNode(document.Root));
        builder.AppendLine();
        return builder.ToString();
    }

    public string RenderNode(TreeNode node) => RenderNode(node, depth: 0);

    public IMappedTokenContextTracker CreateTokenContextTracker() => new XmlTokenContextTracker();

    public bool CanRepresent(
        MappedToken token,
        MappedTokenContext context,
        out string? unsupportedReason)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (context is MappedTokenContext.ChildNode or MappedTokenContext.FieldValue)
        {
            unsupportedReason = null;
            return true;
        }

        unsupportedReason = $"XML currently supports mapped tokens only as child nodes or field values, not '{context}'.";
        return false;
    }

    public bool TryCreateToken(
        MappedToken token,
        MappedTokenContext context,
        out MappedTokenShape shape)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (context == MappedTokenContext.ChildNode)
        {
            shape = new MappedTokenShape(
                "<" + TokenElementName + " marker=\"" + token.PhysicalMarker + "\" semanticKey=\"" + SecurityElement.Escape(token.SemanticKey) + "\" />",
                MappedTokenContext.ChildNode);
            return true;
        }

        if (context == MappedTokenContext.FieldValue)
        {
            shape = new MappedTokenShape(
                token.PhysicalMarker,
                MappedTokenContext.FieldValue);
            return true;
        }

        shape = default!;
        return false;
    }

    public DocumentTree ParseHostWithMappedTokens(string sourceText, string? sourcePath, MergeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var document = XDocument.Parse(sourceText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (document.Root is null)
            throw new InvalidOperationException("XML document has no root element.");

        var root = ParseMappedElement(document.Root);
        if (document.Declaration is not null)
        {
            var fields = new Dictionary<string, string>(root.Fields, StringComparer.Ordinal)
            {
                ["$xmlDeclaration"] = document.Declaration.ToString()
            };
            root = root.WithFields(fields);
        }

        return new DocumentTree(Format, root, sourcePath, sourceText);
    }

    public string RenderHostWithMappedTokens(
        DocumentTree document,
        IReadOnlyDictionary<string, string> mappedSourceByTokenId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mappedSourceByTokenId);

        var builder = new StringBuilder();
        if (document.Root.Fields.TryGetValue("$xmlDeclaration", out var declaration))
            builder.AppendLine(declaration);

        builder.Append(RenderMappedNode(document.Root, mappedSourceByTokenId));
        return builder.ToString();
    }

    private static string RenderNode(TreeNode node, int depth)
    {
        if (node.Conflict is { Kind: not ConflictKind.Scalar })
            return CreateIndentedConflictMarkers(
                node.Conflict.OursText,
                node.Conflict.BaseText,
                node.Conflict.TheirsText,
                new string(' ', depth * 2));

        var builder = new StringBuilder();
        var indent = new string(' ', depth * 2);
        builder.Append(indent);
        builder.Append('<');
        builder.Append(node.Kind);

        foreach (var field in node.Fields
            .Where(field => !field.Key.StartsWith('$')))
        {
            builder.Append(' ');
            builder.Append(field.Key);
            builder.Append("=\"");
            builder.Append(SecurityElement.Escape(field.Value));
            builder.Append('"');
        }

        if (node.Conflict is null && node.Children.Count == 0 && string.IsNullOrEmpty(node.Value))
        {
            builder.Append(" />");
            return builder.ToString();
        }

        builder.Append('>');

        if (node.Conflict is { Kind: ConflictKind.Scalar } scalarConflict)
        {
            builder.AppendLine();
            builder.Append(CreateIndentedConflictMarkers(
                scalarConflict.OursText,
                scalarConflict.BaseText,
                scalarConflict.TheirsText,
                new string(' ', (depth + 1) * 2)));
            builder.AppendLine();
            builder.Append(indent);
        }
        else if (node.Value is not null)
            builder.Append(SecurityElement.Escape(node.Value));

        foreach (var child in node.Children)
        {
            builder.AppendLine();
            builder.Append(RenderNode(child, depth + 1));
        }

        if (node.Children.Count > 0)
        {
            builder.AppendLine();
            builder.Append(indent);
        }

        builder.Append("</");
        builder.Append(node.Kind);
        builder.Append('>');
        return builder.ToString();
    }

    private static TreeNode ParseElement(XElement element)
    {
        var fields = element.Attributes()
            .ToDictionary(AttributeKey, attribute => attribute.Value, StringComparer.Ordinal);

        var childElements = element.Elements().Select(ParseElement).ToArray();
        var value = childElements.Length == 0 ? element.Value : null;

        return new TreeNode(
            element.Name.LocalName,
            fields,
            value,
            childElements,
            sourceText: element.ToString(SaveOptions.DisableFormatting));
    }

    private static TreeNode ParseMappedElement(XElement element)
    {
        if (TryReadTokenElement(element, out var token))
            return new TreeNode(
                "$mappedToken" + token.Id,
                NodeMetadata.Create("mappedToken", new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MappedTokenFields.TokenId] = token.Id,
                    [MappedTokenFields.SemanticKey] = token.SemanticKey,
                    [MappedTokenFields.Context] = MappedTokenContext.ChildNode.ToString()
                }));

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var fieldValueChildren = new List<TreeNode>();
        var fieldOrder = new List<string>();

        foreach (var attribute in element.Attributes())
        {
            var key = AttributeKey(attribute);
            fieldOrder.Add(key);
            if (AttributeTokenMarker.IsMatch(attribute.Value))
            {
                fieldValueChildren.Add(ParseMappedFieldValue(key, attribute.Value));
                continue;
            }

            fields[key] = attribute.Value;
        }

        if (fieldOrder.Count > 0)
            fields[FieldOrderField] = string.Join("\n", fieldOrder);

        return new TreeNode(
                            element.Name.LocalName,
                            fields,
                            children: fieldValueChildren.Concat(element.Nodes().Select(ParseMappedNode)).ToArray(),
                            sourceText: element.ToString(SaveOptions.DisableFormatting));
    }

    private static bool TryReadTokenElement(XElement element, out ParsedToken token)
    {
        token = default;
        if (!string.Equals(element.Name.LocalName, TokenElementName, StringComparison.Ordinal) ||
            element.Attribute("marker") is not { } markerAttribute)
            return false;

        var match = AttributeTokenMarker.Match(markerAttribute.Value);
        if (!match.Success || !string.Equals(match.Value, markerAttribute.Value, StringComparison.Ordinal))
            return false;

        var semanticKey = element.Attribute("semanticKey")?.Value;
        token = new ParsedToken(
            match.Groups["id"].Value,
            string.IsNullOrEmpty(semanticKey) ? MappedTokenContext.ChildNode + ":unknown" : semanticKey);
        return true;
    }

    private static TreeNode ParseMappedFieldValue(string fieldName, string value)
    {
        var fields = NodeMetadata.Create("fieldValue", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NodeMetadata.NameField] = fieldName
        });
        var children = new List<TreeNode>();
        var position = 0;
        var ordinal = 0;

        foreach (Match match in AttributeTokenMarker.Matches(value))
        {
            if (match.Index > position)
                children.Add(new TreeNode(
                    $"$fieldText{ordinal++:D6}",
                    NodeMetadata.Create("fieldText"),
                    value[position..match.Index]));

            var token = ParseAttributeToken(match, fieldName, children.Count(IsMappedToken));
            children.Add(new TreeNode(
                "$mappedToken" + token.Id,
                NodeMetadata.Create("mappedToken", new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MappedTokenFields.TokenId] = token.Id,
                    [MappedTokenFields.SemanticKey] = token.SemanticKey,
                    [MappedTokenFields.Context] = MappedTokenContext.FieldValue.ToString()
                })));
            position = match.Index + match.Length;
        }

        if (position < value.Length)
            children.Add(new TreeNode(
                $"$fieldText{ordinal++:D6}",
                NodeMetadata.Create("fieldText"),
                value[position..]));

        return new TreeNode("$fieldValue:" + fieldName, fields, children: children);
    }

    private static ParsedToken ParseAttributeToken(Match match, string fieldName, int mappedOrdinal) => new(
            match.Groups["id"].Value,
            CreateFieldValueSemanticKey(fieldName, mappedOrdinal));

    private static TreeNode ParseMappedNode(XNode node, int index) => node switch
    {
        XElement element => ParseMappedElement(element),
        XCData cdata => new TreeNode($"$cdata{index:D6}", NodeMetadata.Create("cdata"), cdata.Value),
        XText text => new TreeNode($"$text{index:D6}", NodeMetadata.Create("text"), text.Value),
        XComment comment => new TreeNode($"$comment{index:D6}", NodeMetadata.Create("comment"), comment.Value),
        _ => new TreeNode($"$xmlNode{index:D6}", NodeMetadata.Create("raw"), node.ToString(SaveOptions.DisableFormatting))
    };

    private static string RenderMappedNode(
        TreeNode node,
        IReadOnlyDictionary<string, string> mappedSourceByTokenId)
    {
        if (node.Conflict is not null)
            return ConflictMarkers.Create(node.Conflict.OursText, node.Conflict.BaseText, node.Conflict.TheirsText);

        var type = node.TryGetMetadataType(out var nodeType)
                            ? nodeType
                            : "element";

        if (string.Equals(type, "mappedToken", StringComparison.Ordinal) &&
            node.Fields.TryGetValue(MappedTokenFields.TokenId, out var tokenId))
            return mappedSourceByTokenId[tokenId];

        return type switch
        {
            "text" => SecurityElement.Escape(node.Value ?? string.Empty) ?? string.Empty,
            "cdata" => "<![CDATA[" + (node.Value ?? string.Empty) + "]]>",
            "comment" => "<!--" + (node.Value ?? string.Empty) + "-->",
            "raw" => node.Value ?? string.Empty,
            _ => RenderMappedElement(node, mappedSourceByTokenId)
        };
    }

    private static string RenderMappedElement(
        TreeNode node,
        IReadOnlyDictionary<string, string> mappedSourceByTokenId)
    {
        var builder = new StringBuilder();
        builder.Append('<');
        builder.Append(node.Kind);

        var emittedFields = new HashSet<string>(StringComparer.Ordinal);
        var fieldValuesByName = node.Children
            .Where(IsMappedFieldValue)
            .ToDictionary(child => child.Fields[NodeMetadata.NameField], StringComparer.Ordinal);

        foreach (var fieldName in ReadFieldOrder(node))
        {
            if (node.Fields.TryGetValue(fieldName, out var fieldValue))
            {
                AppendAttribute(builder, fieldName, fieldValue);
                emittedFields.Add(fieldName);
                continue;
            }

            if (fieldValuesByName.TryGetValue(fieldName, out var mappedFieldValue))
            {
                AppendAttribute(builder, fieldName, RenderMappedFieldValue(mappedFieldValue, mappedSourceByTokenId));
                emittedFields.Add(fieldName);
            }
        }

        foreach (var field in node.Fields.Where(field => !field.Key.StartsWith('$', StringComparison.Ordinal) && !emittedFields.Contains(field.Key)))
        {
            AppendAttribute(builder, field.Key, field.Value);
            emittedFields.Add(field.Key);
        }

        foreach (var fieldValue in node.Children.Where(IsMappedFieldValue))
        {
            var fieldName = fieldValue.Fields[NodeMetadata.NameField];
            if (emittedFields.Add(fieldName))
                AppendAttribute(builder, fieldName, RenderMappedFieldValue(fieldValue, mappedSourceByTokenId));
        }

        var xmlChildren = node.Children.Where(child => !IsMappedFieldValue(child)).ToArray();

        if (xmlChildren.Length == 0)
        {
            builder.Append(" />");
            return builder.ToString();
        }

        builder.Append('>');
        foreach (var child in xmlChildren)
            builder.Append(RenderMappedNode(child, mappedSourceByTokenId));

        builder.Append("</");
        builder.Append(node.Kind);
        builder.Append('>');
        return builder.ToString();
    }

    private static void AppendAttribute(StringBuilder builder, string name, string value)
    {
        builder.Append(' ');
        builder.Append(name);
        builder.Append("=\"");
        builder.Append(EscapeAttribute(value));
        builder.Append('"');
    }

    private static IReadOnlyList<string> ReadFieldOrder(TreeNode node) => node.Fields.TryGetValue(FieldOrderField, out var fieldOrder)
            ? fieldOrder.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];

    private static string RenderMappedFieldValue(
        TreeNode node,
        IReadOnlyDictionary<string, string> mappedSourceByTokenId)
    {
        var builder = new StringBuilder();
        foreach (var child in node.Children)
        {
            if (child.TryGetMetadataType(out var type) &&
                string.Equals(type, "mappedToken", StringComparison.Ordinal) &&
                child.Fields.TryGetValue(MappedTokenFields.TokenId, out var tokenId))
            {
                builder.Append(mappedSourceByTokenId[tokenId]);
                continue;
            }

            builder.Append(child.Value ?? string.Empty);
        }

        return builder.ToString();
    }

    private static bool IsMappedFieldValue(TreeNode node) => node.TryGetMetadataType(out var type) &&
            string.Equals(type, "fieldValue", StringComparison.Ordinal);

    private static bool IsMappedToken(TreeNode node) => node.TryGetMetadataType(out var type) &&
            string.Equals(type, "mappedToken", StringComparison.Ordinal);

    private static string CreateChildSemanticKey(int ordinal) => "child:" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string CreateFieldValueSemanticKey(string fieldName, int ordinal) => "field:" + fieldName + "/mapped:" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapeAttribute(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string AttributeKey(XAttribute attribute)
    {
        if (!attribute.IsNamespaceDeclaration)
            return attribute.Name.LocalName;

        return string.Equals(attribute.Name.LocalName, "xmlns", StringComparison.Ordinal)
                            ? "xmlns"
                            : "xmlns:" + attribute.Name.LocalName;
    }

    private static string CreateIndentedConflictMarkers(string? ours, string? @base, string? theirs, string bodyIndent)
    {
        _ = @base;
        return string.Join(
            Environment.NewLine,
            "<<<<<<< ours",
            IndentConflictBody(ours, bodyIndent),
            "=======",
            IndentConflictBody(theirs, bodyIndent),
            ">>>>>>> theirs");
    }

    private static string IndentConflictBody(string? text, string indent) => string.Join(
            Environment.NewLine,
            (text ?? string.Empty)
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => line.Length == 0 ? line : indent + line));

    private sealed class XmlTokenContextTracker : IMappedTokenContextTracker
    {
        private bool _inTag;
        private char? _quote;
        private bool _expectingAttributeValue;
        private int _childMappedOrdinal;
        private string? _currentFieldName;
        private readonly Dictionary<string, int> _fieldMappedOrdinals = new(StringComparer.Ordinal);
        private readonly StringBuilder _attributeName = new();

        public bool TryGetPossibleContexts(out IReadOnlyList<MappedTokenContext> contexts, out string? unsupportedReason)
        {
            if (_quote is not null)
            {
                contexts = [MappedTokenContext.FieldValue];
                unsupportedReason = null;
                return true;
            }

            if (_inTag)
            {
                contexts = [];
                unsupportedReason = "The mapped token is inside XML tag syntax rather than a tree child node or field value.";
                return false;
            }

            contexts = [MappedTokenContext.ChildNode];
            unsupportedReason = null;
            return true;
        }

        public string CreateSemanticKey(MappedTokenContext context)
        {
            if (context == MappedTokenContext.ChildNode)
                return CreateChildSemanticKey(_childMappedOrdinal++);

            if (context == MappedTokenContext.FieldValue)
            {
                var fieldName = _currentFieldName ?? "<unknown>";
                _fieldMappedOrdinals.TryGetValue(fieldName, out var ordinal);
                _fieldMappedOrdinals[fieldName] = ordinal + 1;
                return CreateFieldValueSemanticKey(fieldName, ordinal);
            }

            throw new InvalidOperationException($"XML cannot create a semantic key for mapped context '{context}'.");
        }

        public void Feed(string literalText)
        {
            ArgumentNullException.ThrowIfNull(literalText);

            foreach (var character in literalText)
            {
                if (_quote is not null)
                {
                    if (character == _quote)
                    {
                        _quote = null;
                        _currentFieldName = null;
                        _expectingAttributeValue = false;
                    }

                    continue;
                }

                if (_inTag)
                {
                    switch (character)
                    {
                        case '"' or '\'':
                            _quote = character;
                            _expectingAttributeValue = false;
                            break;

                        case '>':
                            _inTag = false;
                            _attributeName.Clear();
                            _currentFieldName = null;
                            _expectingAttributeValue = false;
                            break;

                        case '=':
                            _currentFieldName = _attributeName.ToString();
                            _attributeName.Clear();
                            _expectingAttributeValue = true;
                            break;

                        case var separator when separator == '/' || char.IsWhiteSpace(separator):
                            if (!_expectingAttributeValue)
                                _attributeName.Clear();
                            break;

                        default:
                            if (!_expectingAttributeValue)
                                _attributeName.Append(character);
                            break;
                    }

                    continue;
                }

                if (character == '<')
                {
                    _inTag = true;
                    _attributeName.Clear();
                    _currentFieldName = null;
                    _expectingAttributeValue = false;
                }
            }
        }
    }

    private readonly record struct ParsedToken(string Id, string SemanticKey);
}
