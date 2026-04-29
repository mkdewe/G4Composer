using G4Composer.Api.Models;
using Swashbuckle.AspNetCore.Filters;

namespace G4Composer.Server.Examples;

public class Quadro11InputListExample : IExamplesProvider<List<QuadroInput>>
{
    public List<QuadroInput> GetExamples() => GetExample();

    public static List<QuadroInput> GetExample() =>
    [
        new QuadroInput
        {
            Name = "1hap_js12B",
            Sequence = "ggttggtgtggttgg",   // małe litery — wymagane przez quadro14G.exe
            Structure = "AB..BA...AB..BA",
            Chi = "S...S....S...S.",
            Orient = "A+;B-",
            Rise = 3.4f,
            Twist = 19.0,
            Path = new List<string> { "A1", "B1", "B4", "A4", "A3", "B3", "B2", "A2" },
            isTest = true,
            RM_Level = 5,
            Iterations = 50,
        },
    ];
}