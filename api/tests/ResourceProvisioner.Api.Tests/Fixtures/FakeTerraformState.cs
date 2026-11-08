namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using Services.Provisioning;

/// <summary>
/// Stands in for the <c>pg</c> state backend, which needs Postgres: tests say which requests have state, since the
/// fake Terraform CLI never writes any.
/// </summary>
public class FakeTerraformState : ITerraformStateStore
{
  /// <summary>The fake backend password, so tests can check it never reaches a file or the log.</summary>
  public const string Password = "test-pg-secret";

  /// <summary>The fake <c>PG_CONN_STR</c>.</summary>
  public const string ConnectionString = $"host='postgres' user='rp' password='{Password}' sslmode='disable'";

  private readonly ConcurrentDictionary<Guid, bool> _requests = new();

  /// <summary>Gives a request state, as an apply would.</summary>
  /// <param name="requestId">The request.</param>
  public void Add(Guid requestId) => _requests[requestId] = true;

  /// <summary>Forgets every request's state.</summary>
  public void Reset() => _requests.Clear();

  /// <inheritdoc />
  public Task<bool> HasStateAsync(Guid requestId, CancellationToken cancellationToken) =>
    Task.FromResult(_requests.ContainsKey(requestId));

  /// <inheritdoc />
  public IReadOnlyDictionary<string, string> BackendEnvironment() =>
    new Dictionary<string, string>
    {
      ["PG_CONN_STR"] = ConnectionString,
      ["PG_SCHEMA_NAME"] = TerraformStateStore.Schema,
    };
}
