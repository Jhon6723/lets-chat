using System.Text.Json;
using System.Text.Json.Nodes;
using LetsChat.Contracts;

namespace LetsChat.ContractTests;

/// <summary>
/// Validates that the C# wire records (LetsChat.Contracts) stay in sync with
/// the canonical TypeScript protocol (shared/protocol). Each test reads a
/// fixture generated from the TS source of truth, deserializes it into the
/// C# mirror, then re-serializes and compares the JSON shape field-by-field.
/// If either side drifts, this suite fails — ADR-0001 (revised) mitigation.
/// </summary>
public class ProtocolContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("envelope.json")]
    public void Envelope_fixture_round_trips(string fixture)
        => AssertRoundTrip<EncryptedEnvelope>(fixture);

    [Theory]
    [InlineData("prekey-bundle.json")]
    public void PreKeyBundle_fixture_round_trips(string fixture)
        => AssertRoundTrip<PreKeyBundle>(fixture);

    [Theory]
    [InlineData("prekey-bundle-response.json")]
    public void PreKeyBundleResponse_fixture_deserializes(string fixture)
    {
        var doc = JsonDocument.Parse(ReadFixture(fixture));
        var bundleJson = doc.RootElement.GetProperty("bundle").GetRawText();
        var bundle = JsonSerializer.Deserialize<PreKeyBundle>(bundleJson, Json);
        Assert.NotNull(bundle);
        AssertJsonEqual(bundleJson, JsonSerializer.Serialize(bundle, Json));
    }

    [Theory]
    [InlineData("relay-client-send.json")]
    [InlineData("relay-client-ack.json")]
    [InlineData("relay-client-fetch-pending.json")]
    public void Relay_client_message_fixtures_round_trip(string fixture)
        => AssertRoundTrip<RelayClientMessage>(fixture);

    [Theory]
    [InlineData("relay-server-envelope.json")]
    [InlineData("relay-server-ack-ok.json")]
    [InlineData("relay-server-pending.json")]
    [InlineData("relay-server-error.json")]
    public void Relay_server_message_fixtures_round_trip(string fixture)
        => AssertRoundTrip<RelayServerMessage>(fixture);

    [Fact]
    public void Envelope_fixture_values_are_correct()
    {
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(ReadFixture("envelope.json"), Json);
        Assert.NotNull(envelope);
        Assert.Equal(ProtocolVersion.Value, envelope.Version);
        Assert.Equal("alice.1", envelope.SenderAddress);
        Assert.Equal("bob.1", envelope.RecipientAddress);
        Assert.Equal("message", envelope.Type);
    }

    /// <summary>
    /// Deserialize the fixture into T, re-serialize, and deep-compare JSON.
    /// Any field the C# mirror drops, renames, or reshapes makes this fail.
    /// </summary>
    private static void AssertRoundTrip<T>(string fixture)
    {
        var fixtureJson = ReadFixture(fixture);
        var value = JsonSerializer.Deserialize<T>(fixtureJson, Json);
        Assert.NotNull(value);
        AssertJsonEqual(fixtureJson, JsonSerializer.Serialize(value, Json));
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(actual);
        Assert.True(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"JSON shape mismatch.\nExpected: {expected}\nActual:   {actual}");
    }

    /// <summary>Locate shared/protocol/fixtures by walking up from the test output dir.</summary>
    private static string ReadFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "shared", "protocol", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir.FullName, "shared", "protocol", "fixtures", name);
        Assert.True(File.Exists(path), $"fixture not found: {path} — run `make fixtures`");
        return File.ReadAllText(path);
    }
}
