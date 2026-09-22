using LetsChat.Contracts;

namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port: the contact-gated public-key directory (ADR-0006 revised
/// 2026-09-21). Stores only public material — signed prekey per device,
/// a pool of one-time prekeys, optional PQ last-resort prekey.
/// </summary>
public interface IPreKeyRepository
{
    /// <summary>
    /// Publish: replace the device's signed prekey (and PQ key when given)
    /// and append the batch of one-time prekeys.
    /// </summary>
    Task PublishAsync(
        Guid deviceId,
        SignedPreKey signedPreKey,
        IReadOnlyList<OneTimePreKey> oneTimePreKeys,
        PqLastResortPreKey? pqLastResortPreKey,
        CancellationToken ct = default);

    /// <summary>
    /// Serve a bundle: the current signed prekey plus one atomically-claimed
    /// one-time prekey (null when the pool is empty) plus the PQ key when
    /// published. Returns null when the device never published.
    /// </summary>
    Task<PreKeyBundle?> FetchBundleAsync(
        Guid deviceId, string address, CancellationToken ct = default);

    /// <summary>Remaining one-time prekeys — the client tops up when low.</summary>
    Task<int> OneTimeCountAsync(Guid deviceId, CancellationToken ct = default);
}
