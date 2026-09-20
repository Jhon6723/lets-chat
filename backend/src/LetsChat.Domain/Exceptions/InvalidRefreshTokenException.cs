namespace LetsChat.Domain.Exceptions;

/// <summary>Thrown when a refresh token is unknown, expired, or already revoked.</summary>
public sealed class InvalidRefreshTokenException : Exception;
