namespace Meridian.Core.Ast;

public sealed record AstDocument(
    string Format,
    AstNode Root,
    string? SourcePath = null,
    string? SourceText = null);
