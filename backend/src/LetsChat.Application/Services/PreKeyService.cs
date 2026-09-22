using LetsChat.Application.Ports;
using LetsChat.Contracts;
using LetsChat.Domain.Entities;

namespace LetsChat.Application.Services;

/// <summary>Why a bundle fetch did not return a bundle.</summary>
public enum PreKeyFetchResult
{
    /// <summary>Bundle served — includes an OTP when the pool had one.</summary>
    Ok,
    /// <summary>No edge allows this fetch (or the address does not exist).</summary>
    Denied,
    /// <summary>Fetch allowed, but the device never published a signed prekey.</summary>
    NoBundle,
}

/// <summary>
/// The contact-gated key directory (ADR-0006 revised 2026-09-21). Publish is
/// scoped to the device itself (the endpoint proves possession via the device
/// signature); fetch additionally requires a contact edge between the two
/// accounts — knowing an address alone yields Denied.
/// </summary>
public sealed class PreKeyService(
    IPreKeyRepository prekeys,
    IDeviceRepository devices,
    IContactRepository contacts)
{
    public Task PublishAsync(
        Device device,
        SignedPreKey signedPreKey,
        IReadOnlyList<OneTimePreKey> oneTimePreKeys,
        PqLastResortPreKey? pqLastResortPreKey,
        CancellationToken ct = default)
        => prekeys.PublishAsync(
            device.Id, signedPreKey, oneTimePreKeys, pqLastResortPreKey, ct);

    /// <summary>
    /// Fetch another device's bundle on behalf of the authenticated fetcher.
    /// An unknown address is Denied, not NotFound — the directory must not
    /// confirm whether an address exists.
    /// </summary>
    public async Task<(PreKeyFetchResult Result, PreKeyBundle? Bundle)> FetchAsync(
        Device fetcher, string address, CancellationToken ct = default)
    {
        var owner = await devices.FindByAddressAsync(address, ct);
        if (owner is null) return (PreKeyFetchResult.Denied, null);

        var allowed = await contacts.CanFetchPrekeysAsync(
            fetcher.AccountId, owner.AccountId, ct);
        if (!allowed) return (PreKeyFetchResult.Denied, null);

        var bundle = await prekeys.FetchBundleAsync(owner.Id, address, ct);
        return bundle is null
            ? (PreKeyFetchResult.NoBundle, null)
            : (PreKeyFetchResult.Ok, bundle);
    }
}
