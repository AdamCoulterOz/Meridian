using Meridian.Core.Formats;

namespace MeridianGit.Formats.Jpeg;

public sealed class JpgAdapter : BinaryFormatAdapter
{
    public override string Format => "image:jpg";

    protected override string RootKind => "$jpg";
}
