using Meridian.Core.Tree;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public interface IFormatRegistry
{
    bool TryResolveAdapterFormat(string format, out string adapterFormat);

    bool TryParse(string format, string sourceText, string? sourcePath, MergeSchema schema, out DocumentTree document);

    bool TryRender(string format, DocumentTree document, out string sourceText);
}
