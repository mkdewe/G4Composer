using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;

namespace G4Composer.Server.Engines;

/// <summary>
/// Wybiera aktywny silnik Quadro na podstawie <see cref="QuadroOptions.Version"/>.
/// Dzięki temu kontroler i job runner nie wiedzą nic o konkretnych wersjach.
/// </summary>
public interface IQuadroEngineSelector
{
    /// <summary>Zwraca aktywny engine (zgodny z konfiguracją).</summary>
    IQuadroEngine Active { get; }

    /// <summary>Zwraca engine konkretnej wersji lub rzuca, gdy nie jest zarejestrowana.</summary>
    IQuadroEngine GetByVersion(string version);
}

public sealed class QuadroEngineSelector : IQuadroEngineSelector
{
    private readonly IReadOnlyDictionary<string, IQuadroEngine> _engines;
    private readonly string _activeVersion;

    public QuadroEngineSelector(IEnumerable<IQuadroEngine> engines, IOptions<QuadroOptions> options)
    {
        _engines = engines.ToDictionary(e => e.Version, StringComparer.OrdinalIgnoreCase);
        _activeVersion = options.Value.Version;

        if (!_engines.ContainsKey(_activeVersion))
            throw new InvalidOperationException(
                $"Configured Quadro version '{_activeVersion}' has no registered IQuadroEngine. " +
                $"Available: {string.Join(", ", _engines.Keys)}.");
    }

    public IQuadroEngine Active => _engines[_activeVersion];

    public IQuadroEngine GetByVersion(string version) =>
        _engines.TryGetValue(version, out var engine)
            ? engine
            : throw new KeyNotFoundException($"Quadro engine version '{version}' is not registered.");
}
