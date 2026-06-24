using Meridian.Core.Formats;

namespace MeridianGit.Formats.Xap;

public sealed class XapAdapter : BinaryFormatAdapter
{
    public override string Format => "xap";

    protected override string RootKind => "$xap";
}
