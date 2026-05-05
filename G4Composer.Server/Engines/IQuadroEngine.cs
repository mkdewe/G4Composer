using System.Globalization;
using System.Text;
using G4Composer.Server.Models;

namespace G4Composer.Server.Engines;

/// <summary>
/// Kontrakt silnika Quadro w konkretnej wersji. Hermetyzuje to, co odróżnia
/// poszczególne wersje binarki: nazwę obrazu Docker, plik wykonywalny oraz
/// format pliku .inp. Dodanie nowej wersji = nowa klasa implementująca
/// ten interfejs + wpis w <c>QuadroOptions.Engines</c>.
/// </summary>
public interface IQuadroEngine
{
    /// <summary>Identyfikator wersji, np. "14G", "14L". Musi być unikalny.</summary>
    string Version { get; }

    /// <summary>Tag obrazu Docker (zaciągany z konfiguracji).</summary>
    string Image { get; }

    /// <summary>Nazwa pliku wykonywalnego w kontenerze (np. "quadro14L.exe").</summary>
    string Executable { get; }

    /// <summary>Generuje zawartość pliku .inp w formacie wymaganym przez wersję silnika.</summary>
    string SerializeInput(QuadroInput input);
}

/// <summary>
/// Bazowa implementacja zawierająca wspólny formatter pliku .inp (identyczny dla 14G/14L
/// w obecnym formacie). Klasy potomne nadpisują tylko to, co się różni.
/// </summary>
public abstract class QuadroEngineBase : IQuadroEngine
{
    public abstract string Version { get; }
    public abstract string Image { get; }
    public abstract string Executable { get; }

    public virtual string SerializeInput(QuadroInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Sequence is passed as-is — quadro14L.exe distinguishes between uppercase (RNA)
        // and lowercase (DNA). Do not normalize case here; validation ensures only valid
        // characters are accepted before reaching this point.
        var sequence = input.Sequence ?? string.Empty;

        var name      = string.IsNullOrWhiteSpace(input.Name)      ? "structure"                       : input.Name;
        var structure = string.IsNullOrWhiteSpace(input.Structure) ? BuildDefaultStructure(sequence)   : input.Structure;
        var chi       = string.IsNullOrWhiteSpace(input.Chi)       ? BuildDefaultChi(sequence)         : input.Chi;
        var orient    = string.IsNullOrWhiteSpace(input.Orient)    ? "A+;B+"                           : input.Orient;
        // Rise can be multi-step (e.g. "3.4;3.4") — write as-is; quadro14L.exe parses semicolons.
        var rise  = string.IsNullOrWhiteSpace(input.Rise) ? "3.4" : input.Rise.Trim();
        // Twist can be multi-step (e.g. "19;29") — write as-is; quadro14L.exe parses the semicolons.
        var twist = string.IsNullOrWhiteSpace(input.Twist) ? "29" : input.Twist.Trim();
        var pathStr   = input.Path is { Count: > 0 } ? string.Join(';', input.Path) : string.Empty;
        var test      = input.IsTest ? "y" : "n";
        var rmLevel   = Math.Max(0, input.RmLevel);
        var iteration = Math.Max(1, input.Iterations);

        // Field names padded with spaces to match the pz74 reference .inp format exactly.
        // quadro14L.exe is sensitive to whitespace style — tabs caused parse failures.
        var sb = new StringBuilder();
        sb.Append("name         ").Append(name).Append('\n');
        sb.Append("sequence    ").Append(sequence).Append('\n');
        sb.Append("structure    ").Append(structure).Append('\n');
        sb.Append("chi        ").Append(chi).Append('\n');
        sb.Append("orient        ").Append(orient).Append('\n');
        sb.Append("rise                ").Append(rise).Append('\n');
        sb.Append("twist        ").Append(twist).Append('\n');
        sb.Append("path        ").Append(pathStr).Append('\n');
        sb.Append("test               ").Append(test).Append('\n');
        sb.Append("rm_level           ").Append(rmLevel).Append('\n');
        sb.Append("iteration          ").Append(iteration).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Buduje domyślną strukturę z naprzemiennymi etykietami nici A/B
    /// dla każdej guaniny w sekwencji. Pozostałe pozycje to '.'.
    /// </summary>
    protected static string BuildDefaultStructure(string sequence)
    {
        var sb = new StringBuilder(sequence.Length);
        var strandIdx = 0;
        ReadOnlySpan<char> labels = ['A', 'B'];

        foreach (var c in sequence)
            sb.Append(char.ToLowerInvariant(c) == 'g' ? labels[strandIdx++ % 2] : '.');

        return sb.ToString();
    }

    /// <summary>
    /// Default chi: a dot for every position. Empty chi means "use program defaults"
    /// for sugar conformation — pz74 reference .inp uses all-dot chi and it works.
    /// </summary>
    protected static string BuildDefaultChi(string sequence)
        => new string('.', sequence.Length);


}
