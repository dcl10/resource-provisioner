namespace ResourceProvisioner.Api.Tests.Fixtures;

using Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Test host for integration tests. The database is an in-memory SQLite connection kept open for the fixture's
/// lifetime, so tests need neither Docker nor Postgres.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  private readonly SqliteConnection _connection = new("DataSource=:memory:");

  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

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
