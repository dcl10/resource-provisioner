using Microsoft.EntityFrameworkCore;
using ResourceProvisioner.Api.Data;
using ResourceProvisioner.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppOptions();

// Keycloak JWTs; every endpoint requires a signed-in user unless it opts out.
builder.Services.AddKeycloakAuth(builder.Configuration, builder.Environment);

// Invitations, sent through the Keycloak admin API as the rp-api service account.
builder.Services.AddInvitations(builder.Configuration);

// Postgres via EF Core. Migrations are applied with `dotnet ef database update`, never at startup.
builder.Services.AddDbContext<AppDbContext>(options =>
  options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// The one anonymous endpoint, for container health checks. Not a controller: ASP.NET Core's health checks
// middleware serves it.
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
