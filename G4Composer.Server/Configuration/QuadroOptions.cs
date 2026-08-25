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
///   "Version": "14N",
///   "ContainerNamePrefix": "q14n",
///   "TimeoutSeconds": 900,
///   "Engines": {
///     "14L": { "Image": "quadro14l:latest", "Executable": "quadro14L.exe",
///              "AlternativeExecutable": "alternatywa14L.exe" },
///     "14N": { "Image": "quadro14n:latest", "Executable": "quadro14N.exe",
///              "AlternativeExecutable": "alternatywa14N.exe" }
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
    /// Fallback dla <see cref="EngineConfig.AlternativeExecutable"/>, używany tylko wtedy,
    /// gdy aktywny silnik nie definiuje własnej alternatywy. Zostawione dla zgodności ze
    /// starymi plikami konfiguracyjnymi — nowe wpisy podawaj per-silnik.
    /// <para>
    /// UWAGA: alternatywa startuje w <b>obrazie aktywnego silnika</b>. Globalna wartość jest
    /// więc pułapką przy zmianie wersji: 2026-08-25 produkcja miała <c>Version=14L</c> i
    /// globalne <c>alternatywa14N.exe</c>, którego w obrazie 14L nie ma — przelot alternatywny
    /// wywalał się po cichu (niefatalnie), a UI od tygodnia pokazywało wyłącznie standard.
    /// Dlatego wartość należy trzymać obok obrazu, w <see cref="EngineConfig"/>.
    /// </para>
    /// </summary>
    public string? AlternativeExecutable { get; set; } = null;

    /// <summary>
    /// Maximum number of ad-hoc (non-Example) entries kept in the PDB cache. When a new
    /// ad-hoc entry would exceed this, the least-recently-accessed ad-hoc entries are evicted
    /// first. Curated Example entries are never evicted by this limit.
    /// </summary>
    public int PdbCacheMaxAdHocEntries { get; set; } = 300;

    /// <summary>Słownik konfiguracji per-wersja silnika.</summary>
    public Dictionary<string, EngineConfig> Engines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["14G"] = new EngineConfig { Image = "quadro14g:latest", Executable = "quadro14G.exe" },
        ["14L"] = new EngineConfig { Image = "quadro14l:latest", Executable = "quadro14L.exe",
                                     AlternativeExecutable = "alternatywa14L.exe" },
        ["14N"] = new EngineConfig { Image = "quadro14n:latest", Executable = "quadro14N.exe",
                                     AlternativeExecutable = "alternatywa14N.exe" },
    };

    public sealed class EngineConfig
    {
        /// <summary>Tag obrazu Docker, np. "quadro14l:latest".</summary>
        public required string Image { get; set; }

        /// <summary>Nazwa pliku wykonywalnego w obrazie, np. "quadro14L.exe".</summary>
        public required string Executable { get; set; }

        /// <summary>
        /// Nazwa binarki alternatywnego przelotu, uruchamianej równolegle w <b>tym samym
        /// obrazie</b> co <see cref="Executable"/> — dlatego musi tu być, a nie globalnie:
        /// obraz i jego alternatywa nie mogą się rozjechać przy zmianie
        /// <see cref="QuadroOptions.Version"/>. <c>null</c> = brak alternatywy dla tej wersji
        /// (wtedy używany jest globalny <see cref="QuadroOptions.AlternativeExecutable"/>).
        /// </summary>
        public string? AlternativeExecutable { get; set; }
    }
}
