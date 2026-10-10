using LiveCue.Relay.Core;

namespace LiveCue.Relay.Tests.Host;

/// <summary>The pause flag and the stopping token as every area observes them (W-design 4.2).</summary>
public sealed class AccessStateTests
{
    [Fact]
    public void Pausing_cancels_the_token_and_resuming_issues_a_fresh_one()
    {
        using var access = new AccessState();
        var first = access.WhileAccepting;
        var cancelledWhen = (bool?)null;
        using var registration = first.Register(() => cancelledWhen = access.Accepting);

        Assert.True(access.Accepting);
        Assert.False(first.IsCancellationRequested);

        // The flag flips before the token is cancelled: whoever is woken by the token sees "paused".
        Assert.True(access.SetAccepting(false));
        Assert.False(access.Accepting);
        Assert.True(first.IsCancellationRequested);
        Assert.False(cancelledWhen);

        // While paused the token is already cancelled; the same value again changes nothing.
        Assert.True(access.WhileAccepting.IsCancellationRequested);
        Assert.False(access.SetAccepting(false));

        Assert.True(access.SetAccepting(true));
        Assert.True(access.Accepting);
        Assert.False(access.WhileAccepting.IsCancellationRequested);
        Assert.NotEqual(first, access.WhileAccepting);
        Assert.False(access.SetAccepting(true));

        // A pause never touches the stopping token.
        Assert.False(access.Stopping.IsCancellationRequested);
    }

    [Fact]
    public void Stopping_is_cancelled_once_and_never_reset()
    {
        using var access = new AccessState();
        var stopping = access.Stopping;
        var calls = 0;
        using var registration = stopping.Register(() => calls++);

        access.BeginStopping();
        access.BeginStopping();
        access.SetAccepting(false);
        access.SetAccepting(true);

        Assert.True(stopping.IsCancellationRequested);
        Assert.True(access.Stopping.IsCancellationRequested);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void After_the_end_both_tokens_are_cancelled_and_everything_can_still_be_read()
    {
        var access = new AccessState();
        var whileAccepting = access.WhileAccepting;

        access.Dispose();
        access.Dispose();

        // A late reader gets a cancelled token, never an exception.
        Assert.False(access.Accepting);
        Assert.True(whileAccepting.IsCancellationRequested);
        Assert.True(access.WhileAccepting.IsCancellationRequested);
        Assert.True(access.Stopping.IsCancellationRequested);
        Assert.False(access.SetAccepting(true));
        Assert.False(access.Accepting);
        access.BeginStopping();
    }
}
