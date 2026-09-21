using LetsChat.Application.Ports;
using LetsChat.Contracts;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>
/// Prod adapter (ADR-0002): pending envelopes persist in Postgres until the
/// recipient acks them. Each operation opens a short-lived context from the
/// factory — the store itself stays singleton like the port's other impls.
/// </summary>
public sealed class PostgresEnvelopeRepository(IDbContextFactory<LetsChatDbContext> db)
    : IEnvelopeRepository
{
    public async Task EnqueueAsync(EncryptedEnvelope envelope, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        ctx.PendingEnvelopes.Add(new PendingEnvelopeRow
        {
            Id = envelope.Id,
            RecipientAddress = envelope.RecipientAddress,
            SenderAddress = envelope.SenderAddress,
            Type = envelope.Type,
            CreatedAt = envelope.CreatedAt,
            Envelope = envelope,
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EncryptedEnvelope>> PendingForAsync(
        string recipientAddress,
        CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        // AsNoTracking + row materialization: EF Core cannot project a
        // JSON-owned entity directly inside a tracked query.
        var rows = await ctx.PendingEnvelopes
            .AsNoTracking()
            .Where(r => r.RecipientAddress == recipientAddress)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(r => r.Envelope).ToList();
    }

    public async Task MarkDeliveredAsync(string envelopeId, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.PendingEnvelopes
            .Where(r => r.Id == envelopeId)
            .ExecuteDeleteAsync(ct);
    }
}
