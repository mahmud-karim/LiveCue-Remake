using System.Text.Json;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// Validation of <c>assistant</c> against the offered catalogue (protocol 7.3; statement P2-041). The
/// catalogue is <c>[{"id":"gpt-test","name":"Test","reasoningEfforts":["low"]}]</c>.
/// </summary>
public sealed class AssistantSelectionTests
{
    private static readonly ModelOffer[] Catalog = [new("gpt-test", "Test", ["low"])];

    [Theory]

    // Accepted: exactly the pair, whatever else the object holds.
    [InlineData("""{"model":"gpt-test","reasoningEffort":"low"}""", null)]
    [InlineData("""{"model":"gpt-test","reasoningEffort":"low","provider":"codex","extra":[1,2]}""", null)]

    // Not an object, or a member missing or not a string.
    [InlineData("null", RelayCodes.InvalidAssistant)]
    [InlineData("[]", RelayCodes.InvalidAssistant)]
    [InlineData("\"model\"", RelayCodes.InvalidAssistant)]
    [InlineData("7", RelayCodes.InvalidAssistant)]
    [InlineData("true", RelayCodes.InvalidAssistant)]
    [InlineData("{}", RelayCodes.InvalidAssistant)]
    [InlineData("""{"model":"gpt-test"}""", RelayCodes.InvalidAssistant)]
    [InlineData("""{"reasoningEffort":"low"}""", RelayCodes.InvalidAssistant)]
    [InlineData("""{"model":7,"reasoningEffort":"low"}""", RelayCodes.InvalidAssistant)]
    [InlineData("""{"model":"gpt-test","reasoningEffort":null}""", RelayCodes.InvalidAssistant)]
    [InlineData("""{"model":["gpt-test"],"reasoningEffort":"low"}""", RelayCodes.InvalidAssistant)]
    [InlineData("""{"Model":"gpt-test","ReasoningEffort":"low"}""", RelayCodes.InvalidAssistant)]

    // Well formed, and not what the catalogue offers.
    [InlineData("""{"model":"--config","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test","reasoningEffort":"low\";shell=true"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test","reasoningEffort":"high"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-other","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"GPT-TEST","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test","reasoningEffort":"LOW"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test ","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test\n","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test","reasoningEffort":"low\n"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"","reasoningEffort":""}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"gpt-test\u0000","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    [InlineData("""{"model":"\ud800","reasoningEffort":"low"}""", RelayCodes.AssistantSelectionUnavailable)]
    public void Validation_cases(string assistantJson, string? expectedCode)
    {
        using var document = JsonDocument.Parse(assistantJson);

        var error = AssistantSelection.Validate(document.RootElement, Catalog, out var choice);

        Assert.Equal(expectedCode, error?.Code);
        if (expectedCode is null)
        {
            // Exactly that pair, and it is the catalogue's own text.
            Assert.Equal(new AssistantChoice("gpt-test", "low"), choice);
            Assert.Same(Catalog[0].Id, choice.Model);
            Assert.Same(Catalog[0].ReasoningEfforts[0], choice.ReasoningEffort);
        }
        else
        {
            Assert.Equal(default, choice);
        }
    }

    [Fact]
    public void An_absent_assistant_is_invalid_and_the_shape_is_read_from_the_body()
    {
        using var without = JsonDocument.Parse("""{"requestId":"r"}""");
        using var with = JsonDocument.Parse("""{"assistant":{"model":"gpt-test","reasoningEffort":"low"}}""");
        using var malformed = JsonDocument.Parse("""{"assistant":"gpt-test"}""");

        Assert.Equal(RelayErrors.InvalidAssistant, AssistantSelection.Validate(default, Catalog, out _));
        Assert.False(AssistantSelection.TryReadShape(without.RootElement, out _, out _));
        Assert.False(AssistantSelection.TryReadShape(malformed.RootElement, out _, out _));
        Assert.True(AssistantSelection.TryReadShape(with.RootElement, out var model, out var effort));
        Assert.Equal(("gpt-test", "low"), (model, effort));
    }

    [Fact]
    public void Nothing_validates_against_an_empty_catalogue()
    {
        using var document = JsonDocument.Parse("""{"model":"gpt-test","reasoningEffort":"low"}""");

        Assert.Equal(
            RelayErrors.AssistantSelectionUnavailable,
            AssistantSelection.Validate(document.RootElement, [], out _));
    }
}
