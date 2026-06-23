using G4Composer.Server.Models;
using G4Composer.Server.Validation;

namespace G4Composer.Server.Tests.Validation;

/// <summary>
/// Tests for <see cref="QuadroInputValidator"/>. Each rule in the validator gets
/// at least one happy-path and one failure case. The reference DNA sequence used
/// throughout is a parallel G4 (acgt lowercase = DNA).
/// </summary>
public class QuadroInputValidatorTests
{
    private readonly QuadroInputValidator _sut = new();

    private static QuadroInput Valid(Action<QuadroInput>? tweak = null)
    {
        var input = new QuadroInput
        {
            Name = "143d",
            Sequence = "ggggttttgggg",
            Structure = "((((....))))",
            Orient = "A+;B-",
            Rise = "3.4",
            Twist = "29",
        };
        tweak?.Invoke(input);
        return input;
    }

    // ── Happy path ─────────────────────────────────────────────────────────
    [Fact]
    public void Validate_MinimalValidInput_IsValid()
    {
        var result = _sut.Validate(Valid());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ── Sequence ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_MissingSequence_FailsWithRequired(string? sequence)
    {
        var result = _sut.Validate(Valid(i => i.Sequence = sequence!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Sequence is required"));
    }

    [Fact]
    public void Validate_UppercaseT_IsRejected()
    {
        var result = _sut.Validate(Valid(i => i.Sequence = "ggggTtttgggg"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("uppercase 'T'"));
    }

    [Fact]
    public void Validate_LowercaseU_IsRejected()
    {
        var result = _sut.Validate(Valid(i => i.Sequence = "gggguuuugggg"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("lowercase 'u'"));
    }

    [Fact]
    public void Validate_UppercaseRnaSequence_IsAccepted()
    {
        // A,C,G,U uppercase = valid RNA
        var result = _sut.Validate(Valid(i => i.Sequence = "GGGGUUUUGGGG"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidCharacters_AreReported()
    {
        var result = _sut.Validate(Valid(i => i.Sequence = "ggggxzgggg"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("invalid characters"));
    }

    // ── Structure ──────────────────────────────────────────────────────────
    [Fact]
    public void Validate_MissingStructure_Fails()
    {
        var result = _sut.Validate(Valid(i => i.Structure = ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Structure is required"));
    }

    // ── Chi ────────────────────────────────────────────────────────────────
    [Fact]
    public void Validate_ChiLengthMismatch_Fails()
    {
        var result = _sut.Validate(Valid(i => i.Chi = "S.N"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Chi length"));
    }

    [Fact]
    public void Validate_ChiInvalidChars_Fails()
    {
        // length matches sequence (12) but contains 'X'
        var result = _sut.Validate(Valid(i => i.Chi = "S...X...S..."));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Chi contains invalid"));
    }

    [Fact]
    public void Validate_ChiValid_Passes()
    {
        var result = _sut.Validate(Valid(i => i.Chi = "S..N....A..."));

        Assert.True(result.IsValid);
    }

    // ── Orient ─────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("A+;B-")]
    [InlineData("A+;B-;C+;D-")]
    public void Validate_OrientValid_Passes(string orient)
    {
        var result = _sut.Validate(Valid(i => i.Orient = orient));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("A1;B-")]   // digit instead of sign
    [InlineData("E+")]      // letter out of A-D
    [InlineData("A")]       // missing sign
    public void Validate_OrientInvalid_Fails(string orient)
    {
        var result = _sut.Validate(Valid(i => i.Orient = orient));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("is invalid"));
    }

    // ── Rise / Twist multi-value ───────────────────────────────────────────
    [Fact]
    public void Validate_MultiStepRise_Passes()
    {
        var result = _sut.Validate(Valid(i => i.Rise = "3.4;3.4;3.4"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RiseNotANumber_Fails()
    {
        var result = _sut.Validate(Valid(i => i.Rise = "3.4;abc"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Rise step 2"));
    }

    [Fact]
    public void Validate_TwistOutOfRange_Fails()
    {
        var result = _sut.Validate(Valid(i => i.Twist = "500"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("out of range"));
    }

    // ── Sugar ──────────────────────────────────────────────────────────────
    [Fact]
    public void Validate_SugarLengthMismatch_Fails()
    {
        var result = _sut.Validate(Valid(i => i.Sugar = "NS"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Sugar length"));
    }

    [Fact]
    public void Validate_SugarInvalidChars_Fails()
    {
        var result = _sut.Validate(Valid(i => i.Sugar = "NSNSNSNSNSNX"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Sugar contains invalid"));
    }

    // ── Path ───────────────────────────────────────────────────────────────
    [Fact]
    public void Validate_PathValid_Passes()
    {
        var result = _sut.Validate(Valid(i => i.Path = ["A1", "B2", "C3", "D4"]));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("A")]    // no number
    [InlineData("1A")]   // wrong order
    [InlineData("E1")]   // letter out of range
    public void Validate_PathInvalidEntry_Fails(string entry)
    {
        var result = _sut.Validate(Valid(i => i.Path = [entry]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("is invalid"));
    }

    // ── Multiple errors aggregate ──────────────────────────────────────────
    [Fact]
    public void Validate_MultipleProblems_ReportsAll()
    {
        var result = _sut.Validate(new QuadroInput
        {
            Sequence = "",       // required
            Structure = "",      // required
            Orient = "E+",       // invalid
        });

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }
}
