using G4Composer.Server.Models;
using Swashbuckle.AspNetCore.Filters;

namespace G4Composer.Server.Examples;

/// <summary>
/// Przykładowe dane wejściowe pokazywane w Swagger / Scalar UI.
/// Nie używać tej klasy w realnej obsłudze requestu — wejścia mają iść z body!
/// </summary>
public sealed class QuadroInputListExample : IExamplesProvider<List<QuadroInput>>
{
    public List<QuadroInput> GetExamples() =>
    [
        new QuadroInput
        {
            Name        = "1hap_js12B",
            Sequence    = "ggttggtgtggttgg",
            Structure   = "AB..BA...AB..BA",
            Chi         = "S...S....S...S.",
            Orient      = "A+;B-",
            Rise        = "3.4",
            Twist       = "19",
            Path        = ["A1", "B1", "B4", "A4", "A3", "B3", "B2", "A2"],
            Sugar = "S",
        },
    ];
}

