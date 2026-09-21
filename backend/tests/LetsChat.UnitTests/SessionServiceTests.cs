using System.Security.Cryptography;
using LetsChat.Application.Services;
using LetsChat.Domain.Entities;
using LetsChat.Domain.Exceptions;
using LetsChat.Infrastructure.Identity;

namespace LetsChat.UnitTests;

/// <summary>
/// Refresh-token rotation rules (phase 1.3): each use consumes the previous
/// token and issues a new one in the same family; presenting a consumed token
/// again revokes the whole family — the theft signal.
/// </summary>
public class SessionServiceTests
{
    private readonly FakeAccountRepository _accounts = new();
    private readonly FakeRefreshTokenRepository _tokens = new();
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        var issuer = new JwtTokenIssuer(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            "test-issuer", "test-audience");
        _service = new SessionService(_tokens, issuer);
    }

    private async Task<(Domain.Aggregates.Account Account, TokenPair Pair)> OpenSession()
    {
        var account = _accounts.Seed("alice");
        var (pair, accountId) = await _service.OpenAsync(account);
        Assert.Equal(account.Id, accountId);
        return (account, pair);
    }

    [Fact]
    public async Task Open_issues_access_and_refresh_tokens()
    {
        var (_, pair) = await OpenSession();

        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
        Assert.True(pair.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refresh_rotates_and_consumes_the_previous_token()
    {
        var (_, pair1) = await OpenSession();

        var pair2 = await _service.RefreshAsync(pair1.RefreshToken, _accounts);

        Assert.NotEqual(pair1.RefreshToken, pair2.RefreshToken);
        var stored1 = _tokens.Tokens.First();
        Assert.NotNull(stored1.ConsumedAt);
        Assert.Equal(stored1.FamilyId,
            _tokens.Tokens.Last().FamilyId); // same family lineage
    }

    [Fact]
    public async Task Refresh_reuse_of_consumed_token_revokes_whole_family()
    {
        var (_, pair1) = await OpenSession();
        var pair2 = await _service.RefreshAsync(pair1.RefreshToken, _accounts);
        var familyId = _tokens.Tokens.First().FamilyId;

        // Attacker replays the already-consumed token1.
        await Assert.ThrowsAsync<RefreshTokenReuseException>(
            () => _service.RefreshAsync(pair1.RefreshToken, _accounts));

        Assert.True(_tokens.FamilyRevoked(familyId));

        // Even the legitimate latest token is now dead.
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(
            () => _service.RefreshAsync(pair2.RefreshToken, _accounts));
    }

    [Fact]
    public async Task Refresh_rejects_unknown_token()
    {
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(
            () => _service.RefreshAsync("nonexistent-token", _accounts));
    }

    [Fact]
    public async Task Refresh_rejects_expired_token()
    {
        var account = _accounts.Seed("alice");
        var issuer = new JwtTokenIssuer(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "t", "t");
        var raw = issuer.GenerateRefreshToken();
        _tokens.Tokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = issuer.HashRefreshToken(raw),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-31),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(
            () => _service.RefreshAsync(raw, _accounts));
    }

    [Fact]
    public async Task Revoke_kills_the_family()
    {
        var (_, pair) = await OpenSession();

        await _service.RevokeAsync(pair.RefreshToken);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(
            () => _service.RefreshAsync(pair.RefreshToken, _accounts));
    }
}
