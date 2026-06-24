using Meridian.Core.Formats;

namespace MeridianGit.Formats.Png;

public sealed class PngAdapter : BinaryFormatAdapter
{
    public override string Format => "image:png";

    protected override string RootKind => "$png";
}
