using Meridian.Core.Formats;

namespace Meridian.Formats.Images;

public sealed class GifAdapter : BinaryFormatAdapter
{
    public override string Format => "image:gif";

    protected override string RootKind => "$gif";
}
