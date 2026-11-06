namespace ResourceProvisioner.Api.Data;

using Microsoft.EntityFrameworkCore;

/// <summary>The app database: Postgres in production, SQLite in tests.</summary>
/// <param name="options">Provider and connection settings.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
