using System.Security.Cryptography;
using System.Text;
using LetsChat.Application.Ports;
using LetsChat.Domain.Entities;

namespace LetsChat.Application.Services;

/// <summary>
/// Per-connection device authentication (ADR-0007): the relay issues a
/// one-use nonce, the client signs relay-auth:{nonce} with the device
/// identity private key, and the connection becomes bound to that device
/// address. No persistent token — proof of possession is per connection.
/// </summary>
public sealed class RelayAuthService(
    IDeviceRepository devices,
    IDeviceSignatureVerifier verifier)
{
    /// <summary>Fresh 256-bit nonce for one connection handshake.</summary>
    public static string IssueChallenge()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Canonical bytes the client must sign to prove possession.</summary>
    public static byte[] ChallengePayload(string nonce)
        => Encoding.UTF8.GetBytes($"relay-auth:{nonce}");

    /// <summary>The authenticated device, or null if the proof fails.</summary>
    public async Task<Device?> AuthenticateAsync(
        string address,
        string nonce,
        string signature,
        CancellationToken ct = default)
    {
        var device = await devices.FindByAddressAsync(address, ct);
        if (device is null) return null;
        return verifier.Verify(
            device.IdentityKeyPublic, ChallengePayload(nonce), signature)
            ? device
            : null;
    }
}
