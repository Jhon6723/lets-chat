namespace LetsChat.Domain.Exceptions;

/// <summary>Thrown when registering a username that already exists.</summary>
public sealed class UsernameTakenException : Exception;
