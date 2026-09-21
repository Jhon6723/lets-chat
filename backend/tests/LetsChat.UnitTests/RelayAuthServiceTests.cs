using LetsChat.Application.Services;
using LetsChat.Domain.Entities;
using LetsChat.Infrastructure.Identity;
using NSec.Cryptography;

namespace LetsChat.UnitTests;

/// <summary>
/// Relay handshake rules (phase 1.5): the client signs relay-auth:{nonce}
/// with the device's identity private key. Real Ed25519 throughout.
/// </summary>
public class RelayAuthServiceTests
{
    private readonly FakeDeviceRepository _devices = new();
    private readonly RelayAuthService _service;

    public RelayAuthServiceTests()
        => _service = new RelayAuthService(_devices, new Ed25519SignatureVerifier());

    private (Device Device, Key Key) SeedDevice(string address)
    {
        var key = Key.Create(SignatureAlgorithm.Ed25519);
        var device = new Device
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            DeviceNumber = 1,
            IdentityKeyPublic = Convert.ToBase64String(
                key.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            Address = address,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _devices.Devices.Add(device);
        return (device, key);
    }

    [Fact]
    public void IssueChallenge_produces_fresh_256bit_nonces()
    {
        var a = RelayAuthService.IssueChallenge();
        var b = RelayAuthService.IssueChallenge();

        Assert.NotEqual(a, b);
        Assert.Equal(32, Convert.FromBase64String(a).Length);
    }

    [Fact]
    public async Task Authenticate_returns_device_on_valid_signature()
    {
        var (device, key) = SeedDevice("carol.1");
        var nonce = RelayAuthService.IssueChallenge();
        var signature = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(
            key, RelayAuthService.ChallengePayload(nonce)));

        var result = await _service.AuthenticateAsync("carol.1", nonce, signature);

        Assert.NotNull(result);
        Assert.Equal(device.Id, result.Id);
    }

    [Fact]
    public async Task Authenticate_rejects_signature_over_different_nonce()
    {
        var (_, key) = SeedDevice("carol.1");
        var signature = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(
            key, RelayAuthService.ChallengePayload("nonce-A")));

        // Replay attempt: signature was made for nonce-A, server issued nonce-B.
        var result = await _service.AuthenticateAsync(
            "carol.1", "nonce-B", signature);

        Assert.Null(result);
    }

    [Fact]
    public async Task Authenticate_rejects_unknown_address()
    {
        var (_, key) = SeedDevice("carol.1");
        var nonce = RelayAuthService.IssueChallenge();
        var signature = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(
            key, RelayAuthService.ChallengePayload(nonce)));

        Assert.Null(await _service.AuthenticateAsync("ghost.9", nonce, signature));
    }

    [Fact]
    public async Task Authenticate_rejects_malformed_signature()
    {
        SeedDevice("carol.1");
        var nonce = RelayAuthService.IssueChallenge();

        Assert.Null(await _service.AuthenticateAsync(
            "carol.1", nonce, "not!valid!base64!!!"));
    }
}
