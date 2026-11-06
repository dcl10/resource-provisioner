namespace ResourceProvisioner.Api.Extensions;

using Config;
using Microsoft.Extensions.Options;
using ResourceCatalog;

/// <summary>Registers the resource catalog.</summary>
public static class CatalogServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="ICatalogService"/> as a singleton loaded from <c>Catalog:Root</c>
  /// (<see cref="CatalogOptions"/>, bound by <see cref="ServiceCollectionExtensions.AddAppOptions"/>), and loads it
  /// when the host starts so an invalid manifest stops startup.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddCatalog(this IServiceCollection services)
  {
    services.AddSingleton<ICatalogService>(sp =>
    {
      var root = sp.GetRequiredService<IOptions<CatalogOptions>>().Value.Root;
      var contentRoot = sp.GetService<IHostEnvironment>()?.ContentRootPath ?? Directory.GetCurrentDirectory();
      return new CatalogService(CatalogLoader.Load(CatalogLoader.ResolveRoot(root, contentRoot)));
    });
    services.AddHostedService<CatalogStartupCheck>();

    return services;
  }
}
