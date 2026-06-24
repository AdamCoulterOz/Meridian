using Meridian.Core.Formats;

namespace MeridianGit.Formats.Gif;

public sealed class GifAdapter : BinaryFormatAdapter
{
    public override string Format => "image:gif";

    protected override string RootKind => "$gif";
}
