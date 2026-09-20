namespace LetsChat.Infrastructure.Persistence;

/// <summary>One account row. Username is stored normalized (lowercase).</summary>
public sealed class AccountRow
{
    public required Guid Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
