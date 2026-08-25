namespace G4Composer.Server.Models;

/// <summary>Standardowa odpowiedź błędu API.</summary>
public sealed record ErrorDto(string Message, string? Details = null)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Odpowiedź błędu zawierająca listę błędów walidacji.</summary>
public sealed record ValidationErrorDto(string Message, IReadOnlyList<string> ValidationErrors)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Status zdrowia silnika Quadro (Docker + obraz).</summary>
public sealed record HealthDto
{
    public required string Status { get; init; }
    public required string EngineVersion { get; init; }
    public bool DockerAvailable { get; init; }
    public bool ImageExists { get; init; }
    public required string ImageName { get; init; }

    /// <summary>Binarka alternatywnego przelotu wg configu aktywnego silnika (null = brak).</summary>
    public string? AlternativeExecutable { get; init; }

    /// <summary>
    /// Czy ta binarka jest obecna w <see cref="ImageName"/> — sprawdzane raz przy starcie.
    /// <c>null</c> = weryfikacja jeszcze trwa (odpala kontener, więc chwilę zajmuje);
    /// <c>false</c> przy niepustym <see cref="AlternativeExecutable"/> oznacza, że alternatywa
    /// będzie się wywalać niefatalnie i UI pokaże tylko model standardowy.
    /// Rozróżnienie jest istotne: gołe <c>false</c> tuż po restarcie wyglądałoby jak awaria.
    /// </summary>
    public bool? AlternativeAvailable { get; init; }

    /// <summary>Opis problemu z konfiguracją silnika wykrytego przy starcie, albo null.</summary>
    public string? ConfigProblem { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Wyjątek rzucany gdy polecenie Docker zwróci kod różny od zera.</summary>
public sealed class DockerException : Exception
{
    public string DockerOutput { get; }

    public DockerException(string message, string dockerOutput) : base(message)
    {
        DockerOutput = dockerOutput;
    }
}
