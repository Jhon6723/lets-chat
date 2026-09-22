using LetsChat.Application.Ports;
using LetsChat.Contracts;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>
/// Postgres adapter for the prekey directory (ADR-0006 revised 2026-09-21).
/// Only public material lives here. The one-time prekey claim is a single
/// DELETE ... FOR UPDATE SKIP LOCKED ... RETURNING statement — two
/// concurrent fetches can never receive the same one-time prekey.
/// </summary>
public sealed class PostgresPreKeyRepository(IDbContextFactory<LetsChatDbContext> db)
    : IPreKeyRepository
{
    public async Task PublishAsync(
        Guid deviceId,
        SignedPreKey signedPreKey,
        IReadOnlyList<OneTimePreKey> oneTimePreKeys,
        PqLastResortPreKey? pqLastResortPreKey,
        CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);

        // Upsert the signed prekey: one current row per device.
        await ctx.SignedPreKeys
            .Where(s => s.DeviceId == deviceId)
            .ExecuteDeleteAsync(ct);
        ctx.SignedPreKeys.Add(new SignedPreKeyRow
        {
            DeviceId = deviceId,
            KeyId = signedPreKey.Id,
            PublicKey = signedPreKey.PublicKey,
            Signature = signedPreKey.Signature,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // PQ last-resort: replace only when the client sends a new one.
        if (pqLastResortPreKey is not null)
        {
            await ctx.PqLastResortPreKeys
                .Where(p => p.DeviceId == deviceId)
                .ExecuteDeleteAsync(ct);
            ctx.PqLastResortPreKeys.Add(new PqLastResortPreKeyRow
            {
                DeviceId = deviceId,
                KeyId = pqLastResortPreKey.Id,
                PublicKey = pqLastResortPreKey.PublicKey,
                Signature = pqLastResortPreKey.Signature,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        // Top-up semantics: skip key_ids already in the pool so a retry or a
        // partial batch never violates the unique index.
        var existingIds = await ctx.OneTimePreKeys
            .Where(o => o.DeviceId == deviceId)
            .Select(o => o.KeyId)
            .ToListAsync(ct);
        var known = existingIds.ToHashSet();

        foreach (var otp in oneTimePreKeys.Where(o => !known.Contains(o.Id)))
        {
            ctx.OneTimePreKeys.Add(new OneTimePreKeyRow
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                KeyId = otp.Id,
                PublicKey = otp.PublicKey,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await ctx.SaveChangesAsync(ct);
    }

    public async Task<PreKeyBundle?> FetchBundleAsync(
        Guid deviceId, string address, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);

        var signed = await ctx.SignedPreKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.DeviceId == deviceId, ct);
        if (signed is null) return null;

        var device = await ctx.Devices
            .AsNoTracking()
            .SingleAsync(d => d.Id == deviceId, ct);

        var oneTime = await ClaimOneTimePreKeyAsync(ctx, deviceId, ct);

        var pq = await ctx.PqLastResortPreKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.DeviceId == deviceId, ct);

        return new PreKeyBundle
        {
            Address = address,
            IdentityKey = device.IdentityKeyPublic,
            SignedPreKey = Map(signed),
            OneTimePreKey = oneTime,
            PqLastResortPreKey = pq is null ? null : Map(pq),
        };
    }

    public async Task<int> OneTimeCountAsync(
        Guid deviceId, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.OneTimePreKeys
            .CountAsync(o => o.DeviceId == deviceId, ct);
    }

    /// <summary>
    /// Atomically claim one OTP: single-statement delete under SKIP LOCKED,
    /// so concurrent fetches never collide on the same key.
    /// </summary>
    private static async Task<OneTimePreKey?> ClaimOneTimePreKeyAsync(
        LetsChatDbContext ctx, Guid deviceId, CancellationToken ct)
    {
        var conn = ctx.Database.GetDbConnection();
        await ctx.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM one_time_prekeys
            WHERE id = (
                SELECT id FROM one_time_prekeys
                WHERE device_id = @device
                ORDER BY key_id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING key_id, public_key;
            """;
        cmd.Parameters.Add(new NpgsqlParameter("device", deviceId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new OneTimePreKey
        {
            Id = reader.GetInt32(0),
            PublicKey = reader.GetString(1),
        };
    }

    private static SignedPreKey Map(SignedPreKeyRow row) => new()
    {
        Id = row.KeyId,
        PublicKey = row.PublicKey,
        Signature = row.Signature,
    };

    private static PqLastResortPreKey Map(PqLastResortPreKeyRow row) => new()
    {
        Id = row.KeyId,
        PublicKey = row.PublicKey,
        Signature = row.Signature,
    };
}
