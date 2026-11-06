namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Stands in for Keycloak JWT validation: reads the caller's claims from the <c>X-Test-Claims</c> header (a JSON array
/// of <c>{ Type, Value }</c>). Requests without the header are anonymous.
/// </summary>
public class TestAuthHandler(
  IOptionsMonitor<AuthenticationSchemeOptions> options,
  ILoggerFactory logger,
  UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
  /// <summary>Name of this authentication scheme.</summary>
  public const string SchemeName = "Test";

  /// <summary>Request header carrying the caller's claims.</summary>
  public const string ClaimsHeader = "X-Test-Claims";

  /// <inheritdoc />
  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue(ClaimsHeader, out var header))
    {
      return Task.FromResult(AuthenticateResult.NoResult());
    }

    var claims = (JsonSerializer.Deserialize<TestClaim[]>(header.ToString()) ?? [])
      .Select(c => new Claim(c.Type, c.Value));
    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

    return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
  }

  /// <summary>One claim in the <see cref="ClaimsHeader"/> JSON.</summary>
  /// <param name="Type">Claim type, e.g. <c>sub</c>.</param>
  /// <param name="Value">Claim value.</param>
  public record TestClaim(string Type, string Value);
}
