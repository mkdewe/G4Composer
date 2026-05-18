using System.Text.RegularExpressions;
using G4Composer.Server.Models;
using Microsoft.Extensions.Logging;

namespace G4Composer.Server.Services;

public interface IRnaSecondaryStructureService
{
    IReadOnlyList<string> ToolNames { get; }
    Task<ToolStructureResult> PredictAsync(string toolName, string sequence, CancellationToken ct);
}

public sealed class RnaSecondaryStructureService : IRnaSecondaryStructureService
{
    private static readonly string[] Tools =
        ["ViennaRNA", "LinearFold", "RNAStructure", "MXfold2", "UNAFold", "SPOT-RNA", "UFold"];

    private static readonly Regex EnergyRegex =
        new(@"\(\s*([-+]?\d+(?:\.\d+)?)\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDockerCommandRunner _docker;
    private readonly ILogger<RnaSecondaryStructureService> _logger;

    public RnaSecondaryStructureService(
        IDockerCommandRunner docker,
        ILogger<RnaSecondaryStructureService> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    public IReadOnlyList<string> ToolNames => Tools;

    public async Task<ToolStructureResult> PredictAsync(string toolName, string sequence, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "ViennaRNA"   => await RunViennaRnaAsync(sequence, ct),
                "LinearFold"  => await RunLinearFoldAsync(sequence, ct),
                "RNAStructure" => await RunRnaStructureAsync(sequence, ct),
                "MXfold2"     => await RunMxfold2Async(sequence, ct),
                "UNAFold"     => await RunUnaFoldAsync(sequence, ct),
                "SPOT-RNA"    => await RunSpotRnaAsync(sequence, ct),
                "UFold"       => await RunUFoldAsync(sequence, ct),
                _ => new ToolStructureResult(toolName, false, null, null, $"Unknown tool: {toolName}"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RNA tool {Tool} failed unexpectedly", toolName);
            return new ToolStructureResult(toolName, false, null, null, ex.Message);
        }
    }

    // ── ViennaRNA ────────────────────────────────────────────────────────────
    // stdout: SEQ\nSTRUCTURE (ENERGY)
    private async Task<ToolStructureResult> RunViennaRnaAsync(string sequence, CancellationToken ct)
    {
        const string tool = "ViennaRNA";
        var cmd = $"echo '{sequence}' | RNAfold --noPS";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "viennarna:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        var lines = SplitLines(result.Stdout);
        // Last non-empty line contains: STRUCTURE (ENERGY)
        var structLine = lines.LastOrDefault(l => l.Trim().Length > 0 && (l.Contains('.') || l.Contains('(')));
        if (structLine is null)
            return Fail(tool, $"Unexpected output: {Truncate(result.Stdout)}");

        var parts    = structLine.Trim().Split(' ', 2);
        var structure = parts[0].Trim();
        double? energy = parts.Length > 1 ? ParseParenEnergy(parts[1]) : null;

        return Ok(tool, structure, energy);
    }

    // ── LinearFold ───────────────────────────────────────────────────────────
    // stdout: SEQ\nSTRUCTURE (SCORE)
    private async Task<ToolStructureResult> RunLinearFoldAsync(string sequence, CancellationToken ct)
    {
        const string tool = "LinearFold";
        var cmd = $"echo '{sequence}' | linearfold";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "linearfold:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        var lines = SplitLines(result.Stdout);
        var structLine = lines.LastOrDefault(l => l.Trim().Length > 0 && (l.Contains('.') || l.Contains('(')));
        if (structLine is null)
            return Fail(tool, $"Unexpected output: {Truncate(result.Stdout)}");

        var parts     = structLine.Trim().Split(' ', 2);
        var structure = parts[0].Trim();
        double? energy = parts.Length > 1 ? ParseParenEnergy(parts[1]) : null;

        return Ok(tool, structure, energy);
    }

    // ── RNAStructure ─────────────────────────────────────────────────────────
    // ct2dot DBN format (two possible headers):
    //   ">ENERGY = -5.40  s"  — folded with free energy
    //   ">s NAME"             — no energy (e.g. completely unfolded sequence)
    // Either way: header line, then sequence, then dot-bracket structure.
    private async Task<ToolStructureResult> RunRnaStructureAsync(string sequence, CancellationToken ct)
    {
        const string tool = "RNAStructure";
        // Fold and ct2dot write progress to stdout — suppress both; cat the .dbn file only.
        var cmd = $"printf ';\\n s\\n{sequence} 1\\n' > /tmp/i.seq && Fold /tmp/i.seq /tmp/o.ct >/dev/null 2>&1 && ct2dot /tmp/o.ct 1 /tmp/o.dbn >/dev/null 2>&1 && cat /tmp/o.dbn";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "rnastructure:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        var lines = SplitLines(result.Stdout);
        double? energy = null;
        string? structure = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith(">")) continue;

            // Optional energy on the header line: ">ENERGY = -5.40  s"
            var eMatch = Regex.Match(line, @"ENERGY\s*=\s*([-+]?\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (eMatch.Success)
                energy = ParseDouble(eMatch.Groups[1].Value);

            // Header → i+1 = sequence, i+2 = dot-bracket structure
            if (i + 2 < lines.Length)
                structure = lines[i + 2].Trim();
            break;
        }

        if (string.IsNullOrWhiteSpace(structure))
            return Fail(tool, $"Could not parse output: {Truncate(result.Stdout)}");

        return Ok(tool, structure, energy);
    }

    // ── MXfold2 ──────────────────────────────────────────────────────────────
    // stdout: >seq\nSEQ\nSTRUCTURE (SCORE)
    private async Task<ToolStructureResult> RunMxfold2Async(string sequence, CancellationToken ct)
    {
        const string tool = "MXfold2";
        var cmd = $"printf '>seq\\n{sequence}\\n' > /tmp/t.fa && mxfold2 predict /tmp/t.fa";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "mxfold2:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        var lines = SplitLines(result.Stdout);
        string? structure = null;
        double? energy = null;

        foreach (var line in lines)
        {
            var l = line.Trim();
            if (l.StartsWith(">")) continue;
            if (l.Length == 0) continue;

            // Check if it looks like a structure (contains . or ( or ))
            if (l[0] is '.' or '(' or ')')
            {
                var parts = l.Split(' ', 2);
                structure = parts[0];
                if (parts.Length > 1) energy = ParseParenEnergy(parts[1]);
                break;
            }
        }

        if (structure is null)
            return Fail(tool, $"Could not parse output: {Truncate(result.Stdout)}");

        return Ok(tool, structure, energy);
    }

    // ── UNAFold ──────────────────────────────────────────────────────────────
    // stdout: CT file format
    private async Task<ToolStructureResult> RunUnaFoldAsync(string sequence, CancellationToken ct)
    {
        const string tool = "UNAFold";
        var cmd = $"cd /tmp && printf '>seq\\n{sequence}\\n' > t.fa && mfold SEQ=t.fa 2>/dev/null && cat t.fa.ct";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "unafold:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        return ParseCtOutput(tool, result.Stdout);
    }

    // ── SPOT-RNA ─────────────────────────────────────────────────────────────
    // stdout: CT file format
    private async Task<ToolStructureResult> RunSpotRnaAsync(string sequence, CancellationToken ct)
    {
        const string tool = "SPOT-RNA";
        var cmd = $"mkdir -p /tmp/out && printf '>seq\\n{sequence}\\n' > /tmp/s.fasta && cd /opt/spot-rna && python3 SPOT-RNA.py --inputs /tmp/s.fasta --outputs /tmp/out 2>/dev/null && cat /tmp/out/seq.ct";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "spot-rna:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        return ParseCtOutput(tool, result.Stdout);
    }

    // ── UFold ────────────────────────────────────────────────────────────────
    // stdout: CT file (header starts with >)
    private async Task<ToolStructureResult> RunUFoldAsync(string sequence, CancellationToken ct)
    {
        const string tool = "UFold";
        var cmd = $"cd /opt/ufold && printf '>seq\\n{sequence}\\n' > data/input.txt && python3 ufold_predict.py 2>/dev/null && cat results/save_ct_file/seq.ct";
        var result = await _docker.RunAsync(
            ["run", "--rm", "--entrypoint", "/bin/sh", "ufold:latest", "-c", cmd], ct);

        if (result.ExitCode != 0)
            return Fail(tool, $"exit {result.ExitCode}: {result.Stderr.Trim().Split('\n').LastOrDefault()}");

        return ParseCtOutputUFold(tool, result.Stdout);
    }

    // ── CT format parsers ────────────────────────────────────────────────────

    /// <summary>
    /// Standard CT format (UNAFold / SPOT-RNA):
    ///   header: "12   dG = -5.40 ..." or "12\t\tseq\t\t..."
    ///   data rows: index  base  prev  next  pairedWith  seqNum
    /// Column index 4 (0-indexed) is the paired position (0 = unpaired).
    /// </summary>
    private static ToolStructureResult ParseCtOutput(string toolName, string stdout)
    {
        var lines = SplitLines(stdout);
        var dataLines = new List<string>();
        double? energy = null;
        bool headerSeen = false;
        int seqLen = int.MaxValue;

        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;

            if (!headerSeen)
            {
                // Header: first token is the sequence length (a number)
                var firstToken = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (firstToken != null && int.TryParse(firstToken, out int parsedLen))
                {
                    headerSeen = true;
                    seqLen = parsedLen;
                    // Try to parse dG energy from UNAFold-style header
                    var eMatch = Regex.Match(t, @"dG\s*=\s*([-+]?\d+(?:\.\d+)?)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (eMatch.Success) energy = ParseDouble(eMatch.Groups[1].Value);
                }
                continue;
            }

            // Stop as soon as we have enough rows — guards against multi-structure CT files
            // where the second structure's header (also starts with a number) would otherwise
            // be misread as a data row.
            if (dataLines.Count >= seqLen) break;
            dataLines.Add(t);
        }

        if (dataLines.Count == 0)
            return Fail(toolName, $"CT file has no data rows: {Truncate(stdout)}");

        return Ok(toolName, CtDataToDotBracket(dataLines), energy);
    }

    /// <summary>
    /// UFold CT format — header starts with '>':
    ///   >seq length: 12\t seq name: seq
    ///   1  G  0  2  12  1
    /// </summary>
    private static ToolStructureResult ParseCtOutputUFold(string toolName, string stdout)
    {
        var lines = SplitLines(stdout);
        var dataLines = new List<string>();

        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith('>')) continue; // skip UFold header lines

            // Check that this line starts with a number (data row)
            var firstToken = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstToken != null && int.TryParse(firstToken, out _))
                dataLines.Add(t);
        }

        if (dataLines.Count == 0)
            return Fail(toolName, $"CT file has no data rows: {Truncate(stdout)}");

        return Ok(toolName, CtDataToDotBracket(dataLines), null);
    }

    /// <summary>
    /// Convert CT data rows to dot-bracket notation.
    /// Each row: index base prev next pairedWith seqNum (space or tab separated)
    /// paired[i] > i → '(', paired[i] &lt; i → ')', 0 → '.'
    /// </summary>
    private static string CtDataToDotBracket(List<string> dataLines)
    {
        var pairs = new int[dataLines.Count];
        for (var i = 0; i < dataLines.Count; i++)
        {
            var cols = dataLines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // Column 4 (0-indexed) is the paired position
            if (cols.Length >= 5 && int.TryParse(cols[4], out var paired))
                pairs[i] = paired;
        }

        var sb = new System.Text.StringBuilder(dataLines.Count);
        for (var i = 0; i < pairs.Length; i++)
        {
            var p = pairs[i];
            if (p == 0)
                sb.Append('.');
            else if (p > i + 1) // paired with a later position (1-indexed)
                sb.Append('(');
            else
                sb.Append(')');
        }

        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static double? ParseParenEnergy(string text)
    {
        var m = EnergyRegex.Match(text);
        return m.Success ? ParseDouble(m.Groups[1].Value) : null;
    }

    private static double? ParseDouble(string s)
        => double.TryParse(s,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string[] SplitLines(string text)
        => text.Split('\n', StringSplitOptions.None);

    private static ToolStructureResult Ok(string tool, string structure, double? energy)
        => new(tool, true, structure, energy, null);

    private static ToolStructureResult Fail(string tool, string error)
        => new(tool, false, null, null, error);

    // Trim whitespace then cap at max chars — avoids IndexOutOfRange when using
    // Trim() result length with the original (longer) string's length.
    private static string Truncate(string text, int max = 200)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..max];
    }
}
