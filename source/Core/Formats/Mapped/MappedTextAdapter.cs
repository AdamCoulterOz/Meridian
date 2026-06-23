namespace Meridian.Core.Formats.Mapped;

public sealed class MappedTextAdapter : OpaqueTextAdapter
{
    public override string Format => "mapped-text";

    protected override string RootKind => "$mapped";
}
