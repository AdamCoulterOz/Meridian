using System.Text;
using Meridian.Core.Formats;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using MeridianGit.Formats.Binary;
using MeridianGit.Formats.Css;
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
        Assert.Equal("esprima", document.Root.Fields["parser"]);
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
