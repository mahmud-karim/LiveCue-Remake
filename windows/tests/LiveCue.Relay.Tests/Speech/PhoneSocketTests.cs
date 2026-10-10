using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// The bounds a phone cannot push past, at the level of the one class that reads from it and writes to
/// it: one received message at a time, nothing of a message beyond C-20, at most W-03 frames waiting.
/// </summary>
public sealed class PhoneSocketTests
{
    [Fact]
    public async Task The_reader_hands_over_one_message_and_waits_until_it_was_taken()
    {
        await using var pair = await SocketPair.CreateAsync();
        var posted = new ConcurrentQueue<(StreamSignal Signal, int Bytes)>();
        using var phone = new PhoneSocket(pair.Relay, new ManualTimeProvider(), (signal, bytes) => posted.Enqueue((signal, bytes.Length)));
        phone.Start();

        // The phone sends fifty frames as fast as it can. Exactly one reaches the stream.
        for (var sent = 0; sent < 50; sent++)
        {
            await pair.Phone.SendAsync(new byte[2560], WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
        }

        await SpeechWait.UntilAsync(() => !posted.IsEmpty, "the first message was posted");
        await Task.Delay(150);
        Assert.Equal([(StreamSignal.PhoneBinary, 2560)], posted);

        // Each one the stream takes lets exactly one more through.
        phone.MessageTaken();
        await SpeechWait.UntilAsync(() => posted.Count == 2, "the second message was posted");
        await Task.Delay(100);
        Assert.Equal(2, posted.Count);

        // Once the stream stops listening, the rest is read and thrown away; only the close is told.
        phone.StopDelivering();
        await pair.Phone.SendAsync(Encoding.UTF8.GetBytes("ignored"), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await pair.Phone.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await SpeechWait.UntilAsync(() => posted.Count == 3, "the close was posted");
        Assert.Equal((StreamSignal.PhoneClosed, 0), posted.Last());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task A_message_beyond_C20_is_reported_once_and_never_stored(int parts)
    {
        await using var pair = await SocketPair.CreateAsync();
        var posted = new ConcurrentQueue<(StreamSignal Signal, int Bytes)>();
        using var phone = new PhoneSocket(pair.Relay, new ManualTimeProvider(), (signal, bytes) => posted.Enqueue((signal, bytes.Length)));
        phone.Start();

        // Exactly C-20 arrives whole, in one frame or in several.
        await SendInPartsAsync(pair.Phone, ProtocolConstants.MaxPhoneFrameBytes, parts);
        await SpeechWait.UntilAsync(() => posted.Count == 1, "the message at the limit was posted");
        Assert.Equal((StreamSignal.PhoneBinary, ProtocolConstants.MaxPhoneFrameBytes), posted.Last());
        phone.MessageTaken();

        // A megabyte: one report with no bytes, at the first byte beyond C-20, and then silence, although
        // the phone goes on sending.
        await SendInPartsAsync(pair.Phone, 1024 * 1024, parts);
        await SpeechWait.UntilAsync(() => posted.Count == 2, "the oversized message was reported");
        Assert.Equal((StreamSignal.PhoneFrameTooLarge, 0), posted.Last());
        await SendInPartsAsync(pair.Phone, 2560, 1);
        await SendInPartsAsync(pair.Phone, 1024 * 1024, parts);
        await Task.Delay(150);
        Assert.Equal(2, posted.Count);

        // The close frame behind all of it is still seen.
        await pair.Phone.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await SpeechWait.UntilAsync(() => posted.Count == 3, "the close was posted");
        Assert.Equal((StreamSignal.PhoneClosed, 0), posted.Last());
    }

    [Fact]
    public async Task At_most_W03_frames_wait_and_they_are_written_in_order_before_the_close()
    {
        await using var pair = await SocketPair.CreateAsync();
        var clock = new ManualTimeProvider();
        var posted = new ConcurrentQueue<StreamSignal>();
        using var phone = new PhoneSocket(pair.Relay, clock, (signal, _) => posted.Enqueue(signal));

        // The writer has not started: everything waits. W-03 frames are taken, one more is not.
        for (var queued = 0; queued < ProtocolConstants.MaxPhoneQueueFrames - 1; queued++)
        {
            Assert.True(phone.TryQueue(Encoding.UTF8.GetBytes($"frame {queued}")));
        }

        Assert.True(phone.TryQueueClose(WebSocketCloseStatus.InternalServerError, "AUDIO_LIMIT"));
        Assert.False(phone.TryQueue(Encoding.UTF8.GetBytes("one too many")));
        Assert.False(phone.TryQueueClose(WebSocketCloseStatus.NormalClosure, string.Empty));

        // The writer delivers them in order, the close frame last, and nothing after it.
        phone.Start();
        var buffer = new byte[256];
        for (var expected = 0; expected < ProtocolConstants.MaxPhoneQueueFrames - 1; expected++)
        {
            var frame = await pair.Phone.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Text, frame.MessageType);
            Assert.Equal($"frame {expected}", Encoding.UTF8.GetString(buffer, 0, frame.Count));
        }

        var close = await pair.Phone.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, pair.Phone.CloseStatus);
        Assert.Equal("AUDIO_LIMIT", pair.Phone.CloseStatusDescription);

        // The wait for the phone's answer began with the close frame: C-58 later it is reported, once.
        Assert.DoesNotContain(StreamSignal.CloseWaitElapsed, posted);
        clock.Advance(ProtocolConstants.CloseHandshakeWait - TimeSpan.FromMilliseconds(1));
        Assert.DoesNotContain(StreamSignal.CloseWaitElapsed, posted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([StreamSignal.CloseWaitElapsed], posted);
        Assert.False(phone.IsWriteStalled);
    }

    private static async Task SendInPartsAsync(WebSocket socket, int bytes, int parts)
    {
        var part = new byte[bytes / parts];
        for (var sent = 1; sent <= parts; sent++)
        {
            await socket.SendAsync(part, WebSocketMessageType.Binary, endOfMessage: sent == parts, CancellationToken.None);
        }
    }

    /// <summary>Two WebSockets joined by a loopback TCP connection: the relay's end and the phone's.</summary>
    private sealed class SocketPair : IAsyncDisposable
    {
        private readonly TcpClient _relaySide;
        private readonly TcpClient _phoneSide;

        private SocketPair(TcpClient relaySide, TcpClient phoneSide)
        {
            _relaySide = relaySide;
            _phoneSide = phoneSide;
            Relay = WebSocket.CreateFromStream(
                relaySide.GetStream(), new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
            Phone = WebSocket.CreateFromStream(
                phoneSide.GetStream(), new WebSocketCreationOptions { IsServer = false, KeepAliveInterval = TimeSpan.Zero });
        }

        public WebSocket Relay { get; }

        public WebSocket Phone { get; }

        public static async Task<SocketPair> CreateAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var phoneSide = new TcpClient { NoDelay = true };
            await phoneSide.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var relaySide = await listener.AcceptTcpClientAsync();
            listener.Stop();
            return new SocketPair(relaySide, phoneSide);
        }

        public ValueTask DisposeAsync()
        {
            Relay.Abort();
            Phone.Abort();
            Relay.Dispose();
            Phone.Dispose();
            _relaySide.Dispose();
            _phoneSide.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
