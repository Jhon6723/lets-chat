using LetsChat.Application.Services;
using LetsChat.Domain.Entities;

namespace LetsChat.UnitTests;

/// <summary>
/// Contact graph rules (phase 1.7): pending edges are directed, accept flips
/// the edge, mutual requests auto-accept, blocks are silent and wipe the pair.
/// These rules are the authorization boundary for prekey fetch (1.6).
/// </summary>
public class ContactServiceTests
{
    private readonly FakeAccountRepository _accounts = new();
    private readonly FakeContactRepository _contacts = new();
    private readonly ContactService _service;

    public ContactServiceTests()
        => _service = new ContactService(_contacts, _accounts);

    [Fact]
    public async Task Request_creates_pending_edge()
    {
        var alice = _accounts.Seed("alice");
        _accounts.Seed("carol");

        var edge = await _service.RequestAsync(alice, "carol");

        Assert.NotNull(edge);
        Assert.Equal(ContactStatus.Pending, edge.Status);
        Assert.Equal(alice.Id, edge.RequesterAccountId);
    }

    [Fact]
    public async Task Request_to_unknown_user_returns_null()
    {
        var alice = _accounts.Seed("alice");

        Assert.Null(await _service.RequestAsync(alice, "ghost"));
        Assert.Empty(_contacts.Edges);
    }

    [Fact]
    public async Task Request_to_self_returns_null()
    {
        var alice = _accounts.Seed("alice");

        Assert.Null(await _service.RequestAsync(alice, "alice"));
        Assert.Empty(_contacts.Edges);
    }

    [Fact]
    public async Task Request_is_idempotent()
    {
        var alice = _accounts.Seed("alice");
        _accounts.Seed("carol");

        var first = await _service.RequestAsync(alice, "carol");
        var second = await _service.RequestAsync(alice, "carol");

        Assert.Equal(first!.Id, second!.Id);
        Assert.Single(_contacts.Edges);
    }

    [Fact]
    public async Task Request_against_pending_reverse_auto_accepts()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        await _service.RequestAsync(carol, "alice"); // carol asked first

        var edge = await _service.RequestAsync(alice, "carol");

        Assert.Equal(ContactStatus.Accepted, edge!.Status);
        Assert.Single(_contacts.Edges); // one edge, not two
        Assert.NotNull(edge.RespondedAt);
    }

    [Fact]
    public async Task Request_is_silently_dropped_when_blocked()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        await _service.BlockAsync(carol, "alice");

        var result = await _service.RequestAsync(alice, "carol");

        Assert.Null(result);
        Assert.Single(_contacts.Edges); // only the blocked edge
        Assert.Equal(ContactStatus.Blocked, _contacts.Edges[0].Status);
    }

    [Fact]
    public async Task Accept_flips_pending_edge()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        var edge = (await _service.RequestAsync(alice, "carol"))!;

        var accepted = await _service.AcceptAsync(carol, edge.Id);

        Assert.NotNull(accepted);
        Assert.Equal(ContactStatus.Accepted, accepted.Status);
    }

    [Fact]
    public async Task Accept_is_rejected_for_the_requester()
    {
        var alice = _accounts.Seed("alice");
        _accounts.Seed("carol");
        var edge = (await _service.RequestAsync(alice, "carol"))!;

        // The requester cannot accept their own request.
        Assert.Null(await _service.AcceptAsync(alice, edge.Id));
        Assert.Equal(ContactStatus.Pending, _contacts.Edges[0].Status);
    }

    [Fact]
    public async Task Accept_is_rejected_for_strangers_and_non_pending()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        var dave = _accounts.Seed("dave");
        var edge = (await _service.RequestAsync(alice, "carol"))!;

        Assert.Null(await _service.AcceptAsync(dave, edge.Id));   // third party
        await _service.AcceptAsync(carol, edge.Id);
        Assert.Null(await _service.AcceptAsync(carol, edge.Id));  // already accepted
        Assert.Null(await _service.AcceptAsync(carol, Guid.NewGuid())); // no such edge
    }

    [Fact]
    public async Task Remove_works_for_decline_cancel_and_unfriend()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        var dave = _accounts.Seed("dave");

        // decline (addressee)
        var e1 = (await _service.RequestAsync(alice, "carol"))!;
        Assert.True(await _service.RemoveAsync(carol, e1.Id));
        Assert.Empty(_contacts.Edges);

        // cancel (requester)
        var e2 = (await _service.RequestAsync(alice, "carol"))!;
        Assert.True(await _service.RemoveAsync(alice, e2.Id));
        Assert.Empty(_contacts.Edges);

        // unfriend (either party of accepted)
        var e3 = (await _service.RequestAsync(alice, "carol"))!;
        await _service.AcceptAsync(carol, e3.Id);
        Assert.True(await _service.RemoveAsync(carol, e3.Id));
        Assert.Empty(_contacts.Edges);

        // stranger cannot remove someone else's edge
        var e4 = (await _service.RequestAsync(alice, "carol"))!;
        Assert.False(await _service.RemoveAsync(dave, e4.Id));
        Assert.Single(_contacts.Edges);
    }

    [Fact]
    public async Task Block_wipes_every_edge_of_the_pair()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        var edge = (await _service.RequestAsync(alice, "carol"))!;
        await _service.AcceptAsync(carol, edge.Id);

        await _service.BlockAsync(carol, "alice");

        var remaining = _contacts.Edges;
        Assert.Single(remaining);
        Assert.Equal(ContactStatus.Blocked, remaining[0].Status);
        Assert.Equal(carol.Id, remaining[0].RequesterAccountId);
    }

    [Fact]
    public async Task List_splits_edges_and_hides_blocks()
    {
        var alice = _accounts.Seed("alice");
        var carol = _accounts.Seed("carol");
        var dave = _accounts.Seed("dave");
        var erin = _accounts.Seed("erin");

        // accepted with carol, outgoing pending to dave, incoming from erin
        var e1 = (await _service.RequestAsync(alice, "carol"))!;
        await _service.AcceptAsync(carol, e1.Id);
        await _service.RequestAsync(alice, "dave");
        await _service.RequestAsync(erin, "alice");
        // blocked pair — must not appear
        var fiona = _accounts.Seed("fiona");
        await _service.BlockAsync(fiona, "alice");

        var list = await _service.ListAsync(alice);

        Assert.Equal(new[] { "carol" }, list.Contacts.Select(c => c.Username).ToArray());
        Assert.Equal(new[] { "dave" }, list.Outgoing.Select(c => c.Username).ToArray());
        Assert.Equal(new[] { "erin" }, list.Incoming.Select(c => c.Username).ToArray());
    }
}
