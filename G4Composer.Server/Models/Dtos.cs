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
