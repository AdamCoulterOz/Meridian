using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

        var set = JsonSerializer.Deserialize<MergeSchemaSet>(json, MergeSchemaJson.Options) ??
            throw new InvalidOperationException("Schema YAML could not be converted into a merge schema set.");

        ValidateRequiredFields(set);
        return NormalizeComparers(set);
    }

    private static void ValidateRequiredFields(MergeSchemaSet set)
    {
        var files = set.Files ?? [];
        for (var index = 0; index < files.Count; index++)
            if (string.IsNullOrWhiteSpace(files[index].Match))
                throw new InvalidOperationException(
                    $"Schema file rule #{index + 1} is missing its required 'match' pattern.");
    }

    // System.Text.Json materializes IReadOnlyDictionary as ordinal-comparer dictionaries, discarding
    // the OrdinalIgnoreCase defaults declared on the records. Rebuild them so a schemaRef resolves
    // case-insensitively regardless of whether the schema was YAML-loaded or built programmatically.
    private static MergeSchemaSet NormalizeComparers(MergeSchemaSet set) => set with
    {
        Defaults = NormalizeSchema(set.Defaults ?? MergeSchema.Empty),
        NestedSchemas = NormalizeSchemaMap(set.NestedSchemas),
        FormatAliases = ToIgnoreCaseMap(set.FormatAliases)
    };

    private static MergeSchema NormalizeSchema(MergeSchema schema) => schema with
    {
        NestedSchemas = NormalizeSchemaMap(schema.NestedSchemas)
    };

    private static IReadOnlyDictionary<string, MergeSchema> NormalizeSchemaMap(IReadOnlyDictionary<string, MergeSchema>? map)
    {
        var result = new Dictionary<string, MergeSchema>(StringComparer.OrdinalIgnoreCase);
        if (map is not null)
            foreach (var pair in map)
                result[pair.Key] = NormalizeSchema(pair.Value);
        return result;
    }

    private static IReadOnlyDictionary<string, string> ToIgnoreCaseMap(IReadOnlyDictionary<string, string>? map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map is not null)
            foreach (var pair in map)
                result[pair.Key] = pair.Value;
        return result;
    }

    private static object ParseSchemaYaml(string yaml, string displayName)
    {
        var yamlObject = YamlDeserializer.Deserialize<object>(yaml) ??
            throw new InvalidOperationException($"Schema YAML document '{displayName}' must contain a root mapping.");

        if (yamlObject is not IDictionary<object, object> && yamlObject is not IDictionary<string, object>)
            throw new InvalidOperationException($"Schema YAML document '{displayName}' must contain a root mapping.");

        return yamlObject;
    }

    // Nearer (overlay) non-mapping values replace earlier ones and mapping keys merge recursively —
    // the documented discovery/include composition semantics (see CONTEXT.md). Rule lists therefore
    // replace rather than concatenate; a schema that wants to extend ancestor rules restates them.
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

    private static HttpClient CreateRemoteHttpClient() => new(new SocketsHttpHandler
    {
        // Disable redirects so a benign-looking URL cannot 30x-redirect to an internal target, and
        // validate the actual connection endpoint (defeating DNS rebinding) before connecting.
        AllowAutoRedirect = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var endPoint = context.DnsEndPoint;
            var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);
            var target = Array.Find(addresses, address => !IsBlockedAddress(address))
                ?? throw new InvalidOperationException(
                    $"Remote schema host '{endPoint.Host}' resolves only to blocked (private/loopback/link-local/metadata) addresses.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, endPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // Blocks addresses that must never be reachable via an attacker-controlled schema include:
    // loopback, link-local (incl. the 169.254.169.254 cloud metadata endpoint), private, CGNAT,
    // unique-local and multicast/reserved ranges.
    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            return IsBlockedAddress(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 127 => true,                                   // "this network" / loopback
                10 => true,                                         // 10.0.0.0/8
                169 when bytes[1] == 254 => true,                   // 169.254.0.0/16 (incl. metadata)
                172 when bytes[1] is >= 16 and <= 31 => true,       // 172.16.0.0/12
                192 when bytes[1] == 168 => true,                   // 192.168.0.0/16
                100 when bytes[1] is >= 64 and <= 127 => true,      // 100.64.0.0/10 (CGNAT)
                >= 224 => true,                                     // multicast / reserved
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.Equals(IPAddress.IPv6Any))
                return true;

            var bytes = address.GetAddressBytes();

            // IPv4-compatible IPv6 (::a.b.c.d, first 96 bits zero) and NAT64 (64:ff9b::/96) embed an
            // IPv4 target in the low 32 bits; re-check that embedded address so a blocked IPv4 (e.g.
            // the 169.254.169.254 metadata endpoint) cannot be reached through an IPv6 encoding.
            var first96Zero = true;
            for (var index = 0; index < 12 && first96Zero; index++)
                first96Zero = bytes[index] == 0;

            var isNat64 = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
                && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0
                && bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0 && bytes[11] == 0;

            if (first96Zero || isNat64)
                return IsBlockedAddress(new IPAddress(bytes[12..16]));

            // Unique-local addresses fc00::/7.
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

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

            if (_activeDocuments.Count >= options.MaxIncludeDepth)
                throw new InvalidOperationException(
                    $"Schema include depth exceeded {options.MaxIncludeDepth}. This bounds resource use against maliciously deep include chains.");

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
                    EnsureIncludeWithinRoot(includeReference);
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

        // Confines local includes to the repository being merged. Without this, an attacker-committed
        // schema could `includes: ["../../../../etc/passwd"]` to read out-of-tree files (or point at a
        // special file such as /dev/zero to hang the merge).
        private void EnsureIncludeWithinRoot(SchemaDocumentReference reference)
        {
            if (options.RepositoryRoot is null || reference is not LocalSchemaDocumentReference local)
                return;

            var root = Path.GetFullPath(options.RepositoryRoot);
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            // GetFullPath normalises ".." but does NOT resolve symlinks, and Git can commit a
            // symlink. Resolve the final target first, or a committed link inside the repo passes
            // containment and the read then follows it out of tree.
            var full = ResolveFinalTarget(Path.GetFullPath(local.Path));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!full.StartsWith(rootWithSeparator, comparison) &&
                !string.Equals(full, root, comparison))
                throw new InvalidOperationException(
                    $"Schema include '{local.Path}' resolves outside the repository root '{root}'.");
        }

        private static string ResolveFinalTarget(string path)
        {
            try
            {
                var resolved = File.ResolveLinkTarget(path, returnFinalTarget: true)
                    ?? (FileSystemInfo?)Directory.ResolveLinkTarget(path, returnFinalTarget: true);
                return resolved is null ? path : Path.GetFullPath(resolved.FullName);
            }
            catch (IOException)
            {
                return path;
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
                if (options.RemoteSchemaFetcher is not null)
                    yaml = options.RemoteSchemaFetcher(uri);
                else if (options.AllowRemoteSchemas)
                    yaml = FetchRemoteSchema(uri);
                else
                    throw new InvalidOperationException(
                        "Remote schema includes are disabled by default because a merge/diff driver runs " +
                        "automatically on untrusted branches. Enable them explicitly " +
                        "(MERIDIANGIT_ALLOW_REMOTE_SCHEMAS=1) only for repositories you trust.");
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
                absoluteUri.Scheme == Uri.UriSchemeHttps)
                return new RemoteSchemaDocumentReference(absoluteUri);

            // Reject plaintext HTTP: an on-path attacker could tamper with merge rules in transit.
            if (Uri.TryCreate(include, UriKind.Absolute, out var httpUri) && httpUri.Scheme == Uri.UriSchemeHttp)
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' must use HTTPS, not plaintext HTTP.");

            if (Uri.TryCreate(include, UriKind.Absolute, out var unsupportedUri) &&
                !string.IsNullOrWhiteSpace(unsupportedUri.Scheme))
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' must be a relative path or an HTTPS URL.");

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

            if (resolved.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"Schema include '{include}' in '{DisplayName}' must resolve to an HTTPS URL.");

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
    /// <summary>Test/host hook that supplies remote schema text without any network access.</summary>
    public Func<Uri, string>? RemoteSchemaFetcher { get; init; }

    /// <summary>
    /// Whether remote (HTTPS) schema includes may be fetched over the network. Off by default: a
    /// merge/diff driver runs automatically on untrusted branches, so an attacker-committed schema
    /// must not be able to make outbound requests unless the host explicitly opts in.
    /// </summary>
    public bool AllowRemoteSchemas { get; init; }

    /// <summary>When set, local includes must resolve inside this directory (repository root).</summary>
    public string? RepositoryRoot { get; init; }

    /// <summary>Maximum include/nesting depth before resolution fails, bounding stack use.</summary>
    public int MaxIncludeDepth { get; init; } = 64;
}

public sealed record MergeSchemaLoadResult(
    MergeSchemaSet SchemaSet,
    IReadOnlyList<RemoteSchemaLoad> RemoteSchemas);

public sealed record RemoteSchemaLoad(
    string Uri,
    bool IsPinnedToGitCommitSha);
