using Meridian.Core.Ast;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public interface IAstFormatRegistry
{
    bool TryResolveAdapterFormat(string format, out string adapterFormat);

    bool TryParse(string format, string sourceText, string? sourcePath, AstSchema schema, out AstDocument document);

    bool TryRender(string format, AstDocument document, out string sourceText);
}
