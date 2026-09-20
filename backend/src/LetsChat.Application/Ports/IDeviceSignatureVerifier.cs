namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port: verifies a device signature made with the private half of
/// an identity key (ADR-0007). Keys and signatures travel base64-encoded;
/// the payload is raw bytes. Signal identity keys are Curve25519 — clients
/// convert to Ed25519 form before publishing (XEdDSA), so verification is
/// plain Ed25519 here.
/// </summary>
public interface IDeviceSignatureVerifier
{
    bool Verify(string publicKeyBase64, byte[] payload, string signatureBase64);
}
