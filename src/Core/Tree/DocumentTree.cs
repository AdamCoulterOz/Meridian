namespace Meridian.Core.Tree;

public sealed record DocumentTree(
    string Format,
    TreeNode Root,
    string? SourcePath = null,
    string? SourceText = null);
