using Meridian.Core.Identity;
using Meridian.Core.Schema;
using Meridian.Formats.Data;

namespace Meridian.Tests;

public sealed class SchemaLoaderTests
{
    private readonly XmlAstFormatAdapter _xml = new();

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
