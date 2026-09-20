using System.IdentityModel.Tokens.Jwt;
using LetsChat.Application.Ports;
using LetsChat.Application.Services;
using LetsChat.Domain.Exceptions;

namespace LetsChat.Api.Endpoints;

public sealed record RegisterDeviceRequest(string IdentityKeyPublic, string Signature);
public sealed record DeviceResponse(Guid DeviceId, int DeviceNumber, string Address);

/// <summary>
/// Device binding endpoints (ADR-0007). Require an account JWT; the request
/// body must additionally carry a signature from the device identity key —
/// proof the caller holds the private half.
/// </summary>
public static class DeviceEndpoints
{
    public static RouteGroupBuilder MapDevices(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/devices").RequireAuthorization();

        group.MapPost("/register", async (
            RegisterDeviceRequest req,
            HttpContext http,
            IAccountRepository accounts,
            DeviceService devices,
            CancellationToken ct) =>
        {
            var sub = http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (sub is null || !Guid.TryParse(sub, out var accountId))
                return Results.Unauthorized();

            var account = await accounts.FindByIdAsync(accountId, ct);
            if (account is null)
                return Results.Unauthorized();

            try
            {
                var device = await devices.RegisterAsync(
                    account, req.IdentityKeyPublic, req.Signature, ct);
                return Results.Created(
                    $"/devices/{device.Id}",
                    new DeviceResponse(device.Id, device.DeviceNumber, device.Address));
            }
            catch (InvalidDeviceSignatureException)
            {
                return Results.BadRequest(new { error = "invalid_device_signature" });
            }
        });

        return group;
    }
}
