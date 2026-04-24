using Meridian.Core.Tree;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public interface IFormatAdapter : ITextRenderer
{
    string Format { get; }

    DocumentTree Parse(string sourceText, string? sourcePath, MergeSchema schema);
}
