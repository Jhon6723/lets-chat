using LetsChat.Contracts;
using LetsChat.Domain.Entities;
using LetsChat.Domain.ValueObjects;
using LetsChat.Infrastructure.Repositories;

namespace LetsChat.IntegrationTests;

/// <summary>
/// Persistence adapters for phases 1.1–1.4 against real Postgres:
/// accounts, devices, refresh tokens and the pending-envelope mailbox.
/// </summary>
[Collection("postgres")]
public class PostgresRepositoryTests(PostgresFixture fx)
{
    private readonly PostgresAccountRepository _accounts = new(fx.Factory);
    private readonly PostgresDeviceRepository _devices = new(fx.Factory);
    private readonly PostgresRefreshTokenRepository _tokens = new(fx.Factory);
    private readonly PostgresEnvelopeRepository _envelopes = new(fx.Factory);

    private Task<Domain.Aggregates.Account> NewAccount(string prefix)
        => _accounts.CreateAsync(
            Username.Parse($"{prefix}{Guid.NewGuid():N}"[..20]), "hash");

    [Fact]
    public async Task Account_round_trips_by_id_and_username()
    {
        var account = await NewAccount("alice");

        var byId = await _accounts.FindByIdAsync(account.Id);
        var byName = await _accounts.FindByUsernameAsync(account.Username);

        Assert.Equal(account.Id, byId!.Id);
        Assert.Equal(account.Id, byName!.Id);
        Assert.Null(await _accounts.FindByUsernameAsync(Username.Parse("ghost_zz")));
    }

    [Fact]
    public async Task Device_numbers_increment_and_address_resolves()
    {
        var account = await NewAccount("carol");
        Device NewDevice(int n) => new()
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            DeviceNumber = n,
            IdentityKeyPublic = $"key-{n}",
            Address = $"{account.Username.Value}.{n}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(1, await _devices.NextDeviceNumberAsync(account.Id));
        await _devices.AddAsync(NewDevice(1));
        Assert.Equal(2, await _devices.NextDeviceNumberAsync(account.Id));
        await _devices.AddAsync(NewDevice(2));

        var found = await _devices.FindByAddressAsync(
            $"{account.Username.Value}.2");
        Assert.NotNull(found);
        Assert.Equal("key-2", found.IdentityKeyPublic);
        Assert.Null(await _devices.FindByAddressAsync("ghost.9"));
    }

    [Fact]
    public async Task Refresh_token_consume_and_family_revocation_persist()
    {
        var account = await NewAccount("alice");
        var family = Guid.NewGuid();
        RefreshToken Tok(string hash) => new()
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            FamilyId = family,
            TokenHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };

        var t1 = Tok("hash-1");
        var t2 = Tok("hash-2");
        await _tokens.AddAsync(t1);
        await _tokens.AddAsync(t2);

        await _tokens.ConsumeAsync(t1.Id);
        var consumed = await _tokens.FindByHashAsync("hash-1");
        Assert.NotNull(consumed!.ConsumedAt);

        await _tokens.RevokeFamilyAsync(family);
        Assert.NotNull((await _tokens.FindByHashAsync("hash-1"))!.RevokedAt);
        Assert.NotNull((await _tokens.FindByHashAsync("hash-2"))!.RevokedAt);
        Assert.Null(await _tokens.FindByHashAsync("nope"));
    }

    [Fact]
    public async Task Envelopes_queue_per_recipient_and_delete_on_ack()
    {
        EncryptedEnvelope Envelope(string id, string from, string to) => new()
        {
            Version = 1,
            Id = id,
            SenderAddress = from,
            RecipientAddress = to,
            Type = "message",
            Ciphertext = "Y2lwaGVy",
            Header = "aGVhZGVy",
            CreatedAt = 1_700_000_000_000,
        };

        await _envelopes.EnqueueAsync(Envelope("e1", "alice.1", "carol.1"));
        await _envelopes.EnqueueAsync(Envelope("e2", "alice.1", "carol.1"));
        await _envelopes.EnqueueAsync(Envelope("e3", "alice.1", "dave.1"));

        var carolMailbox = await _envelopes.PendingForAsync("carol.1");
        Assert.Equal(2, carolMailbox.Count);
        Assert.Equal(new[] { "e1", "e2" },
            carolMailbox.Select(e => e.Id).OrderBy(x => x).ToArray());

        // The mailbox is scoped: dave sees only his own.
        var daveMailbox = await _envelopes.PendingForAsync("dave.1");
        Assert.Single(daveMailbox);
        Assert.Equal("e3", daveMailbox[0].Id);

        await _envelopes.MarkDeliveredAsync("e1");
        var after = await _envelopes.PendingForAsync("carol.1");
        Assert.Single(after);
        Assert.Equal("e2", after[0].Id);
    }
}
