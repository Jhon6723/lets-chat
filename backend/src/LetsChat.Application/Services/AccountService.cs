using LetsChat.Application.Ports;
using LetsChat.Domain.Aggregates;
using LetsChat.Domain.Exceptions;
using LetsChat.Domain.ValueObjects;

namespace LetsChat.Application.Services;

/// <summary>
/// Account auth rules (ADR-0007): register = validate + hash + store;
/// login = verify. Failure modes are deliberately undifferentiated at the
/// endpoint so username enumeration is not possible from responses.
/// </summary>
public sealed class AccountService(IAccountStore accounts, IPasswordHasher hasher)
{
    private const int MinPasswordLength = 10;

    public async Task<Account> RegisterAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var name = Username.Parse(username);
        if (password.Length < MinPasswordLength)
            throw new ArgumentException(
                $"password must be at least {MinPasswordLength} chars");

        if (await accounts.FindByUsernameAsync(name, ct) is not null)
            throw new UsernameTakenException();

        return await accounts.CreateAsync(name, hasher.Hash(password), ct);
    }

    /// <summary>Returns the account on valid credentials, null otherwise.</summary>
    public async Task<Account?> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var name = Username.TryParse(username);
        var account = name is null
            ? null
            : await accounts.FindByUsernameAsync(name, ct);
        return account is not null && hasher.Verify(password, account.PasswordHash)
            ? account
            : null;
    }
}
