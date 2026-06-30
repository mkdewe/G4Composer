using System.Text;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

public interface IOnquadroService
{
    Task<OnquadroResult> AlignAsync(string sequence, CancellationToken ct);
}

public sealed class OnquadroService : IOnquadroService
{
    private readonly IDockerCommandRunner      _docker;
    private readonly ILogger<OnquadroService>  _logger;

    public OnquadroService(IDockerCommandRunner docker, ILogger<OnquadroService> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    public async Task<OnquadroResult> AlignAsync(string sequence, CancellationToken ct)
    {
        var molecule = DetectMolecule(sequence);
        var result = await RunAlignerAsync(sequence, molecule, ct);

        // The --molecule filter restricts the template database to a single nucleic-acid type. Some
        // folds only have a template of the OTHER type — e.g. 4-tetrad G4s are almost all RNA (the
        // only DB template is 6k84) — so a strict DNA query legitimately returns nothing even though
        // a usable cross-type template exists. When a filtered search finds nothing, retry once
        // WITHOUT --molecule (both databases) so we still surface the closest geometric template;
        // the energy minimisation adapts the sugar/backbone afterwards.
        if (molecule is not null && result.Success
            && result.Matches.Count == 0 && (result.InpCandidates?.Count ?? 0) == 0)
        {
            _logger.LogInformation(
                "onquadro: no {Molecule} template matched '{Seq}'; retrying without --molecule (both DBs)",
                molecule, sequence);
            var broadened = await RunAlignerAsync(sequence, null, ct);
            if (broadened.Success
                && (broadened.Matches.Count > 0 || (broadened.InpCandidates?.Count ?? 0) > 0))
                return broadened;
        }
        return result;
    }

    // Runs the aligner once for the given molecule filter (null = no --molecule, search both DBs).
    private async Task<OnquadroResult> RunAlignerAsync(string sequence, string? molecule, CancellationToken ct)
    {
        // Mount a host work dir into the container so the aligner can drop its CSV and the
        // ranked g4composer .inp files (--g4composer-output-dir). We still read CSV from stdout
        // as a fallback when the mount is unavailable.
        var workDir = Path.Combine(Path.GetTempPath(), $"onq_{Guid.NewGuid():N}"[..20]);
        Directory.CreateDirectory(workDir);
        try
        {
            var mount = workDir.Replace('\\', '/');

            var args = new List<string>
                { "run", "--rm", "-v", $"{mount}:/work", "onquadro-aligner:latest", sequence };
            if (molecule is not null) args.Add(molecule);

            var result = await _docker.RunAsync(args, ct);

            var csvPath = Path.Combine(workDir, "out.csv");
            var csv = File.Exists(csvPath)
                ? await File.ReadAllTextAsync(csvPath, ct)
                : result.Stdout;

            var inpCandidates = ParseInpCandidates(workDir);

            // A non-zero exit with no CSV and no .inp output means the run truly failed.
            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(csv) && inpCandidates.Count == 0)
                return new OnquadroResult(false, [],
                    $"onquadro exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

            var matches = ParseCsv(csv);
            return new OnquadroResult(true, matches, null, inpCandidates);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "onquadro-aligner failed");
            return new OnquadroResult(false, [], ex.Message);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    // onquadro's --molecule switch (--molecule DNA | RNA) filters the template database to one
    // nucleic-acid type. We choose it from the CASE of the sequence, following the app's convention:
    // UPPERCASE = RNA (C3'-endo, U), lowercase = DNA (C2'-endo, t). This is case-based, NOT mere
    // uracil presence — a purely-uppercase sequence is RNA even without any U (e.g. GGGAGGGAGGGAGGG),
    // and a purely-lowercase one is DNA. A mixed-case sequence is a hybrid DNA/RNA G4 that cannot be
    // filtered to a single type, so we pass NO --molecule and let the aligner search both databases.
    private static string? DetectMolecule(string sequence)
    {
        bool hasUpper = sequence.Any(char.IsUpper);
        bool hasLower = sequence.Any(char.IsLower);
        if (hasUpper && hasLower) return null;   // hybrid → don't restrict the template database
        if (hasUpper) return "RNA";
        if (hasLower) return "DNA";
        return null;                              // no nucleotide letters → let the aligner decide
    }

    // ── g4composer .inp candidate parser ──────────────────────────────────────

    private List<OnquadroInpCandidate> ParseInpCandidates(string workDir)
    {
        var list = new List<OnquadroInpCandidate>();
        var g4cDir = Path.Combine(workDir, "g4c");
        if (!Directory.Exists(g4cDir)) return list;

        // File names are "{rank}-{template}.inp"; ordinal sort on the name = aligner ranking.
        foreach (var file in Directory.GetFiles(g4cDir, "*.inp")
                     .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            try
            {
                var cand = ParseInpFile(Path.GetFileName(file), File.ReadAllText(file));
                if (cand is not null) list.Add(cand);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse onquadro .inp candidate {File}", file);
            }
        }
        return list;
    }

    private static OnquadroInpCandidate? ParseInpFile(string fileName, string text)
    {
        var meta   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith('#'))
            {
                // # tract_distance=0 linker_score=20 viability=viable loop_lengths=2-2-2 topology=-p-p-p template=6w9p-assembly1
                foreach (var tok in line.TrimStart('#').Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = tok.IndexOf('=');
                    if (eq > 0) meta[tok[..eq]] = tok[(eq + 1)..];
                }
            }
            else
            {
                // key<spaces>value (e.g. "orient      A-;B-;C-;D-")
                var sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                fields[line[..sp]] = line[(sp + 1)..].Trim();
            }
        }

        if (!fields.TryGetValue("sequence", out var seq) || string.IsNullOrWhiteSpace(seq)) return null;
        if (!fields.TryGetValue("path", out var pathStr) || string.IsNullOrWhiteSpace(pathStr)) return null;

        // The aligner writes chi/sugar as all-dots; leaving them empty lets QuadroEngineBase
        // derive sensible per-residue defaults (N/S from sequence case).
        static string CleanDots(string? s)
            => string.IsNullOrEmpty(s) || s.All(c => c == '.') ? "" : s;

        var input = new QuadroInput
        {
            Name      = fields.GetValueOrDefault("name", Path.GetFileNameWithoutExtension(fileName)),
            Sequence  = seq,
            Structure = fields.GetValueOrDefault("structure", ""),
            Chi       = CleanDots(fields.GetValueOrDefault("chi")),
            Sugar     = CleanDots(fields.GetValueOrDefault("sugar")),
            Orient    = fields.GetValueOrDefault("orient", ""),
            Rise      = fields.TryGetValue("rise", out var r) && r.Length > 0 ? r : "3.4",
            Twist     = fields.TryGetValue("twist", out var t) && t.Length > 0 ? t : "29",
            Path      = pathStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        };

        static double ParseD(Dictionary<string, string> m, string key)
            => m.TryGetValue(key, out var v) && double.TryParse(v,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

        var rank = 0;
        var dash = fileName.IndexOf('-');
        if (dash > 0) int.TryParse(fileName[..dash], out rank);

        return new OnquadroInpCandidate(
            Rank:          rank,
            Template:      meta.GetValueOrDefault("template", input.Name ?? ""),
            Viability:     meta.GetValueOrDefault("viability", ""),
            Topology:      meta.GetValueOrDefault("topology", ""),
            LoopLengths:   meta.GetValueOrDefault("loop_lengths", ""),
            TractDistance: ParseD(meta, "tract_distance"),
            LinkerScore:   ParseD(meta, "linker_score"),
            Input:         input
        );
    }

    // ── CSV parser ───────────────────────────────────────────────────────────

    // Parsing is keyed off the header row rather than fixed column indices, so the
    // aligner can add/reorder columns (as it did with Viability/Loop lengths/Topology)
    // without silently shifting the data we read.
    private static List<OnquadroMatch> ParseCsv(string csv)
    {
        var matches = new List<OnquadroMatch>();
        if (string.IsNullOrWhiteSpace(csv)) return matches;

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return matches;

        // Map header name → column index (case-insensitive, trimmed).
        var header = SplitCsvLine(lines[0]);
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
            col[header[i].Trim()] = i;

        int? Index(params string[] names)
        {
            foreach (var n in names)
                if (col.TryGetValue(n, out var i)) return i;
            return null;
        }

        string Get(string[] cols, int? idx)
            => idx is int i && i < cols.Length ? cols[i].Trim() : "";

        // "Input sequence" is the full query the QRS line aligns to (older builds called
        // the equivalent column "Matched sequence").
        var iFiles    = Index("Files");
        var iTetrad   = Index("Tetrad count");
        var iMolecule = Index("Molecule");
        var iTract    = Index("Tract distance");
        var iLinker   = Index("Linker score");
        var iViab     = Index("Viability");
        var iLoops    = Index("Loop lengths");
        var iTopo     = Index("Topology");
        var iSeq      = Index("Input sequence", "Matched sequence");
        var iQrs      = Index("QRS");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.NumberStyles f = System.Globalization.NumberStyles.Float;

        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line);

            if (!int.TryParse(Get(cols, iTetrad), out var tetradCount)) continue;
            if (!double.TryParse(Get(cols, iTract),  f, inv, out var tractDist)) continue;
            if (!double.TryParse(Get(cols, iLinker), f, inv, out var linkerScore)) continue;

            matches.Add(new OnquadroMatch(
                Files:           Get(cols, iFiles),
                TetradCount:     tetradCount,
                Molecule:        Get(cols, iMolecule),
                TractDistance:   tractDist,
                LinkerScore:     linkerScore,
                Qrs:             Get(cols, iQrs),
                MatchedSequence: Get(cols, iSeq),
                Viability:       Get(cols, iViab),
                LoopLengths:     Get(cols, iLoops),
                Topology:        Get(cols, iTopo)
            ));
        }
        return matches;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result  = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            switch (ch)
            {
                case '"': inQuotes = !inQuotes; break;
                case ',' when !inQuotes: result.Add(current.ToString()); current.Clear(); break;
                default: current.Append(ch); break;
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
