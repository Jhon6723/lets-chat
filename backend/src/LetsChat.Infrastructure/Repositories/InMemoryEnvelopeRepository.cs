using System.Collections.Concurrent;
using LetsChat.Application.Ports;
using LetsChat.Contracts;

namespace LetsChat.Infrastructure.Repositories;

/// <summary>
/// Dev adapter: keeps pending envelopes in memory. Swap for the EF Core
/// Postgres adapter (ADR-0002) once persistence lands — the port stays identical.
/// </summary>
public sealed class InMemoryEnvelopeRepository : IEnvelopeRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<EncryptedEnvelope>> _mailboxes = new();
    private readonly ConcurrentDictionary<string, string> _envelopeOwners = new();

    public Task EnqueueAsync(EncryptedEnvelope envelope, CancellationToken ct = default)
    {
        var box = _mailboxes.GetOrAdd(envelope.RecipientAddress, _ => new ConcurrentQueue<EncryptedEnvelope>());
        box.Enqueue(envelope);
        _envelopeOwners[envelope.Id] = envelope.RecipientAddress;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EncryptedEnvelope>> PendingForAsync(
        string recipientAddress,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EncryptedEnvelope>>(
            _mailboxes.TryGetValue(recipientAddress, out var box) ? box.ToArray() : []);

    public Task MarkDeliveredAsync(string envelopeId, CancellationToken ct = default)
    {
        if (_envelopeOwners.TryRemove(envelopeId, out var address)
            && _mailboxes.TryGetValue(address, out var box))
        {
            var remaining = box.Where(e => e.Id != envelopeId).ToArray();
            _mailboxes[address] = new ConcurrentQueue<EncryptedEnvelope>(remaining);
        }
        return Task.CompletedTask;
    }
}
