using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Persistence;

/// <summary>EF Core context — infrastructure concern, never leaves this layer.</summary>
public sealed class LetsChatDbContext(DbContextOptions<LetsChatDbContext> options)
    : DbContext(options)
{
    public DbSet<PendingEnvelopeRow> PendingEnvelopes => Set<PendingEnvelopeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
