using Meridian.Core.Identity;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Core.Tree;
using MeridianGit.Formats.Html;
using MeridianGit.Formats.JavaScript;
using MeridianGit.Formats.Json;
using MeridianGit.Formats.Xml;
using MeridianGit.Formats.Yaml;

namespace Meridian.Tests;

// Regression tests for the code-review fixes. Each test pins a behavior that was previously wrong
// (silent data loss, false conflicts, broken schema binding, identity collisions).
public sealed class ReviewFixesTests
{
    private static readonly MergeSchema Empty = MergeSchema.Empty;

    // ---- Conflict-marker safety net -------------------------------------------------------------

    // The driver decides whether a render projected its conflict by looking for markers. A bare
    // substring search matches marker text that is ordinary CONTENT, which made a conflicted merge
    // look resolved and reintroduced the silent-data-loss class the safety net exists to close.
    [Fact]
    public void MarkerTextInContentIsNotMistakenForAProjectedConflict()
    {
        const string ours = "{\"note\":\"conflict syntax is <<<<<<< ours\",\"a\":3}";
        const string @base = "{\"note\":\"conflict syntax is <<<<<<< ours\",\"a\":1}";
        const string theirs = "{\"note\":\"conflict syntax is <<<<<<< ours\",\"a\":2}";
        // What a non-projecting adapter renders: the conflicted scalar blanked, marker text intact.
        const string rendered = "{\"note\":\"conflict syntax is <<<<<<< ours\",\"a\":0}";

        Assert.False(ConflictMarkers.ProjectedConflict(rendered, ours, @base, theirs));
    }

    [Fact]
    public void RealProjectedConflictIsRecognised()
    {
        const string ours = "{\"a\":3}";
        const string @base = "{\"a\":1}";
        const string theirs = "{\"a\":2}";
        var rendered = ConflictMarkers.CreateDiff3(ours, @base, theirs);

        Assert.True(ConflictMarkers.ProjectedConflict(rendered, ours, @base, theirs));
    }

    // A marker only counts at the start of a line; mid-line marker text is content.
    [Fact]
    public void ContainsMarkerIgnoresMidLineMarkerText()
    {
        Assert.False(ConflictMarkers.ContainsMarker("value: has <<<<<<< ours inside"));
        Assert.True(ConflictMarkers.ContainsMarker("<<<<<<< ours\nx\n=======\ny\n>>>>>>> theirs"));
    }

    // ---- Text-adapter fidelity ------------------------------------------------------------------

    [Fact]
    public void HtmlRawTextElementsAreNotEntityEncoded()
    {
        var adapter = new HtmlFragmentAdapter();
        const string source = "<div><script>if (a < b && c) { go(); }</script><style>a > b {color:red}</style></div>";

        var rendered = adapter.RenderDocument(adapter.Parse(source, "page.html", Empty));

        Assert.Contains("if (a < b && c)", rendered);
        Assert.Contains("a > b {color:red}", rendered);
        Assert.DoesNotContain("&lt;", rendered);
    }

    [Fact]
    public void HtmlFullDocumentRoundTripsExactly()
    {
        var adapter = new HtmlFragmentAdapter();
        const string source = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body><p>hi</p></body></html>";

        Assert.Equal(source, adapter.RenderDocument(adapter.Parse(source, "page.html", Empty)));
    }

    [Fact]
    public void HtmlHeaderFragmentIsNotMisclassifiedAsFullDocument()
    {
        // "<header>" shares a prefix with "<head"; it must still be treated as a mergeable fragment.
        var adapter = new HtmlFragmentAdapter();
        var document = adapter.Parse("<header><nav>x</nav></header>", "frag.html", Empty);

        // A fragment parses into element children; a full document is stored as a single raw node.
        Assert.Equal("$fragment", document.Root.Kind);
        Assert.Contains("<nav>x</nav>", adapter.RenderDocument(document));
    }

    [Fact]
    public void HtmlAttributesNamedLikeMetadataDoNotClobberElements()
    {
        var adapter = new HtmlFragmentAdapter();
        const string source = "<div $type=\"x\" $name=\"y\">content</div>";

        var rendered = adapter.RenderDocument(adapter.Parse(source, "page.html", Empty));

        Assert.Contains("<div", rendered);
        Assert.Contains("content", rendered);
        Assert.Contains("$type=\"x\"", rendered);
    }

    [Fact]
    public void XmlDoctypeRoundTripsWithoutInjectedInternalSubset()
    {
        var adapter = new XmlAdapter();
        const string source = "<!DOCTYPE note SYSTEM \"note.dtd\">\n<note><to>x</to></note>";

        var rendered = adapter.RenderDocument(adapter.Parse(source, "n.xml", Empty));

        Assert.Contains("<!DOCTYPE note SYSTEM \"note.dtd\">", rendered);
        Assert.DoesNotContain("[]", rendered);
    }

