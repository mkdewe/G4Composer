using G4Composer.Server.Models;

namespace G4Composer.Server.Validation;

/// <summary>Generyczny kontrakt walidatora.</summary>
public interface IValidator<in T>
{
    ValidationResult Validate(T instance);
}

/// <summary>Wynik walidacji (lista błędów).</summary>
public sealed class ValidationResult
{
    public IReadOnlyList<string> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    private ValidationResult(IReadOnlyList<string> errors) => Errors = errors;

    public static ValidationResult Success { get; } = new([]);
    public static ValidationResult Failure(IEnumerable<string> errors) => new([.. errors]);
}

/// <summary>
/// Waliduje pojedyncze <see cref="QuadroInput"/>. Reguły są intencjonalnie
/// luźne tam, gdzie pole jest opcjonalne - dopiero engine zdecyduje, jak
/// zinterpretować brakujące dane (defaulty / wyliczenie struktury).
/// </summary>
public sealed class QuadroInputValidator : IValidator<QuadroInput>
{
    // Lowercase only — quadro parses uppercase 'T' as T3 and throws ERROR 2.
    private static readonly HashSet<char> AllowedSequenceChars = ['a', 'c', 'g', 'u', 't'];

    public ValidationResult Validate(QuadroInput input)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Sequence))
        {
            errors.Add("Sequence is required.");
        }
        else
        {
            var invalid = input.Sequence
                .Where(c => !AllowedSequenceChars.Contains(c))
                .Distinct()
                .ToArray();

            if (invalid.Length > 0)
            {
                errors.Add(
                    $"Sequence contains invalid characters: '{string.Join("', '", invalid)}'. " +
                    "Allowed: a, c, g, u, t (lowercase only — uppercase 'T' is parsed as T3).");
            }
        }

        if (input.SugarPucker is not "N" and not "S")
            errors.Add($"SugarPucker must be 'N' (RNA) or 'S' (DNA), got: '{input.SugarPucker}'.");

        if (input.Rise <= 0)
            errors.Add($"Rise must be positive (got: {input.Rise}).");

        if (input.RmLevel < 0)
            errors.Add($"RmLevel must be non-negative (got: {input.RmLevel}).");

        if (input.Iterations <= 0)
            errors.Add($"Iterations must be positive (got: {input.Iterations}).");

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Failure(errors);
    }
}
