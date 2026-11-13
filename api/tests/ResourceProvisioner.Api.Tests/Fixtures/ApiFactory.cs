namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Text.Json;
using Auth;
using Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Services.Agent;
using Services.Keycloak;
using Services.Provisioning;
using Services.Terraform;

/// <summary>
/// Test host for integration tests. Each factory gets its own Postgres database, <c>rp_test_&lt;guid&gt;</c>, on the
/// server named by <see cref="ServerConnectionVariable"/> (by default the <c>postgres-app</c> service in
/// <c>docker-compose.yml</c>), built with the real migrations and dropped with the factory, so test classes stay
/// isolated and can run in parallel. Each DbContext gets its own pooled connection, so the Terraform runner and the
/// logs endpoint can use the database from other threads while a test does.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  /// <summary>
  /// The fixture catalog copied next to the test assembly: <c>test-server</c> and <c>test-firewall</c>, fake types
  /// that cover every variable type. The host loads it instead of the repo's <c>catalog/</c>.
  /// </summary>
  public static readonly string FixtureCatalogRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog");

  /// <summary>
  /// The environment variable holding an Npgsql connection string for the Postgres server tests create their
  /// databases on. Its <c>Database</c> is the one connected to while creating and dropping them.
  /// </summary>
  public const string ServerConnectionVariable = "RP_TEST_POSTGRES";

  /// <summary>The <c>postgres-app</c> service in <c>docker-compose.yml</c>, used when the variable isn't set.</summary>
  public const string DefaultServerConnection =
    "Host=localhost;Port=5432;Database=postgres;Username=rp;Password=dev_password";

  private readonly string _serverConnection;
  private readonly string _databaseName = $"rp_test_{Guid.NewGuid():N}";
  private readonly Lock _databaseLock = new();
  private bool _databaseCreated;

  /// <summary>The connection string of this factory's own database.</summary>
  public string ConnectionString { get; }

  /// <summary>Creates a test host on a new database, created when the host first starts.</summary>
  public ApiFactory()
  {
    var server = Environment.GetEnvironmentVariable(ServerConnectionVariable);
    _serverConnection = string.IsNullOrWhiteSpace(server) ? DefaultServerConnection : server;
    ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnection) { Database = _databaseName }
      .ConnectionString;
  }

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
      // registered twice. Remove every descriptor generic over AppDbContext, plus the context itself, and register it again
      // on this factory's database.
      var dbDescriptors = services
        .Where(d => d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
        .ToList();

      foreach (var descriptor in dbDescriptors)
      {
        services.Remove(descriptor);
      }

      services.AddDbContext<AppDbContext>(o => o.UseNpgsql(ConnectionString));
    });
  }

  /// <inheritdoc />
  protected override IHost CreateHost(IHostBuilder builder)
  {
    // Hosts made with WithWebHostBuilder come through here too and share this factory's database, so it's created
    // only once; migrating it again is a no-op.
    lock (_databaseLock)
    {
      if (!_databaseCreated)
      {
        ExecuteOnServer($"CREATE DATABASE \"{_databaseName}\"");
        _databaseCreated = true;
      }
    }

    var host = base.CreateHost(builder);

    lock (_databaseLock)
    {
      using var scope = host.Services.CreateScope();
      scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

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
      if (_databaseCreated)
      {
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
          NpgsqlConnection.ClearPool(connection);
        }

        // FORCE closes any connection still open, such as a log stream that outlived its test.
        ExecuteOnServer($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        _databaseCreated = false;
      }

      if (Directory.Exists(WorkspacesRoot))
      {
        Directory.Delete(WorkspacesRoot, recursive: true);
      }
    }
  }

  private void ExecuteOnServer(string sql)
  {
    using var connection = new NpgsqlConnection(_serverConnection);
    connection.Open();
    using var command = new NpgsqlCommand(sql, connection);
    command.ExecuteNonQuery();
  }
}
