using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;

namespace G4Composer.Server.Services;

/// <summary>
/// Wynik jednorazowej weryfikacji konfiguracji silnika na starcie procesu.
/// Czytany przez endpoint <c>/health</c>, żeby nie odpalać kontenera przy każdym zapytaniu.
/// </summary>
public sealed class QuadroReadiness
{
    public bool Completed { get; internal set; }
    public bool DockerAvailable { get; internal set; }
    public bool ImageExists { get; internal set; }

    /// <summary>Binarka alternatywy wg konfiguracji aktywnego silnika (może być null).</summary>
    public string? AlternativeExecutable { get; internal set; }

    /// <summary>Czy ta binarka faktycznie jest w obrazie aktywnego silnika.</summary>
    public bool AlternativeAvailable { get; internal set; }

    /// <summary>Opis pierwszego wykrytego problemu, albo null gdy wszystko gra.</summary>
    public string? Problem { get; internal set; }
}

/// <summary>
/// Sprawdza raz, przy starcie, że konfiguracja silnika jest wykonalna: obraz aktywnej wersji
/// istnieje, a binarki standardu i alternatywy są w nim obecne.
/// <para>
/// Powód istnienia: 2026-08-25 produkcja przez tydzień mielił standardowy przelot i po cichu
/// gubiła alternatywę, bo <c>appsettings.Production.json</c> przypinał <c>Version=14L</c>, a
/// nazwa alternatywy pochodziła z bazowego configu i wskazywała <c>alternatywa14N.exe</c>,
/// którego w obrazie 14L nie ma. Awaria alternatywy jest z założenia niefatalna (runner łapie
/// wyjątek), więc jedynym objawem był brak drugiego modelu w UI. Ten test zamienia to w jedną
/// głośną linię w logu przy starcie.
/// </para>
/// <para>
/// Świadomie <b>nie</b> wywala procesu: gdy Docker akurat wstaje po reboocie, padnięcie
/// serwisu byłoby gorsze niż praca w trybie ograniczonym. Stan ląduje w
/// <see cref="QuadroReadiness"/> i jest widoczny w <c>/health</c>.
/// </para>
/// </summary>
public sealed class QuadroReadinessCheck : BackgroundService
{
    private readonly IQuadroEngineSelector _engines;
    private readonly IDockerHealthService  _docker;
    private readonly QuadroOptions         _options;
    private readonly QuadroReadiness       _state;
    private readonly ILogger<QuadroReadinessCheck> _logger;

    public QuadroReadinessCheck(
        IQuadroEngineSelector engines, IDockerHealthService docker,
        IOptions<QuadroOptions> options, QuadroReadiness state,
        ILogger<QuadroReadinessCheck> logger)
    {
        _engines = engines;
        _docker  = docker;
        _options = options.Value;
        _state   = state;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CheckAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown w trakcie sprawdzania — nic do zgłoszenia.
        }
        catch (Exception ex)
        {
            _state.Problem = ex.Message;
            _logger.LogError(ex, "Weryfikacja konfiguracji silnika Quadro nie doszła do skutku");
        }
        finally
        {
            _state.Completed = true;
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        var engine = _engines.Active;
        _state.AlternativeExecutable = engine.AlternativeExecutable;

        _state.DockerAvailable = await _docker.IsDockerAvailableAsync(ct);
        if (!_state.DockerAvailable)
        {
            Fail("Docker jest niedostępny — żaden przelot się nie uda.");
            return;
        }

        _state.ImageExists = await _docker.ImageExistsAsync(engine.Image, ct);
        if (!_state.ImageExists)
        {
            Fail($"Obraz '{engine.Image}' dla wersji {engine.Version} nie istnieje. " +
                 $"Zbuduj go albo popraw Quadro:Engines:{engine.Version}:Image.");
            return;
        }

        var workDir = _options.ContainerWorkDirectory.TrimEnd('/');

        if (!await _docker.ExecutableExistsAsync(engine.Image, $"{workDir}/{engine.Executable}", ct))
        {
            Fail($"Obraz '{engine.Image}' nie zawiera wykonywalnego {workDir}/{engine.Executable}. " +
                 $"Quadro:Version={engine.Version} nie pasuje do tego obrazu.");
            return;
        }

        if (engine.AlternativeExecutable is null)
        {
            _logger.LogInformation(
                "Quadro {Version} ({Image}): brak skonfigurowanej alternatywy — tryb jednoprzelotowy.",
                engine.Version, engine.Image);
            return;
        }

        _state.AlternativeAvailable = await _docker.ExecutableExistsAsync(
            engine.Image, $"{workDir}/{engine.AlternativeExecutable}", ct);

        if (!_state.AlternativeAvailable)
        {
            Fail($"Obraz '{engine.Image}' nie zawiera wykonywalnego " +
                 $"{workDir}/{engine.AlternativeExecutable}. Przelot alternatywny będzie się " +
                 $"wywalał niefatalnie, a UI pokaże wyłącznie model standardowy. Ustaw " +
                 $"Quadro:Engines:{engine.Version}:AlternativeExecutable na binarkę obecną w " +
                 $"tym obrazie (albo na null, jeśli ta wersja nie ma alternatywy).");
            return;
        }

        _logger.LogInformation(
            "Quadro {Version} gotowe: {Image} zawiera {Std} i {Alt}.",
            engine.Version, engine.Image, engine.Executable, engine.AlternativeExecutable);
    }

    private void Fail(string problem)
    {
        _state.Problem = problem;
        _logger.LogError("Konfiguracja silnika Quadro: {Problem}", problem);
    }
}
