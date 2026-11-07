namespace ResourceProvisioner.Api.Services.Requests;

using System.Text.Json.Nodes;
using Models;
using ResourceCatalog;

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

  /// <summary>The output of a referenced request that its dependency variable carries.</summary>
  public const string DependencyOutput = "id";

  private const string SingleReferenceSuffix = "_request_id";

  /// <summary>
  /// The runner variable a reference sets, if any: a string reference named <c>&lt;x&gt;_request_id</c> (such as
  /// <c>project_request_id</c>) gives the module <c>rp_&lt;x&gt;_id</c> (<c>rp_project_id</c>), the referenced
  /// request's <c>id</c> output, since a module can't read another request's state.
  /// </summary>
  /// <param name="variable">A manifest variable.</param>
  /// <returns>The runner variable's name, or null.</returns>
  public static string? DependencyVariable(VariableSpec variable) =>
    variable.References is not null &&
    variable.Type == VariableType.String &&
    variable.Name.EndsWith(SingleReferenceSuffix, StringComparison.Ordinal) &&
    variable.Name.Length > SingleReferenceSuffix.Length
      ? $"rp_{variable.Name[..^SingleReferenceSuffix.Length]}_id"
      : null;

  /// <summary>
  /// The dependency variables for a request: for each <see cref="DependencyVariable"/> it sets, the referenced
  /// request's <see cref="DependencyOutput"/> output.
  /// </summary>
  /// <param name="manifest">The request's type.</param>
  /// <param name="variables">The request's variables.</param>
  /// <param name="outputs">Referenced requests' non-sensitive outputs (JSON objects), by request ID.</param>
  /// <returns>The variables to add to the tfvars.</returns>
  /// <exception cref="InvalidOperationException">
  /// A referenced request has no such output (it hasn't succeeded).
  /// </exception>
  public static JsonObject ForDependencies(
    ResourceManifest manifest,
    JsonObject variables,
    IReadOnlyDictionary<Guid, string?> outputs)
  {
    var result = new JsonObject();
    foreach (var variable in manifest.Variables)
    {
      if (DependencyVariable(variable) is not { } name ||
          variables[variable.Name] is not JsonValue value ||
          !value.TryGetValue<string>(out var reference) ||
          !Guid.TryParse(reference, out var id))
      {
        continue;
      }

      var output = outputs.GetValueOrDefault(id) is { } json ? JsonNode.Parse(json)?[DependencyOutput] : null;
      result[name] = output?.DeepClone() ??
                     throw new InvalidOperationException(
                       $"Request {id}, referenced by {variable.Name}, has no '{DependencyOutput}' output.");
    }

    return result;
  }

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
