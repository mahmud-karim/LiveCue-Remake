using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Text;
using LiveCue.Relay;

namespace LiveCue.Desktop.Live;

/// <summary>
/// The transcript card's text and its bounds (W-design 5.5, architecture 3.12; screens 2.7).
///
/// A request replaces the text: the transcript, and under it, in a quieter colour, the interim text
/// and the instruction when the request has them. Live speech text is appended line by line; above
/// <see cref="UpperBound"/> characters the text is cut to its newest <see cref="LowerBound"/>.
/// </summary>
internal sealed class TranscriptModel
{
    /// <summary>Above this many characters the text is cut.</summary>
    public const int UpperBound = 64_000;

    /// <summary>What a cut leaves at most: the newest part.</summary>
    public const int LowerBound = 48_000;

    /// <summary>The transcript, then the appended speech lines. Empty when there is nothing yet.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>The "[In progress] …" and "Instruction: …" blocks, a blank line between them; empty when none.</summary>
    public string Secondary { get; private set; } = string.Empty;

    /// <summary>True while the card has nothing to show but its placeholder.</summary>
    public bool IsEmpty => Text.Length == 0 && Secondary.Length == 0;

    public void Clear()
    {
        Text = string.Empty;
        Secondary = string.Empty;
    }

    /// <summary>A request arrived: the card shows what the phone sent.</summary>
    public void Show(RequestStarted request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var blocks = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(request.PartialTranscript))
        {
            blocks.Add(LiveText.InProgressPrefix + request.PartialTranscript);
        }

        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            blocks.Add(LiveText.InstructionPrefix + request.Instruction);
        }

        Secondary = string.Join(Environment.NewLine + Environment.NewLine, blocks);
        Text = string.IsNullOrWhiteSpace(request.Transcript) ? string.Empty : Cut(request.Transcript);

        // A request without any of the three still shows what arrived.
        if (IsEmpty)
        {
            Text = Cut(PayloadFormatter.Indent(request.PayloadJson));
        }
    }

    /// <summary>Live speech text: a new line at the end.</summary>
    public void Append(string? speech)
    {
        if (string.IsNullOrEmpty(speech))
        {
            return;
        }

        Text = Cut(Text.Length == 0 ? speech : Text + Environment.NewLine + speech);
    }

    /// <summary>
    /// The bound: a text longer than <see cref="UpperBound"/> keeps its newest <see cref="LowerBound"/>
    /// characters, starting at a character boundary. A shorter text is returned as it is.
    /// </summary>
    public static string Cut(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length > UpperBound ? ActivityLog.Newest(text, LowerBound) : text;
    }
}
