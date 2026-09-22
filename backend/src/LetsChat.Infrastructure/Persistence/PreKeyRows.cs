namespace LetsChat.Infrastructure.Persistence;

/// <summary>The device's current signed prekey — one row per device, replaced on rotate.</summary>
public sealed class SignedPreKeyRow
{
    public required Guid DeviceId { get; set; }
    public required int KeyId { get; set; }
    public required string PublicKey { get; set; }
    public required string Signature { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One row of the one-time prekey pool; consumed (deleted) on fetch.</summary>
public sealed class OneTimePreKeyRow
{
    public required Guid Id { get; set; }
    public required Guid DeviceId { get; set; }
    public required int KeyId { get; set; }
    public required string PublicKey { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Optional post-quantum last-resort prekey — one per device, served never consumed.</summary>
public sealed class PqLastResortPreKeyRow
{
    public required Guid DeviceId { get; set; }
    public required int KeyId { get; set; }
    public required string PublicKey { get; set; }
    public required string Signature { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
