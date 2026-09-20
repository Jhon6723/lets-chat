namespace LetsChat.Domain.Exceptions;

/// <summary>
/// Thrown when a already-consumed refresh token is presented again —
/// the theft signal of ADR-0007. The whole token family must be revoked.
/// </summary>
public sealed class RefreshTokenReuseException : Exception;
