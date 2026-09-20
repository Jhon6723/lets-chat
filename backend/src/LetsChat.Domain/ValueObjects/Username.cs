using System.Text.RegularExpressions;

namespace LetsChat.Domain.ValueObjects;

/// <summary>
/// Username value object: owns normalization (trim + lowercase) and format
/// validation. Equality by value — two Usernames with the same text are the
/// same username. Parse throws on invalid input; construction is impossible
/// without validation.
/// </summary>
public sealed partial record Username
{
    [GeneratedRegex("^[a-z0-9_]{3,32}$")]
    private static partial Regex Pattern();

    public string Value { get; }

    private Username(string value) => Value = value;

    public static Username Parse(string raw)
        => TryParse(raw) ?? throw new ArgumentException(
            "username must be 3-32 chars of [a-z0-9_]", nameof(raw));

    /// <summary>Null-tolerant parse for paths where bad input is a normal case (login).</summary>
    public static Username? TryParse(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        return Pattern().IsMatch(normalized) ? new Username(normalized) : null;
    }

    public override string ToString() => Value;
}
