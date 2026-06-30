namespace G4Composer.Server.Configuration;

/// <summary>
/// Konfiguracja silnika Quadro. Spina wszystkie wartości, które wcześniej były
/// rozsiane jako stałe w kontrolerze (nazwa obrazu, wersja, timeouty, prefiks
/// kontenera, nazwa pliku wykonywalnego). Bindowane z sekcji <c>"Quadro"</c>
/// pliku appsettings.json.
/// </summary>
/// <example>
/// appsettings.json:
/// <code>
/// "Quadro": {
///   "Version": "14L",
///   "ContainerNamePrefix": "q14l",
///   "TimeoutSeconds": 300,
///   "Engines": {
///     "14G": { "Image": "quadro14g:latest", "Executable": "quadro14G.exe" },
///     "14L": { "Image": "quadro14l:latest", "Executable": "quadro14L.exe" }
///   }
/// }
/// </code>
/// </example>
public sealed class QuadroOptions
{
    public const string SectionName = "Quadro";

    /// <summary>Aktywna wersja silnika (klucz słownika <see cref="Engines"/>).</summary>
    public string Version { get; set; } = "14L";

    /// <summary>Prefiks używany w nazwach kontenerów Docker (np. "q14l").</summary>
    public string ContainerNamePrefix { get; set; } = "q14l";

    /// <summary>Limit czasu wykonania pojedynczego joba (sekundy).</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Maksymalna liczba gotowych kandydatów .inp z ONQuadro (--g4composer-output-dir),
    /// które przepuszczamy przez quadro (wg rankingu alignera, od najlepszego). Najniższa
    /// energia wygrywa. Większa wartość = dokładniej, ale wolniej (każdy to osobna minimalizacja).
    /// </summary>
    public int OnquadroCandidateLimit { get; set; } = 8;

    /// <summary>Katalog wewnątrz kontenera, w którym wykonuje się quadroXX.exe.</summary>
    public string ContainerWorkDirectory { get; set; } = "/opt/bin";

    /// <summary>Katalog wewnątrz kontenera podmontowywany z hosta (mount target).</summary>
    public string ContainerDataDirectory { get; set; } = "/data";

    /// <summary>
    /// Executable name of the alternative engine run in parallel alongside the standard one.
    /// Set to null to disable dual-run mode. Example: "alternatywa14L.exe".
    /// The alternative engine uses the same Docker image as the active engine.
    /// </summary>
    public string? AlternativeExecutable { get; set; } = null;

    /// <summary>Słownik konfiguracji per-wersja silnika.</summary>
    public Dictionary<string, EngineConfig> Engines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["14G"] = new EngineConfig { Image = "quadro14g:latest", Executable = "quadro14G.exe" },
        ["14L"] = new EngineConfig { Image = "quadro14l:latest", Executable = "quadro14L.exe" },
    };

    public sealed class EngineConfig
    {
        /// <summary>Tag obrazu Docker, np. "quadro14l:latest".</summary>
        public required string Image { get; set; }

        /// <summary>Nazwa pliku wykonywalnego w obrazie, np. "quadro14L.exe".</summary>
        public required string Executable { get; set; }
    }
}
