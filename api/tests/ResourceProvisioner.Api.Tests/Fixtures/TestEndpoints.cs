namespace ResourceProvisioner.Api.Tests.Fixtures;

using Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Endpoints that exist only in the test host, for testing authorization before the app has real admin endpoints.
/// Mapped after the app's own pipeline, which already runs authentication and authorization.
/// </summary>
public class TestEndpoints : IStartupFilter
{
  /// <summary>An endpoint only <c>rp-admin</c> can call.</summary>
  public const string AdminOnly = "/test/admin-only";

  /// <inheritdoc />
  public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
    app =>
    {
      next(app);
      app.UseRouting();
      app.UseAuthentication();
      app.UseAuthorization();
      app.UseEndpoints(endpoints =>
        endpoints.MapGet(AdminOnly, () => Results.Ok()).RequireAuthorization(AuthPolicies.AdminOnly));
    };
}
