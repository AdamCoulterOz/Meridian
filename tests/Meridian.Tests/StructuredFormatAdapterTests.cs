using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Schema;
using Meridian.Core.Templates;
using Meridian.Formats.Data;
using Meridian.Formats.Images;
using Meridian.Formats.Liquid;
using Meridian.Formats.TemplateText;
using Meridian.Formats.Web;
using Meridian.Formats.Xap;

namespace Meridian.Tests;

public sealed class StructuredFormatAdapterTests
{
    [Fact]
    public void JsonAdapterParsesAndRendersObjectTrees()
    {
        var adapter = new JsonAstFormatAdapter();

        var document = adapter.Parse("""{"name":"Safety","items":[1,true,null]}""", "test.json", AstSchema.Empty);

        Assert.Equal("$root", document.Root.Kind);
        Assert.Contains(document.Root.Children, child => child.Fields["$name"] == "name");
        var rendered = adapter.RenderDocument(document);
        Assert.Contains("\"Safety\"", rendered);
        Assert.Contains("true", rendered);
    }

    [Fact]
    public void Json5AdapterParsesJson5AndRendersJson()
    {
        var adapter = new Json5AstFormatAdapter();

        var document = adapter.Parse("""{ name: 'MAX', value: 10, trailing: [1, 2,], }""", "test.json5", AstSchema.Empty);

        var rendered = adapter.RenderDocument(document);
        Assert.Contains("\"name\"", rendered);
        Assert.Contains("\"MAX\"", rendered);
        Assert.Contains("\"value\"", rendered);
    }

    [Fact]
    public void YamlAdapterParsesAndRendersMappingsAndSequences()
    {
        var adapter = new YamlAstFormatAdapter();

        var document = adapter.Parse("""
kind: AdaptiveDialog
actions:
  - id: sendMessage
    kind: SendActivity
""", "data", AstSchema.Empty);

        Assert.Contains(document.Root.Children, child => child.Fields["$name"] == "kind");
        var rendered = adapter.RenderDocument(document);
        Assert.Contains("AdaptiveDialog", rendered);
        Assert.Contains("sendMessage", rendered);
    }

    [Fact]
    public void HtmlFragmentAdapterParsesAndRendersFragments()
    {
        var adapter = new HtmlFragmentAstFormatAdapter();

        var document = adapter.Parse("""<div class="hero">Hello <strong>team</strong></div>""", "fragment.html", AstSchema.Empty);

        var rendered = adapter.RenderDocument(document);
        Assert.Contains("<div class=\"hero\">", rendered);
        Assert.Contains("<strong>team</strong>", rendered);
    }

    [Fact]
    public void TextAdaptersRoundTripOpaqueContent()
    {
        IAstFormatAdapter[] adapters =
        {
            new TemplateTextAstFormatAdapter(),
            new RawAstFormatAdapter(),
            new CssAstFormatAdapter(),
            new PngAstFormatAdapter(),
            new JpgAstFormatAdapter(),
            new GifAstFormatAdapter(),
            new IcoAstFormatAdapter(),
            new XapAstFormatAdapter()
        };

        foreach (var adapter in adapters)
        {
            var document = adapter.Parse("{% assign x = 1 %}\nplain text", "payload", AstSchema.Empty);

            Assert.Equal("{% assign x = 1 %}\nplain text", adapter.RenderDocument(document));
        }
    }

