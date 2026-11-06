namespace ResourceProvisioner.Api.Tests;

using Config;
using Extensions;
using Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ResourceCatalog;
using Xunit;

public class CatalogStartupCheckTests
{
  [Fact]
  public void CatalogOptions_DefaultsToCatalogFolderName_WhenNotConfigured()
  {
    Assert.Equal("catalog", new CatalogOptions().Root);
  }

  [Fact]
  public async Task StartAsync_LoadsCatalog_WhenManifestsAreValid()
  {
    using var provider = BuildProvider(ApiFactory.FixtureCatalogRoot);

    await StartCatalogCheck(provider);

    Assert.Equal(3, provider.GetRequiredService<ICatalogService>().Types.Count);
  }

  [Fact]
  public async Task StartAsync_Throws_WhenManifestIsInvalid()
  {
    using var catalog = new TempCatalog();
    catalog.AddType("sample", manifest: "{");
    using var provider = BuildProvider(catalog.Root);

    // Started directly rather than through a host, for the reason given in OptionsTests (#27).
    await Assert.ThrowsAsync<CatalogException>(() => StartCatalogCheck(provider));
  }

  [Fact]
  public void CatalogRoot_ComesFromConfiguration_WhenSet()
  {
    using var provider = BuildProvider("/somewhere/else");

    Assert.Equal("/somewhere/else", provider.GetRequiredService<IOptions<CatalogOptions>>().Value.Root);
  }

  private static Task StartCatalogCheck(IServiceProvider provider) =>
    provider.GetServices<IHostedService>().OfType<CatalogStartupCheck>().Single()
      .StartAsync(TestContext.Current.CancellationToken);

  private static ServiceProvider BuildProvider(string root)
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:Root"] = root })
      .Build();

    return new ServiceCollection()
      .AddSingleton<IConfiguration>(configuration)
      .AddLogging()
      .AddAppOptions()
      .AddCatalog()
      .BuildServiceProvider();
  }
}
