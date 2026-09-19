using System.Net.WebSockets;
using System.Text.Json;
using LetsChat.Application.Services;
using LetsChat.Contracts;

namespace LetsChat.Api.Middleware;

/// <summary>
/// Inbound WS adapter: translates socket frames into application calls.
/// Message shape mirrors shared/protocol — the same JSON the PWA serializes
/// (ADR-0001 revised; contract tests keep the two sides honest).
/// </summary>
public sealed class RelayWebSocketMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context, EnvelopeService envelopes)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await next(context);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[16 * 1024];

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Text) continue;

            RelayClientMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<RelayClientMessage>(buffer.AsSpan(0, result.Count), Json);
            }
            catch (JsonException)
            {
                message = null;
            }

            var reply = message switch
            {
                SendMessage send => await Relay(send.Envelope, envelopes, context.RequestAborted),
                AckMessage ack => await Ack(ack.EnvelopeId, envelopes, context.RequestAborted),
                FetchPendingMessage => new PendingMessage([]), // TODO: recipient from authenticated connection
                _ => new ErrorMessage("BAD_MESSAGE", "unknown or malformed message"),
            };

            await Send(socket, reply, context.RequestAborted);
        }
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

    private static Task Send(WebSocket socket, RelayServerMessage message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }
}
