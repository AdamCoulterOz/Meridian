using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Identity;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Core.Mapped;
using Meridian.Formats.Data;

namespace Meridian.Tests;

public sealed class AstMergerTests
{
    private static readonly AstSchema DefaultSchema = new()
    {
        GlobalDiscriminatorFields = ["id", "Id", "languagecode"]
    };

    private readonly XmlAdapter _xml = new();

    [Fact]
    public void GlobalDiscriminatorsIncludeUppercaseId()
    {
        var document = Parse("""<root><item Id="A" /><item Id="B" /></root>""");

        var result = new AstIdentityAssigner().Assign(document, DefaultSchema);

        Assert.False(result.HasErrors);
        Assert.Contains("Id=A", result.Document.Root.Children[0].Identity);
        Assert.Contains("Id=B", result.Document.Root.Children[1].Identity);
    }

    [Fact]
    public void AmbiguousSiblingIdentityIsReported()
    {
        var document = Parse("""<root><item /><item /></root>""");

        var result = new AstIdentityAssigner().Assign(document, DefaultSchema);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path == "root");
    }

    [Fact]
    public void MappedTokenReferenceSemanticKeysDiscriminateSiblingIdentity()
    {
        var firstFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MappedTokenFields.SemanticKey] = "field:class/mapped:0"
        };
        var secondFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MappedTokenFields.SemanticKey] = "field:title/mapped:0"
        };
        var document = new AstDocument(
            "test",
            new AstNode(
                "root",
                children:
                [
                    new AstNode("$mappedToken", firstFields),
                    new AstNode("$mappedToken", secondFields)
                ]));

        var result = new AstIdentityAssigner().Assign(document, AstSchema.Empty);

        Assert.False(result.HasErrors);
        Assert.Contains(MappedTokenFields.SemanticKey + "=field:class/mapped:0", result.Document.Root.Children[0].Identity);
        Assert.Contains(MappedTokenFields.SemanticKey + "=field:title/mapped:0", result.Document.Root.Children[1].Identity);
    }

    [Fact]
    public void UnorderedChildrenMergeIndependentAdds()
    {
        var schema = new AstSchema
        {
            GlobalDiscriminatorFields = ["id", "Id", "languagecode"]
        };

        var result = Merge(
            """<root><Description languagecode="1033">Base</Description></root>""",
            """<root><Description languagecode="1033">Local</Description></root>""",
            """<root><Description languagecode="1033">Base</Description><Description languagecode="3081">Remote</Description></root>""",
            schema);

        Assert.False(result.HasConflicts);
        var text = _xml.RenderDocument(result.Document);
        Assert.Contains("Local", text);
        Assert.Contains("3081", text);
    }

    [Fact]
    public void UnorderedChildrenPreserveOursOrderAndAppendTheirsOnlyAdds()
    {
        var result = Merge(
            """<root><item id="1" /><item id="2" /><item id="3" /></root>""",
            """<root><item id="3" /><item id="1" /><item id="2" /></root>""",
            """<root><item id="1" /><item id="2" /><item id="3" /><item id="4" /></root>""",
            DefaultSchema);

        Assert.False(result.HasConflicts);
        Assert.Equal(["3", "1", "2", "4"], result.Document.Root.Children.Select(child => child.Fields["id"]));
    }

    [Fact]
    public void MergedFieldsPreserveOursAttributeOrder()
    {
        var result = Merge(
            """<root><item id="1" first="1" second="2"><name>Base</name></item></root>""",
            """<root><item id="1" first="1" second="2"><name>Local</name></item></root>""",
            """<root><item id="1" first="1" second="2"><name>Base</name><description>Remote</description></item></root>""",
            DefaultSchema);

        Assert.False(result.HasConflicts);
        Assert.Contains("""<item id="1" first="1" second="2">""", _xml.RenderDocument(result.Document));
    }

    [Fact]
    public void OrderedChildrenConflictWhenBothSidesReorderDifferently()
    {
        var schema = new AstSchema
        {
            GlobalDiscriminatorFields = ["id", "Id", "languagecode"],
            OrderedChildren = [PathSelector.Exact("root")]
        };

        var result = Merge(
            """<root><item id="1" /><item id="2" /><item id="3" /></root>""",
            """<root><item id="2" /><item id="1" /><item id="3" /></root>""",
            """<root><item id="1" /><item id="3" /><item id="2" /></root>""",
            schema);

        Assert.True(result.HasConflicts);
        Assert.Contains(result.Conflicts, conflict => conflict.Kind == ConflictKind.OrderedChildren);
    }

    [Fact]
    public void ScalarConflictRendersGitConflictMarkers()
    {
        var result = Merge(
            """<root><item id="1">Base</item></root>""",
            """<root><item id="1">Local</item></root>""",
            """<root><item id="1">Remote</item></root>""",
            DefaultSchema);

        Assert.True(result.HasConflicts);
        var text = _xml.RenderDocument(result.Document);
        Assert.Contains("<<<<<<< ours", text);
        Assert.DoesNotContain("||||||| base", text);
        Assert.Contains("  Local", text);
        Assert.Contains(">>>>>>> theirs", text);
    }

    [Fact]
    public void NodeConflictPayloadLinesKeepXmlIndentation()
    {
        var result = Merge(
            """
<root>
  <items>
    <item id="1" name="Base" />
  </items>
</root>
""",
            """
<root>
  <items>
    <item id="1" name="Local" />
  </items>
</root>
""",
            """
<root>
  <items>
    <item id="1" name="Remote" />
  </items>
</root>
""",
            DefaultSchema);

        Assert.True(result.HasConflicts);
        var text = _xml.RenderDocument(result.Document);
        Assert.Contains("<<<<<<< ours", text);
        Assert.Contains("    <item id=\"1\" name=\"Local\" />", text);
        Assert.Contains("=======", text);
        Assert.Contains("    <item id=\"1\" name=\"Remote\" />", text);
        Assert.Contains(">>>>>>> theirs", text);
    }

    [Fact]
    public void XmlRendererPreservesDeclarationNamespaceAttributesAndChildOrder()
    {
        var document = _xml.Parse(
            """
<?xml version="1.0" encoding="utf-8"?>
<WebResource xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <WebResourceId>{4eb07c53-7a63-f011-bec3-6045bd3d0183}</WebResourceId>
  <Name>sch_safety_cone</Name>
  <DisplayName>Safety Cone</DisplayName>
</WebResource>
""",
            null,
            DefaultSchema);

        var rendered = _xml.RenderDocument(document);

        Assert.StartsWith("""<?xml version="1.0" encoding="utf-8"?>""", rendered);
        Assert.Contains("""<WebResource xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">""", rendered);
        Assert.True(rendered.IndexOf("<WebResourceId>", StringComparison.Ordinal) < rendered.IndexOf("<Name>", StringComparison.Ordinal));
        Assert.True(rendered.IndexOf("<Name>", StringComparison.Ordinal) < rendered.IndexOf("<DisplayName>", StringComparison.Ordinal));
        Assert.Contains("  <Name>sch_safety_cone</Name>", rendered);
    }

    [Fact]
    public void NestedContentExpanderUsesSchemaContentRules()
    {
        var schema = new AstSchema
        {
            ContentRules = [new ContentRule(PathSelector.Exact("outer/payload"), "xml")]
        };
        var document = _xml.Parse("""<outer><payload>&lt;inner id="1" /&gt;</payload></outer>""", null, schema);
        var registry = new AstFormatRegistry([_xml]);

        var expanded = NestedContentExpander.Expand(document, schema, registry);

        var content = expanded.Root.Children.Single().Children.Single();
        Assert.Equal("$content", content.Kind);
        Assert.Equal("xml", content.Fields["format"]);
        Assert.Equal("inner", content.Children.Single().Kind);
    }

    [Fact]
    public void NestedContentExpanderUsesSchemaRefsRecursively()
    {
        var schema = new AstSchema
        {
            ContentRules =
            [
                new ContentRule(PathSelector.Exact("outer/payload"), "xml", "innerXml")
            ],
            NestedSchemas = new Dictionary<string, AstSchema>(StringComparer.OrdinalIgnoreCase)
            {
                ["innerXml"] = new AstSchema
                {
                    ContentRules =
                    [
                        new ContentRule(PathSelector.Exact("inner/payload"), "xml", "leafXml")
                    ]
                },
                ["leafXml"] = AstSchema.Empty
            }
        };
        var document = _xml.Parse(
            """<outer><payload>&lt;inner&gt;&lt;payload&gt;&amp;lt;leaf id="1" /&amp;gt;&lt;/payload&gt;&lt;/inner&gt;</payload></outer>""",
            null,
            schema);
        var registry = new AstFormatRegistry([_xml]);

        var expanded = NestedContentExpander.Expand(document, schema, registry);

        var outerContent = expanded.Root.Children.Single().Children.Single();
        Assert.Equal("innerXml", outerContent.Fields["schemaRef"]);
        var innerPayload = outerContent.Children.Single().Children.Single();
        var innerContent = innerPayload.Children.Single();
        Assert.Equal("leafXml", innerContent.Fields["schemaRef"]);
        Assert.Equal("leaf", innerContent.Children.Single().Kind);
    }

    [Fact]
    public void NestedContentCollapserReEscapesCleanContentForParentFormat()
    {
        var schema = new AstSchema
        {
            ContentRules = [new ContentRule(PathSelector.Exact("outer/payload"), "xml")]
        };
        var document = _xml.Parse(
            """<outer><payload>&lt;inner id="1"&gt;Tom &amp;amp; Jerry&lt;/inner&gt;</payload></outer>""",
            null,
            schema);
        var registry = new AstFormatRegistry([_xml]);
        var expanded = NestedContentExpander.Expand(document, schema, registry);

        var collapsed = NestedContentCollapser.Collapse(expanded, registry);

        var rendered = _xml.RenderDocument(collapsed);
        Assert.Contains("&lt;inner id=&quot;1&quot;&gt;Tom &amp;amp; Jerry&lt;/inner&gt;", rendered);
        Assert.DoesNotContain("<inner id=\"1\">Tom &amp; Jerry</inner>", rendered);
    }

    [Fact]
    public void NestedContentCollapserFailsLoudlyForUnprojectedNestedConflicts()
    {
        var conflict = new MergeConflict(
            ConflictKind.Scalar,
            "inner",
            "Base",
            "Ours",
            "Theirs",
            "Both sides changed scalar content differently.");
        var document = new AstDocument(
            "xml",
            new AstNode(
                "outer",
                children:
                [
                    new AstNode(
                        "payload",
                        children:
                        [
                            new AstNode(
                                "$content",
                                new Dictionary<string, string> { ["format"] = "xml" },
                                children: [new AstNode("inner", conflict: conflict)])
                        ])
                ]));
        var registry = new AstFormatRegistry([_xml]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NestedContentCollapser.Collapse(document, registry));
        Assert.Contains("owning encoded scalar boundary", exception.Message);
    }

    private AstDocument Parse(string text) => _xml.Parse(text, null, DefaultSchema);

    private MergeResult Merge(string @base, string ours, string theirs, AstSchema schema) => new AstMerger().Merge(
            _xml.Parse(@base, "base.xml", schema),
            _xml.Parse(ours, "ours.xml", schema),
            _xml.Parse(theirs, "theirs.xml", schema),
            schema,
            _xml);
}
