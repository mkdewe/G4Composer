using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;

namespace G4Composer.Server.Engines;

/// <summary>Legacy engine — quadro14G.exe.</summary>
public sealed class Quadro14GEngine : QuadroEngineBase
{
    public const string VersionId = "14G";

    private readonly QuadroOptions.EngineConfig _config;

    public Quadro14GEngine(IOptions<QuadroOptions> options)
    {
        if (!options.Value.Engines.TryGetValue(VersionId, out var cfg))
            throw new InvalidOperationException(
                $"Missing configuration for engine version '{VersionId}' " +
                $"in section '{QuadroOptions.SectionName}.Engines'.");
        _config = cfg;
    }

    public override string Version    => VersionId;
    public override string Image      => _config.Image;
    public override string Executable => _config.Executable;
}

/// <summary>
/// Nowy engine — quadro14L.exe. Obecnie format pliku .inp jest identyczny
/// jak w 14G (dziedziczone z bazy). Gdy 14L zacznie wymagać innych pól,
/// wystarczy nadpisać <see cref="QuadroEngineBase.SerializeInput"/>
/// — reszta kodu pozostaje bez zmian.
/// </summary>
public sealed class Quadro14LEngine : QuadroEngineBase
{
    public const string VersionId = "14L";

    private readonly QuadroOptions.EngineConfig _config;

    public Quadro14LEngine(IOptions<QuadroOptions> options)
    {
        if (!options.Value.Engines.TryGetValue(VersionId, out var cfg))
            throw new InvalidOperationException(
                $"Missing configuration for engine version '{VersionId}' " +
                $"in section '{QuadroOptions.SectionName}.Engines'.");
        _config = cfg;
    }

    public override string Version    => VersionId;
    public override string Image      => _config.Image;
    public override string Executable => _config.Executable;

    // public override string SerializeInput(QuadroInput input)
    // {
    //     // TODO: gdy 14L zmieni format .inp — implementuj tu i nie wołaj base.
    //     // Np. dodatkowe pole / zmieniona kolejność. Reszta architektury bez zmian.
    //     return base.SerializeInput(input);
    // }
}
