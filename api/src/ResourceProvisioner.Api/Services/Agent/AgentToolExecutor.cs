namespace ResourceProvisioner.Api.Services.Agent;

using System.Text.Json;
using System.Text.Json.Nodes;
using Models;
using Requests;
using ResourceCatalog;

/// <summary>
/// Runs the agent's tool calls on the server, as the user who is chatting. Results are JSON for Claude, and never
/// hold a secret: sensitive variables and outputs are left out of every request response this reads.
/// </summary>
/// <param name="catalog">The resource catalog.</param>
/// <param name="requests">The request service; <c>propose_request</c> is the only way requests are created.</param>
/// <param name="logger">Logs unexpected tool failures.</param>
public partial class AgentToolExecutor(
  ICatalogService catalog,
  IProvisioningRequestService requests,
  ILogger<AgentToolExecutor> logger)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  private static readonly RequestStatus[] Finished = [RequestStatus.Cancelled, RequestStatus.Destroyed];

  /// <summary>Runs one tool call.</summary>
  /// <param name="name">The tool.</param>
  /// <param name="input">Its input.</param>
  /// <param name="user">The user chatting; requests are theirs, admins included.</param>
  /// <param name="conversationId">The conversation, recorded on proposed requests.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The result for Claude, and the proposal if one was made.</returns>
  public async Task<ToolOutcome> ExecuteAsync(
    string name,
    JsonObject input,
    RequestUser user,
    Guid conversationId,
    CancellationToken cancellationToken)
  {
    try
    {
      return name switch
      {
        AgentTools.ListResourceTypes => Ok(catalog.Types.Select(t => new
        {
          type = t.Id, t.DisplayName, t.Description, t.CostHint,
        })),
        AgentTools.GetResourceSpec => GetSpec(Str(input, "type")),
        AgentTools.ListMyResources => await ListMine(user, cancellationToken),
        AgentTools.ProposeRequest => await Propose(input, user, conversationId, cancellationToken),
        _ => Error($"There is no tool named '{name}'."),
      };
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      LogToolFailed(logger, ex, name);
      return Error("The tool failed on the server. Tell the user something went wrong and to try again later.");
    }
  }

  private ToolOutcome GetSpec(string type)
  {
    if (catalog.Find(type) is not { } manifest)
    {
      return Error($"Unknown resource type '{type}'. Call {AgentTools.ListResourceTypes} for the types that exist.");
    }

    // Sensitive variables are set by the server; the agent never sees or sets them.
    return Ok(manifest with { Variables = [.. manifest.Variables.Where(v => !v.Sensitive)] });
  }

  private async Task<ToolOutcome> ListMine(RequestUser user, CancellationToken cancellationToken)
  {
    // Only the user's own requests, even for an admin: the agent acts for the user, not as an admin.
    var own = await requests.ListAsync(user with { IsAdmin = false }, cancellationToken);
    return Ok(own
      .Where(r => !Finished.Contains(r.Status))
      .Select(r => new
      {
        requestId = r.Id, r.Type, r.DisplayName, r.Status, r.Variables, r.Outputs, r.CreatedAt,
      }));
  }

  private async Task<ToolOutcome> Propose(
    JsonObject input,
    RequestUser user,
    Guid conversationId,
    CancellationToken cancellationToken)
  {
    var type = Str(input, "type");
    var result = await requests.ProposeAsync(
      user,
      type,
      input["variables"]?.DeepClone() as JsonObject,
      conversationId,
      cancellationToken);

    if (!result.IsValid)
    {
      return Error(JsonSerializer.Serialize(new { errors = result.Errors }, Json));
    }

    var proposal = Proposals.From(result.Request!, catalog.Find(type)!);
    var content = JsonSerializer.Serialize(
      new
      {
        proposal,
        next = "The request is awaiting confirmation. Tell the user to review the card and click Confirm; nothing " +
               "is created until they do.",
      },
      Json);
    return new ToolOutcome(content, IsError: false, proposal);
  }

  private static string Str(JsonObject input, string name) =>
    input[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

  private static ToolOutcome Ok(object value) => new(JsonSerializer.Serialize(value, Json), IsError: false);

  private static ToolOutcome Error(string message) => new(message, IsError: true);

  [LoggerMessage(Level = LogLevel.Error, Message = "The agent tool {Tool} failed")]
  private static partial void LogToolFailed(ILogger logger, Exception exception, string tool);
}

/// <summary>The result of one tool call.</summary>
/// <param name="Content">The <c>tool_result</c> content for Claude.</param>
/// <param name="IsError">Whether to mark it <c>is_error</c>, so Claude corrects itself.</param>
/// <param name="Proposal">The request proposed, for the <c>proposal</c> event; null otherwise.</param>
public record ToolOutcome(string Content, bool IsError, ProposalResponse? Proposal = null);
