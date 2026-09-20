using LetsChat.Application.Ports;
using LetsChat.Contracts;

namespace LetsChat.Application.Services;

/// <summary>
/// Application service for envelope relay. Contains the delivery rules:
/// the server never inspects ciphertext — it only routes, stores and acks.
/// Depends on the IEnvelopeRepository port, injected by DI (ADR-0001).
/// </summary>
public sealed class EnvelopeService(IEnvelopeRepository store)
{
    public async Task RelayAsync(EncryptedEnvelope envelope, CancellationToken ct = default)
    {
        // TODO: online delivery via connected socket before enqueueing.
        await store.EnqueueAsync(envelope, ct);
    }

    public Task<IReadOnlyList<EncryptedEnvelope>> PendingForAsync(
        string recipientAddress,
        CancellationToken ct = default)
        => store.PendingForAsync(recipientAddress, ct);

    public Task AcknowledgeAsync(string envelopeId, CancellationToken ct = default)
        => store.MarkDeliveredAsync(envelopeId, ct);
}
