using System.Text.Json;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Catalog;

/// <summary>A model and a reasoning effort that the catalogue offers together.</summary>
internal readonly record struct AssistantChoice(string Model, string ReasoningEffort);

/// <summary>
/// Validation of a request's <c>assistant</c> member against the offered catalogue (protocol 7.3). A
/// rejected selection is never replaced by another model, and what is accepted is the catalogue's own
/// text: nothing a request wrote reaches the Codex command line.
/// </summary>
internal static class AssistantSelection
{
    private const string AssistantMember = "assistant";
    private const string ModelMember = "model";
    private const string EffortMember = "reasoningEffort";

    /// <summary>
    /// Step R-5: the body has an <c>assistant</c> object whose <c>model</c> and <c>reasoningEffort</c>
    /// are strings. Other members of it are ignored.
    /// </summary>
    public static bool TryReadShape(JsonElement body, out string model, out string reasoningEffort)
    {
        model = string.Empty;
        reasoningEffort = string.Empty;
        return body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(AssistantMember, out var assistant)
            && TryReadAssistant(assistant, out model, out reasoningEffort);
    }

    /// <summary>
    /// Step R-7: an offer with that id whose list contains that effort, both compared ordinally. The
    /// choice returned holds the catalogue's strings.
    /// </summary>
    public static bool TryFind(
        IReadOnlyList<ModelOffer> catalog,
        string model,
        string reasoningEffort,
        out AssistantChoice choice)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var offer in catalog)
        {
            if (!string.Equals(offer.Id, model, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var effort in offer.ReasoningEfforts)
            {
                if (string.Equals(effort, reasoningEffort, StringComparison.Ordinal))
                {
                    choice = new AssistantChoice(offer.Id, effort);
                    return true;
                }
            }
        }

        choice = default;
        return false;
    }

    /// <summary>
    /// Both steps over the value of <c>assistant</c> itself. Null means accepted; otherwise the error is
    /// INVALID_ASSISTANT or ASSISTANT_SELECTION_UNAVAILABLE. A value of kind Undefined stands for an
    /// absent member.
    /// </summary>
    public static RelayError? Validate(JsonElement assistant, IReadOnlyList<ModelOffer> catalog, out AssistantChoice choice)
    {
        choice = default;
        if (!TryReadAssistant(assistant, out var model, out var reasoningEffort))
        {
            return RelayErrors.InvalidAssistant;
        }

        return TryFind(catalog, model, reasoningEffort, out choice) ? null : RelayErrors.AssistantSelectionUnavailable;
    }

    /// <summary>
    /// The text of a JSON string. False for any other kind, and for a string that is not text at all (an
    /// escape that names half of a surrogate pair), which a request may well hold.
    /// </summary>
    public static bool TryReadText(JsonElement value, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            text = value.GetString() ?? string.Empty;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadAssistant(JsonElement assistant, out string model, out string reasoningEffort)
    {
        model = string.Empty;
        reasoningEffort = string.Empty;
        if (assistant.ValueKind != JsonValueKind.Object
            || !assistant.TryGetProperty(ModelMember, out var modelMember)
            || modelMember.ValueKind != JsonValueKind.String
            || !assistant.TryGetProperty(EffortMember, out var effortMember)
            || effortMember.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        // A string that is not text is still a string: it has the shape, and it is in no catalogue.
        _ = TryReadText(modelMember, out model);
        _ = TryReadText(effortMember, out reasoningEffort);
        return true;
    }
}
