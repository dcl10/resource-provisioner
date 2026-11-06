namespace ResourceProvisioner.Api.Tests.Fixtures;

using Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// A controller that exists only in the test host, for testing the <see cref="AuthPolicies.AdminOnly"/> policy before
/// the app has real admin endpoints. <see cref="ApiFactory"/> adds this assembly's controllers to the app.
/// </summary>
[ApiController]
[Route(Route)]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class TestAdminController : ControllerBase
{
  /// <summary>Route of the admin-only test endpoint.</summary>
  public const string Route = "test/admin-only";

  /// <summary>Returns 200 for admins.</summary>
  /// <returns>An empty 200 response.</returns>
  [HttpGet]
  public IActionResult Get() => Ok();
}
