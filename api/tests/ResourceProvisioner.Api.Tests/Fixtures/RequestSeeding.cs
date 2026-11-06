namespace ResourceProvisioner.Api.Tests.Fixtures;

using Data;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Requests;

/// <summary>Puts provisioning requests straight into the test database, in any status.</summary>
public static class RequestSeeding
{
  /// <summary>Variables valid for the fixture type <c>test-server</c>.</summary>
  public const string ServerVariables = """{"name":"web-1","size":"small","node_count":1,"backups":false,"tags":[]}""";

  /// <summary>Saves a request and returns it.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="ownerId">The owner's <c>sub</c>.</param>
  /// <param name="status">Its status.</param>
  /// <param name="type">Its type; <c>test-server</c> by default.</param>
  /// <param name="variables">Its variables as JSON; valid <c>test-server</c> ones by default.</param>
  /// <param name="outputs">Its non-sensitive outputs as JSON, if any.</param>
  /// <param name="sensitiveOutputs">Its sensitive outputs as JSON, if any.</param>
  /// <returns>The saved request.</returns>
  public static async Task<ProvisioningRequest> SeedRequestAsync(
    this ApiFactory factory,
    string ownerId,
    RequestStatus status,
    string type = "test-server",
    string variables = ServerVariables,
    string? outputs = null,
    string? sensitiveOutputs = null)
  {
    var now = DateTimeOffset.UtcNow;
    var request = new ProvisioningRequest
    {
      Id = Guid.NewGuid(),
      OwnerId = ownerId,
      OwnerEmail = $"{ownerId}@example.com",
      Type = type,
      Variables = variables,
      Status = status,
      Outputs = outputs,
      CreatedAt = now,
      UpdatedAt = now,
    };
    if (sensitiveOutputs is not null)
    {
      request.SensitiveOutputs = new RequestSensitiveOutputs { Values = sensitiveOutputs };
    }

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.ProvisioningRequests.Add(request);
    await db.SaveChangesAsync();

    return request;
  }

  /// <summary>Saves a provisioned (<c>Succeeded</c>) droplet request; its type needn't be in the fixture catalog.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="ownerId">The owner's <c>sub</c>.</param>
  /// <param name="status">Its status; <c>Succeeded</c> by default.</param>
  /// <returns>The saved request.</returns>
  public static Task<ProvisioningRequest> SeedDropletAsync(
    this ApiFactory factory,
    string ownerId,
    RequestStatus status = RequestStatus.Succeeded) =>
    factory.SeedRequestAsync(ownerId, status, ProvisioningRequestService.DropletType, """{"name":"web"}""");

  /// <summary>Reads a request back from the database.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="id">Request ID.</param>
  /// <returns>The stored request, or null.</returns>
  public static async Task<ProvisioningRequest?> FindRequestAsync(this ApiFactory factory, Guid id)
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ProvisioningRequests.FindAsync(id);
  }
}
