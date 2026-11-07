namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Threading.Channels;

/// <summary>
/// <see cref="IJobDispatcher"/> in memory. Nothing is lost on a restart: the runner re-queues every <c>Queued</c> request
/// when it starts.
/// </summary>
public class JobDispatcher : IJobDispatcher
{
  private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
    new UnboundedChannelOptions { SingleReader = true });

  /// <inheritdoc />
  public ValueTask EnqueueAsync(Guid requestId, CancellationToken cancellationToken = default) =>
    _channel.Writer.WriteAsync(requestId, cancellationToken);

  /// <inheritdoc />
  public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
    _channel.Reader.ReadAllAsync(cancellationToken);
}
