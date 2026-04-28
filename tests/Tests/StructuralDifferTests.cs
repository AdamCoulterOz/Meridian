using Meridian.Core.Merging;
using Meridian.Core.Schema;
using Meridian.Formats.Data;

namespace Meridian.Tests;

public sealed class StructuralDifferTests
{
    private static readonly MergeSchema DefaultSchema = new()
    {
        GlobalDiscriminatorFields = ["id", "Id", "languagecode"]
    };

    private readonly XmlAdapter _xml = new();

    [Fact]
    public void ReportsFieldValueAndChildChangesBySemanticIdentity()
    {
        var result = Diff(
            """<root><item id="1" name="Old">Before</item></root>""",
            """<root><item id="1" name="New">After</item><item id="2" name="Added" /></root>""",
            DefaultSchema);

        Assert.False(result.HasIdentityErrors);
        Assert.Contains(result.Entries, entry =>
            entry.Kind == StructuralDiffKind.FieldChanged &&
            entry.Path == "root/item[id=1]" &&
            entry.Field == "name" &&
            entry.OldValue == "Old" &&
            entry.NewValue == "New");
        Assert.Contains(result.Entries, entry =>
            entry.Kind == StructuralDiffKind.ValueChanged &&
            entry.Path == "root/item[id=1]" &&
            entry.OldValue == "Before" &&
            entry.NewValue == "After");
        Assert.Contains(result.Entries, entry =>
            entry.Kind == StructuralDiffKind.NodeAdded &&
            entry.Path == "root/item[id=2]" &&
            entry.NewText is not null &&
            entry.NewText.Contains("""<item id="2" name="Added" />""", StringComparison.Ordinal));
    }

    [Fact]
    public void IgnoresUnorderedChildReorders()
    {
        var result = Diff(
            """<root><item id="1" /><item id="2" /></root>""",
            """<root><item id="2" /><item id="1" /></root>""",
            DefaultSchema);

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void ReportsOrderedChildReordersOnlyWhenSchemaDeclaresOrder()
    {
        var schema = new MergeSchema
        {
            GlobalDiscriminatorFields = ["id"],
            OrderedChildren = [PathSelector.Exact("root")]
        };

        var result = Diff(
            """<root><item id="1" /><item id="2" /></root>""",
            """<root><item id="2" /><item id="1" /></root>""",
            schema);

        var orderChange = Assert.Single(result.Entries);
        Assert.Equal(StructuralDiffKind.OrderedChildrenChanged, orderChange.Kind);
        Assert.Equal("root", orderChange.Path);
        Assert.Equal("id=1, id=2", orderChange.OldValue);
        Assert.Equal("id=2, id=1", orderChange.NewValue);
    }

    private StructuralDiffResult Diff(string oldText, string newText, MergeSchema schema)
    {
        var oldDocument = _xml.Parse(oldText, null, schema);
        var newDocument = _xml.Parse(newText, null, schema);

        return new StructuralDiffer().Diff(oldDocument, newDocument, schema, _xml);
    }
}
