using Meridian.Core.Ast;
using Meridian.Core.Schema;

namespace Meridian.Core.Formats;

public sealed class FormatRegistry : IFormatRegistry
{
    private readonly Dictionary<string, IFormatAdapter> _adapters;
    private readonly Dictionary<string, string> _aliases;

    public FormatRegistry(
        IEnumerable<IFormatAdapter> adapters,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToDictionary(adapter => adapter.Format, StringComparer.OrdinalIgnoreCase);
        _aliases = aliases?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase) ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryResolveAdapterFormat(string format, out string adapterFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var current = format;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            if (_adapters.ContainsKey(current))
            {
                adapterFormat = current;
                return true;
            }

            if (!seen.Add(current))
                throw new InvalidOperationException($"Format alias cycle detected while resolving '{format}'.");

            if (!_aliases.TryGetValue(current, out var next))
            {
                adapterFormat = string.Empty;
                return false;
            }

            if (string.IsNullOrWhiteSpace(next))
                throw new InvalidOperationException($"Format alias '{current}' resolves to an empty adapter format.");

            current = next;
        }
    }

    public bool TryParse(string format, string sourceText, string? sourcePath, AstSchema schema, out AstDocument document)
    {
        if (TryResolveAdapterFormat(format, out var adapterFormat) &&
            _adapters.TryGetValue(adapterFormat, out var adapter))
        {
            var parsed = adapter.Parse(sourceText, sourcePath, schema);
            document = string.Equals(parsed.Format, format, StringComparison.OrdinalIgnoreCase)
                ? parsed
                : parsed with { Format = format };
            return true;
        }

        document = null!;
        return false;
    }

    public bool TryRender(string format, AstDocument document, out string sourceText)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (TryResolveAdapterFormat(format, out var adapterFormat) &&
            _adapters.TryGetValue(adapterFormat, out var adapter))
        {
            sourceText = adapter.RenderNode(document.Root);
            return true;
        }

        sourceText = null!;
        return false;
    }
}
