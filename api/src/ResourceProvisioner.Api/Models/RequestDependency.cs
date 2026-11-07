namespace ResourceProvisioner.Api.Models;

/// <summary>
/// One request depending on another: <see cref="RequestId"/> references <see cref="DependsOnId"/> in a variable marked
/// <c>references</c> in its manifest, so it's provisioned only after that one has succeeded, and that one can't be
/// destroyed while it exists. Written when the request is proposed; a request only references earlier ones, so there
/// are no cycles.
/// </summary>
public class RequestDependency
{
  /// <summary>The dependent request.</summary>
  public Guid RequestId { get; set; }

  /// <summary>The request it depends on.</summary>
  public Guid DependsOnId { get; set; }
}
