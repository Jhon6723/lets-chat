using LetsChat.Domain.Entities;

namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port: where contact edges live. The contact graph is the
/// authorization boundary for prekey bundle fetch (decisions log 2026-09-21).
/// </summary>
public interface IContactRepository
{
    /// <summary>All edges between the two accounts, in either direction (0-2 rows).</summary>
    Task<IReadOnlyList<ContactEdge>> FindPairAsync(
        Guid a, Guid b, CancellationToken ct = default);

    /// <summary>Find an edge by id, or null.</summary>
    Task<ContactEdge?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Every edge touching the account (as requester or addressee).</summary>
    Task<IReadOnlyList<ContactEdge>> ListForAccountAsync(
        Guid accountId, CancellationToken ct = default);

    Task AddAsync(ContactEdge edge, CancellationToken ct = default);

    Task UpdateAsync(ContactEdge edge, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Delete every edge between the pair — used by block.</summary>
    Task DeletePairAsync(Guid a, Guid b, CancellationToken ct = default);

    /// <summary>
    /// Whether fetcher may retrieve owner's prekey bundle: always true for the
    /// owner itself; true on any accepted edge between the pair; true while a
    /// pending request from owner to fetcher exists (the target inspects the
    /// requester). Anything else is denied.
    /// </summary>
    Task<bool> CanFetchPrekeysAsync(
        Guid fetcherAccountId, Guid ownerAccountId, CancellationToken ct = default);
}
