namespace LetsChat.Domain.Entities;

/// <summary>
/// One refresh token in a rotation family (ADR-0007 session strategy).
/// TokenHash is the SHA-256 of the opaque token — the raw value never
/// touches the database. FamilyId groups a session lineage: reuse of any
/// consumed token revokes the whole family (theft signal).
/// </summary>
public sealed class RefreshToken
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid FamilyId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
