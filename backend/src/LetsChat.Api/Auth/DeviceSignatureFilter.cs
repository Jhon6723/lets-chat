using System.Security.Cryptography;
using System.Text;
using LetsChat.Application.Ports;
using LetsChat.Domain.Entities;

namespace LetsChat.Api.Auth;

/// <summary>
/// Device-signature auth for sensitive REST endpoints (ADR-0007). The client
/// sends X-Device-Address, X-Device-Timestamp (unix ms) and X-Device-Signature
/// over the canonical payload {method}:{path}:{timestamp}:{sha256(body-hex)}.
/// Freshness is bounded to ±5 min; the resolved device lands in HttpContext.Items.
/// </summary>
public sealed class DeviceSignatureFilter(
    IDeviceRepository devices,
    IDeviceSignatureVerifier verifier) : IEndpointFilter
{
    public const string DeviceItemKey = "device";
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var address = http.Request.Headers["X-Device-Address"].ToString();
        var timestamp = http.Request.Headers["X-Device-Timestamp"].ToString();
        var signature = http.Request.Headers["X-Device-Signature"].ToString();

        if (address.Length == 0 || timestamp.Length == 0 || signature.Length == 0
            || !long.TryParse(timestamp, out var ts)
            || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts)
                > ClockTolerance.TotalMilliseconds)
            return Results.Unauthorized();

        var device = await devices.FindByAddressAsync(address, http.RequestAborted);
        if (device is null)
            return Results.Unauthorized();

        var bodyHash = await HashBodyAsync(http.Request);
        var payload = Encoding.UTF8.GetBytes(
            $"{http.Request.Method}:{http.Request.Path}:{ts}:{bodyHash}");

        if (!verifier.Verify(device.IdentityKeyPublic, payload, signature))
            return Results.Unauthorized();

        http.Items[DeviceItemKey] = device;
        return await next(context);
    }

    private static async Task<string> HashBodyAsync(HttpRequest request)
    {
        // Buffering is enabled early in the pipeline (Program.cs); model
        // binding may already have consumed the stream, so rewind first.
        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

public static class DeviceSignatureExtensions
{
    /// <summary>Gate an endpoint behind a device signature.</summary>
    public static RouteHandlerBuilder RequireDeviceSignature(
        this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<DeviceSignatureFilter>();

    /// <summary>The authenticated device resolved by the signature filter.</summary>
    public static Device? AuthenticatedDevice(this HttpContext http)
        => http.Items[DeviceSignatureFilter.DeviceItemKey] as Device;
}
