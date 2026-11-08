namespace ResourceProvisioner.Api.Services.Provisioning;

/// <summary>
/// Where Terraform keeps each request's state: the app database, through Terraform's <c>pg</c> backend, one
/// Terraform workspace per request. Behind an interface so tests, which run on SQLite, can fake it.
/// </summary>
public interface ITerraformStateStore
{
  /// <summary>Whether Terraform has saved state for a request, which it does as soon as a plan or apply runs.</summary>
  /// <param name="requestId">The request ID, which is also its Terraform workspace's name.</param>
  /// <param name="cancellationToken">Cancels the query.</param>
  /// <returns>True when the request's workspace exists in the backend.</returns>
  Task<bool> HasStateAsync(Guid requestId, CancellationToken cancellationToken);

  /// <summary>
  /// The environment variables that point the <c>pg</c> backend at the database. They hold the database password, so
  /// they go only in Terraform's environment, never in a file or the log.
  /// </summary>
  /// <returns>Variable names and values.</returns>
  IReadOnlyDictionary<string, string> BackendEnvironment();
}
