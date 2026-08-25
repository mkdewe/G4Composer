using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;
using G4Composer.Server.Services;
using G4Composer.Server.Validation;

namespace G4Composer.Server;

/// <summary>
/// Rejestracja warstwy Quadro w kontenerze DI. Pojedyncze miejsce do rozszerzania
/// — by dodać nową wersję silnika, dorzuć kolejny <c>AddSingleton&lt;IQuadroEngine, ...&gt;</c>.
/// </summary>
public static class QuadroServiceCollectionExtensions
{
    public static IServiceCollection AddQuadro(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Konfiguracja ────────────────────────────────────────────────────
        services
            .AddOptions<QuadroOptions>()
            .Bind(configuration.GetSection(QuadroOptions.SectionName))
            .ValidateOnStart();

        // ── Engines (każda nowa wersja = jedna linijka) ─────────────────────
        services.AddSingleton<IQuadroEngine, Quadro14GEngine>();
        services.AddSingleton<IQuadroEngine, Quadro14LEngine>();
        services.AddSingleton<IQuadroEngine, Quadro14NEngine>();
        services.AddSingleton<IQuadroEngineSelector, QuadroEngineSelector>();

        // ── Walidacja ───────────────────────────────────────────────────────
        services.AddSingleton<IValidator<QuadroInput>, QuadroInputValidator>();

        // ── Docker / job runner ─────────────────────────────────────────────
        services.AddSingleton<IDockerCommandRunner, DockerCommandRunner>();
        services.AddSingleton<IDockerHealthService, DockerHealthService>();
        services.AddSingleton<IJobLogStore, InMemoryJobLogStore>();
        services.AddSingleton<IAltPdbStore, InMemoryAltPdbStore>();
        services.AddSingleton<IFrameStore, InMemoryFrameStore>();
        services.AddSingleton<IQuadroJobRunner, QuadroJobRunner>();

        // Scoped — depends on AppDbContext (scoped).
        services.AddScoped<IPdbCacheService, PdbCacheService>();

        return services;
    }
}
