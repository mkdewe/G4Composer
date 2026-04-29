using System.ComponentModel.DataAnnotations;

namespace G4Composer.Api.Models
{
    public class QuadroInput
    {
        /// <summary>Nazwa struktury</summary>
        public string? Name { get; set; }

        /// <summary>
        /// Sekwencja nukleotydowa. Używaj małych liter (ggttgg...).
        /// Program rozróżnia małe 't' (tymidyna RNA) od wielkiego 'T' (T3, tymidyna DNA).
        /// </summary>
        public required string Sequence { get; set; }

        /// <summary>Struktura dot-bracket, np. AB..BA...AB..BA</summary>
        public string? Structure { get; set; }

        /// <summary>Konformacja cukru na pozycjach G, np. S...S....S...S.</summary>
        public string? Chi { get; set; }

        /// <summary>Sugar pucker: N (RNA/North) lub S (DNA/South) — używany tylko do walidacji</summary>
        public string SugarPucker { get; set; } = "N";

        /// <summary>Orientacja nici, np. A+;B-</summary>
        public string? Orient { get; set; }

        /// <summary>Skok helisy (Å), domyślnie 3.4</summary>
        public float Rise { get; set; } = 3.4f;

        /// <summary>Kąt skrętu helisy (°). &gt;&gt;=29, &lt;&lt;=27, &lt;&gt;=19, &gt;&lt;=37</summary>
        public double Twist { get; set; } = 29.0;

        /// <summary>Ścieżka tetrad, np. ["A1","B1","B4","A4","A3","B3","B2","A2"]</summary>
        public List<string>? Path { get; set; }

        public bool isTest { get; set; } = true;

        public int RM_Level { get; set; } = 5;

        /// <summary>Liczba iteracji CYANA</summary>
        public int Iterations { get; set; } = 1000;
    }

    public record ErrorDto(string Message, string? Details = null)
    {
        public string Message { get; init; } = Message;
        public string? Details { get; init; } = Details;
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    }

    public record ErrorDtoWithValidationErrors(string Message, List<string> ValidationErrors)
    {
        public string Message { get; init; } = Message;
        public List<string> ValidationErrors { get; init; } = ValidationErrors;
    }

    public record HealthDto
    {
        public required string Status { get; init; }
        public bool DockerAvailable { get; init; }
        public bool ImageExists { get; init; }
        public required string ImageName { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    public class DockerException : Exception
    {
        public string DockerOutput { get; }
        public DockerException(string message, string dockerOutput) : base(message)
            => DockerOutput = dockerOutput;
    }
}