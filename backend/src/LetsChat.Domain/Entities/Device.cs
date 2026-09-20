namespace LetsChat.Domain.Entities;

/// <summary>
/// A registered device under an account (ADR-0007). IdentityKeyPublic is the
/// Ed25519-form public half of the device's Signal identity keypair — the
/// private key never leaves the client. Address is the routing handle
/// ("username.N") used as sender/recipient in envelopes.
/// </summary>
public sealed class Device
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required int DeviceNumber { get; init; }
    public required string IdentityKeyPublic { get; init; }
    public required string Address { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
