using Meridian.Core.Formats;

namespace Meridian.Formats.Images;

public sealed class IcoAdapter : BinaryFormatAdapter
{
    public override string Format => "image:ico";

    protected override string RootKind => "$ico";
}
