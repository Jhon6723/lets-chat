using LetsChat.Application.Services;
using LetsChat.Domain.Exceptions;
using LetsChat.Infrastructure.Identity;
using NSec.Cryptography;

namespace LetsChat.UnitTests;

/// <summary>
/// Device binding rules (phase 1.4): registration requires a signature over
/// register:{accountId}:{identityKeyPublic} by the private half of the key
/// being published. Uses real Ed25519 — keys and signatures are genuine.
/// </summary>
public class DeviceServiceTests
{
    private readonly FakeDeviceRepository _devices = new();
    private readonly DeviceService _service;

    public DeviceServiceTests()
        => _service = new DeviceService(_devices, new Ed25519SignatureVerifier());

    private static (string PublicKey, Key Key) GenerateIdentityKey()
    {
        var key = Key.Create(SignatureAlgorithm.Ed25519);
        return (Convert.ToBase64String(
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey)), key);
    }

    private static string Sign(Key key, byte[] payload)
        => Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, payload));

    [Fact]
    public async Task Register_with_valid_signature_binds_device()
    {
        var accounts = new FakeAccountRepository();
        var account = accounts.Seed("carol");
        var (publicKey, key) = GenerateIdentityKey();
        var payload = DeviceService.RegistrationPayload(account.Id, publicKey);

        var device = await _service.RegisterAsync(
            account, publicKey, Sign(key, payload));

        Assert.Equal(1, device.DeviceNumber);
        Assert.Equal("carol.1", device.Address);
        Assert.Equal(publicKey, device.IdentityKeyPublic);
    }

    [Fact]
    public async Task Register_assigns_incrementing_device_numbers()
    {
        var accounts = new FakeAccountRepository();
        var account = accounts.Seed("carol");

        var (pk1, k1) = GenerateIdentityKey();
        var first = await _service.RegisterAsync(account, pk1,
            Sign(k1, DeviceService.RegistrationPayload(account.Id, pk1)));

        var (pk2, k2) = GenerateIdentityKey();
        var second = await _service.RegisterAsync(account, pk2,
            Sign(k2, DeviceService.RegistrationPayload(account.Id, pk2)));

        Assert.Equal(1, first.DeviceNumber);
        Assert.Equal(2, second.DeviceNumber);
        Assert.Equal("carol.2", second.Address);
    }

    [Fact]
    public async Task Register_rejects_tampered_signature()
    {
        var accounts = new FakeAccountRepository();
        var account = accounts.Seed("carol");
        var (publicKey, _) = GenerateIdentityKey();
        var (_, otherKey) = GenerateIdentityKey(); // signed by a DIFFERENT key

        var signature = Sign(otherKey,
            DeviceService.RegistrationPayload(account.Id, publicKey));

        await Assert.ThrowsAsync<InvalidDeviceSignatureException>(
            () => _service.RegisterAsync(account, publicKey, signature));
        Assert.Empty(_devices.Devices);
    }

    [Fact]
    public async Task Register_rejects_signature_over_wrong_payload()
    {
        var accounts = new FakeAccountRepository();
        var account = accounts.Seed("carol");
        var (publicKey, key) = GenerateIdentityKey();

        // Signature is valid Ed25519 but over the wrong message — replay attempt.
        var signature = Sign(key, "relay-auth:not-the-registration-payload"u8.ToArray());

        await Assert.ThrowsAsync<InvalidDeviceSignatureException>(
            () => _service.RegisterAsync(account, publicKey, signature));
    }
}
