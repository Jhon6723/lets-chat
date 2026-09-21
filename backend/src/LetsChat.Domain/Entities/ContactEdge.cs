namespace LetsChat.Domain.Entities;

/// <summary>Lifecycle of a contact edge between two accounts.</summary>
public enum ContactStatus
{
    Pending,
    Accepted,
    Blocked,
}

/// <summary>
/// A directed relationship edge between two accounts (ADR-0006 revised
/// 2026-09-21). Pending/Blocked are directional (Requester → Addressee);
/// Accepted grants symmetric privileges — in particular prekey bundle fetch.
/// At most one Pending/Accepted edge exists per account pair; Blocked edges
/// may coexist in both directions.
/// </summary>
public sealed class ContactEdge
{
    public required Guid Id { get; init; }
    public required Guid RequesterAccountId { get; init; }
    public required Guid AddresseeAccountId { get; init; }
    public required ContactStatus Status { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RespondedAt { get; set; }
}
