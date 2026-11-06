namespace ResourceProvisioner.Api.Models;

/// <summary>
/// A request's sensitive Terraform outputs (passwords, connection strings), stored apart from
/// <see cref="ProvisioningRequest.Outputs"/>. Never returned by the list or detail endpoints and never given to the
/// LLM; only an explicit reveal by the owner (#19) reads them.
/// </summary>
public class RequestSensitiveOutputs
{
  /// <summary>The request these belong to; also the primary key.</summary>
  public Guid RequestId { get; set; }

  /// <summary>The sensitive outputs as a JSON object.</summary>
  public required string Values { get; set; }
}
