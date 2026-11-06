using Microsoft.EntityFrameworkCore;
using ResourceProvisioner.Api.Data;
using ResourceProvisioner.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppOptions();

// Postgres via EF Core. Migrations are applied with `dotnet ef database update`, never at startup.
builder.Services.AddDbContext<AppDbContext>(options =>
  options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHealthChecks();

var app = builder.Build();

// The one endpoint that stays anonymous once sign-in is required everywhere (#6), for container health checks.
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
