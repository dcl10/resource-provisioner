namespace ResourceProvisioner.Api.Services.Requests;

using Auth;

/// <summary>The user acting on requests: who they are and whether they can see everyone's.</summary>
/// <param name="UserId">Keycloak user ID (<c>sub</c>).</param>
/// <param name="Email">Email, if the token has one.</param>
/// <param name="IsAdmin">Whether they have <c>rp-admin</c>, which lets them see, but not act on, every request.</param>
public record RequestUser(string UserId, string? Email, bool IsAdmin)
{
  /// <summary>The signed-in user making the current HTTP request.</summary>
  /// <param name="user">The current user.</param>
  /// <returns>The same user as a <see cref="RequestUser"/>.</returns>
  public static RequestUser From(ICurrentUser user) => new(user.UserId, user.Email, user.IsAdmin);
}
