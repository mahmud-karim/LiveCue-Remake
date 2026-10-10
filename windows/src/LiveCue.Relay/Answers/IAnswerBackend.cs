namespace LiveCue.Relay.Answers;

/// <param name="RequestBodyUtf8">The request body as compact JSON; embedded whole in the prompt.</param>
internal sealed record AnswerRun(
    AnswerKind Kind,
    string RequestId,
    string Model,
    string ReasoningEffort,
    ReadOnlyMemory<byte> RequestBodyUtf8);

/// <summary>Answer seam: one run of the answer process.</summary>
internal interface IAnswerBackend
{
    /// <summary>
    /// Returns the bytes of the output file, unvalidated, or null when the run produced no usable file
    /// (could not start, non-zero exit, no file, larger than C-24). When <paramref name="ct"/> fires, the
    /// process tree is killed and OperationCanceledException is thrown. The working folder is gone when the
    /// task completes, whatever the outcome.
    /// </summary>
    Task<byte[]?> RunAsync(AnswerRun run, CancellationToken ct);
}
