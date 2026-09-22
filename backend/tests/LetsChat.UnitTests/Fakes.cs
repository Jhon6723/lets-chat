using LetsChat.Application.Ports;
using LetsChat.Contracts;
using LetsChat.Domain.Aggregates;
using LetsChat.Domain.Entities;
using LetsChat.Domain.ValueObjects;

namespace LetsChat.UnitTests;

/// <summary>
/// In-memory implementations of the outbound ports. Service tests use these
/// for the graph/session rules; the real SQL is covered by the integration
/// suite against Testcontainers Postgres.
/// </summary>
internal sealed class FakeAccountRepository : IAccountRepository
{
    private readonly Dictionary<Guid, Account> _byId = new();

    public Account Seed(string username, string passwordHash = "hash")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Username = Username.Parse(username),
            PasswordHash = passwordHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _byId[account.Id] = account;
        return account;
    }

    public Task<Account?> FindByUsernameAsync(Username username, CancellationToken ct = default)
        => Task.FromResult(_byId.Values.SingleOrDefault(a => a.Username == username));

    public Task<Account?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<Account> CreateAsync(
        Username username, string passwordHash, CancellationToken ct = default)
        => Task.FromResult(Seed(username.Value, passwordHash));
}

internal sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    public readonly List<RefreshToken> Tokens = new();

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(Tokens.SingleOrDefault(t => t.TokenHash == tokenHash));

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task ConsumeAsync(Guid tokenId, CancellationToken ct = default)
    {
        Tokens.Single(t => t.Id == tokenId).ConsumedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        foreach (var t in Tokens.Where(t => t.FamilyId == familyId && t.RevokedAt is null))
            t.RevokedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    /// <summary>Every token of the family carries a revocation timestamp.</summary>
    public bool FamilyRevoked(Guid familyId)
        => Tokens.Where(t => t.FamilyId == familyId).All(t => t.RevokedAt is not null);
}

internal sealed class FakeDeviceRepository : IDeviceRepository
{
    public readonly List<Device> Devices = new();

    public Task<int> NextDeviceNumberAsync(Guid accountId, CancellationToken ct = default)
        => Task.FromResult(
            Devices.Where(d => d.AccountId == accountId)
                .Select(d => (int?)d.DeviceNumber).Max() + 1 ?? 1);

    public Task AddAsync(Device device, CancellationToken ct = default)
    {
        Devices.Add(device);
        return Task.CompletedTask;
    }

    public Task<Device?> FindByAddressAsync(string address, CancellationToken ct = default)
        => Task.FromResult(Devices.SingleOrDefault(d => d.Address == address));
}

internal sealed class FakeContactRepository : IContactRepository
{
    public readonly List<ContactEdge> Edges = new();

    public Task<IReadOnlyList<ContactEdge>> FindPairAsync(
        Guid a, Guid b, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ContactEdge>>(Edges
            .Where(e =>
                (e.RequesterAccountId == a && e.AddresseeAccountId == b)
                || (e.RequesterAccountId == b && e.AddresseeAccountId == a))
            .ToList());

    public Task<ContactEdge?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Edges.SingleOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<ContactEdge>> ListForAccountAsync(
        Guid accountId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ContactEdge>>(Edges
            .Where(e => e.RequesterAccountId == accountId
                || e.AddresseeAccountId == accountId)
            .ToList());

    public Task AddAsync(ContactEdge edge, CancellationToken ct = default)
    {
        Edges.Add(edge);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ContactEdge edge, CancellationToken ct = default)
    {
        var stored = Edges.Single(e => e.Id == edge.Id);
        stored.Status = edge.Status;
        stored.RespondedAt = edge.RespondedAt;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Edges.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task DeletePairAsync(Guid a, Guid b, CancellationToken ct = default)
    {
        Edges.RemoveAll(e =>
            (e.RequesterAccountId == a && e.AddresseeAccountId == b)
            || (e.RequesterAccountId == b && e.AddresseeAccountId == a));
        return Task.CompletedTask;
    }

    public Task<bool> CanFetchPrekeysAsync(
        Guid fetcherAccountId, Guid ownerAccountId, CancellationToken ct = default)
        => Task.FromResult(
            fetcherAccountId == ownerAccountId
            || Edges.Any(e =>
                (e.Status == ContactStatus.Accepted
                    && ((e.RequesterAccountId == fetcherAccountId
                            && e.AddresseeAccountId == ownerAccountId)
                        || (e.RequesterAccountId == ownerAccountId
                            && e.AddresseeAccountId == fetcherAccountId)))
                || (e.Status == ContactStatus.Pending
                    && e.RequesterAccountId == ownerAccountId
                    && e.AddresseeAccountId == fetcherAccountId)));
}

internal sealed class FakePreKeyRepository : IPreKeyRepository
{
    public readonly Dictionary<Guid, SignedPreKey> Signed = new();
    public readonly Dictionary<Guid, Queue<OneTimePreKey>> OneTime = new();
    public readonly Dictionary<Guid, PqLastResortPreKey> Pq = new();

    public Task PublishAsync(
        Guid deviceId,
        SignedPreKey signedPreKey,
        IReadOnlyList<OneTimePreKey> oneTimePreKeys,
        PqLastResortPreKey? pqLastResortPreKey,
        CancellationToken ct = default)
    {
        Signed[deviceId] = signedPreKey;
        if (pqLastResortPreKey is not null) Pq[deviceId] = pqLastResortPreKey;
        if (!OneTime.TryGetValue(deviceId, out var pool))
            OneTime[deviceId] = pool = new Queue<OneTimePreKey>();
        foreach (var otp in oneTimePreKeys) pool.Enqueue(otp);
        return Task.CompletedTask;
    }

    public Task<PreKeyBundle?> FetchBundleAsync(
        Guid deviceId, string address, CancellationToken ct = default)
    {
        if (!Signed.TryGetValue(deviceId, out var signed))
            return Task.FromResult<PreKeyBundle?>(null);

        var otp = OneTime.TryGetValue(deviceId, out var pool) && pool.Count > 0
            ? pool.Dequeue() : null;
        Pq.TryGetValue(deviceId, out var pq);

        return Task.FromResult<PreKeyBundle?>(new PreKeyBundle
        {
            Address = address,
            IdentityKey = "idk",
            SignedPreKey = signed,
            OneTimePreKey = otp,
            PqLastResortPreKey = pq,
        });
    }

    public Task<int> OneTimeCountAsync(Guid deviceId, CancellationToken ct = default)
        => Task.FromResult(
            OneTime.TryGetValue(deviceId, out var pool) ? pool.Count : 0);
}
