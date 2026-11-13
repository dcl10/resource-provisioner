namespace ResourceProvisioner.Api.Tests.Fixtures;

using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;

/// <summary>Puts provisioning requests straight into the test database, in any status.</summary>
public static class RequestSeeding
{
  /// <summary>Variables valid for the fixture type <c>test-server</c>.</summary>
  public const string ServerVariables = """{"name":"web-1","size":"small","node_count":1,"backups":false,"tags":[]}""";

  /// <summary>The name of the project <see cref="SeedRequestAsync"/> puts requests in when not given one.</summary>
  public const string DefaultProjectName = "default";

  /// <summary>
  /// Saves a request and returns it. Unless it's a <c>project</c>, it goes in a project: the one given, or the
  /// owner's <see cref="DefaultProjectName"/> project (<c>Succeeded</c>, made the first time). No dependency on the
  /// project's request is recorded; use <see cref="AddDependencyAsync"/> for that.
  /// </summary>
  /// <param name="factory">The test host.</param>
  /// <param name="ownerId">The owner's <c>sub</c>.</param>
  /// <param name="status">Its status.</param>
  /// <param name="type">Its type; <c>test-server</c> by default.</param>
  /// <param name="variables">Its variables as JSON; valid <c>test-server</c> ones by default.</param>
  /// <param name="outputs">Its non-sensitive outputs as JSON, if any.</param>
  /// <param name="sensitiveOutputs">Its sensitive outputs as JSON, if any.</param>
  /// <param name="terraformVariables">The tfvars its apply saved, if any.</param>
  /// <param name="projectId">The project it goes in; the owner's default project if null.</param>
  /// <param name="withoutProject">Put it in no project, like a request from before projects.</param>
  /// <returns>The saved request.</returns>
  public static async Task<ProvisioningRequest> SeedRequestAsync(
    this ApiFactory factory,
    string ownerId,
    RequestStatus status,
    string type = "test-server",
    string variables = ServerVariables,
    string? outputs = null,
    string? sensitiveOutputs = null,
    string? terraformVariables = null,
    Guid? projectId = null,
    bool withoutProject = false)
  {
    if (type != Project.RequestType && !withoutProject)
    {
      projectId ??= (await factory.DefaultProjectAsync(ownerId)).Id;
    }

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
      TerraformVariables = terraformVariables,
      ProjectId = type == Project.RequestType ? null : projectId,
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

  /// <summary>
  /// Saves a project and the <c>project</c> request that backs it, as proposing one does. A succeeded one has an
  /// <c>id</c> output, <c>prj-&lt;name&gt;</c>.
  /// </summary>
  /// <param name="factory">The test host.</param>
  /// <param name="ownerId">The owner's <c>sub</c>.</param>
  /// <param name="status">Its request's status; <c>Succeeded</c> by default.</param>
  /// <param name="name">Its name.</param>
  /// <returns>The saved project.</returns>
  public static async Task<Project> SeedProjectAsync(
    this ApiFactory factory,
    string ownerId,
    RequestStatus status = RequestStatus.Succeeded,
    string name = "shop")
  {
    var request = await factory.SeedRequestAsync(
      ownerId,
      status,
      type: Project.RequestType,
      variables: $$"""{"name":"{{name}}","description":"","purpose":"Web Application","environment":"Development"}""",
      outputs: status == RequestStatus.Succeeded ? $$"""{"id":"prj-{{name}}"}""" : null);
    var project = new Project
    {
      Id = Guid.NewGuid(),
      OwnerId = ownerId,
      OwnerEmail = $"{ownerId}@example.com",
      Name = name,
      Purpose = "Web Application",
      Environment = "Development",
      CreatedAt = request.CreatedAt,
      ProjectRequestId = request.Id,
    };

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Projects.Add(project);
    await db.SaveChangesAsync();

    return project;
  }

  /// <summary>The owner's <see cref="DefaultProjectName"/> project, made (<c>Succeeded</c>) the first time.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="ownerId">The owner's <c>sub</c>.</param>
  /// <returns>The project.</returns>
  public static async Task<Project> DefaultProjectAsync(this ApiFactory factory, string ownerId)
  {
    using (var scope = factory.Services.CreateScope())
    {
      var existing = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Projects
        .FirstOrDefaultAsync(p => p.OwnerId == ownerId && p.Name == DefaultProjectName);
      if (existing is not null)
      {
        return existing;
      }
    }

    return await factory.SeedProjectAsync(ownerId, name: DefaultProjectName);
  }

  /// <summary>
  /// Saves a <c>test-server</c> request, the type the fixture <c>test-database</c> references (as postgres references
  /// droplets); <c>Succeeded</c> by default.
  /// </summary>
  /// <param name="factory">The test host.</param>
  /// <param name="ownerId">The owner's <c>sub</c>.</param>
  /// <param name="status">Its status; <c>Succeeded</c> by default.</param>
  /// <returns>The saved request.</returns>
  public static Task<ProvisioningRequest> SeedServerAsync(
    this ApiFactory factory,
    string ownerId,
    RequestStatus status = RequestStatus.Succeeded) =>
    factory.SeedRequestAsync(ownerId, status);

  /// <summary>Records that one request depends on another, as proposing a request with a reference does.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="requestId">The dependent request.</param>
  /// <param name="dependsOnId">The request it depends on.</param>
  /// <returns>A task that completes once it's saved.</returns>
  public static async Task AddDependencyAsync(this ApiFactory factory, Guid requestId, Guid dependsOnId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.RequestDependencies.Add(new RequestDependency { RequestId = requestId, DependsOnId = dependsOnId });
    await db.SaveChangesAsync();
  }

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
