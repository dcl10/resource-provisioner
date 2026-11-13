namespace ResourceProvisioner.Api.Services.Provisioning;

/// <summary>
/// Where Terraform keeps each request's state: the app database, through Terraform's <c>pg</c> backend, one
/// Terraform workspace per request. Behind an interface so tests, whose fake Terraform CLI never writes state, can
/// fake it.
/// </summary>
public interface ITerraformStateStore
{
  /// <summary>Whether Terraform has saved state for a request, which it does as soon as a plan or apply runs.</summary>
  /// <param name="requestId">The request ID, which is also its Terraform workspace's name.</param>
  /// <param name="cancellationToken">Cancels the query.</param>
  /// <returns>True when the request's workspace exists in the backend.</returns>
  Task<bool> HasStateAsync(Guid requestId, CancellationToken cancellationToken);

  /// <summary>
  /// Deletes a request's state once its resources are destroyed. Runs in the app database's current transaction, if
  /// there is one, so the state goes with the request or not at all. Never call it for a request whose resources may
  /// still exist: without its state, Terraform can't destroy them.
  /// </summary>
  /// <param name="requestId">The request ID, which is also its Terraform workspace's name.</param>
  /// <param name="cancellationToken">Cancels the command.</param>
  /// <returns>A task that completes once it's deleted, or straight away if there was none.</returns>
  Task DeleteStateAsync(Guid requestId, CancellationToken cancellationToken);

  /// <summary>
  /// The environment variables that point the <c>pg</c> backend at the database. They hold the database password, so
  /// they go only in Terraform's environment, never in a file or the log.
  /// </summary>
  /// <returns>Variable names and values.</returns>
  IReadOnlyDictionary<string, string> BackendEnvironment();
}
