namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using Services.Keycloak;

/// <summary>
/// Dictionary-backed <see cref="IKeycloakUserAdmin"/> for tests, which have no Keycloak. Records the action emails it
/// would have sent, and can be told to fail them.
/// </summary>
public class InMemoryKeycloakUserAdmin : IKeycloakUserAdmin
{
  private readonly ConcurrentDictionary<string, KeycloakUser> _users = new();
  private readonly ConcurrentQueue<SentEmail> _emails = new();

  /// <summary>When true, <see cref="SendActionsEmailAsync"/> throws as Keycloak does when SMTP fails.</summary>
  public bool FailEmails { get; set; }

  /// <summary>Every action email sent, oldest first.</summary>
  public IReadOnlyList<SentEmail> Emails => [.. _emails];

  /// <summary>Adds a user directly, as if created in the Keycloak admin console.</summary>
  /// <param name="email">The user's email.</param>
  /// <param name="emailVerified">Whether the email is verified.</param>
  /// <returns>The user's ID.</returns>
  public string AddUser(string email, bool emailVerified = true)
  {
    var id = Guid.NewGuid().ToString();
    _users[id] = new KeycloakUser(id, email, emailVerified, true);
    return id;
  }

  /// <summary>Gets a user, or null.</summary>
  /// <param name="userId">The user's ID.</param>
  /// <returns>The user.</returns>
  public KeycloakUser? Find(string userId) => _users.GetValueOrDefault(userId);

  /// <summary>Marks a user's email verified, as completing the action email does.</summary>
  /// <param name="userId">The user's ID.</param>
  public void VerifyEmail(string userId) => _users[userId] = _users[userId] with { EmailVerified = true };

  /// <inheritdoc />
  public Task<KeycloakUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
    Task.FromResult(_users.Values.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)));

  /// <inheritdoc />
  public Task<KeycloakUser?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
    Task.FromResult(Find(userId));

  /// <inheritdoc />
  public async Task<string> CreateUserAsync(string email, CancellationToken cancellationToken = default) =>
    await FindByEmailAsync(email, cancellationToken) is not null
      ? throw new KeycloakUserExistsException(email)
      : AddUser(email, emailVerified: false);

  /// <inheritdoc />
  public Task SendActionsEmailAsync(
    string userId,
    IReadOnlyList<string> actions,
    TimeSpan lifespan,
    string clientId,
    string redirectUri,
    CancellationToken cancellationToken = default)
  {
    if (FailEmails)
    {
      throw new KeycloakAdminException("Keycloak's admin API returned 500.");
    }

    _emails.Enqueue(new SentEmail(userId, actions, lifespan, clientId, redirectUri));
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task DisableAsync(string userId, CancellationToken cancellationToken = default)
  {
    _users[userId] = _users[userId] with { Enabled = false };
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
  {
    _users.TryRemove(userId, out _);
    return Task.CompletedTask;
  }

  /// <summary>An action email the fake was asked to send.</summary>
  /// <param name="UserId">Recipient's Keycloak user ID.</param>
  /// <param name="Actions">Required actions in the link.</param>
  /// <param name="Lifespan">How long the link works.</param>
  /// <param name="ClientId">Client the link belongs to.</param>
  /// <param name="RedirectUri">Where the link returns to.</param>
  public record SentEmail(
    string UserId,
    IReadOnlyList<string> Actions,
    TimeSpan Lifespan,
    string ClientId,
    string RedirectUri);
}
