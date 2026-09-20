using LetsChat.Application.Ports;
using LetsChat.Domain.Aggregates;
using LetsChat.Domain.ValueObjects;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>Postgres adapter for the account store port.</summary>
public sealed class PostgresAccountRepository(IDbContextFactory<LetsChatDbContext> db)
    : IAccountRepository
{
    public async Task<Account?> FindByUsernameAsync(
        Username username,
        CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Username == username.Value, ct);
        return row is null ? null : Map(row);
    }

    public async Task<Account?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<Account> CreateAsync(
        Username username,
        string passwordHash,
        CancellationToken ct = default)
    {
        var row = new AccountRow
        {
            Id = Guid.NewGuid(),
            Username = username.Value,
            PasswordHash = passwordHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var ctx = await db.CreateDbContextAsync(ct);
        ctx.Accounts.Add(row);
        await ctx.SaveChangesAsync(ct);
        return Map(row);
    }

    private static Account Map(AccountRow row) => new()
    {
        Id = row.Id,
        Username = Username.Parse(row.Username),
        PasswordHash = row.PasswordHash,
        CreatedAt = row.CreatedAt,
    };
}
