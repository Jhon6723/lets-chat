using LetsChat.Application.Ports;
using LetsChat.Domain.Entities;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>Postgres adapter for the refresh-token rotation port.</summary>
public sealed class PostgresRefreshTokenRepository(IDbContextFactory<LetsChatDbContext> db)
    : IRefreshTokenRepository
{
    public async Task<RefreshToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        return row is null ? null : Map(row);
    }

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        ctx.RefreshTokens.Add(new RefreshTokenRow
        {
            Id = token.Id,
            AccountId = token.AccountId,
            FamilyId = token.FamilyId,
            TokenHash = token.TokenHash,
            CreatedAt = token.CreatedAt,
            ExpiresAt = token.ExpiresAt,
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task ConsumeAsync(Guid tokenId, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.RefreshTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                t => t.ConsumedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(
                t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    private static RefreshToken Map(RefreshTokenRow row) => new()
    {
        Id = row.Id,
        AccountId = row.AccountId,
        FamilyId = row.FamilyId,
        TokenHash = row.TokenHash,
        CreatedAt = row.CreatedAt,
        ExpiresAt = row.ExpiresAt,
        ConsumedAt = row.ConsumedAt,
        RevokedAt = row.RevokedAt,
    };
}
