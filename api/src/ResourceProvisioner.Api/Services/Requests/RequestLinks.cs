namespace ResourceProvisioner.Api.Services.Requests;

using System.Text.Json.Nodes;
using Models;
using ResourceCatalog;

/// <summary>Short descriptions of requests, for dependency links and the messages that name them.</summary>
public static class RequestLinks
{
  /// <summary>The request as a link.</summary>
  /// <param name="request">The request.</param>
  /// <param name="catalog">The catalog, for the type's display name.</param>
  /// <returns>The link.</returns>
  public static RequestLink For(ProvisioningRequest request, ICatalogService catalog) =>
    new(
      request.Id,
      request.Type,
      catalog.Find(request.Type)?.DisplayName ?? request.Type,
      NameOf(request),
      request.Status);

  /// <summary>How messages name a request: <c>Droplet "web-1" (3f2b...)</c>.</summary>
  /// <param name="link">The request.</param>
  /// <returns>The description.</returns>
  public static string Describe(RequestLink link) =>
    link.Name is null ? $"{link.DisplayName} {link.Id}" : $"{link.DisplayName} \"{link.Name}\" ({link.Id})";

  private static string? NameOf(ProvisioningRequest request) =>
    JsonNode.Parse(request.Variables)?["name"] is JsonValue value && value.TryGetValue<string>(out var name)
      ? name
      : null;
}
