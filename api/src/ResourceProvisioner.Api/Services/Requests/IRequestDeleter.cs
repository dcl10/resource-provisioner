namespace ResourceProvisioner.Api.Services.Requests;

/// <summary>
/// Deletes requests that are finished with: a proposal the owner cancelled, or a resource the runner has destroyed.
/// Nothing is kept for them. Each delete is one transaction, which covers the request, its sensitive outputs, its job
/// log, its dependencies both ways, the <c>Project</c> it backs (if any) and, after a destroy, its Terraform state. It
/// joins the app database's current transaction if the caller has started one, so several deletes can go together.
/// </summary>
public interface IRequestDeleter
{
  /// <summary>
  /// Deletes a request the owner cancelled before confirming, so nothing was ever created and it has no state.
  /// Requests still awaiting confirmation that depend on it, or that are in the project it backs, are kept: they
  /// lose the link, so confirming them fails the reference or project check.
  /// </summary>
  /// <param name="requestId">The request, which must be <c>AwaitingConfirmation</c>.</param>
  /// <param name="cancellationToken">Cancels the delete; nothing is deleted then.</param>
  /// <returns>A task that completes once it's deleted.</returns>
  /// <exception cref="RequestConflictException">
  /// It's no longer awaiting confirmation (a confirm got there first), or a request that may exist depends on it.
  /// </exception>
  Task DeleteCancelledAsync(Guid requestId, CancellationToken cancellationToken);

  /// <summary>
  /// Deletes a request whose <c>terraform destroy</c> has just finished, or that never had state to destroy, along
  /// with its Terraform state. Only the runner calls this, and only after a successful destroy: a request whose destroy
  /// failed moves to <c>DestroyFailed</c> and keeps everything, its state above all, so it can be destroyed again.
  /// </summary>
  /// <param name="requestId">The request, which must be <c>Destroying</c>.</param>
  /// <param name="cancellationToken">Cancels the delete; nothing is deleted then.</param>
  /// <returns>A task that completes once it's deleted.</returns>
  /// <exception cref="RequestConflictException">
  /// It isn't <c>Destroying</c>, or a request that may exist depends on it.
  /// </exception>
  Task DeleteDestroyedAsync(Guid requestId, CancellationToken cancellationToken);
}
