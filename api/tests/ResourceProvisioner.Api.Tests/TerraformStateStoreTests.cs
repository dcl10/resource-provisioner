namespace ResourceProvisioner.Api.Tests;

using Data;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Provisioning;
using Xunit;

public class TerraformStateStoreTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task HasStateAsync_ReturnsFalse_BeforeTerraformCreatesTheTable()
  {
    // Its own database, so the table the other tests create isn't there.
    await using var fresh = new ApiFactory();
    using var scope = fresh.Services.CreateScope();
    var store = new TerraformStateStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());

    Assert.False(await store.HasStateAsync(Guid.NewGuid(), CancellationToken.None));
  }

  [Fact]
  public async Task HasStateAsync_ReturnsTrue_OnlyForWorkspacesWithState()
  {
    var withState = Guid.NewGuid().ToString();
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // The table Terraform's pg backend creates on first use, with one row per workspace.
    await db.Database.ExecuteSqlRawAsync(
      """
      CREATE SCHEMA IF NOT EXISTS terraform_state;
      CREATE TABLE IF NOT EXISTS terraform_state.states (id bigserial PRIMARY KEY, name text UNIQUE, data text);
      """);
    await db.Database.ExecuteSqlAsync($"INSERT INTO terraform_state.states (name, data) VALUES ({withState}, '')");
    var store = new TerraformStateStore(db);

    Assert.True(await store.HasStateAsync(Guid.Parse(withState), CancellationToken.None));
    Assert.False(await store.HasStateAsync(Guid.NewGuid(), CancellationToken.None));
  }

  [Fact]
  public void BackendEnvironment_PointsAtTheAppDatabase()
  {
    using var scope = factory.Services.CreateScope();
    var store = new TerraformStateStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());

    var environment = store.BackendEnvironment();

    Assert.Equal(TerraformStateStore.ToLibpq(factory.ConnectionString), environment["PG_CONN_STR"]);
    Assert.Equal(TerraformStateStore.Schema, environment["PG_SCHEMA_NAME"]);
  }

  [Theory]
  [InlineData("", "disable")]
  [InlineData("SslMode=Disable;", "disable")]
  [InlineData("SslMode=Allow;", "disable")]
  [InlineData("SslMode=Prefer;", "disable")]
  [InlineData("SslMode=Require;", "require")]
  [InlineData("SslMode=VerifyCA;", "verify-ca")]
  [InlineData("SslMode=VerifyFull;", "verify-full")]
  public void ToLibpq_MapsSslMode_FromNpgsql(string sslMode, string expected)
  {
    var libpq = TerraformStateStore.ToLibpq($"{sslMode}Host=db;Database=rp;Username=rp;Password=pw");

    Assert.EndsWith($"sslmode='{expected}'", libpq, StringComparison.Ordinal);
  }

  [Fact]
  public void ToLibpq_ConvertsEveryPart_WithDefaultPort()
  {
    var libpq = TerraformStateStore.ToLibpq(
      "Host=postgres-app;Database=resource_provisioner;Username=rp;Password=dev_password");

    Assert.Equal(
      "host='postgres-app' port='5432' dbname='resource_provisioner' user='rp' password='dev_password' " +
      "sslmode='disable'",
      libpq);
  }

  [Fact]
  public void ToLibpq_EscapesQuotesAndBackslashes_InValues()
  {
    var libpq = TerraformStateStore.ToLibpq("Host=db;Port=25060;Username=rp;Password='it''s a\\b c'");

    Assert.Equal(@"host='db' port='25060' user='rp' password='it\'s a\\b c' sslmode='disable'", libpq);
  }
}
