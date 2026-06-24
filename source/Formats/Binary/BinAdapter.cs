using Meridian.Core.Formats;

namespace MeridianGit.Formats.Binary;

public sealed class BinAdapter : BinaryFormatAdapter
{
    public override string Format => "binary";

    protected override string RootKind => "$binary";
}
