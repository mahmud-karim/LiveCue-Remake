using LiveCue.Relay.Core;

namespace LiveCue.Relay.Answers;

/// <summary>
/// One run of <c>codex exec</c> (protocol 12.2).
///
/// First version (foundation): starts nothing and reports "no usable file". Work package WP-W03 builds
/// the run.
/// </summary>
internal sealed class CodexAnswerBackend : IAnswerBackend
{
    public CodexAnswerBackend(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public Task<byte[]?> RunAsync(AnswerRun run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<byte[]?>(null);
    }
}
