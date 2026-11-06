namespace ResourceProvisioner.Api.Auth;

/// <summary>The signed-in user making the current request, for scoping data to its owner.</summary>
public interface ICurrentUser
{
  /// <summary>Keycloak user ID (<c>sub</c>). Store this as the owner of anything the user creates.</summary>
  /// <exception cref="InvalidOperationException">No signed-in user, or the token has no <c>sub</c>.</exception>
  string UserId { get; }

  /// <summary>Email address, if the token has one.</summary>
  string? Email { get; }

  /// <summary>Whether the user has <see cref="Roles.Admin"/>, and so can see every user's data.</summary>
  bool IsAdmin { get; }
}
