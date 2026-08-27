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

    // ---- HTML source fidelity -----------------------------------------------

    // A page written the way people write HTML: attributes in a chosen order and mixed quoting, a
    // valueless attribute, self-closing void elements, named entities, non-ASCII prose, a script
    // body full of characters that must not be encoded, and whitespace between the wrappers. None
    // of it survives a re-serialisation from the parsed tree, so all of it has to come from source.
    private const string RealisticPage =
        "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset='utf-8' />\n" +
        "<title>Café &amp; Co — résumé</title>\n" +
        "<script defer src=\"a.js\"></script>\n<style>a > b { color: red }</style>\n</head>\n" +
        "<body class=\"page\" id=\"top\">\n<p ID=\"P1\" data-flag>2 &lt; 3 &amp;&amp; 4 &gt; 1</p>\n" +
        "<hr/>\n<script>if (a < b && c) { go(); }</script>\n</body>\n</html>\n";

    [Fact]
    public void HtmlRoundTripsARealisticPageExactly()
    {
        var adapter = new HtmlFragmentAdapter();

        var rendered = adapter.RenderDocument(adapter.Parse(RealisticPage, "page.html", EmptySchema));

        Assert.Equal(RealisticPage, rendered);
    }

    [Fact]
    public void HtmlRoundTripsAFragmentExactly()
    {
        var adapter = new HtmlFragmentAdapter();
        const string fragment = "<section class='hero' data-open>\n<p>café &mdash; 2 &lt; 3</p>\n<img src=\"a.png\" />\n</section>";

        Assert.Equal(fragment, adapter.RenderDocument(adapter.Parse(fragment, "fragment.html", EmptySchema)));
    }

    // A merge must rewrite only what it changed. Everything else keeps the spelling it had, so the
    // diff a reviewer sees is the edit rather than the whole file.
    [Fact]
    public void HtmlMergeReSerialisesOnlyTheElementItChanged()
    {
        var adapter = new HtmlFragmentAdapter();

        var result = Merge(
            adapter,
            RealisticPage,
            RealisticPage.Replace("<body class=\"page\" id=\"top\">", "<body class=\"page dark\" id=\"top\">", StringComparison.Ordinal),
            RealisticPage.Replace("2 &lt; 3 &amp;&amp; 4 &gt; 1", "2 &lt; 3", StringComparison.Ordinal));

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);

        // The edits landed.
        Assert.Contains("<body class=\"page dark\" id=\"top\">", rendered);
        Assert.Contains("<p ID=\"P1\" data-flag>2 &lt; 3</p>", rendered);
        // Everything the merge did not touch is byte-for-byte what it was.
        Assert.Contains("<meta charset='utf-8' />", rendered);
        Assert.Contains("<script defer src=\"a.js\"></script>", rendered);
        Assert.Contains("<title>Café &amp; Co — résumé</title>", rendered);
        Assert.Contains("<hr/>", rendered);
        Assert.Contains("if (a < b && c) { go(); }", rendered);
    }

    // Text that cannot be taken from source verbatim is encoded minimally: only the three characters
    // that cannot stand for themselves. A literal '<' in body text is one such case — it is text to
    // the tokenizer but ends the source run — and encoding everything non-ASCII (what
    // WebUtility.HtmlEncode does) would turn the prose around it into a wall of numeric entities.
    [Fact]
    public void HtmlEncodesTextItCannotTakeFromSourceMinimally()
    {
        var adapter = new HtmlFragmentAdapter();

        var rendered = adapter.RenderDocument(adapter.Parse("<p>a < b — café</p>", "page.html", EmptySchema));

        Assert.Equal("<p>a &lt; b — café</p>", rendered);
        Assert.DoesNotContain("&#", rendered);
        // Re-parsing gets the same text back, which is what makes the encoding safe as well as small.
        Assert.Equal(
            rendered,
            adapter.RenderDocument(adapter.Parse(rendered, "page.html", EmptySchema)));
    }

    // A self-closing tag means something different in foreign content: <path/> really is closed, so
    // emitting it verbatim and then adding </path> would leave a stray end tag. Those elements fall
    // back to explicit serialisation rather than guessing.
    [Fact]
    public void HtmlSerialisesSelfClosingForeignElementsExplicitly()
    {
        var adapter = new HtmlFragmentAdapter();

        var rendered = adapter.RenderDocument(
            adapter.Parse("<svg viewBox=\"0 0 8 8\"><path d=\"M0 0\"/></svg>", "icon.html", EmptySchema));

        Assert.Contains("<path d=\"M0 0\"></path>", rendered);
        Assert.DoesNotContain("/></path>", rendered);
    }

    // <title> and <textarea> hold ESCAPABLE raw text: the parser decodes character references in
    // them, so their content has to be encoded on the way back out. Emitting it verbatim (which the
    // renderer used to do, treating them like <script>) turns "&amp;copy;" into "&copy;", and the
    // next parse of that file reads it as ©.
    [Fact]
    public void HtmlEscapableRawTextIsEncodedOnTheWayOut()
    {
        var adapter = new HtmlFragmentAdapter();
        const string source = "<title>&amp;copy; me</title><textarea>a &lt; b</textarea>";

        Assert.Equal(source, adapter.RenderDocument(adapter.Parse(source, "page.html", EmptySchema)));

        // ... and text the merge rewrites is encoded rather than passed through raw.
        var result = Merge(
            adapter,
            source,
            source.Replace("&amp;copy; me", "&amp;copy; you", StringComparison.Ordinal),
            source);

        Assert.False(result.HasConflicts);
        var rendered = adapter.RenderDocument(result.Document);
        Assert.Contains("<title>&amp;copy; you</title>", rendered);
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
