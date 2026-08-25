namespace G4Composer.Server.Services;

/// <summary>Sprawdza dostępność Docker daemona oraz konkretnego obrazu.</summary>
public interface IDockerHealthService
{
    Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default);
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Czy <paramref name="path"/> istnieje w obrazie i jest wykonywalny. Odpala kontener,
    /// więc wołaj to raz na starcie, a nie przy każdym zapytaniu o health.
    /// </summary>
    Task<bool> ExecutableExistsAsync(string image, string path, CancellationToken cancellationToken = default);
}

public sealed class DockerHealthService : IDockerHealthService
{
    private readonly IDockerCommandRunner _docker;

    public DockerHealthService(IDockerCommandRunner docker) => _docker = docker;

    public async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _docker.RunAsync(["info"], cancellationToken);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(image))
            return false;

        try
        {
            var result = await _docker.RunAsync(["image", "inspect", image], cancellationToken);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExecutableExistsAsync(string image, string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            // --entrypoint jest konieczne: obrazy quadro mają ENTRYPOINT ["/bin/bash"], więc
            // bez tego argumenty trafiłyby do basha jako nazwa skryptu, nie jako polecenie.
            var result = await _docker.RunAsync(
                ["run", "--rm", "--entrypoint", "/bin/sh", image, "-c", $"test -x '{path}'"],
                cancellationToken);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
