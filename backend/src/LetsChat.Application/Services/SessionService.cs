using LetsChat.Application.Ports;
using LetsChat.Domain.Aggregates;
using LetsChat.Domain.Entities;
using LetsChat.Domain.Exceptions;

namespace LetsChat.Application.Services;

/// <summary>What a successful login or refresh returns to the endpoint layer.</summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

/// <summary>
/// Session lifecycle (ADR-0007 session strategy): login creates a rotation
/// family; each refresh consumes the previous token and issues a new one in
/// the same family; presenting a consumed token again revokes the whole
/// family because it means the token leaked to a second party.
/// </summary>
public sealed class SessionService(
    IRefreshTokenRepository store,
    ITokenIssuer tokens)
{
    private static readonly TimeSpan RefreshTtl = TimeSpan.FromDays(30);

    public async Task<(TokenPair Tokens, Guid AccountId)> OpenAsync(
        Account account,
        CancellationToken ct = default)
    {
        var familyId = Guid.NewGuid();
        var pair = await RotateAsync(account, familyId, ct);
        return (pair, account.Id);
    }

    /// <summary>Rotate a refresh token; reuse of a consumed one revokes the family.</summary>
    public async Task<TokenPair> RefreshAsync(
        string refreshToken,
        IAccountRepository accounts,
        CancellationToken ct = default)
    {
        var hash = tokens.HashRefreshToken(refreshToken);
        var existing = await store.FindByHashAsync(hash, ct);

        if (existing is null
            || existing.RevokedAt is not null
            || existing.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidRefreshTokenException();

        if (existing.ConsumedAt is not null)
        {
            await store.RevokeFamilyAsync(existing.FamilyId, ct);
            throw new RefreshTokenReuseException();
        }

        await store.ConsumeAsync(existing.Id, ct);
        var account = await accounts.FindByIdAsync(existing.AccountId, ct)
            ?? throw new InvalidRefreshTokenException();
        return await RotateAsync(account, existing.FamilyId, ct);
    }

    /// <summary>Logout: revoke the family this token belongs to.</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await store.FindByHashAsync(
            tokens.HashRefreshToken(refreshToken), ct);
        if (existing is not null)
            await store.RevokeFamilyAsync(existing.FamilyId, ct);
    }

    private async Task<TokenPair> RotateAsync(
        Account account,
        Guid familyId,
        CancellationToken ct)
    {
        var (accessToken, accessExpiresAt) = tokens.SignAccessToken(account);
        var refreshToken = tokens.GenerateRefreshToken();
        await store.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            FamilyId = familyId,
            TokenHash = tokens.HashRefreshToken(refreshToken),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTtl),
        }, ct);
        return new TokenPair(accessToken, accessExpiresAt, refreshToken);
    }
}