    [Fact]
    public void HtmlEqualsPrefixedAttributeRoundTrips()
    {
        // A stray '=' before an attribute name must not be corrupted by the metadata escape scheme.
        var adapter = new HtmlFragmentAdapter();
        const string source = "<div =foo=\"bar\">x</div>";

        Assert.Contains("=foo=\"bar\"", adapter.RenderDocument(adapter.Parse(source, "p.html", Empty)));
    }

    [Fact]
    public void YamlScalarQuotingIsPreserved()
    {
        var adapter = new YamlAdapter();
        const string source = "answer: \"yes\"\nversion: \"1.10\"\n";

        var rendered = adapter.RenderDocument(adapter.Parse(source, "c.yaml", Empty));

        Assert.Contains("answer: \"yes\"", rendered);
        Assert.Contains("version: \"1.10\"", rendered);
    }

    [Fact]
    public void YamlMultiDocumentStreamsAreNotTruncated()
    {
        var adapter = new YamlAdapter();
        const string source = "first: 1\n---\nsecond: 2\n";

        var rendered = adapter.RenderDocument(adapter.Parse(source, "k8s.yaml", Empty));

        Assert.Contains("first: 1", rendered);
        Assert.Contains("second: 2", rendered);
    }

    // ---- Merge correctness ----------------------------------------------------------------------

    [Fact]
    public void JavaScriptIndependentAdditionsMergeWithoutConflict()
    {
        var adapter = new JavaScriptAdapter();
        var @base = adapter.Parse("function a(){return 1;}\n", "s.js", Empty);
        var ours = adapter.Parse("function a(){return 1;}\nfunction b(){return 2;}\n", "s.js", Empty);
        var theirs = adapter.Parse("function a(){return 1;}\nfunction c(){return 3;}\nfunction d(){return 4;}\n", "s.js", Empty);

        var result = new Merger().Merge(@base, ours, theirs, Empty, adapter);
        var rendered = adapter.RenderDocument(result.Document);

        Assert.False(result.HasConflicts);
        Assert.Contains("function b()", rendered);
        Assert.Contains("function c()", rendered);
        Assert.Contains("function d()", rendered);
    }

    [Fact]
    public void JsonNestedScalarConflictIsDetected()
    {
        var adapter = new JsonAdapter();
        var @base = adapter.Parse("{\"a\":1}", "c.json", Empty);
        var ours = adapter.Parse("{\"a\":3}", "c.json", Empty);
        var theirs = adapter.Parse("{\"a\":2}", "c.json", Empty);

        var result = new Merger().Merge(@base, ours, theirs, Empty, adapter);

        // The engine must surface the conflict; the driver's safety net then guarantees markers are
        // written rather than a fabricated resolved value.
        Assert.True(result.HasConflicts);
    }

    // ---- Schema binding & identity --------------------------------------------------------------

    [Fact]
    public void YamlBooleanDiscriminatorFormsBind()
    {
        var set = MergeSchemaYamlLoader.Load(
            """
            files:
              - match: "*.xml"
                discriminators:
                  - path: root/label
                    key:
                      text: true
            """);

        var schema = set.CompileForFile("a.xml");
        Assert.IsType<DiscriminatorKey.Text>(Assert.Single(schema.IdentityRules).Key);
    }

    [Fact]
    public void DoubleStarGlobMatchesZeroDirectories()
    {
        var set = MergeSchemaYamlLoader.Load(
            """
            files:
              - match: "**/*.xml"
                discriminators:
                  - path: root/item
                    key: { attribute: id }
            """);

        Assert.NotEmpty(set.CompileForFile("b.xml").IdentityRules);
        Assert.NotEmpty(set.CompileForFile("nested/dir/b.xml").IdentityRules);
    }

    [Fact]
    public void CompositeIdentityValuesDoNotCollideAcrossSeparators()
    {
        var schema = MergeSchemaYamlLoader.Load(
            """
            files:
              - match: "*.xml"
                discriminators:
                  - path: root/w
                    key:
                      composite:
                        - attribute: sku
                        - attribute: region
                          optional: true
            """).CompileForFile("a.xml");

        var document = new DocumentTree("xml", new TreeNode("root", children:
        [
            new TreeNode("w", Fields(("sku", "x+region=y"))),
            new TreeNode("w", Fields(("sku", "x"), ("region", "y")))
        ]));

        var result = new IdentityAssigner().Assign(document, schema);

        Assert.False(result.HasErrors);
        Assert.NotEqual(result.Document.Root.Children[0].Identity, result.Document.Root.Children[1].Identity);
    }

    private static Dictionary<string, string> Fields(params (string Key, string Value)[] pairs)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            fields[key] = value;
        return fields;
    }
}
