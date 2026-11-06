namespace ResourceProvisioner.Api.Services.Keycloak;

using Config;
using global::Keycloak.AuthServices.Sdk.Kiota.Admin;
using global::Keycloak.AuthServices.Sdk.Kiota.Admin.Models;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions;

/// <summary>
/// <see cref="IKeycloakUserAdmin"/> over Keycloak's admin REST API, through the Kiota client. Its tokens come from the
/// <c>rp-api</c> service account (client credentials, cached and refreshed by Duende.AccessTokenManagement).
/// </summary>
/// <param name="client">The Kiota admin API client.</param>
/// <param name="options">Keycloak settings: the realm, and the secret the token handler uses.</param>
public class KeycloakUserAdmin(KeycloakAdminApiClient client, IOptions<KeycloakOptions> options) : IKeycloakUserAdmin
{
  private readonly KeycloakOptions _options = options.Value;

  /// <inheritdoc />
  public Task<KeycloakUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
    Call(async () =>
    {
      var users = await Realm.Users.GetAsync(config =>
      {
        config.QueryParameters.Email = email;
        config.QueryParameters.Exact = true;
      }, cancellationToken) ?? [];

      return users.Select(ToUser).FirstOrDefault();
    });

  /// <inheritdoc />
  public Task<KeycloakUser?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
    Call(async () =>
    {
      try
      {
        var user = await Realm.Users[userId].GetAsync(cancellationToken: cancellationToken);
        return user is null ? null : ToUser(user);
      }
      catch (ApiException ex) when (ex.ResponseStatusCode == 404)
      {
        return null;
      }
    });

  /// <inheritdoc />
  public Task<string> CreateUserAsync(string email, CancellationToken cancellationToken = default) =>
    Call(async () =>
    {
      try
      {
        await Realm.Users.PostAsync(
          new UserRepresentation { Username = email, Email = email, Enabled = true, EmailVerified = false },
          cancellationToken: cancellationToken);
      }
      catch (ApiException ex) when (ex.ResponseStatusCode == 409)
      {
        throw new KeycloakUserExistsException(email, ex);
      }

      // Keycloak returns the new ID only in the Location header, which the Kiota client doesn't surface.
      var created = await FindByEmailAsync(email, cancellationToken);

      return created?.Id ??
             throw new KeycloakAdminException($"User {email} was created but couldn't be read back from Keycloak.");
    });

  /// <inheritdoc />
  public Task SendActionsEmailAsync(
    string userId,
    IReadOnlyList<string> actions,
    TimeSpan lifespan,
    string clientId,
    string redirectUri,
    CancellationToken cancellationToken = default) =>
    Call(async () =>
    {
      await Realm.Users[userId].ExecuteActionsEmail.PutAsync([.. actions], config =>
      {
        config.QueryParameters.ClientId = clientId;
        config.QueryParameters.RedirectUri = redirectUri;
        config.QueryParameters.Lifespan = (int)lifespan.TotalSeconds;
      }, cancellationToken);

      return true;
    });

  /// <inheritdoc />
  public Task DisableAsync(string userId, CancellationToken cancellationToken = default) =>
    Call(async () =>
    {
      var existing = await Realm.Users[userId].GetAsync(cancellationToken: cancellationToken) ??
                     throw new KeycloakAdminException($"Keycloak user {userId} doesn't exist.");

      // Send the profile fields back unchanged: with Keycloak's user profile, a PUT that leaves them out can clear
      // them. Username is left out because the realm may not allow editing it.
      await Realm.Users[userId].PutAsync(new UserRepresentation
      {
        Email = existing.Email,
        FirstName = existing.FirstName,
        LastName = existing.LastName,
        EmailVerified = existing.EmailVerified,
        Enabled = false,
      }, cancellationToken: cancellationToken);

      return true;
    });

  /// <inheritdoc />
  public Task DeleteAsync(string userId, CancellationToken cancellationToken = default) =>
    Call(async () =>
    {
      await Realm.Users[userId].DeleteAsync(cancellationToken: cancellationToken);
      return true;
    });

  private global::Keycloak.AuthServices.Sdk.Kiota.Admin.Admin.Realms.Item.WithRealmItemRequestBuilder Realm =>
    client.Admin.Realms[_options.Realm];

  private static KeycloakUser ToUser(UserRepresentation user) =>
    new(user.Id ?? "", user.Email, user.EmailVerified ?? false, user.Enabled ?? false);

  /// <summary>
  /// Runs an admin API call, turning a missing secret, an unreachable Keycloak or an unexpected error response into
  /// <see cref="KeycloakAdminException"/>, so callers handle one exception type.
  /// </summary>
  private async Task<T> Call<T>(Func<Task<T>> call)
  {
    if (string.IsNullOrWhiteSpace(_options.Secret))
    {
      throw new KeycloakAdminException(
        "Keycloak:Secret isn't configured, so the API can't call the Keycloak admin API. See docs/auth-setup.md.");
    }

    try
    {
      return await call();
    }
    catch (ApiException ex)
    {
      throw new KeycloakAdminException($"Keycloak's admin API returned {ex.ResponseStatusCode}.", ex);
    }
    catch (HttpRequestException ex)
    {
      throw new KeycloakAdminException("Couldn't reach Keycloak's admin API.", ex);
    }
  }
}
