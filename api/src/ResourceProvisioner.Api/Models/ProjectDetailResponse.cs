namespace ResourceProvisioner.Api.Models;

/// <summary>
/// A project with the request that backs it and every request that belongs to it, for the project page.
/// </summary>
/// <param name="Id">Project ID.</param>
/// <param name="OwnerId">Keycloak user ID (<c>sub</c>) of the owner.</param>
/// <param name="OwnerEmail">The owner's email when it was proposed, if known.</param>
/// <param name="Name">The project's name.</param>
/// <param name="Description">Its description, if set.</param>
/// <param name="Purpose">Its purpose, if set.</param>
/// <param name="Environment">Its environment, if set.</param>
/// <param name="Status">Its status: the status of the <c>project</c> request that backs it.</param>
/// <param name="ProjectRequestId">The <c>project</c> request that backs it.</param>
/// <param name="ResourceCount">How many requests belong to it, leaving out cancelled and destroyed ones.</param>
/// <param name="CreatedAt">When it was proposed.</param>
/// <param name="Request">The <c>project</c> request that backs it.</param>
/// <param name="Resources">
/// Every request that belongs to it, newest first, each with its <c>dependsOn</c> and <c>dependents</c> (members
/// depend on <paramref name="Request"/>).
/// </param>
public record ProjectDetailResponse(
  Guid Id,
  string OwnerId,
  string? OwnerEmail,
  string Name,
  string? Description,
  string? Purpose,
  string? Environment,
  RequestStatus Status,
  Guid ProjectRequestId,
  int ResourceCount,
  DateTimeOffset CreatedAt,
  ProvisioningRequestResponse Request,
  IReadOnlyList<ProvisioningRequestResponse> Resources)
  : ProjectResponse(
    Id,
    OwnerId,
    OwnerEmail,
    Name,
    Description,
    Purpose,
    Environment,
    Status,
    ProjectRequestId,
    ResourceCount,
    CreatedAt);
