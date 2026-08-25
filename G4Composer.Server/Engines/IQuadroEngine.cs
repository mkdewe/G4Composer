using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
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

    /// <summary>
    /// Binarka alternatywnego przelotu uruchamiana równolegle w <see cref="Image"/>.
    /// <c>null</c> = ta wersja nie ma alternatywy. Trzymana przy silniku, bo dzieli z nim
    /// obraz — globalna wartość rozjeżdżała się z <c>Version</c> przy zmianie wersji.
    /// </summary>
    string? AlternativeExecutable { get; }

    /// <summary>Generuje zawartość pliku .inp w formacie wymaganym przez wersję silnika.</summary>
    string SerializeInput(QuadroInput input);

    /// <summary>
    /// Rozkłada jedną strukturę na fizyczne uruchomienia silnika.
    /// <para>
    /// 14G/14L: jedno uruchomienie — <c>iteration_steps</c> każe binarce zrobić drabinkę
    /// checkpointów wewnątrz jednego przebiegu CYANA.
    /// </para>
    /// <para>
    /// 14N: nie zna <c>iteration_steps</c>. Zamiast checkpointów robimy N niezależnych
    /// przelotów, po jednym na wartość z <see cref="QuadroInput.IterationSteps"/> —
    /// każdy z własną fazą budowy, więc każdy daje inny model fizyczny (a nie migawkę
    /// wzdłuż jednej trajektorii, jak checkpointy).
    /// </para>
    /// </summary>
    /// <param name="baseFileName">Nazwa bazowa bez rozszerzenia, np. <c>struct_000</c>.</param>
    IReadOnlyList<QuadroPass> SerializePasses(QuadroInput input, string baseFileName);
}

/// <summary>
/// Jedno fizyczne wywołanie binarki: plik .inp do zapisania i liczba iteracji, którą koduje.
/// <paramref name="Step"/> służy tylko do logowania i raportowania postępu — numery kroków
/// przypisane klatkom biorą się z nazw plików PDB znalezionych po przebiegu.
/// </summary>
/// <param name="ExpectedCyanaStages">
/// Szacowana liczba etapów budowy CYANA, do raportowania postępu. Silnik dobudowuje strukturę
/// resztę po reszcie w kolejności z <c>path</c> i przy każdym etapie wypisuje
/// <c>N angle constraints added.</c> — ale zależnie od struktury etapów bywa o jeden więcej niż
/// pozycji w <c>path</c> (sprawdzone na 70 przykładach: 24 razy +1, reszta dokładnie), więc to
/// tylko punkt startowy. Runner kalibruje się dokładną liczbą po pierwszym przelocie.
/// 0 = brak szacunku.
/// </param>
public sealed record QuadroPass(int Step, string FileName, string Content, int ExpectedCyanaStages = 0);

/// <summary>
/// Bazowa implementacja zawierająca wspólny formatter pliku .inp (identyczny dla 14G/14L
/// w obecnym formacie). Klasy potomne nadpisują tylko to, co się różni.
/// </summary>
public abstract class QuadroEngineBase : IQuadroEngine
{
    private readonly QuadroOptions.EngineConfig _config;
    private readonly string? _globalAlternative;

    /// <param name="versionId">
    /// Klucz w <c>Quadro.Engines</c>. Podawany jawnie, bo <see cref="Version"/> nie jest jeszcze
    /// dostępne w konstruktorze klasy bazowej.
    /// </param>
    protected QuadroEngineBase(IOptions<QuadroOptions> options, string versionId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var opts = options.Value;
        if (!opts.Engines.TryGetValue(versionId, out var cfg))
            throw new InvalidOperationException(
                $"Missing configuration for engine version '{versionId}' " +
                $"in section '{QuadroOptions.SectionName}.Engines'.");

        _config            = cfg;
        _globalAlternative = opts.AlternativeExecutable;
        Version            = versionId;
    }

    public string Version { get; }
    public string Image => _config.Image;
    public string Executable => _config.Executable;

    /// <summary>
    /// Per-silnik, z globalnym ustawieniem jako fallback dla starych configów. Kolejność jest
    /// istotna: wpis przy silniku musi wygrać, bo to on jedzie w parze z <see cref="Image"/>.
    /// </summary>
    public string? AlternativeExecutable => _config.AlternativeExecutable ?? _globalAlternative;

    public virtual string SerializeInput(QuadroInput input)
        => BuildInp(input, ResolveName(input), IterationLine(input));

    /// <summary>
    /// Domyślnie jedno uruchomienie na strukturę — binarka sama rozwija <c>iteration_steps</c>
    /// w drabinkę checkpointów. 14N nadpisuje to N przelotami.
    /// </summary>
    public virtual IReadOnlyList<QuadroPass> SerializePasses(QuadroInput input, string baseFileName)
        => [new QuadroPass(ResolveSteps(input).Max(), $"{baseFileName}.inp", SerializeInput(input),
                           EstimateCyanaStages(input))];

