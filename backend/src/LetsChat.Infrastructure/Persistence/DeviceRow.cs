namespace LetsChat.Infrastructure.Persistence;

/// <summary>One registered device row.</summary>
public sealed class DeviceRow
{
    public required Guid Id { get; set; }
    public required Guid AccountId { get; set; }
    public required int DeviceNumber { get; set; }
    public required string IdentityKeyPublic { get; set; }
    public required string Address { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
