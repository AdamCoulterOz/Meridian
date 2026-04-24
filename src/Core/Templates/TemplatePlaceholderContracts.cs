using Meridian.Core.Ast;
using Meridian.Core.Formats;
using Meridian.Core.Schema;

namespace Meridian.Core.Templates;

public enum TemplateTokenContext
{
    ChildNode,
    FieldValue
}

public sealed record TemplatePlaceholderToken(
    string Id,
    string SemanticKey,
    string Engine,
    string Kind,
    string SourceText,
    string PhysicalMarker);

public sealed record TemplatePlaceholderShape(
    string SourceText,
    TemplateTokenContext Context);

public static class TemplatePlaceholderFields
{
    public const string Engine = "$templateEngine";
    public const string HostFormat = "$hostFormat";
    public const string Mode = "$mode";
    public const string UnsafeReason = "$unsafeReason";
    public const string PlaceholderId = "placeholderId";
    public const string SemanticKey = "$semanticKey";
    public const string Context = "context";
    public const string TemplateKind = "templateKind";
    public const string MarkerPrefix = "__POWERSOURCE_TEMPLATE__";
    public const string MarkerSuffix = "__";
}

public interface ITemplateEngineAstFormatAdapter : IAstFormatAdapter
{
    string EngineName { get; }

    bool IsLiteralNode(AstNode node);

    string GetTemplateKind(AstNode node);

    string RenderTemplateNode(AstNode node);
}

public interface ITemplatePlaceholderContextTracker
{
    bool TryGetPossibleContexts(out IReadOnlyList<TemplateTokenContext> contexts, out string? unsupportedReason);

    string CreateSemanticKey(TemplateTokenContext context);

    void Feed(string literalText);
}

public interface ITemplatePlaceholderHost
{
    string HostFormat { get; }

    ITemplatePlaceholderContextTracker CreatePlaceholderContextTracker();

    bool CanRepresent(
        TemplatePlaceholderToken token,
        TemplateTokenContext context,
        out string? unsupportedReason);

    bool TryCreatePlaceholder(
        TemplatePlaceholderToken token,
        TemplateTokenContext context,
        out TemplatePlaceholderShape shape);

    AstDocument ParseHostWithPlaceholders(string sourceText, string? sourcePath, AstSchema schema);

    string RenderHostWithPlaceholders(
        AstDocument document,
        IReadOnlyDictionary<string, string> templateSourceByPlaceholderId);
}
