namespace ResourceProvisioner.Api.Services.Keycloak;

/// <summary>
/// The Keycloak admin API calls the app makes, as the <c>rp-api</c> service account (realm-management
/// <c>manage-users</c> and <c>view-users</c> only). Behind an interface so tests can use an in-memory fake.
/// </summary>
public interface IKeycloakUserAdmin
{
  /// <summary>Finds a user by exact email address.</summary>
  /// <param name="email">The email address.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The user, or null if there's none.</returns>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be reached or refused the call.</exception>
  Task<KeycloakUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

  /// <summary>Gets a user by ID.</summary>
  /// <param name="userId">Keycloak user ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The user, or null if it no longer exists.</returns>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be reached or refused the call.</exception>
  Task<KeycloakUser?> GetAsync(string userId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Creates an enabled user with the email as username and the email not yet verified. Realm default roles
  /// (<c>rp-user</c>) are given by Keycloak, not by this call.
  /// </summary>
  /// <param name="email">The email address, also used as the username.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new user's ID.</returns>
  /// <exception cref="KeycloakUserExistsException">A user with this username or email already exists.</exception>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be reached or refused the call.</exception>
  Task<string> CreateUserAsync(string email, CancellationToken cancellationToken = default);

  /// <summary>
  /// Emails the user a link to complete the given required actions (Keycloak's <c>execute-actions-email</c>).
  /// </summary>
  /// <param name="userId">Keycloak user ID.</param>
  /// <param name="actions">Required actions, e.g. <c>VERIFY_EMAIL</c> and <c>UPDATE_PASSWORD</c>.</param>
  /// <param name="lifespan">How long the link works.</param>
  /// <param name="clientId">Client the link belongs to.</param>
  /// <param name="redirectUri">Where the user goes once they've finished; must be valid for the client.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be reached, refused the call or couldn't send the
  /// email.</exception>
  Task SendActionsEmailAsync(
    string userId,
    IReadOnlyList<string> actions,
    TimeSpan lifespan,
    string clientId,
    string redirectUri,
    CancellationToken cancellationToken = default);

  /// <summary>Disables a user, so they can't sign in or use an emailed action link.</summary>
  /// <param name="userId">Keycloak user ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be reached or refused the call.</exception>
  Task DisableAsync(string userId, CancellationToken cancellationToken = default);

  /// <summary>Deletes a user. Used to undo a user created for an invitation whose email couldn't be sent.</summary>
  /// <param name="userId">Keycloak user ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be reached or refused the call.</exception>
  Task DeleteAsync(string userId, CancellationToken cancellationToken = default);
}
