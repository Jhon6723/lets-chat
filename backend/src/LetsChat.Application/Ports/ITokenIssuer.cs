using LetsChat.Domain.Aggregates;

namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port for session token mechanics (ADR-0007 session strategy).
/// Access tokens are short-lived JWTs; refresh tokens are opaque random
/// strings stored hashed. The application layer never sees JWT internals.
/// </summary>
public interface ITokenIssuer
{
    /// <summary>Sign a ~15 min account-scope JWT for this account.</summary>
    (string Token, DateTimeOffset ExpiresAt) SignAccessToken(Account account);

    /// <summary>Generate a new opaque refresh token value.</summary>
    string GenerateRefreshToken();

    /// <summary>SHA-256 hex of a refresh token — what the store persists.</summary>
    string HashRefreshToken(string token);
}
