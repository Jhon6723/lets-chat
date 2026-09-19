using System.Text.Json.Serialization;

namespace LetsChat.Contracts;

/// <summary>
/// C# mirror of shared/protocol (TypeScript). Kept honest by contract
/// validation tests against JSON fixtures generated from the TS package —
/// ADR-0001 (revised 2026-09-18). If you change one side, change both.
/// </summary>
public static class ProtocolVersion
{
    public const int Value = 1;
}

/// <summary>Opaque encrypted envelope the relay stores and forwards.</summary>
public sealed record EncryptedEnvelope
{
    public required int Version { get; init; }
    public required string Id { get; init; }
    public required string SenderAddress { get; init; }
    public required string RecipientAddress { get; init; }
    public required string Type { get; init; }
    public required string Ciphertext { get; init; }
    public required string Header { get; init; }
    public required long CreatedAt { get; init; }
}

public sealed record SignedPreKey
{
    public required int Id { get; init; }
    public required string PublicKey { get; init; }
    public required string Signature { get; init; }
}

public sealed record OneTimePreKey
{
    public required int Id { get; init; }
    public required string PublicKey { get; init; }
}

public sealed record PqLastResortPreKey
{
    public required int Id { get; init; }
    public required string PublicKey { get; init; }
    public required string Signature { get; init; }
}

/// <summary>Public prekey material published by a device.</summary>
public sealed record PreKeyBundle
{
    public required string Address { get; init; }
    public required string IdentityKey { get; init; }
    public required SignedPreKey SignedPreKey { get; init; }
    public OneTimePreKey? OneTimePreKey { get; init; }
    public PqLastResortPreKey? PqLastResortPreKey { get; init; }
}

/// <summary>WSS messages client → relay.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SendMessage), "send")]
[JsonDerivedType(typeof(AckMessage), "ack")]
[JsonDerivedType(typeof(FetchPendingMessage), "fetch_pending")]
public abstract record RelayClientMessage;

public sealed record SendMessage(EncryptedEnvelope Envelope) : RelayClientMessage;
public sealed record AckMessage(string EnvelopeId) : RelayClientMessage;
public sealed record FetchPendingMessage : RelayClientMessage;

/// <summary>WSS messages relay → client.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EnvelopeMessage), "envelope")]
[JsonDerivedType(typeof(AckOkMessage), "ack_ok")]
[JsonDerivedType(typeof(PendingMessage), "pending")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record RelayServerMessage;

public sealed record EnvelopeMessage(EncryptedEnvelope Envelope) : RelayServerMessage;
public sealed record AckOkMessage(string EnvelopeId) : RelayServerMessage;
public sealed record PendingMessage(IReadOnlyList<EncryptedEnvelope> Envelopes) : RelayServerMessage;
public sealed record ErrorMessage(string Code, string Message) : RelayServerMessage;
