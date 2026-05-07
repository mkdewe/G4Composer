using System.Text.Json.Serialization;

namespace G4Composer.Server.Models;

/// <summary>
/// Wejście dla obliczeń Quadro (14G / 14L). Format pliku .inp jest generowany
/// przez odpowiedni <see cref="Engines.IQuadroEngine"/> wybierany na podstawie
/// konfiguracji aplikacji.
/// </summary>
public class QuadroInput
{
    /// <summary>Nazwa struktury (używana w polu <c>name</c> pliku .inp).</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Sekwencja nukleotydowa. Wymagane małe litery (np. "ggttgg...").
    /// Quadro rozróżnia małe 't' (tymidyna RNA) od wielkiego 'T' (T3, tymidyna DNA);
    /// duże 'T' powoduje błąd parsera, dlatego walidator je odrzuca.
    /// </summary>
    public required string Sequence { get; set; }

    /// <summary>Struktura w notacji dot-bracket / etykiet nici, np. "AB..BA...AB..BA".</summary>
    public string? Structure { get; set; }

    /// <summary>Konformacja cukru na pozycjach G, np. "S...S....S...S.".</summary>
    public string? Chi { get; set; }

    /// <summary>
    /// Sugar pucker per residue — same length as Sequence. Each character:
    ///   N / n = North (C3'-endo, RNA), S / s = South (C2'-endo, DNA), . = default.
    /// If empty, quadro14L.exe uses its own defaults (all-dot).
    /// </summary>
    public string Sugar { get; set; } = "";

    /// <summary>Orientacja nici, np. "A+;B-".</summary>
    public string? Orient { get; set; }

    /// <summary>Helical rise in Å. Multi-step e.g. "3.4;3.4" for 3 tetrads (N-1 values).</summary>
    public string Rise { get; set; } = "3.4";

    /// <summary>Kąt skrętu helisy w stopniach. Może być wielokrokowy, np. "19;29" dla różnych przejść między tetradami.</summary>
    public string Twist { get; set; } = "29";

    /// <summary>Ścieżka tetrad, np. ["A1","B1","B4","A4","A3","B3","B2","A2"].</summary>
    public List<string>? Path { get; set; }

    /// <summary>
    /// Tryb testowy generatora (mapowany na pole <c>test</c> w .inp jako "y"/"n").
    /// </summary>
    [JsonPropertyName("isTest")] // zgodność wsteczna z istniejącym kontraktem JSON
    public bool IsTest { get; set; } = false;

    /// <summary>Poziom RMSD (pole <c>rm_level</c>).</summary>
    [JsonPropertyName("RM_Level")] // zgodność wsteczna z istniejącym kontraktem JSON
    public int RmLevel { get; set; } = 0;

    /// <summary>Liczba iteracji CYANA (pole <c>iteration</c>).</summary>
    public int Iterations { get; set; } = 100;
}
