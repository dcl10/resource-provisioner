namespace ResourceProvisioner.Api.Extensions;

using Services.Provisioning;
using Services.Requests;
using Services.Terraform;

/// <summary>Registers the Terraform runner.</summary>
public static class RunnerServiceCollectionExtensions
{
  /// <summary>
  /// Registers the job queue, the runner (a hosted service that applies one job at a time), the job processor, the
  /// Terraform CLI wrapper, the Terraform state store and the log service. Needs the catalog, the app database and the <c>Runner</c>,
  /// <c>Workspaces</c>, <c>DigitalOcean</c> and <c>Spaces</c> options.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddTerraformRunner(this IServiceCollection services)
  {
    services.AddSingleton<IJobDispatcher, JobDispatcher>();
    services.AddSingleton<ITerraformProcessRunner, TerraformProcessRunner>();
    services.AddScoped<ITerraformJobProcessor, TerraformJobProcessor>();
    services.AddScoped<ITerraformStateStore, TerraformStateStore>();
    services.AddScoped<IRequestLogService, RequestLogService>();
    services.AddHostedService<TerraformRunner>();

    return services;
  }
}
