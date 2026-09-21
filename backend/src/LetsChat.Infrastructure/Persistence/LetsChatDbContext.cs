using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Persistence;

/// <summary>EF Core context — infrastructure concern, never leaves this layer.</summary>
public sealed class LetsChatDbContext(DbContextOptions<LetsChatDbContext> options)
    : DbContext(options)
{
    public DbSet<PendingEnvelopeRow> PendingEnvelopes => Set<PendingEnvelopeRow>();

    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    public DbSet<RefreshTokenRow> RefreshTokens => Set<RefreshTokenRow>();

    public DbSet<DeviceRow> Devices => Set<DeviceRow>();

    public DbSet<ContactEdgeRow> ContactEdges => Set<ContactEdgeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountRow>(b =>
        {
            b.ToTable("accounts");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Username).HasColumnName("username");
            b.Property(e => e.PasswordHash).HasColumnName("password_hash");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<RefreshTokenRow>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.FamilyId).HasColumnName("family_id");
            b.Property(e => e.TokenHash).HasColumnName("token_hash");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
            b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            b.HasIndex(e => e.TokenHash).IsUnique();
            b.HasIndex(e => e.FamilyId);
        });

        modelBuilder.Entity<DeviceRow>(b =>
        {
            b.ToTable("devices");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.DeviceNumber).HasColumnName("device_number");
            b.Property(e => e.IdentityKeyPublic).HasColumnName("identity_key_public");
            b.Property(e => e.Address).HasColumnName("address");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasIndex(e => e.Address).IsUnique();
            b.HasIndex(e => new { e.AccountId, e.DeviceNumber }).IsUnique();
            b.HasOne<AccountRow>()
                .WithMany()
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContactEdgeRow>(b =>
        {
            b.ToTable("contact_edges");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.RequesterAccountId).HasColumnName("requester_account_id");
            b.Property(e => e.AddresseeAccountId).HasColumnName("addressee_account_id");
            b.Property(e => e.Status).HasColumnName("status");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.RespondedAt).HasColumnName("responded_at");
            b.HasIndex(e => new { e.RequesterAccountId, e.AddresseeAccountId }).IsUnique();
            b.HasIndex(e => e.AddresseeAccountId);
            b.HasOne<AccountRow>()
                .WithMany()
                .HasForeignKey(e => e.RequesterAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<AccountRow>()
                .WithMany()
                .HasForeignKey(e => e.AddresseeAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PendingEnvelopeRow>(b =>
        {
            b.ToTable("pending_envelopes");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.RecipientAddress).HasColumnName("recipient_address");
            b.Property(e => e.SenderAddress).HasColumnName("sender_address");
            b.Property(e => e.Type).HasColumnName("type");
            b.Property(e => e.CreatedAt).HasColumnName("created_at_ms");
            b.HasIndex(e => e.RecipientAddress);
            b.OwnsOne(e => e.Envelope, env => env.ToJson("payload"));
        });
    }
}
