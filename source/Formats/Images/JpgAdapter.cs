using Meridian.Core.Formats;

namespace Meridian.Formats.Images;

public sealed class JpgAdapter : BinaryFormatAdapter
{
    public override string Format => "image:jpg";

    protected override string RootKind => "$jpg";
}
