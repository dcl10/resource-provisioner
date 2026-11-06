namespace ResourceProvisioner.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;

/// <summary>Who the caller is, as the API sees them.</summary>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/me")]
public class MeController(ICurrentUser currentUser) : ControllerBase
{
  /// <summary>
  /// Returns the signed-in user. Open to any signed-in user (the fallback policy), so it's also a quick way to check
  /// that a token is accepted.
  /// </summary>
  /// <returns>The user's ID, email and admin flag.</returns>
  [HttpGet]
  public MeResponse Get() => new(currentUser.UserId, currentUser.Email, currentUser.IsAdmin);
}
