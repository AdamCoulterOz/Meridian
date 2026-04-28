using System.Text.Json;
using Meridian.Core.Identity;
using Meridian.Core.Schema;
using Meridian.Formats.Data;

namespace Meridian.Tests;

public sealed class SchemaLoaderTests
{
    private readonly XmlAdapter _xml = new();

    [Fact]
    public void MeridianSchemaJsonSchemaIsValidJson()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schemas", "meridian.schema.json"));
        var json = File.ReadAllText(path);
        AssertNoDuplicateJsonObjectKeys(json);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Meridian Merge Schema", document.RootElement.GetProperty("title").GetString());
        Assert.True(document.RootElement.GetProperty("$defs").TryGetProperty("nodeIdentityRule", out _));
    }

    private static void AssertNoDuplicateJsonObjectKeys(string json)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        var objectProperties = new Stack<HashSet<string>>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                objectProperties.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString() ?? string.Empty;
                Assert.True(objectProperties.Peek().Add(propertyName), $"Duplicate JSON property '{propertyName}'.");
            }
        }
    }

    [Fact]
    public void SchemaLoaderCompilesCompanionPathFromMatchedPathRules()
    {
        var schemaSet = MergeSchemaYamlLoader.Load("""
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
    public void SchemaDiscoveryFindsMeridianSchemasFromRepositoryRootToFileDirectory()
    {
        var root = CreateTemporaryRepository();
        Directory.CreateDirectory(Path.Combine(root, "area", "sub"));
        File.WriteAllText(Path.Combine(root, "root.meridian.yaml"), "name: root");
        File.WriteAllText(Path.Combine(root, "area", "area.meridian.yaml"), "name: area");
        File.WriteAllText(Path.Combine(root, "area", "sub", "z.meridian.yaml"), "name: z");
        File.WriteAllText(Path.Combine(root, "area", "sub", "a.meridian.yaml"), "name: a");

        var result = MergeSchemaDiscovery.DiscoverForFile("area/sub/file.xml", root);

        Assert.Equal(root, result.RepositoryRoot);
        Assert.Equal(
            [
                "root.meridian.yaml",
                "area/area.meridian.yaml",
                "area/sub/a.meridian.yaml",
                "area/sub/z.meridian.yaml"
            ],
            result.SchemaFiles.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray());
    }

    [Fact]
    public void SchemaLoaderOverlaysSchemaFilesByRecursiveMappingKey()
    {
        var root = CreateTemporaryRepository();
        var child = Path.Combine(root, "App");
        Directory.CreateDirectory(child);
        var rootSchema = Path.Combine(root, "root.meridian.yaml");
        var childSchema = Path.Combine(child, "app.meridian.yaml");
        File.WriteAllText(rootSchema, """
schemaVersion: 0.1
name: root
defaults:
  globalDiscriminatorFields:
    - id
  orderedChildren:
    - root/baseOrder
nestedSchemas:
  payload:
    contentRules:
      - path: payload/name
        format: plain
files:
  - match: "**/*.xml"
    discriminators:
      - path: root/item
        key:
          attribute: id
""");
        File.WriteAllText(childSchema, """
schemaVersion: 0.1
name: child
defaults:
  globalDiscriminatorFields:
    - sku
nestedSchemas:
  payload:
    orderedChildren:
      - payload/items
files:
  - match: App/*.xml
    discriminators:
      - path: root/item
        key:
          attribute: sku
""");

        var schemaSet = MergeSchemaYamlLoader.LoadFiles([rootSchema, childSchema]);
        var schema = schemaSet.CompileForFile("App/product.xml");

        Assert.Equal("child", schemaSet.Name);
        Assert.Equal(["sku"], schema.GlobalDiscriminatorFields);
        Assert.Equal("root/baseOrder", Assert.Single(schema.OrderedChildren).Pattern);

        var nested = schema.NestedSchemas["payload"];
        Assert.Equal("payload/name", Assert.Single(nested.ContentRules).Path.Pattern);
        Assert.Equal("payload/items", Assert.Single(nested.OrderedChildren).Pattern);

        var identityRule = Assert.Single(schema.IdentityRules);
        Assert.Equal("root/item", identityRule.Path.Pattern);
        var field = Assert.IsType<DiscriminatorKey.Field>(identityRule.Key);
        Assert.Equal("sku", field.Name);
    }

    [Fact]
    public void CompanionPathFromMatchedPathFailsWhenMetadataTemplateDisagrees()
    {
        var schemaSet = MergeSchemaYamlLoader.Load("""
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
        var schemaSet = MergeSchemaYamlLoader.Load("""
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
    public void MergeSchemaRoundTripsPolymorphicRuntimeSchemaWithStj()
    {
        var schema = new MergeSchema
        {
            IdentityRules =
            [
                new NodeIdentityRule(
                    PathSelector.Exact("root/item"),
                    new DiscriminatorKey.Composite(
                    [
                        new(new DiscriminatorKey.Field("id")),
                        new(new DiscriminatorKey.PathValue("name"), Optional: true)
                    ]))
            ],
            CompanionRules =
            [
                new CompanionRule(
                    PathTemplate: "items/{root/name}",
                    FormatFrom: new FormatFromRule(
                        "root/type",
                        [
                            new FormatMapEntry(new SchemaScalarValue.Integer(3), "javascript")
                        ]))
            ]
        };

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<MergeSchema>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

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
        var schemaSet = MergeSchemaYamlLoader.Load("""
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

        var result = new IdentityAssigner().Assign(document, schema);

        Assert.False(result.HasErrors);
        Assert.Contains("Name=a", result.Document.Root.Children[0].Identity);
        Assert.Contains("Name=b", result.Document.Root.Children[1].Identity);
    }

    [Fact]
    public void SchemaLoaderCompilesDescendantCompositeDiscriminatorRules()
    {
        var schemaSet = MergeSchemaYamlLoader.Load("""
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

        var result = new IdentityAssigner().Assign(document, schema);

        Assert.False(result.HasErrors);
        Assert.Contains("Dependent/@schemaName=contact", result.Document.Root.Children[0].Children[0].Children[0].Identity);
        Assert.Contains("Dependent/@schemaName=incident", result.Document.Root.Children[0].Children[0].Children[1].Identity);
    }

    private static string CreateTemporaryRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        return root;
    }
}
