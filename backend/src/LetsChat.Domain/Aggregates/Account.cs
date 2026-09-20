using LetsChat.Domain.ValueObjects;

namespace LetsChat.Domain.Aggregates;

/// <summary>
/// Account aggregate root — social identity (ADR-0007). Identity comes from
/// Id, not field values, so it is a class, not a record: two rows with equal
/// fields but different Ids are different accounts.
/// PasswordHash is domain-internal and must never reach a wire DTO.
/// </summary>
public sealed class Account
{
    public required Guid Id { get; init; }
    public required Username Username { get; init; }
    public required string PasswordHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
