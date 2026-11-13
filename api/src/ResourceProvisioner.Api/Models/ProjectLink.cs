namespace ResourceProvisioner.Api.Models;

/// <summary>A short reference to a project: the one a request belongs to, or backs.</summary>
/// <param name="Id">Project ID.</param>
/// <param name="Name">The project's name.</param>
public record ProjectLink(Guid Id, string Name);
