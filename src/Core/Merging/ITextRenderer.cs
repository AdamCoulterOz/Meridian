using Meridian.Core.Tree;

namespace Meridian.Core.Merging;

public interface ITextRenderer
{
    string RenderDocument(DocumentTree document);

    string RenderNode(TreeNode node);
}
