namespace ResourceProvisioner.Api.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

/// <summary>
/// The body of <c>POST /api/projects</c>: the <c>project</c> request's variables, checked against its manifest like
/// any other request's.
/// </summary>
public record CreateProjectRequest
{
  /// <summary>The variables, by name: <c>name</c>, and optionally <c>description</c>, <c>purpose</c> and so on.</summary>
  [Required]
  public required JsonObject Variables { get; init; }
}
