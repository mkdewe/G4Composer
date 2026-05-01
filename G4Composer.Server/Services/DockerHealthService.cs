namespace G4Composer.Server.Services;

/// <summary>Sprawdza dostępność Docker daemona oraz konkretnego obrazu.</summary>
public interface IDockerHealthService
{
    Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default);
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default);
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
}
