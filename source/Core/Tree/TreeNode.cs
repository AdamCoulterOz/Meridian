using Meridian.Core.Merging;

namespace Meridian.Core.Tree;

public sealed record TreeNode
{
    public TreeNode(
        string kind,
        IReadOnlyDictionary<string, string>? fields = null,
        string? value = null,
        IReadOnlyList<TreeNode>? children = null,
        string? path = null,
        string? identity = null,
        string? sourceText = null,
        MergeConflict? conflict = null)
    {
        Kind = kind;
        Fields = fields ?? EmptyFields;
        Value = value;
        Children = children ?? EmptyChildren;
        Path = path;
        Identity = identity;
        SourceText = sourceText;
        Conflict = conflict;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyFields = new Dictionary<string, string>();
    private static readonly IReadOnlyList<TreeNode> EmptyChildren = [];

    public string Kind { get; }

    public IReadOnlyDictionary<string, string> Fields { get; init; }

    public string? Value { get; init; }

    public IReadOnlyList<TreeNode> Children { get; init; }

    public string? Path { get; init; }

    public string? Identity { get; init; }

    public string? SourceText { get; }

    public MergeConflict? Conflict { get; init; }

    public bool IsConflict => Conflict is not null;

    public TreeNode WithChildren(IReadOnlyList<TreeNode> children) => this with { Children = children };

    public TreeNode WithFields(IReadOnlyDictionary<string, string> fields) => this with { Fields = fields };

    public TreeNode WithValue(string? value) => this with { Value = value };

    public TreeNode WithPathAndIdentity(string path, string identity) => this with { Path = path, Identity = identity };

    public static TreeNode ConflictNode(string kind, string path, string identity, MergeConflict conflict) => new(kind, path: path, identity: identity, conflict: conflict);
}
