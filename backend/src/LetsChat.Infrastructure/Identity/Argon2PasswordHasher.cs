using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LetsChat.Application.Ports;

namespace LetsChat.Infrastructure.Identity;

/// <summary>
/// Argon2id hasher (ADR-0007). OWASP-recommended parameters; the encoded form
/// $argon2id$m=...,t=...,p=...$salt$hash is self-describing so parameters can
/// be raised later and old hashes still verify.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int MemorySizeKiB = 65536; // 64 MiB
    private const int Iterations = 3;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = ComputeHash(password, salt, MemorySizeKiB, Iterations, Parallelism);
        return $"$argon2id$m={MemorySizeKiB},t={Iterations},p={Parallelism}" +
               $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        // ["", "argon2id", "m=...,t=...,p=...", salt, hash]
        if (parts is not [_, "argon2id", var parms, var saltB64, var hashB64])
            return false;

        var p = parms.Split(',');
        if (p.Length != 3
            || !int.TryParse(p[0][2..], out var memory)
            || !int.TryParse(p[1][2..], out var iterations)
            || !int.TryParse(p[2][2..], out var parallelism))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(saltB64);
            expected = Convert.FromBase64String(hashB64);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = ComputeHash(password, salt, memory, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] ComputeHash(
        string password, byte[] salt, int memoryKiB, int iterations, int parallelism)
        => new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        }.GetBytes(HashBytes);
}
