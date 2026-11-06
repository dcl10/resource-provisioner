namespace ResourceProvisioner.Api.Services.Invitations;

using Config;
using Data;
using Keycloak;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models;

/// <summary>
/// <see cref="IInvitationService"/> on Keycloak. Keycloak holds the user and sends the email; the <c>Invitation</c>
/// table records who was invited, by whom and when, and the link's expiry. Status is derived when read.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="keycloak">The Keycloak admin API.</param>
/// <param name="options">Invitation settings: app URL, client and expiry.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs failed clean-ups.</param>
public partial class InvitationService(
  AppDbContext db,
  IKeycloakUserAdmin keycloak,
  IOptions<InvitationOptions> options,
  TimeProvider time,
  ILogger<InvitationService> logger) : IInvitationService
{
  /// <summary>The required actions an invited user completes from the email: verify the address, set a password.</summary>
  public static readonly IReadOnlyList<string> RequiredActions = ["VERIFY_EMAIL", "UPDATE_PASSWORD"];

  private readonly InvitationOptions _options = options.Value;

  /// <inheritdoc />
  public async Task<IReadOnlyList<InvitationResponse>> ListAsync(CancellationToken cancellationToken = default)
  {
    // SQLite (tests) can't order by DateTimeOffset, so sort in memory; the table is small.
    var invitations = (await db.Invitations.AsNoTracking().ToListAsync(cancellationToken))
      .OrderByDescending(x => x.SentAt)
      .ToList();

    var now = time.GetUtcNow();
    var verified = await Task.WhenAll(invitations.Select(x => IsEmailVerified(x, cancellationToken)));

    return [.. invitations.Select((x, i) => ToResponse(x, x.StatusAt(now, verified[i])))];
  }

  /// <inheritdoc />
  public async Task<InvitationResponse> InviteAsync(
    string email,
    string invitedBy,
    CancellationToken cancellationToken = default)
  {
    email = email.Trim().ToLowerInvariant();

    if (await keycloak.FindByEmailAsync(email, cancellationToken) is not null)
    {
      throw new InvitationConflictException($"{email} already has an account.");
    }

    string userId;
    try
    {
      userId = await keycloak.CreateUserAsync(email, cancellationToken);
    }
    catch (KeycloakUserExistsException)
    {
      // Another user has this as their username, or someone invited the same address at the same moment.
      throw new InvitationConflictException($"{email} already has an account.");
    }

    var invitation = new Invitation { Email = email, KeycloakUserId = userId, InvitedBy = invitedBy };

    try
    {
      await Send(invitation, cancellationToken);
      db.Invitations.Add(invitation);
      await db.SaveChangesAsync(cancellationToken);
    }
    catch
    {
      // Without an invitation the user could never be sent a link, and the address couldn't be invited again.
      await DeleteUserQuietly(userId);
      throw;
    }

    return ToResponse(invitation, InvitationStatus.Pending);
  }

  /// <inheritdoc />
  public async Task<InvitationResponse> ResendAsync(Guid id, CancellationToken cancellationToken = default)
  {
    var invitation = await Find(id, cancellationToken);

    var status = await StatusOf(invitation, cancellationToken);
    if (status is InvitationStatus.Accepted or InvitationStatus.Revoked)
    {
      throw new InvitationConflictException(
        $"The invitation to {invitation.Email} was {status.ToString().ToLowerInvariant()}, so it can't be resent.");
    }

    await Send(invitation, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    return ToResponse(invitation, InvitationStatus.Pending);
  }

  /// <inheritdoc />
  public async Task<InvitationResponse> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
  {
    var invitation = await Find(id, cancellationToken);

    var status = await StatusOf(invitation, cancellationToken);
    if (status is InvitationStatus.Accepted or InvitationStatus.Revoked)
    {
      throw new InvitationConflictException(
        $"The invitation to {invitation.Email} was {status.ToString().ToLowerInvariant()}, so it can't be revoked.");
    }

    await keycloak.DisableAsync(invitation.KeycloakUserId, cancellationToken);

    invitation.RevokedAt = time.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);

    return ToResponse(invitation, InvitationStatus.Revoked);
  }

  private async Task<Invitation> Find(Guid id, CancellationToken cancellationToken) =>
    await db.Invitations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ??
    throw new KeyNotFoundException($"Invitation {id} doesn't exist.");

  private async Task<InvitationStatus> StatusOf(Invitation invitation, CancellationToken cancellationToken) =>
    invitation.StatusAt(time.GetUtcNow(), await IsEmailVerified(invitation, cancellationToken));

  /// <summary>
  /// Whether the invited user has verified their email, i.e. accepted. Revoked invitations don't need Keycloak
  /// asking, and a user deleted from Keycloak counts as not verified.
  /// </summary>
  private async Task<bool> IsEmailVerified(Invitation invitation, CancellationToken cancellationToken) =>
    invitation.RevokedAt is null &&
    (await keycloak.GetAsync(invitation.KeycloakUserId, cancellationToken))?.EmailVerified == true;

  /// <summary>Sends the action email and records when it went and when its link expires.</summary>
  private async Task Send(Invitation invitation, CancellationToken cancellationToken)
  {
    var lifespan = TimeSpan.FromDays(_options.ExpiryDays);

    await keycloak.SendActionsEmailAsync(
      invitation.KeycloakUserId,
      RequiredActions,
      lifespan,
      _options.ClientId,
      _options.AppUrl,
      cancellationToken);

    var now = time.GetUtcNow();
    invitation.SentAt = now;
    invitation.ExpiresAt = now + lifespan;
  }

  private async Task DeleteUserQuietly(string userId)
  {
    try
    {
      await keycloak.DeleteAsync(userId);
    }
    catch (Exception ex)
    {
      LogDeleteFailed(logger, ex, userId);
    }
  }

  [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't delete Keycloak user {UserId} after a failed invitation")]
  private static partial void LogDeleteFailed(ILogger logger, Exception exception, string userId);

  private static InvitationResponse ToResponse(Invitation x, InvitationStatus status) =>
    new(x.Id, x.Email, status, x.InvitedBy, x.SentAt, x.ExpiresAt, x.RevokedAt);
}
