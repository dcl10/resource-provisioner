namespace ResourceProvisioner.Api.Models;

/// <summary>
/// One event on <c>POST /api/conversations/{id}/messages</c>: a piece of the agent's reply, a proposal, the end of the
/// reply, or an error. The kind is the SSE event type.
/// </summary>
/// <param name="Kind">Which it is.</param>
/// <param name="Text">The text delta, for <see cref="ChatEventKind.Text"/>.</param>
/// <param name="Proposal">The proposed request, for <see cref="ChatEventKind.Proposal"/>.</param>
/// <param name="Error">What went wrong, in words the user can read, for <see cref="ChatEventKind.Error"/>.</param>
public record ChatEvent(
  ChatEventKind Kind,
  string? Text = null,
  ProposalResponse? Proposal = null,
  string? Error = null)
{
  /// <summary>A piece of the reply's text.</summary>
  /// <param name="text">The delta.</param>
  /// <returns>The event.</returns>
  public static ChatEvent ForText(string text) => new(ChatEventKind.Text, Text: text);

  /// <summary>A request the agent proposed.</summary>
  /// <param name="proposal">The proposal.</param>
  /// <returns>The event.</returns>
  public static ChatEvent ForProposal(ProposalResponse proposal) => new(ChatEventKind.Proposal, Proposal: proposal);

  /// <summary>The reply is complete.</summary>
  public static readonly ChatEvent Done = new(ChatEventKind.Done);

  /// <summary>The reply stopped early.</summary>
  /// <param name="message">Why, for the user.</param>
  /// <returns>The event.</returns>
  public static ChatEvent ForError(string message) => new(ChatEventKind.Error, Error: message);
}

/// <summary>The kinds of <see cref="ChatEvent"/>.</summary>
public enum ChatEventKind
{
  /// <summary>A text delta (SSE event <c>text</c>).</summary>
  Text,

  /// <summary>A proposed request for the user to confirm (SSE event <c>proposal</c>).</summary>
  Proposal,

  /// <summary>The reply is complete (SSE event <c>done</c>).</summary>
  Done,

  /// <summary>The reply stopped early; the stream then ends (SSE event <c>error</c>).</summary>
  Error,
}
