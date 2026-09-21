using System.Net.WebSockets;
using System.Text.Json;
using LetsChat.Application.Services;
using LetsChat.Contracts;
using LetsChat.Domain.Entities;

namespace LetsChat.Api.Middleware;

/// <summary>
/// Inbound WS adapter: nonce-challenge authentication first (ADR-0007 — the
/// connection proves possession of a registered device identity key), then
/// the relay loop bound to that device address.
/// </summary>
public sealed class RelayWebSocketMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(15);

    public async Task InvokeAsync(
        HttpContext context,
        EnvelopeService envelopes,
        RelayAuthService auth)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await next(context);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var device = await AuthenticateAsync(socket, auth, context.RequestAborted);
        if (device is null) return; // error already sent, socket closed

        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveAsync<RelayClientMessage>(
                socket, context.RequestAborted);
            if (message is null) break;

            RelayServerMessage reply = message switch
            {
                SendMessage send => await Relay(send.Envelope, envelopes, context.RequestAborted),
                AckMessage ack => await Ack(ack.EnvelopeId, envelopes, context.RequestAborted),
                FetchPendingMessage => new PendingMessage([]), // TODO(1.8): fetch device's mailbox
                _ => new ErrorMessage("BAD_MESSAGE", "unknown or malformed message"),
            };

            await Send(socket, reply, context.RequestAborted);
        }
    }

    /// <summary>
    /// Challenge → answer handshake. Sends a fresh nonce, waits up to
    /// AuthTimeout for an "auth" message, verifies the signature against the
    /// device's registered identity key. Returns the bound device or null.
    /// </summary>
    private static async Task<Device?> AuthenticateAsync(
        WebSocket socket, RelayAuthService auth, CancellationToken ct)
    {
        var nonce = RelayAuthService.IssueChallenge();
        await Send(socket, new AuthChallengeMessage(nonce), ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AuthTimeout);

        RelayClientMessage? message;
        try
        {
            message = await ReceiveAsync<RelayClientMessage>(socket, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            message = null;
        }

        if (message is not AuthMessage answer)
        {
            await Send(socket,
                new ErrorMessage("AUTH_REQUIRED", "first message must be auth"), ct);
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation, "auth required", ct);
            return null;
        }

        var device = await auth.AuthenticateAsync(
            answer.Address, nonce, answer.Signature, ct);
        if (device is null)
        {
            await Send(socket,
                new ErrorMessage("AUTH_FAILED", "invalid device signature"), ct);
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation, "auth failed", ct);
            return null;
        }

        await Send(socket, new AuthOkMessage(device.Address), ct);
        return device;
    }

    private static async Task<RelayServerMessage> Relay(
        EncryptedEnvelope envelope, EnvelopeService envelopes, CancellationToken ct)
    {
        await envelopes.RelayAsync(envelope, ct);
        return new AckOkMessage(envelope.Id);
    }

    private static async Task<RelayServerMessage> Ack(
        string envelopeId, EnvelopeService envelopes, CancellationToken ct)
    {
        await envelopes.AcknowledgeAsync(envelopeId, ct);
        return new AckOkMessage(envelopeId);
    }

    /// <summary>Receive one complete text message (fragmentation-safe), or null on close/error.</summary>
    private static async Task<T?> ReceiveAsync<T>(WebSocket socket, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (WebSocketException)
            {
                return default;
            }
            if (result.MessageType == WebSocketMessageType.Close) return default;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        try
        {
            return JsonSerializer.Deserialize<T>(ms.ToArray(), Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static Task Send(WebSocket socket, RelayServerMessage message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }
}
