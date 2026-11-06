namespace ResourceProvisioner.Api.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Design-time factory for <see cref="AppDbContext"/>, so <c>dotnet ef</c> builds the context directly instead of
/// starting the web host. Reads the same connection string as the app, including user-secrets.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
  /// <inheritdoc />
  public AppDbContext CreateDbContext(string[] args)
  {
    var configuration = new ConfigurationBuilder()
      .SetBasePath(Directory.GetCurrentDirectory())
      .AddJsonFile("appsettings.json", optional: true)
      .AddJsonFile("appsettings.Development.json", optional: true)
      .AddUserSecrets<AppDbContextFactory>(optional: true)
      .AddEnvironmentVariables()
      .Build();

    // Adding a migration doesn't connect, so any well-formed connection string will do when none is configured.
    var connectionString = configuration.GetConnectionString("DefaultConnection")
                           ?? "Host=localhost;Database=resource_provisioner";

    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString);

    return new AppDbContext(optionsBuilder.Options);
  }
}
