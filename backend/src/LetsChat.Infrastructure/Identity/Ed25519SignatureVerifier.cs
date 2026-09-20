using LetsChat.Application.Ports;
using NSec.Cryptography;

namespace LetsChat.Infrastructure.Identity;

/// <summary>
/// Ed25519 signature verification for device identity keys. Clients sign with
/// the private half of their Signal identity keypair (Curve25519 converted to
/// Ed25519 form via XEdDSA on the client side); here it is plain RFC 8032
/// verification.
/// </summary>
public sealed class Ed25519SignatureVerifier : IDeviceSignatureVerifier
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    public bool Verify(string publicKeyBase64, byte[] payload, string signatureBase64)
    {
        try
        {
            var keyBytes = Convert.FromBase64String(publicKeyBase64);
            var signature = Convert.FromBase64String(signatureBase64);
            var publicKey = PublicKey.Import(
                Algorithm, keyBytes, KeyBlobFormat.RawPublicKey);
            return Algorithm.Verify(publicKey, payload, signature);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
