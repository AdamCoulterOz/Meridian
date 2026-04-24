using System.Text.Json.Nodes;

namespace Meridian.Formats.Data;

public sealed class Json5AstFormatAdapter : JsonAstFormatAdapter
{
    public override string Format => "json5";

    protected override JsonNode? ParseJsonNode(string sourceText)
    {
        return global::Json5.Json5.Parse(sourceText);
    }
}
