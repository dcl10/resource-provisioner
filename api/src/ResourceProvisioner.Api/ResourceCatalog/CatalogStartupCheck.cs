namespace ResourceProvisioner.Api.ResourceCatalog;

/// <summary>
/// Loads the catalog when the host starts, so an invalid manifest stops startup instead of failing the first request
/// that needs it.
/// </summary>
/// <param name="services">The root service provider.</param>
/// <param name="logger">Logs which resource types loaded.</param>
public partial class CatalogStartupCheck(IServiceProvider services, ILogger<CatalogStartupCheck> logger)
  : IHostedService
{
  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    // Resolving the singleton runs CatalogLoader.Load, which throws CatalogException on any invalid manifest.
    var catalog = services.GetRequiredService<ICatalogService>();
    var types = string.Join(", ", catalog.Types.Select(t => t.Id));
    LogLoaded(logger, catalog.Types.Count, types);

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} resource types from the catalog: {Types}")]
  private static partial void LogLoaded(ILogger logger, int count, string types);
}
