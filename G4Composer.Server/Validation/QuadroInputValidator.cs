using System.Globalization;
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
/// Waliduje pojedyncze <see cref="QuadroInput"/>.
/// </summary>
public sealed class QuadroInputValidator : IValidator<QuadroInput>
{
    // UPPERCASE = RNA (A,C,G,U) · lowercase = DNA (a,c,g,t) · mixed sequences allowed.
    // Lowercase 'u' is NOT accepted — quadro14L explicitly rejects it (ERROR 2).
    private static readonly HashSet<char> AllowedChars = ['A','C','G','U','a','c','g','t'];

    private const double MaxRise  = 10.0;
    private const double MinRise  = -10.0;
    private const double MaxTwist = 180.0;
    private const double MinTwist = -180.0;

    public ValidationResult Validate(QuadroInput input)
    {
        var errors = new List<string>();

        // ── Sequence ──────────────────────────────────────────────────────────
        ValidateSequence(input.Sequence, errors);

        // ── Structure ─────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(input.Structure))
            errors.Add("Structure is required.");

        // ── Chi ───────────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(input.Chi)
            && !string.IsNullOrEmpty(input.Sequence)
            && input.Chi.Length != input.Sequence.Length)
        {
            errors.Add($"Chi length ({input.Chi.Length}) must match sequence length ({input.Sequence.Length}).");
        }
        if (!string.IsNullOrEmpty(input.Chi))
        {
            var invalidChi = input.Chi.Where(c => c != 'S' && c != 'N' && c != 'A' && c != '.').Distinct().ToArray();
            if (invalidChi.Length > 0)
                errors.Add($"Chi contains invalid characters: '{string.Join("', '", invalidChi)}'. Allowed: S, N, A, .");
        }

        // ── Orient ────────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(input.Orient))
            ValidateOrient(input.Orient, errors);

        // ── Rise (multi-step string) ──────────────────────────────────────────
        ValidateMultiValueField(input.Rise, "Rise", MinRise, MaxRise, errors);

        // ── Twist (multi-step string) ─────────────────────────────────────────
        ValidateMultiValueField(input.Twist, "Twist", MinTwist, MaxTwist, errors);

        // ── Shugar (per-residue, like Chi) ───────────────────────────────────
        if (!string.IsNullOrEmpty(input.Sugar)
            && !string.IsNullOrEmpty(input.Sequence)
            && input.Sugar.Length != input.Sequence.Length)
        {
            errors.Add($"Sugar length ({input.Sugar.Length}) must match sequence length ({input.Sequence.Length}).");
        }
        if (!string.IsNullOrEmpty(input.Sugar))
        {
            var invalidShugar = input.Sugar
                .Where(c => c != 'N' && c != 'n' && c != 'S' && c != 's' && c != '.')
                .Distinct().ToArray();
            if (invalidShugar.Length > 0)
                errors.Add($"Sugar contains invalid characters: '{string.Join("', '", invalidShugar)}'. Allowed: N, S, .");
        }

        // ── Path ──────────────────────────────────────────────────────────────
        if (input.Path is { Count: > 0 })
        {
            var pathRegex = new System.Text.RegularExpressions.Regex(@"^[A-D]\d+$");
            foreach (var (entry, idx) in input.Path.Select((e, i) => (e, i)))
            {
                if (!pathRegex.IsMatch(entry ?? ""))
                    errors.Add($"Path[{idx}]: '{entry}' is invalid. Expected format: letter A-D + number (e.g. A1, B2).");
            }
        }

        // ── Iterations ────────────────────────────────────────────────────────
        if (input.Iterations <= 0)
            errors.Add($"Iterations must be positive (got: {input.Iterations}).");

        // ── RmLevel ───────────────────────────────────────────────────────────
        if (input.RmLevel < 0)
            errors.Add($"RmLevel must be non-negative (got: {input.RmLevel}).");

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Failure(errors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ValidateSequence(string? sequence, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            errors.Add("Sequence is required.");
            return;
        }

        // Uppercase T is invalid (RNA uses U, not T).
        // Lowercase u is also invalid — quadro14L rejects it with ERROR 2.
        if (sequence.Contains('T'))
            errors.Add("Sequence contains uppercase 'T' — RNA residues must use 'U' (uppercase).");
        if (sequence.Contains('u'))
            errors.Add("Sequence contains lowercase 'u' — use uppercase 'U' for RNA uridine (quadro14L rejects lowercase 'u').");

        var invalid = sequence.Where(c => !AllowedChars.Contains(c)).Distinct().ToArray();
        if (invalid.Length > 0)
            errors.Add($"Sequence contains invalid characters: '{string.Join("', '", invalid)}'. " +
                "Use UPPERCASE for RNA (A,C,G,U) or lowercase for DNA (a,c,g,t). Mixed sequences are allowed.");
    }

    private static void ValidateOrient(string orient, List<string> errors)
    {
        var parts = orient.Split(';', StringSplitOptions.TrimEntries);
        var pattern = new System.Text.RegularExpressions.Regex(@"^[A-D][+-]$");
        foreach (var (part, idx) in parts.Select((p, i) => (p, i)))
        {
            if (!pattern.IsMatch(part))
                errors.Add($"Orient[{idx}]: '{part}' is invalid. Expected format: letter A-D + sign +/- (e.g. A+, B-).");
        }
    }

    private static void ValidateMultiValueField(
        string? fieldValue, string fieldName, double min, double max, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(fieldValue)) return;

        var parts = fieldValue.Split(';', StringSplitOptions.TrimEntries);
        foreach (var (part, idx) in parts.Select((p, i) => (p, i)))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                errors.Add($"{fieldName} step {idx + 1}: '{part}' is not a valid number.");
            }
            else if (val < min || val > max)
            {
                errors.Add($"{fieldName} step {idx + 1}: {val} is out of range [{min}, {max}].");
            }
        }
    }
}
