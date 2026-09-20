using LetsChat.Domain.Entities;

namespace LetsChat.Application.Ports;

/// <summary>Outbound port: persistence for refresh token rotation state.</summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>Mark a token as consumed by rotation.</summary>
    Task ConsumeAsync(Guid tokenId, CancellationToken ct = default);

    /// <summary>Revoke every token in the family — logout, or reuse detected.</summary>
    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);
}
