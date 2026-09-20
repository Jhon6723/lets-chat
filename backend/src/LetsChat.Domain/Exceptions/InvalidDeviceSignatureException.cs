namespace LetsChat.Domain.Exceptions;

/// <summary>
/// Thrown when a device registration or operation cannot prove possession
/// of the identity key — the signature does not verify.
/// </summary>
public sealed class InvalidDeviceSignatureException : Exception;
