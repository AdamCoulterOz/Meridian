using System.Text.Json.Nodes;
using MeridianGit.Formats.Json;

namespace MeridianGit.Formats.Json5;

public sealed class Json5Adapter : JsonAdapter
{
    public override string Format => "json5";

    protected override JsonNode? ParseJsonNode(string sourceText) => global::Json5.Json5.Parse(sourceText);
}
