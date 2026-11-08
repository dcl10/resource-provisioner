namespace ResourceProvisioner.Api.Tests;

using Services.Provisioning;
using Xunit;

public class TerraformStateStoreTests
{
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
