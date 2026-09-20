using LetsChat.Domain.Aggregates;
using LetsChat.Domain.ValueObjects;

namespace LetsChat.Application.Ports;

/// <summary>
/// Outbound port: where accounts live. The domain never sees a concrete type —
/// implementations: PostgresAccountStore (EF Core, ADR-0002).
/// </summary>
public interface IAccountStore
{
    /// <summary>Find an account by username, or null.</summary>
    Task<Account?> FindByUsernameAsync(Username username, CancellationToken ct = default);

    /// <summary>Persist a new account. Caller supplies the already-hashed password.</summary>
    Task<Account> CreateAsync(
        Username username,
        string passwordHash,
        CancellationToken ct = default);
}
