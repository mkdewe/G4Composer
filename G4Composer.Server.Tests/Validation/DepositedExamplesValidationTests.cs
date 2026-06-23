using G4Composer.Server.Models;
using G4Composer.Server.Validation;

namespace G4Composer.Server.Tests.Validation;

/// <summary>
/// Regression: every deposited example in the database seeder must pass the
/// QuadroInputValidator, because the Home "Examples → Run" path sends the
/// deposited .inp straight to /api/quadro11/run.
/// </summary>
public class DepositedExamplesValidationTests
{
    private readonly QuadroInputValidator _sut = new();

    public static TheoryData<string, QuadroInput> Deposited()
    {
        QuadroInput Make(string seq, string structure, string chi,
                         string orient, string rise, string twist, string path) =>
            new()
            {
                Name = "x",
                Sequence = seq,
                Structure = structure,
                Chi = chi,
                Orient = orient,
                Rise = rise,
                Twist = twist,
                Path = [.. path.Split(';', StringSplitOptions.RemoveEmptyEntries)],
            };

        return new TheoryData<string, QuadroInput>
        {
            { "6r9k", Make(
                "gcgtgggtcagggtgggtgggacgc", "((((^^^...^^^.^^^.^^^))))", "....SSS..................",
                "A+;B+;C+", "3.4", "27;27", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4") },
            { "6r9l", Make(
                "gcgtgggtcagggtgggtgggacgc", "((((^^^...^^^.^^^.^^^))))", "....SS....S...S...S......",
                "A+;B+;C-", "3.4", "27;19", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4") },
            { "6r9k_g4", Make(
                "gggtcagggtgggtggg", "^^^...^^^.^^^.^^^", "SSS..............",
                "A+;B+;C+", "3.4", "27;27", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4") },
            { "6r9l_g4", Make(
                "gggtcagggtgggtggg", "^^^...^^^.^^^.^^^", "SS....S...S...S..",
                "A+;B+;C-", "3.4", "27;19", "A1;B1;C1;C2;B2;A2;C3;B3;A3;C4;B4;A4") },
            { "1hap", Make(
                "ggttggtgtggttgg", "AB..BA...AB..BA", "S...S....S...S.",
                "A+;B-", "3.4", "19", "A1;B1;B4;A4;A3;B3;B2;A2") },
        };
    }

    [Theory]
    [MemberData(nameof(Deposited))]
    public void DepositedExample_PassesValidation(string pdbId, QuadroInput input)
    {
        var result = _sut.Validate(input);

        Assert.True(result.IsValid,
            $"{pdbId} failed validation: {string.Join(" | ", result.Errors)}");
    }
}
