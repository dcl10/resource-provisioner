namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Text.Json;
using Auth;
using Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Services.Agent;
using Services.Keycloak;
using Services.Provisioning;
using Services.Terraform;

/// <summary>
/// Test host for integration tests. The database is a SQLite file in the temp folder, deleted with the fixture, so
/// tests need neither Docker nor Postgres. A file rather than one shared in-memory connection, because the Terraform
/// runner and the logs endpoint use the database from other threads while a test does; each DbContext gets its own
/// connection, and SQLite's locking keeps them apart.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  /// <summary>
  /// The fixture catalog copied next to the test assembly: <c>test-server</c> and <c>test-firewall</c>, fake types
  /// that cover every variable type. The host loads it instead of the repo's <c>catalog/</c>.
  /// </summary>
  public static readonly string FixtureCatalogRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog");

  private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"rp-test-{Guid.NewGuid():N}.db");

  /// <summary>The fake Keycloak admin API the app uses, shared by every request to this factory's host.</summary>
  public InMemoryKeycloakUserAdmin Keycloak { get; } = new();

  /// <summary>The fake Claude the chat agent talks to.</summary>
  public FakeChatModel ChatModel { get; } = new();

  /// <summary>The fake Terraform CLI the job processor runs.</summary>
  public FakeTerraform Terraform { get; } = new();

  /// <summary>The fake state backend: which requests Terraform has state for.</summary>
  public FakeTerraformState TerraformState { get; } = new();

  /// <summary>A temp folder for request workspaces, deleted with the factory.</summary>
  public string WorkspacesRoot { get; } = Path.Combine(Path.GetTempPath(), $"rp-workspaces-{Guid.NewGuid():N}");

  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

    builder.ConfigureAppConfiguration(config =>
      config.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["Catalog:Root"] = FixtureCatalogRoot,
        ["Workspaces:Root"] = WorkspacesRoot,
        ["DigitalOcean:Token"] = "test-do-token",
        ["Runner:LogPollInterval"] = "00:00:00.020",
      }));

    // Tests have no Keycloak: authenticate from the X-Test-Claims header instead of a JWT.
    builder.ConfigureTestServices(services =>
    {
      services
        .AddAuthentication(TestAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

      // Controllers in this test assembly (TestAdminController) aren't discovered by default; add them.
      services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);

      // Tests never call Keycloak's admin API.
      services.RemoveAll<IKeycloakUserAdmin>();
      services.AddSingleton<IKeycloakUserAdmin>(Keycloak);

      // Tests never run Terraform. The runner's background service is off so tests control when jobs run (and other
      // tests' confirmed requests stay Queued); tests that need it start a TerraformRunner themselves.
      // Tests never call Anthropic.
      services.RemoveAll<IChatModel>();
      services.AddSingleton<IChatModel>(ChatModel);

      services.RemoveAll<ITerraformProcessRunner>();
      services.AddSingleton<ITerraformProcessRunner>(Terraform);
      services.RemoveAll<ITerraformStateStore>();
      services.AddSingleton<ITerraformStateStore>(TerraformState);
      var runner = services.Single(d => d.ImplementationType == typeof(TerraformRunner));
      services.Remove(runner);
    });

    builder.ConfigureServices(services =>
    {
      // AddDbContext registers more than DbContextOptions<AppDbContext>: EF Core also adds an
      // IDbContextOptionsConfiguration<AppDbContext> per call, so removing only the options would leave Npgsql
      // registered alongside SQLite. Remove every descriptor generic over AppDbContext, plus the context itself.
      var dbDescriptors = services
        .Where(d => d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
        .ToList();

      foreach (var descriptor in dbDescriptors)
      {
        services.Remove(descriptor);
      }

      services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_databasePath}"));
    });
  }

  /// <inheritdoc />
  protected override IHost CreateHost(IHostBuilder builder)
  {
    var host = base.CreateHost(builder);

    using var scope = host.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

    return host;
  }

  /// <summary>Creates a client that sends no credentials.</summary>
  /// <returns>An anonymous client.</returns>
  public HttpClient CreateAnonymousClient() => CreateClient();

  /// <summary>
  /// Creates a client signed in as a user with the given realm roles, carried in <c>realm_access</c> as Keycloak puts
  /// them, so the realm roles claims transformation runs as it does in production.
  /// </summary>
  /// <param name="userId">The user's <c>sub</c>.</param>
  /// <param name="email">The user's email, or null to leave the claim out.</param>
  /// <param name="roles">Realm roles; <c>rp-user</c> if none are given.</param>
  /// <returns>An authenticated client.</returns>
  public HttpClient CreateUserClient(
    string userId = "user-1",
    string? email = "user@example.com",
    params string[] roles)
  {
    var realmAccess = JsonSerializer.Serialize(new { roles = roles.Length > 0 ? roles : [Roles.User] });

    List<TestAuthHandler.TestClaim> claims =
    [
      new(KeycloakClaims.Subject, userId),
      new(KeycloakClaims.RealmAccess, realmAccess),
    ];
    if (email is not null)
    {
      claims.Add(new TestAuthHandler.TestClaim(KeycloakClaims.Email, email));
    }

    var client = CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimsHeader, JsonSerializer.Serialize(claims));

    return client;
  }

  /// <summary>Creates a client signed in as an admin (<c>rp-admin</c> and <c>rp-user</c>).</summary>
  /// <returns>An authenticated admin client.</returns>
  public HttpClient CreateAdminClient() =>
    CreateUserClient("admin-1", "admin@example.com", Roles.Admin, Roles.User);

  /// <inheritdoc />
  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);
    if (disposing)
    {
      SqliteConnection.ClearAllPools();
      File.Delete(_databasePath);
      if (Directory.Exists(WorkspacesRoot))
      {
        Directory.Delete(WorkspacesRoot, recursive: true);
      }
    }
  }
}
