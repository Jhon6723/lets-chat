using LetsChat.Contracts;
using LetsChat.Domain.Entities;
using LetsChat.Domain.ValueObjects;
using LetsChat.Infrastructure.Repositories;

namespace LetsChat.IntegrationTests;

/// <summary>
/// The real SQL behind the prekey directory (phase 1.6): publish upserts,
/// atomic one-time-prekey claim, PQ last-resort serving.
/// </summary>
[Collection("postgres")]
public class PostgresPreKeyRepositoryTests(PostgresFixture fx)
{
    private readonly PostgresAccountRepository _accounts = new(fx.Factory);
    private readonly PostgresDeviceRepository _devices = new(fx.Factory);
    private readonly PostgresPreKeyRepository _prekeys = new(fx.Factory);

    private async Task<Device> SeedDevice()
    {
        var account = await _accounts.CreateAsync(
            Username.Parse($"pk{Guid.NewGuid():N}"[..20]), "hash");
        var device = new Device
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            DeviceNumber = 1,
            IdentityKeyPublic = "identity-key",
            Address = $"{account.Username.Value}.1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _devices.AddAsync(device);
        return device;
    }

    private static SignedPreKey Spk(int id) =>
        new() { Id = id, PublicKey = $"spk-{id}", Signature = $"sig-{id}" };

    private static List<OneTimePreKey> Otps(params int[] ids) =>
        ids.Select(i => new OneTimePreKey { Id = i, PublicKey = $"otp-{i}" })
            .ToList();

    [Fact]
    public async Task Fetch_returns_null_when_nothing_published()
    {
        var device = await SeedDevice();
        Assert.Null(await _prekeys.FetchBundleAsync(device.Id, device.Address));
    }

    [Fact]
    public async Task Publish_then_fetch_serves_bundle_and_consumes_otps()
    {
        var device = await SeedDevice();
        await _prekeys.PublishAsync(
            device.Id, Spk(7), Otps(1, 2, 3), pqLastResortPreKey: null);

        var seen = new HashSet<int>();
        for (var i = 0; i < 3; i++)
        {
            var bundle = await _prekeys.FetchBundleAsync(device.Id, device.Address);
            Assert.NotNull(bundle);
            Assert.Equal(device.Address, bundle!.Address);
            Assert.Equal("identity-key", bundle.IdentityKey);
            Assert.Equal("spk-7", bundle.SignedPreKey.PublicKey);
            Assert.NotNull(bundle.OneTimePreKey);
            Assert.True(seen.Add(bundle.OneTimePreKey!.Id),
                "each fetch must burn a different one-time prekey");
        }

        // Pool drained: bundle still serves with signed prekey alone.
        var last = await _prekeys.FetchBundleAsync(device.Id, device.Address);
        Assert.Null(last!.OneTimePreKey);
        Assert.Equal(0, await _prekeys.OneTimeCountAsync(device.Id));
    }

    [Fact]
    public async Task Republish_replaces_signed_prekey_and_tops_up_pool()
    {
        var device = await SeedDevice();
        await _prekeys.PublishAsync(
            device.Id, Spk(7), Otps(1, 2), pqLastResortPreKey: null);
        await _prekeys.PublishAsync(
            device.Id, Spk(8), Otps(2, 3, 4), pqLastResortPreKey: null);

        // Re-published key_id 2 was already pooled — top-up skipped it,
        // so the pool holds {1,2,3,4}, not a duplicated 2.
        Assert.Equal(4, await _prekeys.OneTimeCountAsync(device.Id));

        var bundle = await _prekeys.FetchBundleAsync(device.Id, device.Address);
        Assert.Equal("spk-8", bundle!.SignedPreKey.PublicKey);
    }

    [Fact]
    public async Task Pq_last_resort_is_served_but_never_consumed()
    {
        var device = await SeedDevice();
        var pq = new PqLastResortPreKey
        {
            Id = 3,
            PublicKey = "pq-key",
            Signature = "pq-sig",
        };
        await _prekeys.PublishAsync(device.Id, Spk(7), [], pq);

        var first = await _prekeys.FetchBundleAsync(device.Id, device.Address);
        var second = await _prekeys.FetchBundleAsync(device.Id, device.Address);

        Assert.Equal("pq-key", first!.PqLastResortPreKey!.PublicKey);
        Assert.Equal("pq-key", second!.PqLastResortPreKey!.PublicKey);
    }

    [Fact]
    public async Task Concurrent_fetches_never_share_a_one_time_prekey()
    {
        var device = await SeedDevice();
        var ids = Enumerable.Range(1, 20).ToArray();
        await _prekeys.PublishAsync(
            device.Id, Spk(7), Otps(ids), pqLastResortPreKey: null);

        var bundles = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => _prekeys.FetchBundleAsync(device.Id, device.Address)));

        var claimed = bundles.Select(b => b!.OneTimePreKey!.Id).ToList();
        Assert.Equal(20, claimed.Distinct().Count());
        Assert.Equal(0, await _prekeys.OneTimeCountAsync(device.Id));
    }
}
