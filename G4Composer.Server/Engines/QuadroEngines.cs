using Microsoft.Extensions.Options;
using G4Composer.Server.Configuration;
using G4Composer.Server.Models;

namespace G4Composer.Server.Engines;

/// <summary>Legacy engine — quadro14G.exe.</summary>
public sealed class Quadro14GEngine : QuadroEngineBase
{
    public const string VersionId = "14G";

    public Quadro14GEngine(IOptions<QuadroOptions> options) : base(options, VersionId) { }
}

/// <summary>
/// Nowy engine — quadro14L.exe. Obecnie format pliku .inp jest identyczny
/// jak w 14G (dziedziczone z bazy). Gdy 14L zacznie wymagać innych pól,
/// wystarczy nadpisać <see cref="QuadroEngineBase.SerializeInput"/>
/// — reszta kodu pozostaje bez zmian.
/// </summary>
public sealed class Quadro14LEngine : QuadroEngineBase
{
    public const string VersionId = "14L";

    public Quadro14LEngine(IOptions<QuadroOptions> options) : base(options, VersionId) { }

    // public override string SerializeInput(QuadroInput input)
    // {
    //     // TODO: gdy 14L zmieni format .inp — implementuj tu i nie wołaj base.
    //     // Np. dodatkowe pole / zmieniona kolejność. Reszta architektury bez zmian.
    //     return base.SerializeInput(input);
    // }
}

/// <summary>
/// Engine quadro14N.exe.
/// <para>
/// 14N nie zna <c>iteration_steps</c> — to była lokalna łatka doklejona do 14L. Rozumie
/// tylko <c>iteration</c>, które steruje minimalizacją CYANA na <b>każdym etapie budowy</b>
/// cząsteczki (reszta po reszcie, w kolejności z <c>path</c>). Finalna minimalizacja CYANA
/// jest w 14N zaszyta na 100 i nie reaguje na <c>iteration</c>.
/// </para>
/// <para>
/// Dlatego zamiast checkpointów robimy <b>N niezależnych przelotów</b>, po jednym na wartość
/// z <see cref="QuadroInput.IterationSteps"/>. Każdy przelot ma własną fazę budowy, więc
/// oddaje Xplorowi inną strukturę startową i daje inny model fizyczny. Checkpointy tego nie
/// robiły: wszystkie miały identyczną fazę budowy i różniły się tylko ogonem.
/// </para>
/// <para>
/// Nazwy: przelot K dostaje <c>name = &lt;base&gt;_&lt;K&gt;</c>, więc binarka zapisuje
/// <c>&lt;base&gt;_&lt;K&gt;.pdb</c> + <c>&lt;base&gt;_&lt;K&gt;_energy.txt</c> — dokładnie
/// ten sam wzorzec, który kolektor klatek już rozpoznaje.
/// </para>
/// </summary>
public sealed class Quadro14NEngine : QuadroEngineBase
{
    public const string VersionId = "14N";

    public Quadro14NEngine(IOptions<QuadroOptions> options) : base(options, VersionId) { }

    /// <summary>
    /// Kanoniczna postać .inp — używana do hasha cache'a i do podglądu w UI, nie do
    /// uruchamiania. Niesie najgłębszy przelot; <c>iteration</c> jest wyłączone z hasha
    /// (patrz <c>PdbCacheService.HashIgnoredFieldNames</c>), żeby wszystkie przeloty tej samej
    /// struktury trafiły do jednego wpisu jako osobne klatki.
    /// </summary>
    protected override string IterationLine(QuadroInput input)
        => "iteration          " + ResolveSteps(input).Max() + "\n";

    public override IReadOnlyList<QuadroPass> SerializePasses(QuadroInput input, string baseFileName)
    {
        var name = ResolveName(input);
        return [.. ResolveSteps(input)
            .Distinct()
            .OrderBy(step => step)
            .Select(step => new QuadroPass(
                step,
                $"{baseFileName}_{step}.inp",
                BuildInp(input, $"{name}_{step}", $"iteration          {step}\n"),
                EstimateCyanaStages(input)))];
    }
}
