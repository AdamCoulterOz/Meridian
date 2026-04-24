using Meridian.Core.Ast;

namespace Meridian.Core.Merging;

public interface IAstTextRenderer
{
    string RenderDocument(AstDocument document);

    string RenderNode(AstNode node);
}
