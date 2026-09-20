namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port for password hashing. Argon2id is an infrastructure detail
/// (ADR-0007); the application layer only knows this shape. The encoded hash
/// is self-describing (params + salt embedded) so parameters can evolve.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Constant-time verification of a password against an encoded hash.</summary>
    bool Verify(string password, string encodedHash);
}
