using LetsChat.Application.Ports;
using LetsChat.Domain.Entities;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>Postgres adapter for the contact repository port.</summary>
public sealed class PostgresContactRepository(IDbContextFactory<LetsChatDbContext> db)
    : IContactRepository
{
    public async Task<IReadOnlyList<ContactEdge>> FindPairAsync(
        Guid a, Guid b, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var rows = await ctx.ContactEdges
            .AsNoTracking()
            .Where(e =>
                (e.RequesterAccountId == a && e.AddresseeAccountId == b)
                || (e.RequesterAccountId == b && e.AddresseeAccountId == a))
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<ContactEdge?> FindByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.ContactEdges
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ContactEdge>> ListForAccountAsync(
        Guid accountId, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var rows = await ctx.ContactEdges
            .AsNoTracking()
            .Where(e => e.RequesterAccountId == accountId
                || e.AddresseeAccountId == accountId)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task AddAsync(ContactEdge edge, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        ctx.ContactEdges.Add(new ContactEdgeRow
        {
            Id = edge.Id,
            RequesterAccountId = edge.RequesterAccountId,
            AddresseeAccountId = edge.AddresseeAccountId,
            Status = ToWire(edge.Status),
            CreatedAt = edge.CreatedAt,
            RespondedAt = edge.RespondedAt,
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ContactEdge edge, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.ContactEdges
            .Where(e => e.Id == edge.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, ToWire(edge.Status))
                .SetProperty(e => e.RespondedAt, edge.RespondedAt), ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.ContactEdges.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeletePairAsync(Guid a, Guid b, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.ContactEdges
            .Where(e =>
                (e.RequesterAccountId == a && e.AddresseeAccountId == b)
                || (e.RequesterAccountId == b && e.AddresseeAccountId == a))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> CanFetchPrekeysAsync(
        Guid fetcherAccountId, Guid ownerAccountId, CancellationToken ct = default)
    {
        if (fetcherAccountId == ownerAccountId) return true;

        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.ContactEdges
            .AsNoTracking()
            .AnyAsync(e =>
                // accepted edge in either direction
                (e.Status == "accepted"
                    && ((e.RequesterAccountId == fetcherAccountId
                            && e.AddresseeAccountId == ownerAccountId)
                        || (e.RequesterAccountId == ownerAccountId
                            && e.AddresseeAccountId == fetcherAccountId)))
                // pending owner → fetcher: the target inspects the requester
                || (e.Status == "pending"
                    && e.RequesterAccountId == ownerAccountId
                    && e.AddresseeAccountId == fetcherAccountId), ct);
    }

    private static string ToWire(ContactStatus status) => status switch
    {
        ContactStatus.Pending => "pending",
        ContactStatus.Accepted => "accepted",
        ContactStatus.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static ContactEdge Map(ContactEdgeRow row) => new()
    {
        Id = row.Id,
        RequesterAccountId = row.RequesterAccountId,
        AddresseeAccountId = row.AddresseeAccountId,
        Status = Enum.Parse<ContactStatus>(row.Status, ignoreCase: true),
        CreatedAt = row.CreatedAt,
        RespondedAt = row.RespondedAt,
    };
}
