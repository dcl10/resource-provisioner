namespace ResourceProvisioner.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Models;
using ResourceCatalog;

/// <summary>The resource types the agent can provision.</summary>
/// <param name="catalog">The loaded catalog.</param>
[ApiController]
[Route("api/catalog")]
public class CatalogController(ICatalogService catalog) : ControllerBase
{
  /// <summary>Lists every resource type with its variables and outputs. Open to any signed-in user.</summary>
  /// <returns>The resource types, ordered by ID.</returns>
  [HttpGet]
  public IEnumerable<ResourceTypeResponse> Get() => catalog.Types.Select(ResourceTypeResponse.From);
}
