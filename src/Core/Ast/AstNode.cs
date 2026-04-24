namespace Meridian.Core.Ast;

public sealed record AstNode
{
    public AstNode(
        string kind,
        IReadOnlyDictionary<string, string>? fields = null,
        string? value = null,
        IReadOnlyList<AstNode>? children = null,
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
    private static readonly IReadOnlyList<AstNode> EmptyChildren = [];

    public string Kind { get; }

    public IReadOnlyDictionary<string, string> Fields { get; init; }

    public string? Value { get; init; }

    public IReadOnlyList<AstNode> Children { get; init; }

    public string? Path { get; init; }

    public string? Identity { get; init; }

    public string? SourceText { get; }

    public MergeConflict? Conflict { get; init; }

    public bool IsConflict => Conflict is not null;

    public AstNode WithChildren(IReadOnlyList<AstNode> children) => this with { Children = children };

    public AstNode WithFields(IReadOnlyDictionary<string, string> fields) => this with { Fields = fields };

    public AstNode WithValue(string? value) => this with { Value = value };

    public AstNode WithPathAndIdentity(string path, string identity) => this with { Path = path, Identity = identity };

    public static AstNode ConflictNode(string kind, string path, string identity, MergeConflict conflict) => new AstNode(kind, path: path, identity: identity, conflict: conflict);
}
