using Meridian.Core.Tree;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

/// <summary>
/// A format adapter that can losslessly round-trip raw bytes. Binary formats
/// (images, packages) implement this so callers such as the Git integration can
/// read and write file content as bytes instead of decoding it as text, which
/// would corrupt non-UTF-8 content.
/// </summary>
public interface IBinaryFormatAdapter : IFormatAdapter
{
    DocumentTree ParseBytes(ReadOnlyMemory<byte> content, string? sourcePath, MergeSchema schema);

    byte[] RenderDocumentBytes(DocumentTree document);
}
