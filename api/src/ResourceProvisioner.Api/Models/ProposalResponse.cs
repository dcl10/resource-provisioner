namespace ResourceProvisioner.Api.Models;

/// <summary>
/// A request the agent proposed, for the confirmation card. Built from the type's manifest, so it shows every
/// variable with its label and never a sensitive one.
/// </summary>
/// <param name="RequestId">The request, in <c>AwaitingConfirmation</c> when proposed.</param>
/// <param name="Type">The resource type's ID.</param>
/// <param name="DisplayName">The resource type's display name.</param>
/// <param name="CostHint">The type's rough cost.</param>
/// <param name="Items">The variables with defaults applied, in manifest order.</param>
/// <param name="DependsOn">
/// The requests it references, provisioned before it, as they were when it was proposed. Null on proposals stored
/// before dependencies existed.
/// </param>
public record ProposalResponse(
  Guid RequestId,
  string Type,
  string DisplayName,
  string CostHint,
  IReadOnlyList<ProposalItem> Items,
  IReadOnlyList<RequestLink>? DependsOn = null);

/// <summary>One variable on the confirmation card.</summary>
/// <param name="Name">The variable name.</param>
/// <param name="Label">Its label, or the name if the manifest gives none.</param>
/// <param name="Value">Its value for display: strings as they are, anything else as JSON.</param>
public record ProposalItem(string Name, string Label, string Value);
