using Microsoft.EntityFrameworkCore;
using ResourceProvisioner.Api.Data;
using ResourceProvisioner.Api.Endpoints;
using ResourceProvisioner.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppOptions();

// Keycloak JWTs; every endpoint requires a signed-in user unless it opts out.
builder.Services.AddKeycloakAuth(builder.Configuration, builder.Environment);

// Postgres via EF Core. Migrations are applied with `dotnet ef database update`, never at startup.
builder.Services.AddDbContext<AppDbContext>(options =>
  options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// The one anonymous endpoint, for container health checks.
app.MapHealthChecks("/health").AllowAnonymous();

app.MapMeEndpoints();

app.Run();
