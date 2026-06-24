using Meridian.Core.Formats;

namespace MeridianGit.Formats.Ico;

public sealed class IcoAdapter : BinaryFormatAdapter
{
    public override string Format => "image:ico";

    protected override string RootKind => "$ico";
}
