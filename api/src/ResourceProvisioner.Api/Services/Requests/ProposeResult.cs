namespace ResourceProvisioner.Api.Services.Requests;

using Models;

/// <summary>The outcome of proposing a request: the new request, or why it was refused.</summary>
/// <param name="Request">The request, created in <c>AwaitingConfirmation</c>; null when refused.</param>
/// <param name="Errors">
/// Problems keyed by field path (<c>size</c>, <c>droplet_request_ids[0]</c>, <c>type</c>), for the agent to fix and
/// try again. Empty when the request was created.
/// </param>
public record ProposeResult(ProvisioningRequestResponse? Request, IReadOnlyDictionary<string, string[]> Errors)
{
  /// <summary>Whether the request was created.</summary>
  public bool IsValid => Request is not null;
}
