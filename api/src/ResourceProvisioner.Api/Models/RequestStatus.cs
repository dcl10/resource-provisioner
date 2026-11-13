namespace ResourceProvisioner.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Where a provisioning request has got to. The legal moves are in <c>RequestStateMachine</c>. Stored and serialized
/// by name (<c>"Queued"</c>), not number. There is no final status: a cancelled proposal and a destroyed resource are
/// deleted (<c>IRequestDeleter</c>), not kept.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequestStatus>))]
public enum RequestStatus
{
  /// <summary>Proposed by the agent; nothing happens until the owner confirms.</summary>
  AwaitingConfirmation,

  /// <summary>Confirmed by the owner and waiting for the runner.</summary>
  Queued,

  /// <summary>The runner is applying the Terraform module.</summary>
  Applying,

  /// <summary>Provisioned; the resource exists and its outputs are stored.</summary>
  Succeeded,

  /// <summary>Applying or destroying failed. Resources may be left behind, so it can still be destroyed.</summary>
  Failed,

  /// <summary>The runner is destroying the resource. Once it's destroyed, the request is deleted.</summary>
  Destroying,
}
