using System.Diagnostics;
using LiveCue.Relay.Processes;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Processes;

/// <summary>
/// Architecture 3.7: a process in the kill-on-close job ends when the job's last handle closes, which
/// is what happens when the application ends for any reason. Here the handle is closed by hand.
/// </summary>
public sealed class JobObjectTests
{
    [Fact]
    public async Task A_child_dies_when_the_job_handle_closes()
    {
        using var folder = new TempFolder();
        using var child = StartChild("spawn", folder.Combine("ids.txt"), "600000");
        var job = JobObject.TryCreate();
        try
        {
            Assert.NotNull(job);
            Assert.True(job.TryAssign(child));

            // The grandchild is started after the assignment or before it; either way it belongs to the
            // child's tree, and a process started inside a job is in that job.
            var (childId, grandchildId) = await TestChild.WaitForSpawnAsync(folder.Combine("ids.txt"));
            Assert.Equal(child.Id, childId);
            Assert.False(child.HasExited);

            job.Dispose();

            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await TestChild.WaitUntilGoneAsync(childId, grandchildId);
        }
        finally
        {
            job?.Dispose();
            TestChild.Kill(child.Id);
        }
    }

    [Fact]
    public async Task A_process_that_has_ended_cannot_be_assigned_and_two_jobs_are_independent()
    {
        using var ended = StartChild("exit", "0");
        await ended.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var first = JobObject.TryCreate();
        using var second = JobObject.TryCreate();
        using var kept = StartChild("sleep", "600000");
        using var dropped = StartChild("sleep", "600000");
        try
        {
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.False(first.TryAssign(ended));
            Assert.True(first.TryAssign(kept));
            Assert.True(second.TryAssign(dropped));

            second.Dispose();
            await dropped.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            // Closing one job ends nothing of the other.
            await Task.Delay(200);
            Assert.False(kept.HasExited);
            Assert.False(second.TryAssign(kept));
        }
        finally
        {
            TestChild.Kill(kept.Id);
            TestChild.Kill(dropped.Id);
        }
    }

    private static Process StartChild(params string[] modeAndArguments)
    {
        var start = new ProcessStartInfo(DotnetHost.Path) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(TestChild.Dll);
        foreach (var argument in modeAndArguments)
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("The test child did not start.");
    }
}
