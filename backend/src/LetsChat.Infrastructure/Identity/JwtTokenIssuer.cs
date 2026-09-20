using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LetsChat.Application.Ports;
using LetsChat.Domain.Aggregates;
using Microsoft.IdentityModel.Tokens;

namespace LetsChat.Infrastructure.Identity;

/// <summary>
/// JWT + opaque-refresh implementation of the token port (ADR-0007 session
/// strategy). Access tokens are HS256 JWTs with sub/scope claims; refresh
/// tokens are 256-bit random values — only their SHA-256 hash is stored.
/// </summary>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private static readonly TimeSpan AccessTtl = TimeSpan.FromMinutes(15);

    private readonly SigningCredentials _credentials;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtTokenIssuer(string signingKeyBase64, string issuer, string audience)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(signingKeyBase64));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _issuer = issuer;
        _audience = audience;
    }

    public (string Token, DateTimeOffset ExpiresAt) SignAccessToken(Account account)
    {
        var expires = DateTimeOffset.UtcNow.Add(AccessTtl);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new Claim("username", account.Username.Value),
                new Claim("scope", "account"),
            ]),
            SigningCredentials = _credentials,
        };
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        return (handler.WriteToken(token), expires);
    }

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string HashRefreshToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
