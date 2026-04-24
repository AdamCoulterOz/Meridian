using System.Text.Json.Nodes;
using Meridian.Formats.Json;

namespace Meridian.Formats.Json5;

public sealed class Json5AstFormatAdapter : JsonAstFormatAdapter
{
    public override string Format => "json5";

    protected override JsonNode? ParseJsonNode(string sourceText)
    {
        return global::Json5.Json5.Parse(sourceText);
    }
}
