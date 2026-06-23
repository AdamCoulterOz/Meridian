namespace Meridian.Core.Formats;

public sealed class RawAdapter : OpaqueTextAdapter
{
    public override string Format => "raw";

    protected override string RootKind => "$raw";
}
