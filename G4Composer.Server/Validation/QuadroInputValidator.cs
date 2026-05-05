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
    // RNA: lowercase a,c,g,u,t — DNA: UPPERCASE A,C,G,U,T
    // Mixed case (e.g. "aggGTT") is rejected.
    private static readonly HashSet<char> AllowedLower  = ['a', 'c', 'g', 'u', 't'];
    private static readonly HashSet<char> AllowedUpper  = ['A', 'C', 'G', 'U', 'T'];
    private static readonly HashSet<char> AllowedPucker = ['N', 'S'];

    private const double MaxRise  = 10.0;
    private const double MinRise  = 1.0;
    private const double MaxTwist = 180.0;
    private const double MinTwist = 0.0;

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

        // ── SugarPucker (multi-step string) ──────────────────────────────────
        if (!string.IsNullOrWhiteSpace(input.SugarPucker))
        {
            var parts = input.SugarPucker.Split(';', StringSplitOptions.TrimEntries);
            foreach (var (part, idx) in parts.Select((p, i) => (p, i)))
            {
                if (part.Length != 1 || !AllowedPucker.Contains(part[0]))
                    errors.Add($"SugarPucker step {idx + 1}: must be 'N' or 'S', got '{part}'.");
            }
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

        bool hasLower = sequence.Any(c => AllowedLower.Contains(c));
        bool hasUpper = sequence.Any(c => AllowedUpper.Contains(c));

        if (hasLower && hasUpper)
        {
            errors.Add("Sequence cannot mix lowercase (RNA) and uppercase (DNA). Use all-lowercase for RNA or all-UPPERCASE for DNA.");
            return;
        }

        var allowed = hasUpper ? AllowedUpper : AllowedLower;
        var invalid = sequence.Where(c => !allowed.Contains(c)).Distinct().ToArray();
        if (invalid.Length > 0)
            errors.Add($"Sequence contains invalid characters: '{string.Join("', '", invalid)}'. " +
                "Use lowercase for RNA (a,c,g,u,t) or UPPERCASE for DNA (A,C,G,U,T).");
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
