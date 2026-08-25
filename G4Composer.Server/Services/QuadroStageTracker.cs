namespace G4Composer.Server.Services;

/// <summary>Gdzie w obrębie jednego przelotu silnika jesteśmy.</summary>
/// <param name="Stage">Stabilny identyfikator etapu (dla UI/logiki, nie do wyświetlania).</param>
/// <param name="Label">Tekst dla użytkownika.</param>
/// <param name="Fraction">Postęp w obrębie przelotu, 0..1.</param>
/// <param name="CyanaStages">Ile etapów budowy CYANA już zaobserwowano (do kalibracji).</param>
public sealed record QuadroStage(string Stage, string Label, double Fraction, int CyanaStages);

/// <summary>
/// Wyciąga etap przetwarzania z surowego wyjścia silnika.
/// <para>
/// quadro nie raportuje postępu — ale CYANA i Xplor-NIH wypisują regularne markery, które
/// jednoznacznie wyznaczają etap. Zweryfikowane na 70 przykładach z bazy (420 przebiegów):
/// </para>
/// <list type="bullet">
///   <item><c>N angle constraints added.</c> — jeden na każdy etap budowy CYANA (reszta po
///     reszcie, w kolejności z <c>path</c>). Ich liczba to ~<c>path.Count</c>, ale zależnie od
///     struktury bywa o 1 większa, więc totalu nie da się wyliczyć z góry — stąd kalibracja.</item>
///   <item><c>PDB coordinate file "final_cyana.pdb" written.</c> — koniec fazy CYANA</item>
///   <item><c>X-PLOR&gt;topology</c> — budowa topologii (t_xplor1.inp)</item>
///   <item><c>POWELL: number of degrees of freedom</c> — start minimalizacji kartezjańskiej;
///     pierwsza z zamrożonym rdzeniem tetrad, druga z pełnym rozluźnieniem</item>
///   <item><c>cycle=   600</c> — co 100 kroków z 1000 (nprint=100), postęp wewnątrz minimalizacji</item>
///   <item><c>libp= N</c> — diagnostyka wypisywana przez 14N na samym końcu</item>
/// </list>
/// </summary>
public static class QuadroStageTracker
{
    /// <summary>nstep w t_xplor2.inp — obie minimalizacje Powella są zaszyte na 1000.</summary>
    private const int PowellSteps = 1000;

    // Granice etapów w obrębie przelotu. Wagi z pomiaru: faza CYANA to zwykle ~60% czasu.
    private const double BuildStart   = 0.02, BuildEnd   = 0.55;
    private const double CyanaDone    = 0.60;
    private const double TopologyDone = 0.68;
    private const double Powell1Start = 0.70, Powell1End = 0.83;
    private const double Powell2Start = 0.85, Powell2End = 0.97;

    /// <summary>
    /// Etap na podstawie dotychczasowego wyjścia.
    /// </summary>
    /// <param name="log">Wyjście silnika zebrane do tej pory (może być ucięte w połowie linii).</param>
    /// <param name="expectedCyanaStages">
    /// Spodziewana liczba etapów budowy. Pierwszy przelot dostaje szacunek z <c>path.Count</c>;
    /// kolejne — dokładną liczbę z przelotu poprzedniego. &lt;= 0 oznacza brak szacunku.
    /// </param>
    public static QuadroStage Parse(string? log, int expectedCyanaStages)
    {
        if (string.IsNullOrEmpty(log))
            return new QuadroStage("starting", "Starting engine", 0.0, 0);

        var cyanaStages = 0;   // "angle constraints added."
        var powellStarts = 0;  // "POWELL: number of degrees of freedom"
        var powellEnds = 0;    // "POWELL: STEP number limit"
        var lastCycle = 0;     // ostatnie "cycle=   NNN" w bieżącej minimalizacji
        var cyanaFinished = false;
        var topologyStarted = false;
        var engineFinished = false;

        foreach (var line in log.Split('\n'))
        {
            if (line.Contains("angle constraints added.", StringComparison.Ordinal))
                cyanaStages++;
            else if (line.Contains("final_cyana.pdb\" written", StringComparison.Ordinal))
                cyanaFinished = true;
            else if (line.Contains("X-PLOR>topology", StringComparison.Ordinal))
                topologyStarted = true;
            else if (line.Contains("POWELL: number of degrees of freedom", StringComparison.Ordinal))
            {
                powellStarts++;
                lastCycle = 0;
            }
            else if (line.Contains("POWELL: STEP number limit", StringComparison.Ordinal))
                powellEnds++;
            else if (line.StartsWith("libp=", StringComparison.Ordinal))
                engineFinished = true;
            else
            {
                var c = ParseCycle(line);
                if (c > 0) lastCycle = c;
            }
        }

        if (engineFinished)
            return new QuadroStage("finished", "Engine finished", 1.0, cyanaStages);

        // Druga minimalizacja Xplora — wszystko rozluźnione, z więzami płaskości/NOE.
        if (powellStarts >= 2)
        {
            var f = powellEnds >= 2 ? 1.0 : CycleFraction(lastCycle);
            return new QuadroStage("refining-full",
                $"Xplor refinement — full relaxation ({Pct(f)}%)",
                Lerp(Powell2Start, Powell2End, f), cyanaStages);
        }

        // Pierwsza minimalizacja Xplora — rdzeń tetrad zamrożony.
        if (powellStarts == 1)
        {
            var f = powellEnds >= 1 ? 1.0 : CycleFraction(lastCycle);
            return new QuadroStage("refining-core",
                $"Xplor refinement — tetrad core fixed ({Pct(f)}%)",
                Lerp(Powell1Start, Powell1End, f), cyanaStages);
        }

        if (topologyStarted)
            return new QuadroStage("topology", "Building Xplor topology", TopologyDone, cyanaStages);

        if (cyanaFinished)
            return new QuadroStage("converting", "Converting CYANA output for Xplor", CyanaDone, cyanaStages);

        if (cyanaStages > 0)
        {
            // Bez wiarygodnego totalu nie udajemy dokładności — krzywa asymptotyczna zawsze
            // rośnie i nigdy nie przekracza końca fazy, więc pasek nie cofa się ani nie utyka.
            var f = expectedCyanaStages > 0
                ? Math.Min(1.0, (double)cyanaStages / expectedCyanaStages)
                : 1.0 - Math.Pow(0.88, cyanaStages);

            return new QuadroStage("building",
                expectedCyanaStages > 0
                    ? $"Building structure in CYANA (residue {cyanaStages}/{expectedCyanaStages})"
                    : $"Building structure in CYANA (residue {cyanaStages})",
                Lerp(BuildStart, BuildEnd, f), cyanaStages);
        }

        return new QuadroStage("starting", "Starting engine", BuildStart, 0);
    }

    /// <summary>Wyciąga N z linii <c>--------------- cycle=   600 ------ stepsize= ...</c>.</summary>
    private static int ParseCycle(string line)
    {
        var i = line.IndexOf("cycle=", StringComparison.Ordinal);
        if (i < 0) return 0;

        var span = line.AsSpan(i + "cycle=".Length).TrimStart();
        var end = 0;
        while (end < span.Length && char.IsAsciiDigit(span[end])) end++;
        return end > 0 && int.TryParse(span[..end], out var v) ? v : 0;
    }

    private static double CycleFraction(int cycle) =>
        cycle <= 0 ? 0.0 : Math.Min(1.0, (double)cycle / PowellSteps);

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    private static int Pct(double f) => (int)Math.Round(Math.Clamp(f, 0, 1) * 100);
}
