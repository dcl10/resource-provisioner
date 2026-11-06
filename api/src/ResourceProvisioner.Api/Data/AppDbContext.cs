namespace ResourceProvisioner.Api.Data;

using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>The app database: Postgres in production, SQLite in tests.</summary>
/// <param name="options">Provider and connection settings.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
  /// <summary>Invitations sent by admins.</summary>
  public DbSet<Invitation> Invitations => Set<Invitation>();

  /// <inheritdoc />
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Invitation>(invitation =>
    {
      invitation.Property(x => x.Email).HasMaxLength(254);
      invitation.Property(x => x.KeycloakUserId).HasMaxLength(64);
      invitation.Property(x => x.InvitedBy).HasMaxLength(64);

      // Not unique: an address whose Keycloak user was deleted can be invited again.
      invitation.HasIndex(x => x.Email);
      invitation.HasIndex(x => x.KeycloakUserId).IsUnique();
    });
  }
}
