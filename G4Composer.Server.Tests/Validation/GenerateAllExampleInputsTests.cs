using System.Text;
using G4Composer.Server.Configuration;
using G4Composer.Server.Data;
using G4Composer.Server.Data.Entities;
using G4Composer.Server.Engines;
using G4Composer.Server.Models;
using G4Composer.Server.Validation;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace G4Composer.Server.Tests.Validation;

/// <summary>
/// Reads every deposited example from the local SQLite database, rebuilds the exact
/// QuadroInput the Home "Examples → Run" path sends, validates it with the real
/// <see cref="QuadroInputValidator"/> and serialises it with the real engine
/// (<see cref="Quadro14LEngine"/> via <see cref="QuadroEngineBase.SerializeInput"/>).
///
/// The full generated .inp for every example is written to ONE report file at the
/// repo root (example-inputs.generated.txt) so the inputs can be inspected, and the
/// test fails if ANY deposited example does not pass validation.
/// </summary>
public class GenerateAllExampleInputsTests
{
    private readonly ITestOutputHelper _out;
    public GenerateAllExampleInputsTests(ITestOutputHelper output) => _out = output;

    // Minimal concrete engine so we can call the real SerializeInput without DI.
    // Version/Image/Executable pochodzą teraz z QuadroOptions (klasa bazowa je czyta w
    // konstruktorze), więc podajemy domyślne opcje — wpis "14L" jest w nich zaszyty.
    private sealed class ReportEngine : QuadroEngineBase
    {
        public ReportEngine()
            : base(Microsoft.Extensions.Options.Options.Create(new QuadroOptions()),
                   Quadro14LEngine.VersionId) { }
    }

    [Fact]
    public void GenerateAndValidate_AllDepositedExamples()
    {
        var repoRoot = FindRepoRoot();
        var dbPath   = Path.Combine(repoRoot, "G4Composer.Server", "g4composer.db");
        // The local SQLite DB is NOT committed (manual-test only — see CLAUDE.md), so it is absent in
        // CI. This is an integration check that only makes sense where the DB exists; no-op out when
        // it's missing so CI stays green while local runs still validate every deposited example.
        // (xUnit 2.9 has no dynamic Assert.Skip, so we return instead of failing.)
        if (!File.Exists(dbPath))
        {
            _out.WriteLine($"Local SQLite DB not found at {dbPath} — skipping (DB is local-only, not committed).");
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        List<StructureExample> examples;
        using (var db = new AppDbContext(options))
        {
            examples = db.StructureExamples
                .AsNoTracking()
                .Include(x => x.Subtype)
                .OrderBy(x => x.PdbId)
                .ToList();
        }

        var validator = new QuadroInputValidator();
        var engine    = new ReportEngine();

        var report  = new StringBuilder();
        var failures = new List<string>();
        var passCount = 0;

        // Header written after we know counts (placeholder filled at the end).
        var body = new StringBuilder();

        foreach (var (e, i) in examples.Select((e, i) => (e, i + 1)))
        {
            // Same mapping as HomeSection "Examples → Run": only these fields are sent.
            // Sugar is NOT sent (the client key "Shugar" doesn't bind to "Sugar"), and
            // RmLevel / IterationSteps fall back to QuadroInput defaults — so the engine
            // auto-derives shugar from sequence case, exactly like a real run.
            var input = new QuadroInput
            {
                Name      = string.IsNullOrWhiteSpace(e.InpName) ? e.PdbId : e.InpName,
                Sequence  = e.Sequence,
                Structure = e.Structure,
                Chi       = e.Chi,
                Orient    = string.IsNullOrWhiteSpace(e.Orient) ? "A+;B-" : e.Orient,
                Rise      = string.IsNullOrWhiteSpace(e.Rise) ? "3.4" : e.Rise,
                Twist     = string.IsNullOrWhiteSpace(e.Twist) ? "29" : e.Twist,
                Path      = string.IsNullOrWhiteSpace(e.Path)
                    ? null
                    : [.. e.Path.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            };

            var result = validator.Validate(input);
            var inp    = engine.SerializeInput(input);

            if (result.IsValid) passCount++;
            else failures.Add($"{e.PdbId}: {string.Join(" | ", result.Errors)}");

            var status = result.IsValid ? "PASS" : "FAIL";
            body.Append("================================================================\n");
            body.Append($"[{i}] {e.PdbId}   subtype={e.Subtype?.Code ?? "(none)"}   ")
                .Append($"tetrads={e.Tetrads}   theoretical={e.IsTheoretical}   VALIDATION: {status}\n");
            if (!result.IsValid)
                foreach (var err in result.Errors)
                    body.Append($"  ! {err}\n");
            body.Append("----------------------------------------------------------------\n");
            body.Append(inp);
            body.Append('\n');
        }

        report.Append("# G4Composer — generated QuadroInput / .inp for every deposited example\n");
        report.Append($"# DB:        {dbPath}\n");
        report.Append("# Generator: Quadro14L engine (QuadroEngineBase.SerializeInput) — real backend code\n");
        report.Append("# Validator: QuadroInputValidator — real backend code\n");
        report.Append("# Mapping:   mirrors Home \"Examples → Run\"; rm_level + iteration_steps use QuadroInput defaults\n");
        report.Append($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        report.Append($"# Total: {examples.Count}   Passed: {passCount}   Failed: {examples.Count - passCount}\n");
        report.Append('\n');
        report.Append(body);

        var outPath = Path.Combine(repoRoot, "example-inputs.generated.txt");
        File.WriteAllText(outPath, report.ToString());

        // Also dump one real .inp per example so they can be fed straight to quadro14L
        // via Docker (smoke-run script). Same engine serializer = identical format.
        var inpDir = Path.Combine(repoRoot, "out-quadro", "example-inp");
        Directory.CreateDirectory(inpDir);
        foreach (var e in examples)
        {
            var input = new QuadroInput
            {
                Name      = string.IsNullOrWhiteSpace(e.InpName) ? e.PdbId : e.InpName,
                Sequence  = e.Sequence,
                Structure = e.Structure,
                Chi       = e.Chi,
                Orient    = string.IsNullOrWhiteSpace(e.Orient) ? "A+;B-" : e.Orient,
                Rise      = string.IsNullOrWhiteSpace(e.Rise) ? "3.4" : e.Rise,
                Twist     = string.IsNullOrWhiteSpace(e.Twist) ? "29" : e.Twist,
                Path      = string.IsNullOrWhiteSpace(e.Path)
                    ? null
                    : [.. e.Path.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            };
            File.WriteAllText(Path.Combine(inpDir, $"{e.PdbId}.inp"), engine.SerializeInput(input));
        }

        _out.WriteLine($"Wrote {examples.Count} example inputs to {outPath}");
        _out.WriteLine($"Wrote {examples.Count} .inp files to {inpDir}");
        _out.WriteLine($"Passed: {passCount} / {examples.Count}");

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} deposited example(s) failed validation:\n" + string.Join("\n", failures));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "G4Composer.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (G4Composer.slnx).");
    }
}
