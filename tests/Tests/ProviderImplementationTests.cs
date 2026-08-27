using System.Text;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Core.Tree;
using MeridianGit.Formats.Binary;
using MeridianGit.Formats.Css;
using MeridianGit.Formats.Html;
using MeridianGit.Formats.JavaScript;
using MeridianGit.Formats.Png;
using MeridianGit.Formats.Xap;

namespace Meridian.Tests;

public sealed class ProviderImplementationTests
{
    private static readonly MergeSchema EmptySchema = MergeSchema.Empty;

    private static MergeResult Merge(IFormatAdapter adapter, string @base, string ours, string theirs) =>
        new Merger().Merge(
            adapter.Parse(@base, "base", EmptySchema),
            adapter.Parse(ours, "ours", EmptySchema),
            adapter.Parse(theirs, "theirs", EmptySchema),
            EmptySchema,
            adapter);

    // ---- CSS ----------------------------------------------------------------

    [Fact]
    public void CssRoundTripsRealStylesheetExactly()
    {
        var adapter = new CssAdapter();
        var source = """
            /* header */
            .a {
              color: red;
              margin: 0;
            }

            @media (min-width: 600px) {
              .a { color: blue; }
            }
            """;

        var document = adapter.Parse(source, "styles.css", EmptySchema);

        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void CssRoundTripsArbitraryNonCssTextExactly()
    {
        var adapter = new CssAdapter();
        var source = "not really css; just text with } stray braces {";

        var document = adapter.Parse(source, "weird.css", EmptySchema);

        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void CssParsesRulesAndDeclarationsStructurally()
    {
        var adapter = new CssAdapter();

        var document = adapter.Parse(".a {\n  color: red;\n}\n", "styles.css", EmptySchema);

        var rule = Assert.Single(document.Root.Children, child => child.Fields.TryGetValue("$type", out var t) && t == "block");
        var declaration = Assert.Single(rule.Children, child => child.Fields.TryGetValue("$type", out var t) && t == "declaration");
        Assert.Equal("color", declaration.Fields["$name"]);
    }

    [Fact]
    public void CssMergesIndependentDeclarationEditsInTheSameRule()
    {
        var adapter = new CssAdapter();

        var result = Merge(
            adapter,
            ".a {\n  color: red;\n  margin: 0;\n}\n",
            ".a {\n  color: blue;\n  margin: 0;\n}\n",
            ".a {\n  color: red;\n  margin: 8px;\n}\n");

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.Contains("color: blue;", rendered);
        Assert.Contains("margin: 8px;", rendered);
    }

    [Fact]
    public void CssMergesIndependentlyAddedRules()
    {
        var adapter = new CssAdapter();

        var result = Merge(
            adapter,
            ".a { color: red; }\n",
            ".a { color: red; }\n.b { color: green; }\n",
            ".a { color: red; }\n.c { color: blue; }\n");

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.Contains(".b { color: green; }", rendered);
        Assert.Contains(".c { color: blue; }", rendered);
    }

    [Fact]
    public void CssConflictsWhenBothSidesChangeTheSameDeclaration()
    {
        var adapter = new CssAdapter();

        var result = Merge(
            adapter,
            ".a { color: red; }\n",
            ".a { color: blue; }\n",
            ".a { color: green; }\n");

        Assert.True(result.HasConflicts);
    }

    // ---- JavaScript ---------------------------------------------------------

    [Fact]
    public void JavaScriptRoundTripsSourceExactly()
    {
        var adapter = new JavaScriptAdapter();
        var source = "import { x } from './x';\n\nconst answer = 42;\nfunction greet(name) {\n  return `Hi ${name}`;\n}\n";

        var document = adapter.Parse(source, "script.js", EmptySchema);

        Assert.Equal(source, adapter.RenderDocument(document));
        Assert.Equal("script", document.Root.Fields["$type"]);
    }

    [Fact]
    public void JavaScriptParsesTopLevelStatementsStructurally()
    {
        var adapter = new JavaScriptAdapter();

        var document = adapter.Parse("const a = 1;\nfunction f() {}\n", "script.js", EmptySchema);

        Assert.Equal(2, document.Root.Children.Count);
        Assert.Contains(document.Root.Children, child => child.Kind == "$statement:var:a");
        Assert.Contains(document.Root.Children, child => child.Kind == "$statement:function:f");
    }

    [Fact]
    public void JavaScriptMergesIndependentTopLevelChanges()
    {
        var adapter = new JavaScriptAdapter();

        var result = Merge(
            adapter,
            "function a() {\n  return 1;\n}\nfunction b() {\n  return 2;\n}\n",
            "function a() {\n  return 11;\n}\nfunction b() {\n  return 2;\n}\n",
            "function a() {\n  return 1;\n}\nfunction b() {\n  return 2;\n}\nfunction c() {\n  return 3;\n}\n");

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.Contains("return 11;", rendered);
        Assert.Contains("function c()", rendered);
    }

    [Fact]
    public void JavaScriptConflictsWhenBothSidesEditTheSameDeclaration()
    {
        var adapter = new JavaScriptAdapter();

        var result = Merge(
            adapter,
            "function a() {\n  return 1;\n}\n",
            "function a() {\n  return 2;\n}\n",
            "function a() {\n  return 3;\n}\n");

        Assert.True(result.HasConflicts);
    }

    [Fact]
    public void JavaScriptInvalidSourceFailsLoudly()
    {
        var adapter = new JavaScriptAdapter();

        Assert.ThrowsAny<Exception>(() => adapter.Parse("function (", "broken.js", EmptySchema));
    }

    // ---- HTML documents -----------------------------------------------------

    // A complete page (doctype + <html>/<head>/<body>) is parsed as a real document, so edits to
    // disjoint subtrees merge. It used to be kept as one opaque blob, which conflicted the whole
    // file on any two-sided edit.
    private const string DocumentBase =
        "<!doctype html>\n<html><head><title>Base</title></head><body><p id=\"x\">base</p><p id=\"y\">keep</p></body></html>\n";

    [Fact]
    public void HtmlFullDocumentParsesAsADocumentNotAnOpaqueBlob()
    {
        var adapter = new HtmlFragmentAdapter();

        var document = adapter.Parse(DocumentBase, "page.html", EmptySchema);

        Assert.Equal(HtmlDocumentAdapter.RootKind, document.Root.Kind);
        var html = Assert.Single(document.Root.Children, child => child.GetMetadataName() == "html");
        Assert.Contains(html.Children, child => child.GetMetadataName() == "head");
        Assert.Contains(html.Children, child => child.GetMetadataName() == "body");
    }

    [Fact]
    public void HtmlFullDocumentMergesDisjointHeadAndBodyEdits()
    {
        var adapter = new HtmlFragmentAdapter();

        var result = Merge(
            adapter,
            DocumentBase,
            DocumentBase.Replace("<title>Base</title>", "<title>OURS</title>", StringComparison.Ordinal),
            DocumentBase.Replace("<p id=\"y\">keep</p>", "<p id=\"y\">THEIRS</p>", StringComparison.Ordinal));

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.Contains("<title>OURS</title>", rendered);
        Assert.Contains("<p id=\"y\">THEIRS</p>", rendered);
        // Both edits applied, and the wrappers the opaque blob used to protect are still there.
        Assert.StartsWith("<!doctype html>\n<html>", rendered, StringComparison.Ordinal);
        Assert.Contains("<head>", rendered);
        Assert.Contains("<body>", rendered);
        Assert.EndsWith("</body></html>\n", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlFullDocumentPreservesTheDoctypeVerbatim()
    {
        var adapter = new HtmlFragmentAdapter();
        // Lowercase, and a legacy doctype with identifiers: the parser exposes neither verbatim.
        const string legacy = "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"http://www.w3.org/TR/html4/strict.dtd\">";

        Assert.Equal(
            DocumentBase,
            adapter.RenderDocument(adapter.Parse(DocumentBase, "page.html", EmptySchema)));

        // A doctype changed on one side only survives the merge as written.
        var result = Merge(
            adapter,
            DocumentBase,
            DocumentBase.Replace("<!doctype html>", legacy, StringComparison.Ordinal),
            DocumentBase.Replace("<title>Base</title>", "<title>THEIRS</title>", StringComparison.Ordinal));

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.StartsWith(legacy, rendered, StringComparison.Ordinal);
        Assert.Contains("<title>THEIRS</title>", rendered);
    }

    [Fact]
    public void HtmlFullDocumentMergesAnAttributeOnlyChangeOnTheHtmlElement()
    {
        var adapter = new HtmlFragmentAdapter();

        var result = Merge(
            adapter,
            DocumentBase,
            DocumentBase.Replace("<html>", "<html lang=\"en\">", StringComparison.Ordinal),
            DocumentBase.Replace("<p id=\"y\">keep</p>", "<p id=\"y\">THEIRS</p>", StringComparison.Ordinal));

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.Contains("<html lang=\"en\">", rendered);
        Assert.Contains("<p id=\"y\">THEIRS</p>", rendered);
    }

    [Fact]
    public void HtmlFullDocumentConflictsWhenBothSidesChangeTheSameElementText()
    {
        var adapter = new HtmlFragmentAdapter();

        var result = Merge(
            adapter,
            DocumentBase,
            DocumentBase.Replace("<p id=\"x\">base</p>", "<p id=\"x\">OURS</p>", StringComparison.Ordinal),
            DocumentBase.Replace("<p id=\"x\">base</p>", "<p id=\"x\">THEIRS</p>", StringComparison.Ordinal));

        Assert.True(result.HasConflicts);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Contains("body", conflict.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlFullDocumentKeepsScriptAndStyleBodiesVerbatim()
    {
        var adapter = new HtmlFragmentAdapter();
        const string source =
            "<!doctype html>\n<html><head><style>a > b {color:red}</style></head>" +
            "<body><script>if (a < b && c) { go(); }</script></body></html>\n";

        var rendered = adapter.RenderDocument(adapter.Parse(source, "page.html", EmptySchema));

        Assert.Equal(source, rendered);
    }

    // Whitespace after </html> is folded into the body by the parser, while a comment there is
    // attached to the document. Both have to come back out exactly once: dropping the comment loses
    // content, and emitting the whitespace twice grows the file on every merge.
    [Fact]
    public void HtmlFullDocumentRoundTripsContentAfterTheHtmlElement()
    {
        var adapter = new HtmlFragmentAdapter();
        const string source = "<!doctype html>\n<html><head></head><body><p>a</p></body></html>\n<!-- build: 1 -->\n";

        var once = adapter.RenderDocument(adapter.Parse(source, "page.html", EmptySchema));
        var twice = adapter.RenderDocument(adapter.Parse(once, "page.html", EmptySchema));

        Assert.Equal(source, once);
        Assert.Equal(source, twice);
    }

    // ---- Binary -------------------------------------------------------------

    private static readonly byte[] BaseBytes = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xFE, 0x01];
    private static readonly byte[] OursBytes = [0x89, 0x50, 0x4E, 0x47, 0x10, 0xFF, 0xFE, 0x02];
    private static readonly byte[] TheirsBytes = [0x89, 0x50, 0x4E, 0x47, 0x20, 0xFF, 0xFE, 0x03];

    [Fact]
    public void GenericBinaryAdapterRoundTripsNonTextBytesExactly()
    {
        var adapter = new BinAdapter();

        var document = adapter.ParseBytes(BaseBytes, "payload.bin", EmptySchema);

        Assert.Equal("binary", document.Format);
        Assert.Equal("$binary", document.Root.Kind);
        Assert.Equal(BaseBytes, adapter.RenderDocumentBytes(document));
    }

    [Fact]
    public void BinaryAdapterRoundTripsNonTextBytesExactly()
    {
        var adapter = new PngAdapter();

        var document = adapter.ParseBytes(BaseBytes, "image.png", EmptySchema);

        Assert.Equal(BaseBytes, adapter.RenderDocumentBytes(document));
    }

    [Fact]
    public void BinaryMergeTakesTheSingleChangedSide()
    {
        var adapter = new PngAdapter();

        var result = new Merger().Merge(
            adapter.ParseBytes(BaseBytes, "base", EmptySchema),
            adapter.ParseBytes(BaseBytes, "ours", EmptySchema),
            adapter.ParseBytes(TheirsBytes, "theirs", EmptySchema),
            EmptySchema,
            adapter);

        Assert.False(result.HasConflicts);
        Assert.Equal(TheirsBytes, adapter.RenderDocumentBytes(result.Document));
    }

    [Fact]
    public void BinaryMergeReportsConflictWhenBothSidesChangeDifferently()
    {
        var adapter = new PngAdapter();

        var result = new Merger().Merge(
            adapter.ParseBytes(BaseBytes, "base", EmptySchema),
            adapter.ParseBytes(OursBytes, "ours", EmptySchema),
            adapter.ParseBytes(TheirsBytes, "theirs", EmptySchema),
            EmptySchema,
            adapter);

        Assert.True(result.HasConflicts);
        Assert.Throws<InvalidOperationException>(() => adapter.RenderDocumentBytes(result.Document));
    }

    [Fact]
    public void BinaryStringContractStaysConsistentWithByteContract()
    {
        var adapter = new XapAdapter();
        const string content = "any text content";

        var viaString = adapter.RenderDocument(adapter.Parse(content, "a.xap", EmptySchema));
        var viaBytes = adapter.RenderDocumentBytes(adapter.ParseBytes(Encoding.UTF8.GetBytes(content), "a.xap", EmptySchema));

        Assert.Equal(content, viaString);
        Assert.Equal(content, Encoding.UTF8.GetString(viaBytes));
    }
}
