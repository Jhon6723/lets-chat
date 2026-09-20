using LetsChat.Application.Services;
using LetsChat.Domain.Exceptions;

namespace LetsChat.Api.Endpoints;

public sealed record RegisterRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password);
public sealed record AccountResponse(Guid AccountId, string Username);

/// <summary>
/// Account auth endpoints (ADR-0007). Rate-limited per IP (Program.cs "auth"
/// policy). Login currently returns the account; JWT issuance lands in the
/// session task — responses never reveal which credential part failed.
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
            LoginRequest req, AccountService accounts, CancellationToken ct) =>
        {
            var account = await accounts.VerifyCredentialsAsync(
                req.Username, req.Password, ct);
            return account is null
                ? Results.Unauthorized()
                : Results.Ok(new AccountResponse(account.Id, account.Username.Value));
        });

        return group;
    }
}
