namespace ResourceProvisioner.Api.Extensions;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Services.Projects;
using Services.Requests;

/// <summary>Registers provisioning requests.</summary>
public static class RequestServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="IProvisioningRequestService"/>, <see cref="IRequestDeleter"/> and
  /// <see cref="IProjectService"/>. Needs the catalog, the app database, the Terraform state store (registered by
  /// <see cref="RunnerServiceCollectionExtensions.AddTerraformRunner"/>) and <c>LimitsOptions</c> (bound by
  /// <see cref="ServiceCollectionExtensions.AddAppOptions"/>).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddProvisioningRequests(this IServiceCollection services)
  {
    services.AddScoped<IProvisioningRequestService, ProvisioningRequestService>();
    services.AddScoped<IRequestDeleter, RequestDeleter>();
    services.AddScoped<IProjectService, ProjectService>();
    services.TryAddSingleton(TimeProvider.System);

    return services;
  }
}
