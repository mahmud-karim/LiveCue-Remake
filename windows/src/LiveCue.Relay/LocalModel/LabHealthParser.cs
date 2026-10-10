using System.Text.Json;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.LocalModel;

/// <summary>
/// Reads the four members the relay uses of the lab's health reply (protocol 11.2) and decides whether a
/// reply is usable (protocol 9.2). Every other member of the reply is ignored.
/// </summary>
internal static class LabHealthParser
{
    /// <summary>
    /// A <see cref="LabHealth"/> only for one JSON object whose <c>ready</c> and <c>busy</c> are booleans
    /// and whose <c>relayProtocol</c> is a whole number. <c>model</c> is taken when it is a string and is
    /// null otherwise. Anything else, including a reply above W-06, is null: "no lab".
    /// </summary>
    public static LabHealth? Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > ProtocolConstants.MaxProbeBodyBytes || !WireJson.TryParseObject(utf8, out var root))
        {
            return null;
        }

        if (!TryReadBoolean(root, "ready", out var ready)
            || !TryReadBoolean(root, "busy", out var busy)
            || !root.TryGetProperty("relayProtocol", out var protocol)
            || protocol.ValueKind != JsonValueKind.Number
            || !protocol.TryGetInt32(out var relayProtocol))
        {
            return null;
        }

        var model = root.TryGetProperty("model", out var member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()
            : null;
        return new LabHealth(ready, model, busy, relayProtocol);
    }

    /// <summary>
    /// Protocol 9.2 "usable": a reply with <c>ready</c> true, <c>relayProtocol</c> equal to the one version
    /// the relay speaks and <c>model</c> one of C-04. On true, <paramref name="model"/> is that member of
    /// C-04.
    /// </summary>
    public static bool IsUsable(LabHealth? health, out string model)
    {
        model = string.Empty;
        if (health is not { Ready: true, RelayProtocol: ProtocolConstants.LabRelayProtocol, Model: { } reported })
        {
            return false;
        }

        foreach (var known in ProtocolConstants.SpeechModels)
        {
            if (string.Equals(known, reported, StringComparison.Ordinal))
            {
                model = known;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var member) || member.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = member.ValueKind == JsonValueKind.True;
        return true;
    }
}
