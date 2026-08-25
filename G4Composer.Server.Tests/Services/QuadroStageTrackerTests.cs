using G4Composer.Server.Services;

namespace G4Composer.Server.Tests.Services;

/// <summary>
/// Markery pochodzą z prawdziwego wyjścia quadro14N (2gku, 24 reszty, iteration 100) —
/// przebieg zachowany przy okazji migracji na 14N.
/// </summary>
public class QuadroStageTrackerTests
{
    private const int Expected = 13;   // tyle etapów budowy ma 2gku

    private static string BuildUp(int stages) =>
        string.Join('\n', Enumerable.Range(1, stages)
            .Select(i => $"    {40 + i} angle constraints added.\n    Constraints for {i} ribose rings added."));

    [Fact]
    public void EmptyLog_ReportsStarting()
    {
        var s = QuadroStageTracker.Parse("", Expected);

        Assert.Equal("starting", s.Stage);
        Assert.Equal(0.0, s.Fraction);
    }

    [Fact]
    public void DuringBuildUp_ReportsResidueProgress()
    {
        var s = QuadroStageTracker.Parse(BuildUp(4), Expected);

        Assert.Equal("building", s.Stage);
        Assert.Equal(4, s.CyanaStages);
        Assert.Contains("4/13", s.Label);
        // 4/13 przez fazę 0.02..0.55
        Assert.InRange(s.Fraction, 0.18, 0.20);
    }

    [Fact]
    public void BuildUpWithoutEstimate_StillAdvancesAndStaysInPhase()
    {
        var a = QuadroStageTracker.Parse(BuildUp(3), 0);
        var b = QuadroStageTracker.Parse(BuildUp(9), 0);

        Assert.Equal("building", a.Stage);
        Assert.True(b.Fraction > a.Fraction, "postęp musi rosnąć nawet bez znanego totalu");
        Assert.True(b.Fraction < 0.55, "faza budowy nie może przekroczyć własnej granicy");
    }

    [Fact]
    public void MoreStagesThanEstimated_DoesNotOvershoot()
    {
        // Część struktur ma o jeden etap więcej niż pozycji w path — pasek nie może wyjść poza fazę.
        var s = QuadroStageTracker.Parse(BuildUp(Expected + 1), Expected);

        Assert.Equal("building", s.Stage);
        Assert.InRange(s.Fraction, 0.54, 0.5501);
    }

    [Fact]
    public void AfterCyana_ReportsConverting()
    {
        var log = BuildUp(Expected) + "\n    PDB coordinate file \"final_cyana.pdb\" written.";

        var s = QuadroStageTracker.Parse(log, Expected);

        Assert.Equal("converting", s.Stage);
        Assert.Equal(0.60, s.Fraction, 3);
    }

    [Fact]
    public void XplorTopology_IsItsOwnStage()
    {
        var log = BuildUp(Expected)
            + "\n    PDB coordinate file \"final_cyana.pdb\" written."
            + "\n X-PLOR>topology @TOPPAR:dna-rna-allatom.top end ";

        var s = QuadroStageTracker.Parse(log, Expected);

        Assert.Equal("topology", s.Stage);
    }

    [Fact]
    public void FirstPowell_TracksCyclesAndReportsFixedCore()
    {
        var log = BuildUp(Expected)
            + "\n X-PLOR>topology @TOPPAR:dna-rna-allatom.top end "
            + "\n X-PLOR>minimize powell nstep=1000 nprint=100 end "
            + "\n POWELL: number of degrees of freedom=  2121"
            + "\n --------------- cycle=   600 ------ stepsize=    0.0001 ------------";

        var s = QuadroStageTracker.Parse(log, Expected);

        Assert.Equal("refining-core", s.Stage);
        Assert.Contains("60%", s.Label);
        Assert.InRange(s.Fraction, 0.77, 0.79);   // 60% przez fazę 0.70..0.83
    }

    [Fact]
    public void SecondPowell_IsFullRelaxation_AndCycleCounterResets()
    {
        var log = BuildUp(Expected)
            + "\n POWELL: number of degrees of freedom=  2121"
            + "\n --------------- cycle=  1000 ------ stepsize=    0.0001 ------------"
            + "\n POWELL: STEP number limit. Normal termination"
            + "\n POWELL: number of degrees of freedom=  2337"
            + "\n --------------- cycle=   200 ------ stepsize=    0.0001 ------------";

        var s = QuadroStageTracker.Parse(log, Expected);

        Assert.Equal("refining-full", s.Stage);
        Assert.Contains("20%", s.Label);   // licznik cykli zresetowany przy drugim POWELL-u
    }

    [Fact]
    public void EngineDiagnosticsLine_MeansFinished()
    {
        var log = BuildUp(Expected)
            + "\n POWELL: number of degrees of freedom=  2121"
            + "\n POWELL: STEP number limit. Normal termination"
            + "\n POWELL: number of degrees of freedom=  2337"
            + "\n POWELL: STEP number limit. Normal termination"
            + "\n X-PLOR: total CPU time=      0.9700 s"
            + "\nlibp= 1    libpr= 1"
            + "\nlih= 1  lihr= 1";

        var s = QuadroStageTracker.Parse(log, Expected);

        Assert.Equal("finished", s.Stage);
        Assert.Equal(1.0, s.Fraction);
    }

    [Fact]
    public void Progress_IsMonotonicAcrossTheWholeRun()
    {
        string[] snapshots =
        [
            "",
            BuildUp(1),
            BuildUp(7),
            BuildUp(Expected),
            BuildUp(Expected) + "\n    PDB coordinate file \"final_cyana.pdb\" written.",
            BuildUp(Expected) + "\n    PDB coordinate file \"final_cyana.pdb\" written.\n X-PLOR>topology x",
            BuildUp(Expected) + "\n X-PLOR>topology x\n POWELL: number of degrees of freedom=  2121\n cycle=   100 ",
            BuildUp(Expected) + "\n X-PLOR>topology x\n POWELL: number of degrees of freedom=  2121\n cycle=   900 ",
            BuildUp(Expected) + "\n POWELL: number of degrees of freedom=  1\n POWELL: STEP number limit. Normal termination"
                              + "\n POWELL: number of degrees of freedom=  2\n cycle=   500 ",
            BuildUp(Expected) + "\n POWELL: number of degrees of freedom=  1\n POWELL: STEP number limit. Normal termination"
                              + "\n POWELL: number of degrees of freedom=  2\n POWELL: STEP number limit. Normal termination"
                              + "\nlibp= 1    libpr= 1",
        ];

        var previous = -1.0;
        foreach (var snap in snapshots)
        {
            var s = QuadroStageTracker.Parse(snap, Expected);
            Assert.True(s.Fraction >= previous,
                $"postęp cofnął się: {previous:F3} -> {s.Fraction:F3} na etapie '{s.Stage}'");
            Assert.InRange(s.Fraction, 0.0, 1.0);
            previous = s.Fraction;
        }
        Assert.Equal(1.0, previous);
    }

    [Fact]
    public void TruncatedLastLine_DoesNotThrow()
    {
        // Czytamy plik, do którego kontener wciąż pisze — ostatnia linia bywa ucięta.
        var log = BuildUp(3) + "\n --------------- cycle=   4";

        var s = QuadroStageTracker.Parse(log, Expected);

        Assert.Equal("building", s.Stage);
    }
}
