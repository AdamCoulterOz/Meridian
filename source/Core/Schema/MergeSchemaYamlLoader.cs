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

        return DeserializeSchemaSet(ToJsonCompatible(yamlObject));
    }

    public static MergeSchemaSet LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    public static MergeSchemaSet LoadFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        object? merged = null;
        var count = 0;

        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var yamlObject = YamlDeserializer.Deserialize<object>(File.ReadAllText(path)) ??
                throw new InvalidOperationException($"Schema YAML file '{path}' must contain a root mapping.");

            merged = MergeValues(merged, ToJsonCompatible(yamlObject));
            count++;
        }

        if (count == 0)
            throw new InvalidOperationException("At least one schema YAML file is required.");

        return DeserializeSchemaSet(merged);
    }

    private static MergeSchemaSet DeserializeSchemaSet(object? jsonCompatible)
    {
        var json = JsonSerializer.Serialize(jsonCompatible, MergeSchemaJson.Options);

        return JsonSerializer.Deserialize<MergeSchemaSet>(json, MergeSchemaJson.Options) ??
            throw new InvalidOperationException("Schema YAML could not be converted into a merge schema set.");
    }

    private static object? MergeValues(object? baseValue, object? overlayValue)
    {
        var baseMap = AsMap(baseValue);
        var overlayMap = AsMap(overlayValue);

        if (baseMap is null || overlayMap is null)
            return overlayValue;

        var merged = new Dictionary<string, object?>(baseMap, StringComparer.Ordinal);
        foreach (var pair in overlayMap)
            merged[pair.Key] = merged.TryGetValue(pair.Key, out var existing)
                ? MergeValues(existing, pair.Value)
                : pair.Value;

        return merged;
    }

    private static IDictionary<string, object?>? AsMap(object? value) => value as IDictionary<string, object?>;

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
