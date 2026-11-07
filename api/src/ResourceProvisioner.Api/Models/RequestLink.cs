namespace ResourceProvisioner.Api.Models;

/// <summary>A short reference to another request: what a request depends on, or what depends on it.</summary>
/// <param name="Id">Request ID.</param>
/// <param name="Type">The resource type's ID.</param>
/// <param name="DisplayName">The resource type's display name.</param>
/// <param name="Name">The resource's <c>name</c> variable, if it has one.</param>
/// <param name="Status">Where it has got to.</param>
public record RequestLink(Guid Id, string Type, string DisplayName, string? Name, RequestStatus Status);
