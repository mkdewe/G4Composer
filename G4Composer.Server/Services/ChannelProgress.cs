using System.Threading.Channels;
using G4Composer.Server.Models;

namespace G4Composer.Server.Services;

/// <summary>
/// Adapts <see cref="IProgress{T}"/> onto a channel so a long-running job (executing on a
/// background task) can report coarse milestones that a controller drains and forwards over
/// Server-Sent Events. <see cref="Report"/> is non-blocking; if the reader has gone away the
/// write is silently dropped.
/// </summary>
public sealed class ChannelProgress(ChannelWriter<JobProgress> writer) : IProgress<JobProgress>
{
    public void Report(JobProgress value) => writer.TryWrite(value);
}
