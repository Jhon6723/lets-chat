using LetsChat.Application.Ports;
using LetsChat.Domain.Aggregates;
using LetsChat.Domain.Entities;
using LetsChat.Domain.ValueObjects;

namespace LetsChat.Application.Services;

/// <summary>
/// Contact graph rules (ADR-0006 revised 2026-09-21): a pending request is a
/// directed edge; accepting flips it to accepted; declining/cancelling deletes
/// it. A request in the reverse direction counts as acceptance — mutual intent.
/// Block wipes every edge of the pair and plants a directed blocked edge.
/// Unknown targets return null so the endpoint can answer generically —
/// the response never reveals whether a username exists.
/// </summary>
public sealed class ContactService(
    IContactRepository contacts,
    IAccountRepository accounts)
{
    /// <summary>
    /// Create a pending edge requester → target. Returns null when the target
    /// is unknown, is the requester, or a block exists — callers answer
    /// identically for all three (no enumeration).
    /// </summary>
    public async Task<ContactEdge?> RequestAsync(
        Account requester, string targetUsername, CancellationToken ct = default)
    {
        var name = Username.TryParse(targetUsername);
        if (name is null) return null;

        var target = await accounts.FindByUsernameAsync(name, ct);
        if (target is null || target.Id == requester.Id) return null;

        var pair = await contacts.FindPairAsync(requester.Id, target.Id, ct);
        if (pair.Any(e => e.Status == ContactStatus.Blocked)) return null;

        // Idempotent: an edge I already initiated stands.
        var mine = pair.FirstOrDefault(e => e.RequesterAccountId == requester.Id);
        if (mine is not null) return mine;

        var reverse = pair.FirstOrDefault(e => e.RequesterAccountId == target.Id);
        if (reverse?.Status == ContactStatus.Pending)
        {
            reverse.Status = ContactStatus.Accepted;
            reverse.RespondedAt = DateTimeOffset.UtcNow;
            await contacts.UpdateAsync(reverse, ct);
            return reverse;
        }
        if (reverse is not null) return reverse; // already accepted

        var edge = new ContactEdge
        {
            Id = Guid.NewGuid(),
            RequesterAccountId = requester.Id,
            AddresseeAccountId = target.Id,
            Status = ContactStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await contacts.AddAsync(edge, ct);
        return edge;
    }

    /// <summary>Only the addressee of a pending edge may accept it.</summary>
    public async Task<ContactEdge?> AcceptAsync(
        Account caller, Guid edgeId, CancellationToken ct = default)
    {
        var edge = await contacts.FindByIdAsync(edgeId, ct);
        if (edge is null
            || edge.Status != ContactStatus.Pending
            || edge.AddresseeAccountId != caller.Id)
            return null;

        edge.Status = ContactStatus.Accepted;
        edge.RespondedAt = DateTimeOffset.UtcNow;
        await contacts.UpdateAsync(edge, ct);
        return edge;
    }

    /// <summary>
    /// Addressee declines or requester cancels a pending edge; either party
    /// removes an accepted one. Both are the same row-delete.
    /// </summary>
    public async Task<bool> RemoveAsync(
        Account caller, Guid edgeId, CancellationToken ct = default)
    {
        var edge = await contacts.FindByIdAsync(edgeId, ct);
        if (edge is null
            || edge.Status == ContactStatus.Blocked
            || (edge.RequesterAccountId != caller.Id
                && edge.AddresseeAccountId != caller.Id))
            return false;

        await contacts.DeleteAsync(edgeId, ct);
        return true;
    }

    /// <summary>
    /// Wipe every edge of the pair and plant blocker → target blocked.
    /// Returns null for unknown/self targets — generic response, same as
    /// RequestAsync.
    /// </summary>
    public async Task<ContactEdge?> BlockAsync(
        Account blocker, string targetUsername, CancellationToken ct = default)
    {
        var name = Username.TryParse(targetUsername);
        if (name is null) return null;

        var target = await accounts.FindByUsernameAsync(name, ct);
        if (target is null || target.Id == blocker.Id) return null;

        await contacts.DeletePairAsync(blocker.Id, target.Id, ct);
        var edge = new ContactEdge
        {
            Id = Guid.NewGuid(),
            RequesterAccountId = blocker.Id,
            AddresseeAccountId = target.Id,
            Status = ContactStatus.Blocked,
            CreatedAt = DateTimeOffset.UtcNow,
            RespondedAt = DateTimeOffset.UtcNow,
        };
        await contacts.AddAsync(edge, ct);
        return edge;
    }

    /// <summary>
    /// The caller's view of their graph: accepted edges become contacts,
    /// pending edges split into incoming (addressed to me) and outgoing
    /// (I initiated). Blocked edges are never listed — blocking is private.
    /// </summary>
    public async Task<ContactList> ListAsync(Account caller, CancellationToken ct = default)
    {
        var edges = await contacts.ListForAccountAsync(caller.Id, ct);
        var accepted = new List<ContactEntry>();
        var incoming = new List<ContactEntry>();
        var outgoing = new List<ContactEntry>();

        foreach (var edge in edges)
        {
            if (edge.Status == ContactStatus.Blocked) continue;
            var otherId = edge.RequesterAccountId == caller.Id
                ? edge.AddresseeAccountId : edge.RequesterAccountId;
            var other = await accounts.FindByIdAsync(otherId, ct);
            var entry = new ContactEntry(
                edge.Id, other?.Username.Value ?? "?", edge.CreatedAt);

            if (edge.Status == ContactStatus.Accepted)
                accepted.Add(entry);
            else if (edge.AddresseeAccountId == caller.Id)
                incoming.Add(entry);
            else
                outgoing.Add(entry);
        }

        return new ContactList(accepted, incoming, outgoing);
    }
}

public sealed record ContactEntry(Guid EdgeId, string Username, DateTimeOffset Since);
public sealed record ContactList(
    IReadOnlyList<ContactEntry> Contacts,
    IReadOnlyList<ContactEntry> Incoming,
    IReadOnlyList<ContactEntry> Outgoing);
