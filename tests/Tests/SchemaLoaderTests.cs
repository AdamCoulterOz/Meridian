using System.Text.Json;
using Meridian.Core.Identity;
using Meridian.Core.Schema;
using Meridian.Formats.Data;

namespace Meridian.Tests;

public sealed class SchemaLoaderTests
{
    private readonly XmlAdapter _xml = new();

    [Fact]
    public void SchemaLoaderCompilesCompanionPathFromMatchedPathRules()
    {
        var schemaSet = AstSchemaYamlLoader.Load("""
schemaVersion: 0.1
name: test
files:
  - match: WebResources/*.data.xml
    root: WebResource
    companions:
      - pathFromMatchedPath:
          removeSuffix: .data.xml
        format: raw
""");
        var schema = schemaSet.CompileForFile("WebResources/demo.data.xml", "WebResource");
        var companion = Assert.Single(schema.CompanionRules);

        Assert.Equal("WebResources/demo", companion.ResolvePath(
            _xml.Parse("<WebResource />", null, schema).Root,
            "WebResources/demo.data.xml"));
    }

    [Fact]
    public void CompanionPathFromMatchedPathFailsWhenMetadataTemplateDisagrees()
    {
        var schemaSet = AstSchemaYamlLoader.Load("""
schemaVersion: 0.1
name: test
files:
  - match: WebResources/*.data.xml
    root: WebResource
    companions:
      - pathFromMatchedPath:
          removeSuffix: .data.xml
        pathTemplate: WebResources/{WebResource/Name}
        format: raw
""");
        var schema = schemaSet.CompileForFile("WebResources/demo.data.xml", "WebResource");
        var metadata = _xml.Parse("<WebResource><Name>other</Name></WebResource>", null, schema);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Assert.NotNull(Assert.Single(schema.CompanionRules).ResolvePath(metadata.Root, "WebResources/demo.data.xml")));
        Assert.Contains("Companion path mismatch", error.Message);
    }

    [Fact]
    public void SchemaLoaderBindsCompanionFormatFromIntegerEnumKeys()
    {
        var schemaSet = AstSchemaYamlLoader.Load("""
schemaVersion: 0.1
name: test
files:
  - match: WebResources/*.data.xml
    root: WebResource
    companions:
      - pathTemplate: WebResources/{WebResource/Name}
        formatFrom:
          path: WebResource/WebResourceType
          enum:
            1: html:fragment
            3: javascript
            11: svg
        defaultFormat: raw
""");
        var schema = schemaSet.CompileForFile("WebResources/demo.data.xml", "WebResource");
        var metadata = _xml.Parse("<WebResource><Name>demo</Name><WebResourceType>3</WebResourceType></WebResource>", null, schema);
        var companion = Assert.Single(schema.CompanionRules);

        Assert.Equal("WebResources/demo", companion.ResolvePath(metadata.Root));
        Assert.Equal("javascript", companion.ResolveFormat(metadata.Root));
    }

    [Fact]
    public void AstSchemaRoundTripsPolymorphicRuntimeSchemaWithStj()
    {
        var schema = new AstSchema
        {
            IdentityRules = new[]
            {
                new NodeIdentityRule(
                    PathSelector.Exact("root/item"),
                    new DiscriminatorKey.Composite(new CompositePart[]
                    {
                        new(new DiscriminatorKey.Field("id")),
                        new(new DiscriminatorKey.PathValue("name"), Optional: true)
                    }))
            },
            CompanionRules = new[]
            {
                new CompanionRule(
                    PathTemplate: "items/{root/name}",
                    FormatFrom: new FormatFromRule(
                        "root/type",
                        new[]
                        {
                            new FormatMapEntry(new SchemaScalarValue.Integer(3), "javascript")
                        }))
            }
        };

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AstSchema>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"$type\":\"composite\"", json);
        var composite = Assert.IsType<DiscriminatorKey.Composite>(Assert.Single(roundTripped!.IdentityRules).Key);
        Assert.IsType<DiscriminatorKey.Field>(composite.Parts[0].Key);
        Assert.IsType<DiscriminatorKey.PathValue>(composite.Parts[1].Key);
        var formatEntry = Assert.Single(Assert.Single(roundTripped.CompanionRules).FormatFrom!.Enum);
        Assert.IsType<SchemaScalarValue.Integer>(formatEntry.Value);
        Assert.True(formatEntry.Value.Matches("3"));
    }

    [Fact]
    public void SchemaLoaderCompilesChildElementDiscriminatorRules()
    {
        var schemaSet = AstSchemaYamlLoader.Load("""
schemaVersion: 0.1
name: test
files:
  - match: WebResources/*.data.xml
    root: WebResources
    discriminators:
      - path: WebResources/WebResource
        key:
          element: Name
""");
        var schema = schemaSet.CompileForFile("WebResources/resources.data.xml", "WebResources");
        var document = _xml.Parse("""
<WebResources>
  <WebResource><Name>a</Name></WebResource>
  <WebResource><Name>b</Name></WebResource>
</WebResources>
""", null, schema);

        var result = new AstIdentityAssigner().Assign(document, schema);

        Assert.False(result.HasErrors);
        Assert.Contains("Name=a", result.Document.Root.Children[0].Identity);
        Assert.Contains("Name=b", result.Document.Root.Children[1].Identity);
    }

    [Fact]
    public void SchemaLoaderCompilesDescendantCompositeDiscriminatorRules()
    {
        var schemaSet = AstSchemaYamlLoader.Load("""
schemaVersion: 0.1
name: test
files:
  - match: Other/Solution.xml
    root: ImportExportXml
    discriminators:
      - path: ImportExportXml/SolutionManifest/MissingDependencies/MissingDependency
        key:
          composite:
            - path: Required/@type
            - path: Required/@schemaName
            - path: Dependent/@type
            - path: Dependent/@schemaName
""");
        var schema = schemaSet.CompileForFile("Other/Solution.xml", "ImportExportXml");
        var document = _xml.Parse("""
<ImportExportXml>
  <SolutionManifest>
    <MissingDependencies>
      <MissingDependency>
        <Required type="1" schemaName="account" />
        <Dependent type="1" schemaName="contact" />
      </MissingDependency>
      <MissingDependency>
        <Required type="1" schemaName="account" />
        <Dependent type="1" schemaName="incident" />
      </MissingDependency>
    </MissingDependencies>
  </SolutionManifest>
</ImportExportXml>
""", null, schema);

        var result = new AstIdentityAssigner().Assign(document, schema);

        Assert.False(result.HasErrors);
        Assert.Contains("Dependent/@schemaName=contact", result.Document.Root.Children[0].Children[0].Children[0].Identity);
        Assert.Contains("Dependent/@schemaName=incident", result.Document.Root.Children[0].Children[0].Children[1].Identity);
    }

}
