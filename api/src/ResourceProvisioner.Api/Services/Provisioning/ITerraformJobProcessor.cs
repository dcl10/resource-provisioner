namespace ResourceProvisioner.Api.Services.Provisioning;

/// <summary>Runs one job for the Terraform runner: whatever the request's status says is due.</summary>
public interface ITerraformJobProcessor
{
  /// <summary>
  /// Applies a <c>Queued</c> request (<c>Applying</c>, then <c>Succeeded</c> with its outputs stored, or
  /// <c>Failed</c>) once everything it depends on has succeeded, or destroys a <c>Destroying</c> one once everything
  /// that depends on it has gone (then deletes it with everything stored for it, its Terraform state included, or
  /// moves it to <c>DestroyFailed</c> keeping it all, and the requests waiting for it back to the status they had).
  /// Until then it does nothing, and the runner queues it again. A request in any other status, or one that no longer
  /// exists, is left alone, so a duplicate or stale queue entry is harmless.
  /// </summary>
  /// <param name="requestId">Request ID.</param>
  /// <param name="cancellationToken">Stops the job (the app is shutting down); the request is left as it is.</param>
  /// <returns>A task that completes when the job is done.</returns>
  Task RunAsync(Guid requestId, CancellationToken cancellationToken);
}
