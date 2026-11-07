namespace ResourceProvisioner.Api.Services.Provisioning;

/// <summary>Request IDs waiting for the Terraform runner, which takes them one at a time.</summary>
public interface IJobDispatcher
{
  /// <summary>Adds a request for the runner. It decides what to do from the request's status when it gets there.</summary>
  /// <param name="requestId">Request ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>A task that completes once it's queued.</returns>
  ValueTask EnqueueAsync(Guid requestId, CancellationToken cancellationToken = default);

  /// <summary>Takes request IDs in the order they were queued, waiting for more until cancelled.</summary>
  /// <param name="cancellationToken">Stops reading.</param>
  /// <returns>The queued request IDs.</returns>
  IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}
