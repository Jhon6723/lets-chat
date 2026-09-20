using LetsChat.Application.Services;
using LetsChat.Domain.Exceptions;

namespace LetsChat.Api.Endpoints;

public sealed record RegisterRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record AccountResponse(Guid AccountId, string Username);
public sealed record LoginResponse(
    Guid AccountId,
    string Username,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);
public sealed record RefreshResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

/// <summary>
/// Account auth endpoints (ADR-0007). Rate-limited per IP (Program.cs "auth"
/// policy). Responses never reveal which credential part failed.
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting("auth");

        group.MapPost("/register", async (
            RegisterRequest req, AccountService accounts, CancellationToken ct) =>
        {
            try
            {
                var account = await accounts.RegisterAsync(req.Username, req.Password, ct);
                return Results.Created(
                    $"/accounts/{account.Id}",
                    new AccountResponse(account.Id, account.Username.Value));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UsernameTakenException)
            {
                return Results.Conflict(new { error = "username_taken" });
            }
        });

        group.MapPost("/login", async (
            LoginRequest req,
            AccountService accounts,
            SessionService sessions,
            CancellationToken ct) =>
        {
            var account = await accounts.VerifyCredentialsAsync(
                req.Username, req.Password, ct);
            if (account is null)
                return Results.Unauthorized();

            var (tokens, accountId) = await sessions.OpenAsync(account, ct);
            return Results.Ok(new LoginResponse(
                accountId,
                account.Username.Value,
                tokens.AccessToken,
                tokens.AccessTokenExpiresAt,
                tokens.RefreshToken));
        });

        group.MapPost("/refresh", async (
            RefreshRequest req,
            SessionService sessions,
            LetsChat.Application.Ports.IAccountRepository accountStore,
            CancellationToken ct) =>
        {
            try
            {
                var tokens = await sessions.RefreshAsync(
                    req.RefreshToken, accountStore, ct);
                return Results.Ok(new RefreshResponse(
                    tokens.AccessToken,
                    tokens.AccessTokenExpiresAt,
                    tokens.RefreshToken));
            }
            catch (Exception ex) when (ex is InvalidRefreshTokenException
                                           or RefreshTokenReuseException)
            {
                return Results.Unauthorized();
            }
        });

        group.MapPost("/logout", async (
            LogoutRequest req, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.RevokeAsync(req.RefreshToken, ct);
            return Results.NoContent();
        });

        return group;
    }
}
