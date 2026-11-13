namespace ResourceProvisioner.Api.Services.Agent;

using System.Text.Json.Nodes;
using Models;
using ResourceCatalog;

/// <summary>Builds the confirmation card's content from a request and its type's manifest.</summary>
public static class Proposals
{
  /// <summary>
  /// The proposal for a request: every variable it has (sensitive ones are already left out of the response), in
  /// manifest order, labelled from the manifest.
  /// </summary>
  /// <param name="request">The proposed request.</param>
  /// <param name="manifest">Its type's manifest.</param>
  /// <returns>The proposal.</returns>
  public static ProposalResponse From(ProvisioningRequestResponse request, ResourceManifest manifest)
  {
    var items = manifest.Variables
      .Where(v => !v.Sensitive && request.Variables.ContainsKey(v.Name))
      .Select(v => new ProposalItem(v.Name, v.Label ?? v.Name, Display(request.Variables[v.Name])))
      .ToList();

    return new ProposalResponse(
      request.Id,
      manifest.Id,
      manifest.DisplayName,
      manifest.CostHint,
      items,
      request.DependsOn,
      request.Project);
  }

  private static string Display(JsonNode? value) =>
    value switch
    {
      null => "none",
      JsonValue v when v.TryGetValue<string>(out var s) => s,
      _ => value.ToJsonString(),
    };
}
