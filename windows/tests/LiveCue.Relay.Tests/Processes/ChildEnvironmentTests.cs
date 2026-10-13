using System.Collections;
using System.Text;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Tests.LocalModel;

namespace LiveCue.Relay.Tests.Processes;

/// <summary>
/// Security test ST-03 (finding SEC-01), relay side: a Codex child gets exactly the allowlist of
/// protocol 12.6. Proved twice: on the function that fills an environment, and through the real runner
/// and a real child process.
///
/// The class runs outside the parallel collections because it changes environment variables of the
/// test process.
/// </summary>
[Collection(ProcessEnvironmentGroup.Name)]
public sealed class ChildEnvironmentTests
{
    /// <summary>Names a parent may well hold and that no child of the relay's Codex may ever see.</summary>
    private static readonly string[] Secrets =
    [
        "LIVECUE_X",
        "LIVECUE_DOTNET_EXE",
        "MY_SECRET_TOKEN",
        "OPENAI_API_KEY",
        "ANTHROPIC_API_KEY",
        "GITHUB_TOKEN",
        "AWS_SECRET_ACCESS_KEY",
    ];

    [Fact]
    public async Task Codex_children_get_exactly_the_allowlist()
    {
        var before = Secrets.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var homeBefore = Environment.GetEnvironmentVariable("CODEX_HOME");
        foreach (var name in Secrets)
        {
            Environment.SetEnvironmentVariable(name, before[name] ?? "synthetic-value-for-a-test");
        }

        Environment.SetEnvironmentVariable("CODEX_HOME", homeBefore ?? @"C:\LiveCue-tests\codex-home");
        try
        {
            var runner = new ProcessRunner(TimeProvider.System);
            var result = await runner.RunAsync(
                TestChild.Request("env-names") with
                {
                    Environment = EnvironmentPolicy.CodexAllowlist,
                    MaxStandardOutputBytes = 1_048_576,
                },
                CancellationToken.None);

            Assert.Equal(ProcessOutcome.Exited, result.Outcome);
            Assert.Equal(0, result.ExitCode);
            var seen = Encoding.UTF8.GetString(result.StandardOutput.Span)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var allowed = ProcessRunnerTests.CodexNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // No name outside the list of protocol 12.6 ...
            Assert.NotEmpty(seen);
            Assert.All(seen, name => Assert.Contains(name, allowed));

            // ... and so none of these, each of which the parent demonstrably has.
            Assert.All(Secrets, name =>
            {
                Assert.NotNull(Environment.GetEnvironmentVariable(name));
                Assert.DoesNotContain(name, seen, StringComparer.OrdinalIgnoreCase);
            });
            Assert.DoesNotContain(seen, name => name.StartsWith("LIVECUE_", StringComparison.OrdinalIgnoreCase));

            // What Codex needs is there: Windows itself, the profile, and its own home.
            Assert.Contains("SystemRoot", seen, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("USERPROFILE", seen, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("CODEX_HOME", seen, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var name in Secrets)
            {
                Environment.SetEnvironmentVariable(name, before[name]);
            }

            Environment.SetEnvironmentVariable("CODEX_HOME", homeBefore);
        }
    }

    [Fact]
    public void The_lists_are_those_of_the_protocol_and_names_are_compared_without_regard_to_case()
    {
        var parent = new Hashtable
        {
            ["SYSTEMROOT"] = @"C:\Windows",
            ["windir"] = @"C:\Windows",
            ["PATH"] = @"C:\Windows\System32",
            ["pathext"] = ".EXE",
            ["Temp"] = @"C:\t",
            ["TMP"] = @"C:\t",
            ["UserProfile"] = @"C:\LiveCue-tests\profile",
            ["APPDATA"] = @"C:\a",
            ["LOCALAPPDATA"] = @"C:\l",
            ["ProgramData"] = @"C:\ProgramData",
            ["ProgramFiles"] = @"C:\Program Files",
            ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
            ["ComSpec"] = @"C:\Windows\System32\cmd.exe",
            ["HOMEDRIVE"] = "C:",
            ["HOMEPATH"] = @"\LiveCue-tests\profile",
            ["codex_home"] = @"C:\codex",
            ["https_proxy"] = "http://proxy.example.test:3128",
            ["HTTP_PROXY"] = "http://proxy.example.test:3128",
            ["ALL_PROXY"] = "http://proxy.example.test:3128",
            ["NO_PROXY"] = "localhost",
            ["LIVECUE_X"] = "1",
            ["MY_SECRET_TOKEN"] = "synthetic",
            ["OPENAI_API_KEY"] = "synthetic",
            ["USERNAME"] = "someone",
            ["PSModulePath"] = "x",
            ["SystemDrive"] = "C:",
            ["PATH_EXTRA"] = "x",
            ["XPath"] = "x",
        };

        var inherit = Filled(EnvironmentPolicy.Inherit, parent);
        var lab = Filled(EnvironmentPolicy.LabScriptAllowlist, parent);
        var codex = Filled(EnvironmentPolicy.CodexAllowlist, parent);

        Assert.Equal(parent.Count, inherit.Count);

        // The parent's own spelling of each name is kept, and so is its value.
        Assert.Equal(
            ["APPDATA", "ComSpec", "LOCALAPPDATA", "PATH", "pathext", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
                "SYSTEMROOT", "Temp", "TMP", "UserProfile", "windir"],
            lab.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(13, ProcessRunnerTests.LabScriptNames.Length);
        Assert.Equal(
            ["ALL_PROXY", "APPDATA", "codex_home", "ComSpec", "HOMEDRIVE", "HOMEPATH", "https_proxy", "HTTP_PROXY",
                "LOCALAPPDATA", "NO_PROXY", "PATH", "pathext", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
                "SYSTEMROOT", "Temp", "TMP", "UserProfile", "windir"],
            codex.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(20, ProcessRunnerTests.CodexNames.Length);
        Assert.Equal(@"C:\codex", codex["codex_home"]);
        Assert.Equal(@"C:\LiveCue-tests\profile", lab["UserProfile"]);
    }

    [Fact]
    public void A_name_the_parent_does_not_have_is_not_invented_and_what_was_there_is_replaced()
    {
        var parent = new Hashtable { ["SystemRoot"] = @"C:\Windows", ["LIVECUE_X"] = "1" };
        var target = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPENAI_API_KEY"] = "synthetic",
            ["SystemRoot"] = "stale",
        };

        ChildEnvironment.Fill(target, EnvironmentPolicy.CodexAllowlist, parent);

        Assert.Equal(["SystemRoot"], target.Keys);
        Assert.Equal(@"C:\Windows", target["SystemRoot"]);
    }

    private static Dictionary<string, string?> Filled(EnvironmentPolicy policy, IDictionary parent)
    {
        var target = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["STALE"] = "x" };
        ChildEnvironment.Fill(target, policy, parent);
        return target;
    }
}
