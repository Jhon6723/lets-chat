using LetsChat.Contracts;

namespace LetsChat.Infrastructure.Persistence;

/// <summary>
/// One undelivered envelope in a device's mailbox. Queryable metadata lives
/// in columns; the full wire envelope rides in a single jsonb payload so the
/// relay stays dumb — schema does not migrate when the wire format grows.
/// </summary>
public sealed class PendingEnvelopeRow
{
    public required string Id { get; set; }
    public required string RecipientAddress { get; set; }
    public required string SenderAddress { get; set; }
    public required string Type { get; set; }
    public required long CreatedAt { get; set; }
    public required EncryptedEnvelope Envelope { get; set; }
}
