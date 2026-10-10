using System.Text;
using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// One read attempt of Codex's model list (protocol 12.1): a short exchange of JSON lines with
/// <c>codex app-server --listen stdio://</c>. Three lines are written, two replies are read, and the
/// process is killed whatever happens. Of each entry only the id, the display name, the hidden flag and
/// the reasoning efforts are kept; nothing about the account is read.
///
/// The process gets the allowlisted environment of protocol 12.6, at most C-24 of output and the
/// per-attempt limit of C-44. Every failure is a <see cref="CatalogReadException"/>.
/// </summary>
internal sealed class CodexCatalogSource : ICatalogSource
{
    private const string InitializedLine = """{"method":"initialized"}""";
    private const string ModelListLine = """{"id":2,"method":"model/list","params":{"limit":100,"includeHidden":false}}""";

    private static readonly string[] Arguments = ["app-server", "--listen", "stdio://"];

    private readonly RelayContext _context;
    private readonly string _initializeLine;

    public CodexCatalogSource(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _initializeLine = InitializeLine(context.Options.RelayVersion);
    }

    public async Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_context.Options.CodexExecutablePath is not { } codex)
        {
            throw new CatalogReadException();
        }

        // The session has the same limit. This timer makes the attempt end on the relay's clock even if
        // a session should fail to.
        using var limit = new CancellationTokenSource(ProtocolConstants.CatalogAttemptTimeout, _context.Clock);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        try
        {
            var session = await _context.Processes.StartSessionAsync(
                new ProcessRequest
                {
                    FileName = codex,
                    Arguments = Arguments,
                    Environment = EnvironmentPolicy.CodexAllowlist,
                    MaxStandardOutputBytes = ProtocolConstants.MaxCodexOutputBytes,
                    Timeout = ProtocolConstants.CatalogAttemptTimeout,
                },
                attempt.Token).ConfigureAwait(false);

            // Disposing closes standard input and kills the process tree, in every case.
            await using (session.ConfigureAwait(false))
            {
                return await ExchangeAsync(session, attempt.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The per-attempt limit passed.
            throw new CatalogReadException();
        }
        catch (Exception exception) when (exception is ProcessFailedException or IOException or ArgumentException)
        {
            // Not started, ended early, too much output, too slow, or a pipe that broke while writing.
            throw new CatalogReadException();
        }
    }

    /// <summary>Line 1 of protocol 12.1, with the relay's version as a JSON string.</summary>
    private static string InitializeLine(string relayVersion) => Encoding.UTF8.GetString(WireJson.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", 1);
        writer.WriteString("method", "initialize");
        writer.WriteStartObject("params");
        writer.WriteStartObject("clientInfo");
        writer.WriteString("name", "livecue_catalog");
        writer.WriteString("title", "LiveCue");
        writer.WriteString("version", relayVersion);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }));

    private async Task<IReadOnlyList<RawCatalogEntry>> ExchangeAsync(IProcessSession session, CancellationToken ct)
    {
        await session.WriteLineAsync(_initializeLine, ct).ConfigureAwait(false);
        var initialized = await ReadReplyAsync(session, id: 1, ct).ConfigureAwait(false);
        if (initialized.TryGetProperty("error", out _))
        {
            throw new CatalogReadException();
        }

        await session.WriteLineAsync(InitializedLine, ct).ConfigureAwait(false);
        await session.WriteLineAsync(ModelListLine, ct).ConfigureAwait(false);
        var listed = await ReadReplyAsync(session, id: 2, ct).ConfigureAwait(false);
        if (listed.TryGetProperty("error", out _)
            || !listed.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogReadException();
        }

        // A next-page cursor is ignored: the entries received are the catalogue.
        var entries = new List<RawCatalogEntry>(data.GetArrayLength());
        foreach (var element in data.EnumerateArray())
        {
            if (ToEntry(element) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// The next line that is a JSON object whose <c>id</c> is the number <paramref name="id"/>. Lines
    /// that are not JSON objects, notifications and replies to anything else are skipped. The end of the
    /// output is a failed read.
    /// </summary>
    private static async Task<JsonElement> ReadReplyAsync(IProcessSession session, int id, CancellationToken ct)
    {
        while (true)
        {
            var line = await session.ReadLineAsync(ct).ConfigureAwait(false) ?? throw new CatalogReadException();
            if (WireJson.TryParseObject(Encoding.UTF8.GetBytes(line), out var message)
                && message.TryGetProperty("id", out var found)
                && found.ValueKind == JsonValueKind.Number
                && found.TryGetInt32(out var number)
                && number == id)
            {
                return message;
            }
        }
    }

    /// <summary>What is kept of one entry, or null for an entry without a string <c>model</c>.</summary>
    private static RawCatalogEntry? ToEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("model", out var model)
            || !AssistantSelection.TryReadText(model, out var id))
        {
            return null;
        }

        string? displayName = null;
        if (element.TryGetProperty("displayName", out var name) && AssistantSelection.TryReadText(name, out var text))
        {
            displayName = text;
        }

        // Hidden unless the member is absent, false or null.
        var hidden = element.TryGetProperty("hidden", out var flag)
            && flag.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);

        var efforts = new List<string>();
        if (element.TryGetProperty("supportedReasoningEfforts", out var supported)
            && supported.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in supported.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("reasoningEffort", out var effort)
                    && AssistantSelection.TryReadText(effort, out var value))
                {
                    efforts.Add(value);
                }
            }
        }

        return new RawCatalogEntry(id, displayName, hidden, efforts);
    }
}
