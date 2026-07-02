using G4Composer.Server.Engines;
using G4Composer.Server.Services;

namespace G4Composer.Server.Tests.Domain;

/// <summary>
/// Utility (NOT a CI test): dumps the .inp files for every predicted topology candidate of a
/// sequence, using the real <see cref="G4TopologyGenerator"/> + engine serializer. Gated by the
/// INP_DUMP_DIR / INP_DUMP_SEQ env vars so it is a no-op in normal test runs. Used to build an
/// apples-to-apples set (one sequence, several topologies) for the openmm-utils energy experiment.
/// </summary>
public class ModelInpDumpUtility
{
    private sealed class Engine14L : QuadroEngineBase
    {
        public override string Version    => "14L";
        public override string Image      => "quadro14l:latest";
        public override string Executable => "quadro14L.exe";
    }

    [Fact]
    public void DumpCandidateInpFiles()
    {
        var dir = Environment.GetEnvironmentVariable("INP_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;   // no-op unless explicitly requested

        var seq = Environment.GetEnvironmentVariable("INP_DUMP_SEQ") ?? "gggttagggttagggttaggg";
        Directory.CreateDirectory(dir);

        var set = G4TopologyGenerator.GenerateCandidates("GQ", seq, null);
        var engine = new Engine14L();

        var index = new List<string> { "slug,notation,label,confidence" };
        foreach (var t in set.Topologies)
        {
            var slug = "m_" + t.LoopNotation
                .Replace("+", "p").Replace("-", "m")
                .Replace("(", "").Replace(")", "");
            t.Input.Name = slug;   // output PDB will be {slug}_{step}.pdb
            File.WriteAllText(Path.Combine(dir, slug + ".inp"), engine.SerializeInput(t.Input));
            index.Add($"{slug},{t.LoopNotation},\"{t.Label}\",{t.Confidence}");
        }
        File.WriteAllLines(Path.Combine(dir, "candidates.csv"), index);
    }
}
