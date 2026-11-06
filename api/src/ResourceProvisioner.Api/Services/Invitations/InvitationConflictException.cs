namespace ResourceProvisioner.Api.Services.Invitations;

/// <summary>
/// The invitation action doesn't fit the current state: the email already has an account, or the invitation was
/// already accepted or revoked. The API answers 409.
/// </summary>
/// <param name="message">Why, in words an admin can act on.</param>
public class InvitationConflictException(string message) : Exception(message);
