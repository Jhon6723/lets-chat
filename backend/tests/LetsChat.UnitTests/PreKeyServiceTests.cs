using LetsChat.Application.Services;
using LetsChat.Contracts;
using LetsChat.Domain.Entities;

namespace LetsChat.UnitTests;

/// <summary>
/// The contact-gated directory rules (phase 1.6): fetch is allowed only
/// through a contact edge — accepted both ways, or pending toward the
/// fetcher. An unknown address is Denied, not NotFound, so probing never
/// confirms that an address exists.
/// </summary>
public class PreKeyServiceTests
{
    private readonly FakeDeviceRepository _devices = new();
    private readonly FakeContactRepository _contacts = new();
    private readonly FakePreKeyRepository _prekeys = new();
    private readonly PreKeyService _service;

    public PreKeyServiceTests()
        => _service = new PreKeyService(_prekeys, _devices, _contacts);

    private Device SeedDevice(string address, Guid? accountId = null)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            AccountId = accountId ?? Guid.NewGuid(),
            DeviceNumber = 1,
            IdentityKeyPublic = "idk",
            Address = address,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _devices.Devices.Add(device);
        return device;
    }

    private void Publish(Device owner)
        => _prekeys.PublishAsync(owner.Id,
            new SignedPreKey { Id = 1, PublicKey = "spk", Signature = "sig" },
            [new OneTimePreKey { Id = 10, PublicKey = "otp" }],
            pqLastResortPreKey: null);

    private void Edge(Guid requester, Guid addressee, ContactStatus status)
        => _contacts.Edges.Add(new ContactEdge
        {
            Id = Guid.NewGuid(),
            RequesterAccountId = requester,
            AddresseeAccountId = addressee,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    [Fact]
    public async Task Fetch_is_denied_for_unknown_address()
    {
        var fetcher = SeedDevice("alice.1");
        var (result, bundle) = await _service.FetchAsync(fetcher, "ghost.9");
        Assert.Equal(PreKeyFetchResult.Denied, result);
        Assert.Null(bundle);
    }

    [Fact]
    public async Task Fetch_is_denied_without_a_contact_edge()
    {
        var fetcher = SeedDevice("alice.1");
        var owner = SeedDevice("carol.2");
        Publish(owner);

        var (result, _) = await _service.FetchAsync(fetcher, "carol.2");
        Assert.Equal(PreKeyFetchResult.Denied, result);
    }

    [Fact]
    public async Task Fetch_is_allowed_bidirectionally_once_accepted()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var fetcher = SeedDevice("alice.1", account);
        var owner = SeedDevice("carol.2", other);
        Publish(owner);
        Edge(account, other, ContactStatus.Accepted);

        var (result, bundle) = await _service.FetchAsync(fetcher, "carol.2");
        Assert.Equal(PreKeyFetchResult.Ok, result);
        Assert.NotNull(bundle);
        Assert.Equal("carol.2", bundle.Address);
    }

    [Fact]
    public async Task Fetch_pending_lets_target_inspect_requester_only()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var fetcher = SeedDevice("carol.1", account);   // carol = target
        var owner = SeedDevice("alice.2", other);        // alice = requester
        Publish(owner);
        Edge(other, account, ContactStatus.Pending);    // alice → carol pending

        Assert.Equal(PreKeyFetchResult.Ok,
            (await _service.FetchAsync(fetcher, "alice.2")).Result);

        // The reverse direction stays denied while pending.
        var requesterDevice = SeedDevice("alice.1", other);
        Assert.Equal(PreKeyFetchResult.Denied,
            (await _service.FetchAsync(requesterDevice, "carol.1")).Result);
    }

    [Fact]
    public async Task Fetch_allowed_but_nothing_published_is_NoBundle()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var fetcher = SeedDevice("alice.1", account);
        SeedDevice("carol.2", other); // registered, never published
        Edge(account, other, ContactStatus.Accepted);

        var (result, bundle) = await _service.FetchAsync(fetcher, "carol.2");
        Assert.Equal(PreKeyFetchResult.NoBundle, result);
        Assert.Null(bundle);
    }

    [Fact]
    public async Task Publish_then_fetch_consumes_one_time_prekeys()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var fetcher = SeedDevice("alice.1", account);
        var owner = SeedDevice("carol.2", other);
        Edge(account, other, ContactStatus.Accepted);

        await _service.PublishAsync(owner,
            new SignedPreKey { Id = 1, PublicKey = "spk", Signature = "sig" },
            [
                new OneTimePreKey { Id = 10, PublicKey = "otp-a" },
                new OneTimePreKey { Id = 11, PublicKey = "otp-b" },
            ],
            pqLastResortPreKey: null);

        var first = (await _service.FetchAsync(fetcher, "carol.2")).Bundle!;
        var second = (await _service.FetchAsync(fetcher, "carol.2")).Bundle!;
        var third = (await _service.FetchAsync(fetcher, "carol.2")).Bundle!;

        // Each fetch burns a different OTP; an empty pool still serves the
        // bundle with signed prekey alone.
        Assert.NotEqual(first.OneTimePreKey!.Id, second.OneTimePreKey!.Id);
        Assert.Null(third.OneTimePreKey);
        Assert.Equal("spk", third.SignedPreKey.PublicKey);
    }
}