    [Fact]
    public void LiquidAdapterParsesTemplateTokensAndRoundTripsSource()
    {
        var adapter = new LiquidAstFormatAdapter();
        var source = """
<h1>{{ page.title }}</h1>
{% assign x = 1 %}
{% raw %}{{ untouched }}{% endraw %}
{% comment %}hidden {{ value }}{% endcomment %}
""";

        var document = adapter.Parse(source, "template.liquid", AstSchema.Empty);

        Assert.Equal("$liquid", document.Root.Kind);
        Assert.Contains(document.Root.Children, child => child.Fields["$type"] == "output" && child.Value == " page.title ");
        Assert.Contains(document.Root.Children, child => child.Fields["$type"] == "tag" && child.Fields["tagName"] == "assign");
        Assert.Contains(document.Root.Children, child => child.Fields["$type"] == "rawBlock" && child.Value == "{{ untouched }}");
        Assert.Contains(document.Root.Children, child => child.Fields["$type"] == "commentBlock" && child.Value == "hidden {{ value }}");
        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void LiquidAdapterPreservesWhitespaceTrimDelimiters()
    {
        var adapter = new LiquidAstFormatAdapter();
        var source = """Hello {{- user.name -}}{% if user.active -%}yes{%- endif %}""";

        var document = adapter.Parse(source, "template.liquid", AstSchema.Empty);

        Assert.Equal(source, adapter.RenderDocument(document));
        Assert.Contains(document.Root.Children, child =>
            child.Fields["$type"] == "output" &&
            child.Fields["open"] == "{{-" &&
            child.Fields["close"] == "-}}");
    }

    [Fact]
    public void LiquidXmlAdapterParsesHostXmlWithTemplatePlaceholders()
    {
        var adapter = new TemplatedHostAstFormatAdapter(new LiquidAstFormatAdapter(), new XmlAstFormatAdapter());
        var source = """
<ul>
{% for item in items %}
  <li class="item {{ item.kind }}">{{ item.name }}</li>
{% endfor %}
</ul>
""";

        var document = adapter.Parse(source, "template.xml", AstSchema.Empty);

        Assert.Equal("liquid:xml", document.Format);
        Assert.Equal("safe", document.Root.Fields["$mode"]);
        Assert.Equal("liquid", document.Root.Fields["$templateEngine"]);
        Assert.Equal("xml", document.Root.Fields["$hostFormat"]);
        var templates = document.Root.Children.Single(child => child.Kind == "$templates").Children;
        Assert.Contains(templates, child => child.Fields["templateKind"] == "tag");
        Assert.Contains(templates, child => child.Fields["context"] == "FieldValue");
        Assert.Contains(templates, child => child.Fields["templateKind"] == "output");
        Assert.Contains(templates, child => child.Fields[TemplatePlaceholderFields.SemanticKey] == "child:0");
        Assert.Contains(templates, child => child.Fields[TemplatePlaceholderFields.SemanticKey] == "field:class/template:0");
        var host = document.Root.Children.Single(child => child.Kind == "$host").Children.Single();
        var listItem = host.Children.Single(child => child.Kind == "li");
        var classField = listItem.Children.Single(child => child.Kind == "$fieldValue:class");
        Assert.Contains(classField.Children, child => child.Fields.TryGetValue(TemplatePlaceholderFields.SemanticKey, out var semanticKey) && semanticKey == "field:class/template:0");
        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void LiquidXmlAdapterFallsBackWhenTemplateAppearsInXmlTagSyntax()
    {
        var adapter = new TemplatedHostAstFormatAdapter(new LiquidAstFormatAdapter(), new XmlAstFormatAdapter());
        var source = """<div {{ dynamicAttrs }}>Hello</div>""";

        var document = adapter.Parse(source, "template.xml", AstSchema.Empty);

        Assert.Equal("unsafe", document.Root.Fields["$mode"]);
        Assert.Contains("no valid xml AST token context", document.Root.Fields["$unsafeReason"]);
        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void LiquidXmlAdapterPreservesAttributeOrderAcrossPlainAndTemplatedFields()
    {
        var adapter = new TemplatedHostAstFormatAdapter(new LiquidAstFormatAdapter(), new XmlAstFormatAdapter());
        var source = """<div class="x {{ dynamic }}" id="hero" title="{{ title }}">Hello</div>""";

        var document = adapter.Parse(source, "template.xml", AstSchema.Empty);

        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void LiquidXmlAdapterUsesCollisionResistantPhysicalMarkersWithoutLeakingThemIntoAstIdentity()
    {
        var adapter = new TemplatedHostAstFormatAdapter(new LiquidAstFormatAdapter(), new XmlAstFormatAdapter());
        var source = """<div data-marker="__POWERSOURCE_TEMPLATE__not-a-real-marker__" class="x {{ dynamic }}"><__ps_template id="tpl000000" /></div>""";

        var document = adapter.Parse(source, "template.xml", AstSchema.Empty);

        var host = document.Root.Children.Single(child => child.Kind == "$host").Children.Single();
        var fieldValue = host.Children.Single(child => child.Kind == "$fieldValue:class");
        var placeholder = fieldValue.Children.Single(child => child.Kind.StartsWith("$templatePlaceholder", StringComparison.Ordinal));

        Assert.Equal("tpl000000", placeholder.Fields["placeholderId"]);
        Assert.Equal("field:class/template:0", placeholder.Fields[TemplatePlaceholderFields.SemanticKey]);
        Assert.Equal("FieldValue", placeholder.Fields["context"]);
        Assert.Contains(host.Children, child => child.Kind == "__ps_template");
        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void JavaScriptAdapterValidatesWithEsprimaAndRoundTripsSource()
    {
        var adapter = new JavaScriptAstFormatAdapter();
        var source = "const answer = 42;\nfunction greet(name) { return `Hi ${name}`; }\n";

        var document = adapter.Parse(source, "script.js", AstSchema.Empty);

        Assert.Equal("javascript", document.Format);
        Assert.Equal("esprima", document.Root.Fields["parser"]);
        Assert.Equal(source, adapter.RenderDocument(document));
    }

    [Fact]
    public void NestedJsonInsideXmlIsDecodedAndReEscapedAcrossAdapters()
    {
        var schema = new AstSchema
        {
            ContentRules = new[]
            {
                new ContentRule(PathSelector.Exact("outer/payload"), "json", "payloadJson")
            },
            NestedSchemas = new Dictionary<string, AstSchema>(StringComparer.OrdinalIgnoreCase)
            {
                ["payloadJson"] = new AstSchema
                {
                    ContentRules = new[]
                    {
                        new ContentRule(PathSelector.Exact("$root/message"), "html:fragment", "messageHtml")
                    }
                },
                ["messageHtml"] = AstSchema.Empty
            }
        };
        var xml = new XmlAstFormatAdapter();
        var registry = new AstFormatRegistry(new IAstFormatAdapter[]
        {
            xml,
            new JsonAstFormatAdapter(),
            new HtmlFragmentAstFormatAdapter()
        });
        var document = xml.Parse(
            """<outer><payload>{"message":"&lt;strong&gt;Hi&lt;/strong&gt;"}</payload></outer>""",
            null,
            schema);

        var expanded = new NestedContentExpander().Expand(document, schema, registry);
        var collapsed = new NestedContentCollapser().Collapse(expanded, registry);

        var rendered = xml.RenderDocument(collapsed);
        Assert.Contains("&quot;message&quot;", rendered);
        Assert.Contains("&lt;strong&gt;Hi&lt;/strong&gt;", rendered);
    }

    [Fact]
    public void RegistryExposesAllStructuredFormats()
    {
        var registry = new AstFormatRegistry(new IAstFormatAdapter[]
        {
            new XmlAstFormatAdapter(),
            new JsonAstFormatAdapter(),
            new Json5AstFormatAdapter(),
            new YamlAstFormatAdapter(),
            new HtmlFragmentAstFormatAdapter(),
            new JavaScriptAstFormatAdapter(),
            new LiquidAstFormatAdapter(),
            new TemplatedHostAstFormatAdapter(new LiquidAstFormatAdapter(), new XmlAstFormatAdapter()),
            new TemplateTextAstFormatAdapter(),
            new RawAstFormatAdapter(),
            new CssAstFormatAdapter(),
            new PngAstFormatAdapter(),
            new JpgAstFormatAdapter(),
            new GifAstFormatAdapter(),
            new IcoAstFormatAdapter(),
            new XapAstFormatAdapter()
        }, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["svg"] = "xml",
            ["xsl"] = "xml",
            ["resx"] = "xml"
        });

        foreach (var format in new[] { "json", "json5", "yaml", "html:fragment", "javascript", "liquid:multi", "liquid:xml", "template-text", "raw", "css", "image:png", "image:jpg", "image:gif", "image:ico", "svg", "xsl", "resx", "xap" })
        {
            var source = format switch
            {
                "json" => """{"ok":true}""",
                "json5" => """{ ok: true }""",
                "yaml" => "ok: true",
                "html:fragment" => "<p>ok</p>",
                "javascript" => "const ok = true;",
                "liquid:xml" => "<root>{{ ok }}</root>",
                "svg" => "<svg />",
                "xsl" => "<xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" version=\"1.0\" />",
                "resx" => "<root />",
                _ => "ok"
            };

            Assert.True(registry.TryParse(format, source, null, AstSchema.Empty, out var document));
            Assert.Equal(format, document.Format);
            Assert.True(registry.TryRender(format, document, out var rendered));
            Assert.False(string.IsNullOrWhiteSpace(rendered));
        }
    }
}
