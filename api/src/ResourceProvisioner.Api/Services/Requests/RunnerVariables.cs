namespace ResourceProvisioner.Api.Services.Requests;

using System.Text.Json.Nodes;
using Models;

/// <summary>
/// The Terraform variables the runner forces on every module, worked out from the request alone so neither the user
/// nor the LLM can choose them: <c>rp_name_suffix</c> and <c>rp_tags</c> (see docs/catalog.md).
/// </summary>
public static class RunnerVariables
{
  /// <summary>The variable holding the name suffix.</summary>
  public const string NameSuffixVariable = "rp_name_suffix";

  /// <summary>The variable holding the forced tags.</summary>
  public const string TagsVariable = "rp_tags";

  /// <summary>The tag every resource the app creates carries.</summary>
  public const string CommonTag = "resource-provisioner";

  private const int ShortLength = 8;

  /// <summary>
  /// The suffix added to the resource's DigitalOcean name: the request ID's first 8 hex digits, lowercase, so names
  /// never clash in the shared account.
  /// </summary>
  /// <param name="requestId">The request ID.</param>
  /// <returns>8 lowercase hex characters.</returns>
  public static string NameSuffix(Guid requestId) => requestId.ToString("N")[..ShortLength];

  /// <summary>
  /// A short form of the owner's <c>sub</c> for the owner tag: its first 8 lowercase letters or digits. Keycloak subs
  /// are GUIDs, so this is the GUID's first 8 hex digits. It labels the owner; ownership itself is checked by
  /// request, not by tag.
  /// </summary>
  /// <param name="ownerId">The owner's Keycloak user ID.</param>
  /// <returns>Up to 8 lowercase letters or digits.</returns>
  /// <exception cref="ArgumentException">The ID has no letters or digits.</exception>
  public static string ShortOwner(string ownerId)
  {
    var chars = ownerId.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).Take(ShortLength).ToArray();
    return chars.Length > 0
      ? new string(chars)
      : throw new ArgumentException("The owner ID has no letters or digits.", nameof(ownerId));
  }

  /// <summary>
  /// The tags forced on every resource: <c>resource-provisioner</c>, <c>rp-&lt;requestId&gt;</c> (the full lowercase
  /// GUID, which droplet references turn into) and <c>rp-owner-&lt;short-sub&gt;</c>.
  /// </summary>
  /// <param name="requestId">The request ID.</param>
  /// <param name="ownerId">The owner's Keycloak user ID.</param>
  /// <returns>The three tags.</returns>
  public static IReadOnlyList<string> Tags(Guid requestId, string ownerId) =>
    [CommonTag, RequestTag(requestId), $"rp-owner-{ShortOwner(ownerId)}"];

  /// <summary>The <c>rp-&lt;requestId&gt;</c> tag of a request, as modules build it from a droplet reference.</summary>
  /// <param name="requestId">The request ID.</param>
  /// <returns>The tag.</returns>
  public static string RequestTag(Guid requestId) => $"rp-{requestId:D}";

  /// <summary>The runner's variables for a request, to merge over its own variables in the tfvars.</summary>
  /// <param name="request">The request.</param>
  /// <returns><c>{ "rp_name_suffix": ..., "rp_tags": [...] }</c>.</returns>
  public static JsonObject For(ProvisioningRequest request) =>
    new()
    {
      [NameSuffixVariable] = NameSuffix(request.Id),
      [TagsVariable] = new JsonArray([.. Tags(request.Id, request.OwnerId).Select(t => JsonValue.Create(t))]),
    };
}
