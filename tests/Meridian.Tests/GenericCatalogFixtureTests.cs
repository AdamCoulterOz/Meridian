using Meridian.Core.Identity;
using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Formats.Xml;

namespace Meridian.Tests;

public sealed class GenericCatalogFixtureTests
{
    [Fact]
    public void GenericCatalogFixtureMergesSchemaDrivenXml()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenericCatalog");
        var schema = AstSchemaYamlLoader
            .LoadFile(Path.Combine(fixturePath, "catalog.schema.yaml"))
            .CompileForFile("catalog.xml", "Catalog");
        var xml = new XmlAstFormatAdapter();

        var result = new AstMerger().Merge(
            xml.Parse(ReadFixture(fixturePath, "base.xml"), "catalog.xml", schema),
            xml.Parse(ReadFixture(fixturePath, "ours.xml"), "catalog.xml", schema),
            xml.Parse(ReadFixture(fixturePath, "theirs.xml"), "catalog.xml", schema),
            schema,
            xml);

        Assert.False(result.HasConflicts);
        Assert.DoesNotContain(result.IdentityDiagnostics, diagnostic => diagnostic.Severity == IdentityDiagnosticSeverity.Error);
        Assert.Equal(
            Normalize(ReadFixture(fixturePath, "expected.xml")),
            Normalize(xml.RenderDocument(result.Document)));
    }

    private static string ReadFixture(string fixturePath, string name)
    {
        return File.ReadAllText(Path.Combine(fixturePath, name));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }
}
