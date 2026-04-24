using Meridian.Core.Ast;
using Meridian.Core.Merging;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public interface IFormatAdapter : IAstTextRenderer
{
    string Format { get; }

    AstDocument Parse(string sourceText, string? sourcePath, AstSchema schema);
}
