using Microsoft.EntityFrameworkCore;
using ResourceProvisioner.Api.Data;
using ResourceProvisioner.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppOptions();

// Keycloak JWTs; every endpoint requires a signed-in user unless it opts out.
builder.Services.AddKeycloakAuth(builder.Environment);

// Invitations, sent through the Keycloak admin API as the rp-api service account.
builder.Services.AddInvitations(builder.Configuration);

// Postgres via EF Core. Migrations are applied with `dotnet ef database update`, never at startup.
builder.Services.AddDbContext<AppDbContext>(options =>
  options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Resource types from catalog/<type>/resource.json, loaded at startup; an invalid manifest stops the app.
builder.Services.AddCatalog();

// Provisioning requests: proposed by the agent, confirmed or cancelled by their owner, within the active limits.
builder.Services.AddProvisioningRequests();

// The Terraform runner: applies confirmed requests one at a time, each in its own workspace with its own state.
builder.Services.AddTerraformRunner();

// The chat agent (Claude with tools generated from the catalog) and its per-user message rate limit.
builder.Services.AddChatAgent();

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// After authorization, so the chat limit is keyed by a signed-in user's sub.
app.UseRateLimiter();

app.MapControllers();

// The one anonymous endpoint, for container health checks. Not a controller: ASP.NET Core's health checks
// middleware serves it.
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
