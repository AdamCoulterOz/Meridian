using System.Net.Http.Headers;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace Meridian.Core.Schema;

public static class MergeSchemaYamlLoader
{
    public const string RemoteSchemaCachePolicy = "per load operation in-memory cache by exact URL; no persistent cache; HTTP requests send Cache-Control: no-cache";

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly HttpClient RemoteHttpClient = CreateRemoteHttpClient();

    public static MergeSchemaSet Load(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var yamlObject = ToJsonCompatible(ParseSchemaYaml(yaml, "inline schema"));
        if (ReadIncludeReferences(yamlObject, "inline schema").Count > 0)
            throw new InvalidOperationException("Inline schema YAML cannot resolve includes. Use LoadFile or LoadFiles for schemas that compose other documents.");

        return DeserializeSchemaSet(yamlObject);
    }

    public static MergeSchemaSet LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFileWithDiagnostics(path).SchemaSet;
    }

    public static MergeSchemaSet LoadFiles(IEnumerable<string> paths)
    {
        return LoadFilesWithDiagnostics(paths).SchemaSet;
    }

    public static MergeSchemaLoadResult LoadFileWithDiagnostics(string path, MergeSchemaLoadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFilesWithDiagnostics([path], options);
    }

    public static MergeSchemaLoadResult LoadFilesWithDiagnostics(IEnumerable<string> paths, MergeSchemaLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var context = new SchemaLoadContext(options ?? new MergeSchemaLoadOptions());
        object? merged = null;
        var count = 0;

        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var reference = SchemaDocumentReference.FromLocalPath(path);
            merged = MergeValues(merged, context.Resolve(reference));
            count++;
        }

        if (count == 0)
            throw new InvalidOperationException("At least one schema YAML file is required.");

        return new MergeSchemaLoadResult(
            DeserializeSchemaSet(merged),
            context.RemoteSchemas);
    }

    private static MergeSchemaSet DeserializeSchemaSet(object? jsonCompatible)
    {
        var json = JsonSerializer.Serialize(jsonCompatible, MergeSchemaJson.Options);

        return JsonSerializer.Deserialize<MergeSchemaSet>(json, MergeSchemaJson.Options) ??
            throw new InvalidOperationException("Schema YAML could not be converted into a merge schema set.");
    }

    private static object ParseSchemaYaml(string yaml, string displayName)
    {
        var yamlObject = YamlDeserializer.Deserialize<object>(yaml) ??
            throw new InvalidOperationException($"Schema YAML document '{displayName}' must contain a root mapping.");

        if (yamlObject is not IDictionary<object, object> && yamlObject is not IDictionary<string, object>)
            throw new InvalidOperationException($"Schema YAML document '{displayName}' must contain a root mapping.");

        return yamlObject;
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

    private static IReadOnlyList<string> ReadIncludeReferences(object? value, string displayName)
    {
        var map = AsMap(value);
        if (map is null)
            return [];

        var includes = new List<string>();
        ReadIncludeReferenceList(map, "includes", displayName, includes);
        ReadIncludeReferenceList(map, "references", displayName, includes);
        return includes;
    }

    private static void ReadIncludeReferenceList(IDictionary<string, object?> map, string key, string displayName, List<string> includes)
    {
        if (!map.TryGetValue(key, out var includeValue))
            return;

        if (includeValue is not IEnumerable<object?> sequence || includeValue is string)
            throw new InvalidOperationException($"Schema document '{displayName}' property '{key}' must be a list of include paths or URLs.");

        foreach (var item in sequence)
        {
            if (item is not string include || string.IsNullOrWhiteSpace(include))
                throw new InvalidOperationException($"Schema document '{displayName}' property '{key}' must contain only non-empty string include paths or URLs.");

            includes.Add(include);
        }
    }

    private static void RemoveIncludeReferences(object? value)
    {
        var map = AsMap(value);
        if (map is null)
            return;

        map.Remove("includes");
        map.Remove("references");
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

    private static HttpClient CreateRemoteHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string FetchRemoteSchema(Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.UserAgent.ParseAdd("MeridianSchemaLoader/0.1");

        using var response = RemoteHttpClient.Send(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Remote schema '{uri.AbsoluteUri}' returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private sealed class SchemaLoadContext(MergeSchemaLoadOptions options)
    {
        private readonly Dictionary<string, object?> _resolvedDocuments = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _remoteYaml = new(StringComparer.Ordinal);
        private readonly List<RemoteSchemaLoad> _remoteSchemas = [];
        private readonly Stack<string> _activeDocuments = new();
        private readonly HashSet<string> _activeDocumentKeys = new(StringComparer.Ordinal);

        public IReadOnlyList<RemoteSchemaLoad> RemoteSchemas => _remoteSchemas;

        public object? Resolve(SchemaDocumentReference reference)
        {
            if (_resolvedDocuments.TryGetValue(reference.Key, out var cachedDocument))
                return cachedDocument;

            if (!_activeDocumentKeys.Add(reference.Key))
            {
                var cycle = _activeDocuments.Reverse().Concat([reference.DisplayName]);
                throw new InvalidOperationException("Schema include cycle detected: " + string.Join(" -> ", cycle));
            }

            _activeDocuments.Push(reference.DisplayName);
            try
            {
                var yaml = ReadSchemaText(reference);
                var yamlObject = ToJsonCompatible(ParseSchemaYaml(yaml, reference.DisplayName));
                var includes = ReadIncludeReferences(yamlObject, reference.DisplayName);

                object? merged = null;
                foreach (var include in includes)
                {
                    var includeReference = reference.ResolveInclude(include);
                    merged = MergeValues(merged, Resolve(includeReference));
                }

                RemoveIncludeReferences(yamlObject);
                merged = MergeValues(merged, yamlObject);
                _resolvedDocuments.Add(reference.Key, merged);
                return merged;
            }
            finally
            {
                _activeDocuments.Pop();
                _activeDocumentKeys.Remove(reference.Key);
            }
        }

        private string ReadSchemaText(SchemaDocumentReference reference)
        {
            if (reference is LocalSchemaDocumentReference local)
            {
                if (!File.Exists(local.Path))
                    throw new InvalidOperationException($"Schema file '{local.Path}' does not exist.");

                return File.ReadAllText(local.Path);
            }

            if (reference is RemoteSchemaDocumentReference remote)
                return ReadRemoteSchemaText(remote.Uri);

            throw new InvalidOperationException("Unsupported schema document reference.");
        }

        private string ReadRemoteSchemaText(Uri uri)
        {
            var key = uri.AbsoluteUri;
            if (_remoteYaml.TryGetValue(key, out var cachedYaml))
                return cachedYaml;

            string yaml;
            try
            {
                yaml = options.RemoteSchemaFetcher is null
                    ? FetchRemoteSchema(uri)
                    : options.RemoteSchemaFetcher(uri);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Remote schema '{key}' could not be fetched: {error.Message}", error);
            }

            _remoteYaml.Add(key, yaml);
            _remoteSchemas.Add(new RemoteSchemaLoad(key, IsPinnedToGitCommitSha(uri)));
            return yaml;
        }
    }

    private abstract record SchemaDocumentReference(string Key, string DisplayName)
    {
        public static SchemaDocumentReference FromLocalPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return new LocalSchemaDocumentReference(fullPath);
        }

        public SchemaDocumentReference ResolveInclude(string include)
        {
            if (Uri.TryCreate(include, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
                return new RemoteSchemaDocumentReference(absoluteUri);

            if (Uri.TryCreate(include, UriKind.Absolute, out var unsupportedUri) &&
                !string.IsNullOrWhiteSpace(unsupportedUri.Scheme))
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' must be a relative path or an HTTP/HTTPS URL.");

            if (include.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' is not a valid path.");

            return ResolveRelativeInclude(include);
        }

        protected abstract SchemaDocumentReference ResolveRelativeInclude(string include);
    }

    private sealed record LocalSchemaDocumentReference(string Path)
        : SchemaDocumentReference("file:" + System.IO.Path.GetFullPath(Path), System.IO.Path.GetFullPath(Path))
    {
        protected override SchemaDocumentReference ResolveRelativeInclude(string include)
        {
            if (System.IO.Path.IsPathRooted(include))
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' must be relative, not rooted.");

            var baseDirectory = System.IO.Path.GetDirectoryName(Path) ??
                throw new InvalidOperationException($"Schema file '{Path}' has no containing directory.");
            return FromLocalPath(System.IO.Path.Combine(baseDirectory, include));
        }
    }

    private sealed record RemoteSchemaDocumentReference(Uri Uri)
        : SchemaDocumentReference("url:" + Uri.AbsoluteUri, Uri.AbsoluteUri)
    {
        protected override SchemaDocumentReference ResolveRelativeInclude(string include)
        {
            if (!Uri.TryCreate(Uri, include, out var resolved))
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' is not a valid relative URL.");

            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' must resolve to an HTTP/HTTPS URL.");

            return new RemoteSchemaDocumentReference(resolved);
        }
    }

    private static bool IsPinnedToGitCommitSha(Uri uri)
    {
        if (!string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(IsGitCommitSha);
    }

    private static bool IsGitCommitSha(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);
}

public sealed record MergeSchemaLoadOptions
{
    public Func<Uri, string>? RemoteSchemaFetcher { get; init; }
}

public sealed record MergeSchemaLoadResult(
    MergeSchemaSet SchemaSet,
    IReadOnlyList<RemoteSchemaLoad> RemoteSchemas);

public sealed record RemoteSchemaLoad(
    string Uri,
    bool IsPinnedToGitCommitSha);
