namespace ResourceProvisioner.Api.Extensions;

using System.Threading.RateLimiting;
using Anthropic;
using Auth;
using Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Services.Agent;

/// <summary>Registers the chat agent and its rate limit.</summary>
public static class AgentServiceCollectionExtensions
{
  /// <summary>The rate limiting policy on <c>POST /api/conversations/{id}/messages</c>.</summary>
  public const string ChatRateLimitPolicy = "chat-messages";

  /// <summary>
  /// Registers the agent: the Anthropic client (with <c>Anthropic:ApiKey</c>), <see cref="IChatModel"/>, the tool
  /// executor, <see cref="IChatAgent"/> and <see cref="IConversationService"/>; plus the per-user rate limit on
  /// sending messages, <c>Limits:ChatMessagesPerMinute</c> in a fixed one-minute window keyed by the user's
  /// <c>sub</c>. Needs the catalog, the request service and the app database.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddChatAgent(this IServiceCollection services)
  {
    services.AddSingleton(sp => new AnthropicClient
    {
      ApiKey = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey,
    });
    services.AddSingleton<IChatModel, AnthropicChatModel>();
    services.AddScoped<AgentToolExecutor>();
    services.AddScoped<IChatAgent, ChatAgent>();
    services.AddScoped<IConversationService, ConversationService>();

    services.AddRateLimiter(options =>
    {
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
      options.OnRejected = async (context, cancellationToken) =>
      {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
          http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        }

        await http.Response.WriteAsJsonAsync(
          new ProblemDetails
          {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many messages",
            Detail = "You've sent too many messages in the last minute. Wait a moment and try again.",
          },
          cancellationToken);
      };

      options.AddPolicy(ChatRateLimitPolicy, http =>
      {
        var limit = http.RequestServices.GetRequiredService<IOptions<LimitsOptions>>().Value.ChatMessagesPerMinute;
        var userId = http.User.FindFirst(KeycloakClaims.Subject)?.Value ?? "";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
          PermitLimit = limit,
          Window = TimeSpan.FromMinutes(1),
          QueueLimit = 0,
        });
      });
    });

    return services;
  }
}
