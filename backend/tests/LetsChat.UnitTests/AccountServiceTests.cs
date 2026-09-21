using LetsChat.Application.Services;
using LetsChat.Domain.Exceptions;
using LetsChat.Infrastructure.Identity;

namespace LetsChat.UnitTests;

/// <summary>
/// Registration/login rules (phase 1.2). Uses the real Argon2id hasher —
/// the hashes in these tests are genuine, not stubs.
/// </summary>
public class AccountServiceTests
{
    private readonly FakeAccountRepository _accounts = new();
    private readonly AccountService _service;

    public AccountServiceTests()
        => _service = new AccountService(_accounts, new Argon2PasswordHasher());

    [Fact]
    public async Task Register_persists_account_with_hashed_password()
    {
        var account = await _service.RegisterAsync("alice", "supersecret1");

        Assert.Equal("alice", account.Username.Value);
        Assert.NotEqual("supersecret1", account.PasswordHash);
        Assert.NotNull(await _accounts.FindByIdAsync(account.Id));
    }

    [Fact]
    public async Task Register_normalizes_username_case()
    {
        var account = await _service.RegisterAsync("  Alice  ", "supersecret1");
        Assert.Equal("alice", account.Username.Value);
    }

    [Fact]
    public async Task Register_rejects_short_password()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.RegisterAsync("alice", "short"));
    }

    [Fact]
    public async Task Register_rejects_invalid_username()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.RegisterAsync("bad name!", "supersecret1"));
    }

    [Fact]
    public async Task Register_rejects_taken_username()
    {
        await _service.RegisterAsync("alice", "supersecret1");
        await Assert.ThrowsAsync<UsernameTakenException>(
            () => _service.RegisterAsync("alice", "different-pw"));
    }

    [Fact]
    public async Task Verify_returns_account_on_valid_credentials()
    {
        await _service.RegisterAsync("alice", "supersecret1");
        var account = await _service.VerifyCredentialsAsync("alice", "supersecret1");
        Assert.NotNull(account);
    }

    [Fact]
    public async Task Verify_returns_null_on_wrong_password()
    {
        await _service.RegisterAsync("alice", "supersecret1");
        Assert.Null(await _service.VerifyCredentialsAsync("alice", "wrongpassword"));
    }

    [Fact]
    public async Task Verify_returns_null_on_unknown_user()
        => Assert.Null(await _service.VerifyCredentialsAsync("ghost", "supersecret1"));

    [Fact]
    public async Task Verify_returns_null_on_malformed_username()
        => Assert.Null(await _service.VerifyCredentialsAsync("!!bad!!", "supersecret1"));
}
