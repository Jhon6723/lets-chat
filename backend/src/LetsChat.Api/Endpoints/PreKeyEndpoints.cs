using LetsChat.Api.Auth;
using LetsChat.Application.Services;
using LetsChat.Contracts;

namespace LetsChat.Api.Endpoints;

public sealed record PublishPreKeysRequest(
    SignedPreKey SignedPreKey,
    IReadOnlyList<OneTimePreKey> OneTimePreKeys,
    PqLastResortPreKey? PqLastResortPreKey);

/// <summary>
/// The prekey directory (ADR-0006 revised 2026-09-21). Both endpoints sit
/// behind the device-signature filter: PUT proves the caller owns the device
/// being published; GET authenticates the fetcher so the contact-edge check
/// can run. Without an edge the directory answers 403 — an address alone is
/// worth nothing.
/// </summary>
public static class PreKeyEndpoints
{
    public static RouteGroupBuilder MapPreKeys(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/prekeys");

        group.MapPut("/{address}", async (
            string address,
            PublishPreKeysRequest req,
            HttpContext http,
            PreKeyService prekeys,
            CancellationToken ct) =>
        {
            var device = http.AuthenticatedDevice();
            if (device is null) return Results.Unauthorized();
            if (!string.Equals(device.Address, address, StringComparison.Ordinal))
                return Results.Forbid(); // can't publish under someone else's address

            await prekeys.PublishAsync(
                device, req.SignedPreKey, req.OneTimePreKeys,
                req.PqLastResortPreKey, ct);
            return Results.NoContent();
        }).RequireDeviceSignature();

        group.MapGet("/{address}", async (
            string address,
            HttpContext http,
            PreKeyService prekeys,
            CancellationToken ct) =>
        {
            var device = http.AuthenticatedDevice();
            if (device is null) return Results.Unauthorized();

            var (result, bundle) = await prekeys.FetchAsync(device, address, ct);
            return result switch
            {
                PreKeyFetchResult.Ok => Results.Ok(bundle),
                PreKeyFetchResult.NoBundle => Results.NotFound(
                    new { error = "no_bundle_published" }),
                _ => Results.Forbid(),
            };
        }).RequireDeviceSignature();

        return group;
    }
}