    /// <summary>
    /// Punkt startowy dla paska postępu: ile etapów budowy CYANA się spodziewamy.
    /// Jeden na pozycję w <c>path</c>; runner koryguje to obserwacją.
    /// </summary>
    protected static int EstimateCyanaStages(QuadroInput input) => input.Path?.Count ?? 0;

    /// <summary>
    /// Linia .inp sterująca głębokością minimalizacji. 14G/14L: drabinka checkpointów
    /// (<c>iteration_steps</c>). 14N: pojedyncza wartość (<c>iteration</c>).
    /// </summary>
    protected virtual string IterationLine(QuadroInput input)
        => "iteration_steps    " + string.Join(',', ResolveSteps(input)) + "\n";

    protected static string ResolveName(QuadroInput input)
        => string.IsNullOrWhiteSpace(input.Name) ? "structure" : input.Name;

    protected static int[] ResolveSteps(QuadroInput input)
        => input.IterationSteps is { Length: > 0 } ? input.IterationSteps : [100];

    /// <summary>
    /// Wspólny formatter .inp. <paramref name="name"/> i <paramref name="iterationLine"/>
    /// są parametrami, bo 14N generuje jeden plik na przelot — każdy z inną nazwą
    /// (<c>&lt;name&gt;_&lt;K&gt;</c>, żeby PDB-ki się nie nadpisywały) i inną wartością iteration.
    /// </summary>
    protected static string BuildInp(QuadroInput input, string name, string iterationLine)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Sequence is passed as-is — the engine distinguishes between uppercase (RNA)
        // and lowercase (DNA). Do not normalize case here; validation ensures only valid
        // characters are accepted before reaching this point.
        var sequence = input.Sequence ?? string.Empty;

        var structure = string.IsNullOrWhiteSpace(input.Structure) ? BuildDefaultStructure(sequence)   : input.Structure;
        var chi       = string.IsNullOrWhiteSpace(input.Chi)    ? BuildDefaultChi(sequence)    : input.Chi;
        // Sugar: per-residue sugar pucker (N=North/RNA, S=South/DNA, .=default).
        // Auto-generated from sequence case when empty: UPPERCASE→N, lowercase→S.
        var sugar     = string.IsNullOrWhiteSpace(input.Sugar) ? BuildDefaultSugar(sequence) : input.Sugar;
        var orient    = string.IsNullOrWhiteSpace(input.Orient)    ? "A+;B+"                           : input.Orient;
        // Rise can be multi-step (e.g. "3.4;3.4") — write as-is; quadro14L.exe parses semicolons.
        var rise  = string.IsNullOrWhiteSpace(input.Rise) ? "3.4" : input.Rise.Trim();
        // Twist can be multi-step (e.g. "19;29") — write as-is; quadro14L.exe parses the semicolons.
        var twist = string.IsNullOrWhiteSpace(input.Twist) ? "29" : input.Twist.Trim();
        var pathStr   = input.Path is { Count: > 0 } ? string.Join(';', input.Path) : string.Empty;
        const string test    = "n";
        // rm_level only controls cleanup of quadro14L's intermediate work files —
        // it does not affect geometry or energy. Honour the input (default 5).
        var rmLevel = input.RmLevel;

        // Field names padded with spaces to match the pz74 reference .inp format exactly.
        // quadro14L.exe is sensitive to whitespace style — tabs caused parse failures.
        var sb = new StringBuilder();
        sb.Append("name         ").Append(name).Append('\n');
        sb.Append("sequence    ").Append(sequence).Append('\n');
        sb.Append("structure    ").Append(structure).Append('\n');
        sb.Append("chi        ").Append(chi).Append('\n');
        sb.Append("sugar      ").Append(sugar).Append('\n');
        sb.Append("orient        ").Append(orient).Append('\n');
        sb.Append("rise                ").Append(rise).Append('\n');
        sb.Append("twist        ").Append(twist).Append('\n');
        sb.Append("path        ").Append(pathStr).Append('\n');
        sb.Append("test               ").Append(test).Append('\n');
        sb.Append("rm_level           ").Append(rmLevel).Append('\n');
        sb.Append(iterationLine);
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

    /// <summary>
    /// Default sugar: derived from sequence case — uppercase residue → N (North/RNA),
    /// lowercase residue → S (South/DNA). Mixed sequences get per-residue assignment.
    /// </summary>
    protected static string BuildDefaultSugar(string sequence)
    {
        var sb = new StringBuilder(sequence.Length);
        foreach (var c in sequence)
            sb.Append(char.IsUpper(c) ? 'N' : 'S');
        return sb.ToString();
    }
}
