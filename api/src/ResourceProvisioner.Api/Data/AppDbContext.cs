namespace ResourceProvisioner.Api.Data;

using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>The app database, on Postgres (in tests too).</summary>
/// <param name="options">Provider and connection settings.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
  /// <summary>Invitations sent by admins.</summary>
  public DbSet<Invitation> Invitations => Set<Invitation>();

  /// <summary>Provisioning requests, one per resource a user asked for.</summary>
  public DbSet<ProvisioningRequest> ProvisioningRequests => Set<ProvisioningRequest>();

  /// <summary>
  /// Sensitive Terraform outputs, one row per provisioned request that has any. Read only through an explicit reveal.
  /// </summary>
  public DbSet<RequestSensitiveOutputs> RequestSensitiveOutputs => Set<RequestSensitiveOutputs>();

  /// <summary>The Terraform runner's log lines, per request.</summary>
  public DbSet<JobLogLine> JobLogLines => Set<JobLogLine>();

  /// <summary>Which requests depend on which, from their reference variables.</summary>
  public DbSet<RequestDependency> RequestDependencies => Set<RequestDependency>();

  /// <summary>Chats with the agent.</summary>
  public DbSet<Conversation> Conversations => Set<Conversation>();

  /// <summary>Every turn of every conversation, append-only.</summary>
  public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

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

    modelBuilder.Entity<ProvisioningRequest>(request =>
    {
      request.Property(x => x.OwnerId).HasMaxLength(64);
      request.Property(x => x.OwnerEmail).HasMaxLength(254);
      request.Property(x => x.Type).HasMaxLength(64);

      // By name, so the column reads well and a reordered enum can't change stored statuses. A concurrency token, so
      // a confirm racing a cancel (or the runner) fails instead of both writing.
      request.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();

      request.HasIndex(x => x.OwnerId);
      request.HasIndex(x => x.Status);

      request.HasOne(x => x.SensitiveOutputs)
        .WithOne()
        .HasForeignKey<RequestSensitiveOutputs>(x => x.RequestId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<RequestSensitiveOutputs>(outputs =>
    {
      outputs.ToTable("RequestSensitiveOutputs");
      outputs.HasKey(x => x.RequestId);
    });

    modelBuilder.Entity<RequestDependency>(dependency =>
    {
      dependency.HasKey(x => new { x.RequestId, x.DependsOnId });
      dependency.HasIndex(x => x.DependsOnId);
      dependency.HasOne<ProvisioningRequest>()
        .WithMany()
        .HasForeignKey(x => x.RequestId)
        .OnDelete(DeleteBehavior.Cascade);
      dependency.HasOne<ProvisioningRequest>()
        .WithMany()
        .HasForeignKey(x => x.DependsOnId)
        .OnDelete(DeleteBehavior.Restrict);
    });

    modelBuilder.Entity<Conversation>(conversation =>
    {
      conversation.Property(x => x.OwnerId).HasMaxLength(64);
      conversation.Property(x => x.OwnerEmail).HasMaxLength(254);
      conversation.Property(x => x.Title).HasMaxLength(Services.Agent.ChatAgent.TitleLength);
      conversation.HasIndex(x => x.OwnerId);
    });

    modelBuilder.Entity<ConversationMessage>(message =>
    {
      message.Property(x => x.Role).HasMaxLength(16);
      message.HasIndex(x => new { x.ConversationId, x.Id });
      message.HasOne<Conversation>()
        .WithMany()
        .HasForeignKey(x => x.ConversationId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<JobLogLine>(line =>
    {
      line.Property(x => x.Stream).HasConversion<string>().HasMaxLength(16);
      line.HasIndex(x => new { x.RequestId, x.Id });
      line.HasOne<ProvisioningRequest>()
        .WithMany()
        .HasForeignKey(x => x.RequestId)
        .OnDelete(DeleteBehavior.Cascade);
    });
  }
}
