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

  /// <summary>
  /// The requests whose state <see cref="DeleteStateAsync"/> was asked to delete, whether or not they had any. Not
  /// transactional, unlike the real store: a delete stays recorded even if the transaction it was in rolls back.
  /// </summary>
  public ConcurrentBag<Guid> Deleted { get; } = [];

  /// <summary>Forgets every request's state, and every delete.</summary>
  public void Reset()
  {
    _requests.Clear();
    Deleted.Clear();
  }

  /// <inheritdoc />
  public Task<bool> HasStateAsync(Guid requestId, CancellationToken cancellationToken) =>
    Task.FromResult(_requests.ContainsKey(requestId));

  /// <inheritdoc />
  public Task DeleteStateAsync(Guid requestId, CancellationToken cancellationToken)
  {
    _requests.TryRemove(requestId, out _);
    Deleted.Add(requestId);
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public IReadOnlyDictionary<string, string> BackendEnvironment() =>
    new Dictionary<string, string>
    {
      ["PG_CONN_STR"] = ConnectionString,
      ["PG_SCHEMA_NAME"] = TerraformStateStore.Schema,
    };
}
