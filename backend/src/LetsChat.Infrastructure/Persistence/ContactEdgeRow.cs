namespace LetsChat.Infrastructure.Persistence;

/// <summary>One contact edge row; status stored as text (pending/accepted/blocked).</summary>
public sealed class ContactEdgeRow
{
    public required Guid Id { get; set; }
    public required Guid RequesterAccountId { get; set; }
    public required Guid AddresseeAccountId { get; set; }
    public required string Status { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
