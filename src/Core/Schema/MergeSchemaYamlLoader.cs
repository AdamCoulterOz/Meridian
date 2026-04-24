using System.Text.Json;
using YamlDotNet.Serialization;

namespace Meridian.Core.Schema;

public static class MergeSchemaYamlLoader
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

    public static MergeSchemaSet Load(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var yamlObject = YamlDeserializer.Deserialize<object>(yaml) ??
            throw new InvalidOperationException("Schema YAML must contain a root mapping.");
        var json = JsonSerializer.Serialize(ToJsonCompatible(yamlObject), MergeSchemaJson.Options);

        return JsonSerializer.Deserialize<MergeSchemaSet>(json, MergeSchemaJson.Options) ??
            throw new InvalidOperationException("Schema YAML could not be converted into a merge schema set.");
    }

    public static MergeSchemaSet LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static object? ToJsonCompatible(object? value) => value switch
    {
        null => null,
        IDictionary<object, object> map => map.ToDictionary(
            pair => Convert.ToString(pair.Key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            pair => ToJsonCompatible(pair.Value),
            StringComparer.Ordinal),
        IDictionary<string, object> map => map.ToDictionary(
            pair => pair.Key,
            pair => ToJsonCompatible(pair.Value),
            StringComparer.Ordinal),
        IEnumerable<object> sequence when value is not string => sequence.Select(ToJsonCompatible).ToArray(),
        _ => value
    };
}
