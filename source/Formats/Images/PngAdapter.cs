using Meridian.Core.Formats;

namespace Meridian.Formats.Images;

public sealed class PngAdapter : BinaryFormatAdapter
{
    public override string Format => "image:png";

    protected override string RootKind => "$png";
}
