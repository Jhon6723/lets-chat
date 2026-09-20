using LetsChat.Contracts;

namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port: where pending envelopes live until the recipient acks them.
/// Implementations: InMemoryEnvelopeRepository (dev), PostgresEnvelopeRepository (prod,
/// EF Core — ADR-0002). Registered via DI; the domain never sees a concrete type.
/// </summary>
public interface IEnvelopeRepository
{
    /// <summary>Persist an envelope for later delivery to its recipient.</summary>
    Task EnqueueAsync(EncryptedEnvelope envelope, CancellationToken ct = default);

    /// <summary>All undelivered envelopes addressed to a device, FIFO.</summary>
    Task<IReadOnlyList<EncryptedEnvelope>> PendingForAsync(
        string recipientAddress,
        CancellationToken ct = default);

    /// <summary>Remove an envelope after the recipient acknowledges it.</summary>
    Task MarkDeliveredAsync(string envelopeId, CancellationToken ct = default);
}
