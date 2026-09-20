using System.Text;
using LetsChat.Application.Ports;
using LetsChat.Domain.Aggregates;
using LetsChat.Domain.Entities;
using LetsChat.Domain.Exceptions;

namespace LetsChat.Application.Services;

/// <summary>
/// Device lifecycle (ADR-0007): registering a device publishes its identity
/// public key under the account. The request must carry a signature over the
/// canonical payload — proof the caller holds the private key. This is the
/// invariant that stops prekey/mailbox MITM: no one can bind a device to an
/// account without the key.
/// </summary>
public sealed class DeviceService(
    IDeviceRepository devices,
    IDeviceSignatureVerifier verifier)
{
    /// <summary>Canonical bytes the client signs at registration.</summary>
    public static byte[] RegistrationPayload(Guid accountId, string identityKeyPublic)
        => Encoding.UTF8.GetBytes($"register:{accountId}:{identityKeyPublic}");

    public async Task<Device> RegisterAsync(
        Account account,
        string identityKeyPublic,
        string signature,
        CancellationToken ct = default)
    {
        var payload = RegistrationPayload(account.Id, identityKeyPublic);
        if (!verifier.Verify(identityKeyPublic, payload, signature))
            throw new InvalidDeviceSignatureException();

        var number = await devices.NextDeviceNumberAsync(account.Id, ct);
        var device = new Device
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            DeviceNumber = number,
            IdentityKeyPublic = identityKeyPublic,
            Address = $"{account.Username.Value}.{number}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await devices.AddAsync(device, ct);
        return device;
    }
}
