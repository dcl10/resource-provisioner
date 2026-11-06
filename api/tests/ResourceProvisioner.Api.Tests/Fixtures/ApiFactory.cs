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
using Services.Keycloak;

/// <summary>
/// Test host for integration tests. The database is an in-memory SQLite connection kept open for the fixture's
/// lifetime, so tests need neither Docker nor Postgres.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  /// <summary>
  /// The fixture catalog copied next to the test assembly: <c>test-server</c> and <c>test-firewall</c>, fake types
  /// that cover every variable type. The host loads it instead of the repo's <c>catalog/</c>.
  /// </summary>
  public static readonly string FixtureCatalogRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog");

  private readonly SqliteConnection _connection = new("DataSource=:memory:");

  /// <summary>The fake Keycloak admin API the app uses, shared by every request to this factory's host.</summary>
  public InMemoryKeycloakUserAdmin Keycloak { get; } = new();

  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

    builder.ConfigureAppConfiguration(config =>
      config.AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:Root"] = FixtureCatalogRoot }));

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

      services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
    });
  }

  /// <inheritdoc />
  protected override IHost CreateHost(IHostBuilder builder)
  {
    _connection.Open();

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
      _connection.Dispose();
    }
  }
}
