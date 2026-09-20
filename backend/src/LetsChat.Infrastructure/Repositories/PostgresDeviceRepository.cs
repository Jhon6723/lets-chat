using LetsChat.Application.Ports;
using LetsChat.Domain.Entities;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>Postgres adapter for the device repository port.</summary>
public sealed class PostgresDeviceRepository(IDbContextFactory<LetsChatDbContext> db)
    : IDeviceRepository
{
    public async Task<int> NextDeviceNumberAsync(
        Guid accountId,
        CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var max = await ctx.Devices
            .Where(d => d.AccountId == accountId)
            .MaxAsync(d => (int?)d.DeviceNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(Device device, CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        ctx.Devices.Add(new DeviceRow
        {
            Id = device.Id,
            AccountId = device.AccountId,
            DeviceNumber = device.DeviceNumber,
            IdentityKeyPublic = device.IdentityKeyPublic,
            Address = device.Address,
            CreatedAt = device.CreatedAt,
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<Device?> FindByAddressAsync(
        string address,
        CancellationToken ct = default)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Address == address, ct);
        return row is null ? null : Map(row);
    }

    private static Device Map(DeviceRow row) => new()
    {
        Id = row.Id,
        AccountId = row.AccountId,
        DeviceNumber = row.DeviceNumber,
        IdentityKeyPublic = row.IdentityKeyPublic,
        Address = row.Address,
        CreatedAt = row.CreatedAt,
    };
}
