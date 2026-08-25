using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Engines;

namespace G4Composer.Server.Tests.Engines;

/// <summary>
/// Alternatywa startuje w obrazie aktywnego silnika, więc jej nazwa musi pochodzić z tego
/// samego wpisu konfiguracji co obraz. Regresja z 2026-08-25: produkcja miała
/// <c>Version=14L</c> i globalne <c>alternatywa14N.exe</c>, którego w obrazie 14L nie ma —
/// przelot alternatywny wywalał się niefatalnie i UI pokazywało wyłącznie model standardowy.
/// </summary>
public class EngineAlternativeExecutableTests
{
    private static IOptions<QuadroOptions> Opts(QuadroOptions o) => Options.Create(o);

    [Fact]
    public void Defaults_PairEachEngineWithItsOwnAlternative()
    {
        var options = Opts(new QuadroOptions());

        var l = new Quadro14LEngine(options);
        var n = new Quadro14NEngine(options);

        Assert.Equal("alternatywa14L.exe", l.AlternativeExecutable);
        Assert.Equal("alternatywa14N.exe", n.AlternativeExecutable);
        Assert.Equal("quadro14l:latest", l.Image);
        Assert.Equal("quadro14n:latest", n.Image);
    }

    [Fact]
    public void PerEngineValue_BeatsGlobalOne()
    {
        // Dokładnie feralna konfiguracja: globalna alternatywa wskazuje na 14N, a pytamy o 14L.
        var options = Opts(new QuadroOptions { AlternativeExecutable = "alternatywa14N.exe" });

        var engine = new Quadro14LEngine(options);

        Assert.Equal("alternatywa14L.exe", engine.AlternativeExecutable);
    }

    [Fact]
    public void GlobalValue_IsUsedOnlyWhenEngineHasNone()
    {
        var options = new QuadroOptions { AlternativeExecutable = "stary_globalny.exe" };
        options.Engines["14L"] = new QuadroOptions.EngineConfig
        {
            Image = "quadro14l:latest",
            Executable = "quadro14L.exe",
            // AlternativeExecutable celowo nieustawione — stary config sprzed rozdzielenia.
        };

        var engine = new Quadro14LEngine(Opts(options));

        Assert.Equal("stary_globalny.exe", engine.AlternativeExecutable);
    }

    [Fact]
    public void NoAlternativeAnywhere_MeansSinglePassMode()
    {
        var options = new QuadroOptions();
        options.Engines["14G"] = new QuadroOptions.EngineConfig
        {
            Image = "quadro14g:latest",
            Executable = "quadro14G.exe",
        };

        var engine = new Quadro14GEngine(Opts(options));

        Assert.Null(engine.AlternativeExecutable);
    }

    [Fact]
    public void MissingEngineEntry_FailsLoudly()
    {
        var options = new QuadroOptions();
        options.Engines.Remove("14N");

        var ex = Assert.Throws<InvalidOperationException>(() => new Quadro14NEngine(Opts(options)));

        Assert.Contains("14N", ex.Message);
        Assert.Contains("Engines", ex.Message);
    }
}
