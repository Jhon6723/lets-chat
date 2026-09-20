namespace LetsChat.Infrastructure.Persistence;

/// <summary>One refresh token row. Raw token never stored — hash only.</summary>
public sealed class RefreshTokenRow
{
    public required Guid Id { get; set; }
    public required Guid AccountId { get; set; }
    public required Guid FamilyId { get; set; }
    public required string TokenHash { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
