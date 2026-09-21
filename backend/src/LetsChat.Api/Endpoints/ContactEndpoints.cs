using System.IdentityModel.Tokens.Jwt;
using LetsChat.Application.Ports;
using LetsChat.Application.Services;

namespace LetsChat.Api.Endpoints;

public sealed record ContactRequestBody(string Username);
public sealed record ContactEntryResponse(Guid EdgeId, string Username, DateTimeOffset Since);
public sealed record ContactListResponse(
    IReadOnlyList<ContactEntryResponse> Contacts,
    IReadOnlyList<ContactEntryResponse> Incoming,
    IReadOnlyList<ContactEntryResponse> Outgoing);

/// <summary>
/// Contact graph endpoints (ADR-0006 revised 2026-09-21). Account-JWT scope —
/// this is social identity, not device material. Responses are deliberately
/// generic: requesting or blocking an unknown/blocked username answers the
/// same as a real one, so the endpoint cannot be used for enumeration.
/// </summary>
public static class ContactEndpoints
{
    public static RouteGroupBuilder MapContacts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contacts").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http,
            IAccountRepository accounts,
            ContactService contacts,
            CancellationToken ct) =>
        {
            var account = await RequireAccount(http, accounts, ct);
            if (account is null) return Results.Unauthorized();

            var list = await contacts.ListAsync(account, ct);
            return Results.Ok(new ContactListResponse(
                Map(list.Contacts), Map(list.Incoming), Map(list.Outgoing)));
        });

        group.MapPost("/requests", async (
            ContactRequestBody req,
            HttpContext http,
            IAccountRepository accounts,
            ContactService contacts,
            CancellationToken ct) =>
        {
            var account = await RequireAccount(http, accounts, ct);
            if (account is null) return Results.Unauthorized();

            var edge = await contacts.RequestAsync(account, req.Username, ct);
            // Same shape whether the request was created, auto-accepted, or the
            // target doesn't exist / blocked us — no enumeration oracle.
            return Results.Ok(new
            {
                status = edge?.Status.ToString().ToLowerInvariant() ?? "pending",
            });
        });

        group.MapPost("/requests/{id:guid}/accept", async (
            Guid id,
            HttpContext http,
            IAccountRepository accounts,
            ContactService contacts,
            CancellationToken ct) =>
        {
            var account = await RequireAccount(http, accounts, ct);
            if (account is null) return Results.Unauthorized();

            var edge = await contacts.AcceptAsync(account, id, ct);
            return edge is null
                ? Results.NotFound(new { error = "request_not_found" })
                : Results.Ok(new { status = "accepted" });
        });

        // Decline (addressee) or cancel (requester) a pending edge;
        // remove an accepted contact — same verb, same row-delete.
        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAccountRepository accounts,
            ContactService contacts,
            CancellationToken ct) =>
        {
            var account = await RequireAccount(http, accounts, ct);
            if (account is null) return Results.Unauthorized();

            return await contacts.RemoveAsync(account, id, ct)
                ? Results.NoContent()
                : Results.NotFound(new { error = "edge_not_found" });
        });

        group.MapPost("/blocks", async (
            ContactRequestBody req,
            HttpContext http,
            IAccountRepository accounts,
            ContactService contacts,
            CancellationToken ct) =>
        {
            var account = await RequireAccount(http, accounts, ct);
            if (account is null) return Results.Unauthorized();

            await contacts.BlockAsync(account, req.Username, ct);
            return Results.NoContent();
        });

        return group;
    }

    private static async Task<Domain.Aggregates.Account?> RequireAccount(
        HttpContext http, IAccountRepository accounts, CancellationToken ct)
    {
        var sub = http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (sub is null || !Guid.TryParse(sub, out var accountId))
            return null;
        return await accounts.FindByIdAsync(accountId, ct);
    }

    private static IReadOnlyList<ContactEntryResponse> Map(
        IReadOnlyList<ContactEntry> entries)
        => entries.Select(e => new ContactEntryResponse(e.EdgeId, e.Username, e.Since))
            .ToList();
}
