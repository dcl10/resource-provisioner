namespace ResourceProvisioner.Api.Services.Requests;

/// <summary>
/// The action doesn't fit the request's current status (confirming a queued request), or the request changed
/// while the action ran. The API answers 409.
/// </summary>
/// <param name="message">Why, in words the user can act on.</param>
public class RequestConflictException(string message) : Exception(message);

/// <summary>
/// The user can see the request but isn't allowed to act on it: an admin confirming or cancelling someone else's.
/// The API answers 403.
/// </summary>
/// <param name="message">Why, in words the user can act on.</param>
public class RequestForbiddenException(string message) : Exception(message);

/// <summary>Confirming would go over an active-request limit. The API answers 409.</summary>
/// <param name="message">Which limit, and what to do about it.</param>
public class RequestLimitException(string message) : Exception(message);

/// <summary>
/// The request's variables are no longer valid at confirm: the catalog changed, or a referenced droplet was
/// destroyed. The API answers 422 with the errors by field path.
/// </summary>
/// <param name="errors">Problems keyed by field path, as <see cref="ResourceCatalog.VariableValidationResult"/>.</param>
public class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
  : Exception("The request's variables are no longer valid.")
{
  /// <summary>Problems keyed by field path.</summary>
  public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
