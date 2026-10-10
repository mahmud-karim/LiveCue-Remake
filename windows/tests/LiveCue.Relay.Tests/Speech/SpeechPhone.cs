using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>How a stream ended, as the phone saw it.</summary>
/// <param name="Frames">Every text frame that arrived before the end, in order.</param>
/// <param name="Status">The close code, or null when the connection dropped without a close frame.</param>
/// <param name="Reason">The close reason text; empty when there was none.</param>
internal sealed record SpeechEnding(IReadOnlyList<string> Frames, WebSocketCloseStatus? Status, string Reason)
{
    /// <summary>True when the connection ended without a close frame.</summary>
    public bool Dropped => Status is null;

    /// <summary>The frames of type <c>error</c>.</summary>
    public IReadOnlyList<JsonElement> Errors =>
        [.. Frames.Select(SpeechPhone.Parse).Where(frame => SpeechPhone.TypeOf(frame) == "error")];

    /// <summary>The <c>type</c> of every frame, in order.</summary>
    public IReadOnlyList<string?> Types => [.. Frames.Select(frame => SpeechPhone.TypeOf(SpeechPhone.Parse(frame)))];
}

/// <summary>
/// The phone's end of a speech socket in a test: a <see cref="ClientWebSocket"/> with the reading and
/// writing a test needs. Every wait is bounded in real time, so a broken relay fails a test instead of
/// hanging it; no wait stands for a protocol limit.
/// </summary>
internal sealed class SpeechPhone : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly byte[] _buffer = new byte[64 * 1024];

    public SpeechPhone(ClientWebSocket socket)
    {
        Socket = socket;
    }

    public ClientWebSocket Socket { get; }

    /// <summary>The close code, once a close frame arrived.</summary>
    public WebSocketCloseStatus? CloseStatus => Socket.CloseStatus;

    public static JsonElement Parse(string frame)
    {
        using var document = JsonDocument.Parse(frame);
        return document.RootElement.Clone();
    }

    public static string? TypeOf(JsonElement frame) =>
        frame.ValueKind == JsonValueKind.Object
        && frame.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    /// <summary>The next text frame; null when a close frame arrived instead.</summary>
    public async Task<string?> ReceiveTextAsync()
    {
        using var limit = new CancellationTokenSource(Patience);
        using var message = new MemoryStream();
        while (true)
        {
            var part = await Socket.ReceiveAsync(_buffer.AsMemory(), limit.Token);
            if (part.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            Assert.Equal(WebSocketMessageType.Text, part.MessageType);
            message.Write(_buffer, 0, part.Count);
            if (part.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
    }

    /// <summary>The next frame, which must exist and be of that type.</summary>
    public async Task<JsonElement> ExpectAsync(string type)
    {
        var text = await ReceiveTextAsync();
        Assert.NotNull(text);
        var frame = Parse(text);
        Assert.True(type == TypeOf(frame), $"Expected a '{type}' frame, got: {Shorten(text)}");
        return frame;
    }

    /// <summary>The next frame of that type; <c>loading</c> frames before it are skipped.</summary>
    public async Task<JsonElement> WaitForAsync(string type)
    {
        while (true)
        {
            var text = await ReceiveTextAsync();
            Assert.True(text is not null, $"The socket closed ({Socket.CloseStatusDescription}) before a '{type}' frame.");
            var frame = Parse(text);
            var found = TypeOf(frame);
            if (found == type)
            {
                return frame;
            }

            Assert.True(found == "loading", $"Expected a '{type}' frame, got: {Shorten(text)}");
        }
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> audio)
    {
        using var limit = new CancellationTokenSource(Patience);
        await Socket.SendAsync(audio, WebSocketMessageType.Binary, endOfMessage: true, limit.Token);
    }

    /// <summary>Sends that many zero bytes as one audio frame.</summary>
    public Task SendAudioAsync(int bytes) => SendAudioAsync(new byte[bytes]);

    public async Task SendTextAsync(string text)
    {
        using var limit = new CancellationTokenSource(Patience);
        await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, limit.Token);
    }

    public Task SendEndStreamAsync() => SendTextAsync("""{"type":"endStream"}""");

    /// <summary>
    /// Reads until the stream ends: every frame, then the close frame (which is answered, as a phone
    /// does) or the drop of the connection.
    /// </summary>
    public async Task<SpeechEnding> ReadToEndAsync(bool answerClose = true)
    {
        var frames = new List<string>();
        try
        {
            while (true)
            {
                var text = await ReceiveTextAsync();
                if (text is null)
                {
                    break;
                }

                frames.Add(text);
            }
        }
        catch (WebSocketException)
        {
            return new SpeechEnding(frames, null, string.Empty);
        }

        if (answerClose)
        {
            try
            {
                using var limit = new CancellationTokenSource(Patience);
                await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, limit.Token);
            }
            catch (WebSocketException)
            {
                // The relay already dropped the connection; the close frame was still received.
            }
        }

        return new SpeechEnding(frames, Socket.CloseStatus, Socket.CloseStatusDescription ?? string.Empty);
    }

    /// <summary>Closes from the phone's side and waits for the relay's answer.</summary>
    public async Task CloseAsync()
    {
        using var limit = new CancellationTokenSource(Patience);
        await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, limit.Token);
    }

    public void Dispose() => Socket.Dispose();

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
