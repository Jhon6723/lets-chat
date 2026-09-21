using LetsChat.Domain.Entities;
using LetsChat.Domain.ValueObjects;
using LetsChat.Infrastructure.Repositories;

namespace LetsChat.IntegrationTests;

/// <summary>
/// The real SQL behind the contact graph — above all CanFetchPrekeysAsync,
/// which is the authorization boundary for prekey bundle fetch (1.6).
/// Runs against a real Postgres container: the query semantics are what
/// production executes, not an in-memory approximation.
/// </summary>
[Collection("postgres")]
public class PostgresContactRepositoryTests(PostgresFixture fx)
{
    private readonly PostgresAccountRepository _accounts = new(fx.Factory);
    private readonly PostgresContactRepository _contacts = new(fx.Factory);

    private async Task<(Guid Alice, Guid Carol)> SeedPair()
    {
        var alice = await _accounts.CreateAsync(
            Username.Parse($"al{Guid.NewGuid():N}"[..20]), "hash");
        var carol = await _accounts.CreateAsync(
            Username.Parse($"ca{Guid.NewGuid():N}"[..20]), "hash");
        return (alice.Id, carol.Id);
    }

    private ContactEdge Edge(Guid requester, Guid addressee, ContactStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            RequesterAccountId = requester,
            AddresseeAccountId = addressee,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // --- CanFetchPrekeysAsync: the full access matrix ---------------------

    [Fact]
    public async Task Fetch_allowed_for_own_account()
    {
        var (alice, _) = await SeedPair();
        Assert.True(await _contacts.CanFetchPrekeysAsync(alice, alice));
    }

    [Fact]
    public async Task Fetch_denied_without_relationship()
    {
        var (alice, carol) = await SeedPair();
        Assert.False(await _contacts.CanFetchPrekeysAsync(alice, carol));
        Assert.False(await _contacts.CanFetchPrekeysAsync(carol, alice));
    }

    [Fact]
    public async Task Fetch_allowed_bidirectionally_once_accepted()
    {
        var (alice, carol) = await SeedPair();
        await _contacts.AddAsync(Edge(alice, carol, ContactStatus.Accepted));

        Assert.True(await _contacts.CanFetchPrekeysAsync(alice, carol));
        Assert.True(await _contacts.CanFetchPrekeysAsync(carol, alice));
    }

    [Fact]
    public async Task Fetch_pending_allows_only_target_to_inspect_requester()
    {
        var (alice, carol) = await SeedPair();
        await _contacts.AddAsync(Edge(alice, carol, ContactStatus.Pending));

        // carol (target) inspects alice's (requester) bundle — the bootstrap.
        Assert.True(await _contacts.CanFetchPrekeysAsync(carol, alice));
        // alice may NOT pull carol's bundle while pending.
        Assert.False(await _contacts.CanFetchPrekeysAsync(alice, carol));
    }

    [Fact]
    public async Task Fetch_denied_when_blocked()
    {
        var (alice, carol) = await SeedPair();
        await _contacts.AddAsync(Edge(carol, alice, ContactStatus.Blocked));

        Assert.False(await _contacts.CanFetchPrekeysAsync(alice, carol));
        Assert.False(await _contacts.CanFetchPrekeysAsync(carol, alice));
    }

    // --- Pair operations ---------------------------------------------------

    [Fact]
    public async Task FindPair_returns_edges_in_both_directions()
    {
        var (alice, carol) = await SeedPair();
        await _contacts.AddAsync(Edge(alice, carol, ContactStatus.Pending));
        await _contacts.AddAsync(Edge(carol, alice, ContactStatus.Blocked));

        var pair = await _contacts.FindPairAsync(alice, carol);

        Assert.Equal(2, pair.Count);
        // order-independent lookup
        var same = await _contacts.FindPairAsync(carol, alice);
        Assert.Equal(2, same.Count);
    }

    [Fact]
    public async Task Update_persists_status_and_responded_at()
    {
        var (alice, carol) = await SeedPair();
        var edge = Edge(alice, carol, ContactStatus.Pending);
        await _contacts.AddAsync(edge);

        edge.Status = ContactStatus.Accepted;
        edge.RespondedAt = DateTimeOffset.UtcNow;
        await _contacts.UpdateAsync(edge);

        var stored = await _contacts.FindByIdAsync(edge.Id);
        Assert.Equal(ContactStatus.Accepted, stored!.Status);
        Assert.NotNull(stored.RespondedAt);
    }

    [Fact]
    public async Task DeletePair_removes_every_edge_between_the_accounts()
    {
        var (alice, carol) = await SeedPair();
        await _contacts.AddAsync(Edge(alice, carol, ContactStatus.Accepted));
        await _contacts.AddAsync(Edge(carol, alice, ContactStatus.Blocked));

        await _contacts.DeletePairAsync(alice, carol);

        Assert.Empty(await _contacts.FindPairAsync(alice, carol));
    }

    [Fact]
    public async Task ListForAccount_returns_edges_touching_the_account()
    {
        var (alice, carol) = await SeedPair();
        var dave = await _accounts.CreateAsync(
            Username.Parse($"da{Guid.NewGuid():N}"[..20]), "hash");
        await _contacts.AddAsync(Edge(alice, carol, ContactStatus.Accepted));
        await _contacts.AddAsync(Edge(dave.Id, alice, ContactStatus.Pending));

        var aliceEdges = await _contacts.ListForAccountAsync(alice);

        Assert.Equal(2, aliceEdges.Count);
    }

    [Fact]
    public async Task Duplicate_directed_edge_is_rejected_by_unique_index()
    {
        var (alice, carol) = await SeedPair();
        await _contacts.AddAsync(Edge(alice, carol, ContactStatus.Pending));

        await Assert.ThrowsAnyAsync<Exception>(
            () => _contacts.AddAsync(Edge(alice, carol, ContactStatus.Pending)));
    }
}
