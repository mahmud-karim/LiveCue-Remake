using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Core;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// Protocol P2-145 (test plan 1.7, S-39): a scripted run through every route, a stream, a show and a
/// reset, with hostile requests in between; every event the desktop receives is serialised and searched
/// for the tokens, an <c>Authorization</c> value, the pairing payload, the self-check nonces and the
/// stored hash. The responses of the run are searched as well.
/// </summary>
public sealed class TokenSweepTests
{
    private static readonly JsonSerializerOptions EventJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task No_event_notice_or_diagnostic_of_a_full_run_contains_the_token_or_an_authorization_value()
    {
        var relay = await ConformanceRelay.StartAsync();
        try
        {
            var secrets = new List<string>();
            var bodies = new List<string>();
            var first = relay.Token;
            secrets.Add(first);
            secrets.Add(await ((IPairingExport)relay.Relay).GetPairingPayloadAsync(CancellationToken.None));
            secrets.AddRange(StoredSecrets(relay));

            async Task EveryRouteAsync()
            {
                foreach (var route in Wire.HttpRoutes)
                {
                    bodies.Add((await relay.ExchangeAsync(relay.RequestFor(route))).Body);
                }

                using var phone = await relay.OpenReadyStreamAsync();
                await phone.SendAudioAsync(2560);
                bodies.Add((await phone.ReceiveTextAsync())!);
                await phone.SendEndStreamAsync();
                bodies.AddRange((await phone.ReadToEndAsync()).Frames);
            }

            // Hostile requests, each carrying a token-like text of its own that must not be echoed.
            async Task HostileAsync(string accepted)
            {
                var unknown = new string('u', 43);
                var nearMiss = accepted[..^1] + (accepted[^1] == 'A' ? "B" : "A");
                secrets.Add(unknown);
                secrets.Add(nearMiss);
                (string, string)[][] forms =
                [
                    [("Authorization", TestTokens.Bearer(unknown))],
                    [("Authorization", TestTokens.Bearer(nearMiss))],
                    [("Authorization", "bearer " + accepted)],
                    [("Authorization", "Bearer  " + accepted)],
                    [("Authorization", "Bearer " + accepted + "\x2028")],
                    [("Authorization", TestTokens.Bearer(accepted)), ("Authorization", TestTokens.Bearer(accepted))],
                    [("Authorization", TestTokens.Bearer(accepted)), ("Origin", "https://bad.test")],
                    [("Authorization", TestTokens.Bearer(accepted)), ("Tailscale-Funnel-Request", "?1")],
                    [("Cookie", "token=" + accepted)],
                    [("X-Token", accepted)],
                ];
                foreach (var headers in forms)
                {
                    using var raw = await relay.OpenRawAsync();
                    await raw.SendAsync(System.Text.Encoding.UTF8.GetBytes(Wire.Head(relay.Port, "GET /v1/health HTTP/1.1", headers)));
                    var response = await Wire.ReadRawAsync(raw);
                    Assert.NotNull(response);
                    Assert.StartsWith("HTTP/1.1 401", Wire.StatusLine(response), StringComparison.Ordinal);
                    bodies.Add(response);
                }

                // The token where it does not belong in an authenticated request: the target and the body.
                bodies.Add((await relay.ExchangeAsync(HttpMethod.Get, "/v1/health?token=" + accepted)).Body);
                bodies.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/pair/verify", $$"""{"token":"{{accepted}}"}""")).Body);
                bodies.Add((await relay.ExchangeAsync(HttpMethod.Delete, "/v1/requests/" + accepted)).Body);
            }

            await EveryRouteAsync();
            await HostileAsync(first);

            // The two events a clean run does not raise: a notice about the selection, and a diagnostic.
            bodies.Add((await relay.ExchangeAsync(
                HttpMethod.Post,
                "/v1/assist",
                """{"requestId":"request-0008","assistant":{"model":"gpt-not-offered","reasoningEffort":"low"}}""")).Body);
            relay.Codex.Fault = _ => PrivateText.Failure(relay);
            bodies.Add((await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody())).Body);
            relay.Codex.Fault = null;

            // Show: the self-check gets the token and a nonce; neither may reach an event.
            await relay.Relay.ShowPairingAsync(CancellationToken.None);
            await relay.Events.WaitForAsync<SelfCheckCompleted>();

            // Reset with an answer in flight, then the same run with the new token.
            relay.Codex.ExecScenario = "hang";
            var run = relay.Codex.ExecReportFiles.Count;
            var inFlight = relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-0009"));
            await relay.Codex.ReadExecReportAsync(run);
            await relay.Relay.ResetPairingAsync(CancellationToken.None);
            bodies.Add((await inFlight.WaitAsync(TimeSpan.FromSeconds(30))).Body);
            relay.Codex.ExecScenario = "ok";
            var second = await relay.ReadTokenAsync();
            secrets.Add(second);
            secrets.Add(await ((IPairingExport)relay.Relay).GetPairingPayloadAsync(CancellationToken.None));
            secrets.AddRange(StoredSecrets(relay));
            await relay.Events.WaitForAsync<SelfCheckCompleted>(_ => relay.SelfCheck.Calls.Count == 2);
            await EveryRouteAsync();
            await HostileAsync(second);
            await HostileAsync(first);

            // Pause, resume and stop; the last statistics event comes with the stop.
            await relay.Relay.SetAcceptingAsync(false);
            await relay.Relay.SetAcceptingAsync(true);
            await relay.Relay.StopAsync(CancellationToken.None);
            await relay.Events.Completion.WaitAsync(TimeSpan.FromSeconds(15));

            secrets.AddRange(relay.SelfCheck.Calls.Select(call => call.Nonce));
            Assert.Equal(2, relay.SelfCheck.Calls.Count);
            Assert.Equal([first, second], relay.SelfCheck.Calls.Select(call => call.Token));
            secrets.Add("Bearer");
            secrets.Add("bearer ");
            secrets.Add("Authorization");

            var events = relay.Events.Snapshot();
            Assert.NotEqual(first, second);

            // The run did produce every kind of event the relay has, bar the two of a fault.
            var kinds = events.Select(item => item.GetType()).ToHashSet();
            var all = typeof(RelayEvent).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(RelayEvent)) && type != typeof(RelayFaulted))
                .ToList();
            Assert.All(all, type => Assert.Contains(type, kinds));

            foreach (var relayEvent in events)
            {
                var forms = new[]
                {
                    JsonSerializer.Serialize(relayEvent, relayEvent.GetType(), EventJson),
                    PrivateText.Serialise(relayEvent),
                    relayEvent.ToString(),
                };
                foreach (var text in forms)
                {
                    foreach (var secret in secrets)
                    {
                        Assert.False(
                            text.Contains(secret, StringComparison.Ordinal),
                            $"A {relayEvent.GetType().Name} event carries a secret ({secret[..Math.Min(6, secret.Length)]}…).");
                    }
                }
            }

            // No response echoes a token either: not the accepted one, not the one a caller presented.
            foreach (var body in bodies)
            {
                foreach (var secret in secrets.Where(secret => secret.Length >= 16))
                {
                    Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
                }
            }

            // The three counters saw every refused request once.
            var totals = events.OfType<EdgeStatistics>().Last();
            Assert.Equal(3 * 7, totals.Malformed);
            Assert.Equal(3 * 2, totals.TokenNotAccepted);
            Assert.Equal(3, totals.FunnelInPrivateMode);
        }
        finally
        {
            await relay.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_relay_has_no_log_to_write_a_secret_to()
    {
        // "Nothing reaches a log" is true by construction: the listener has no logging provider, and the
        // relay assembly refers to no console, debug, trace or event-log type and to no logger.
        await using var relay = await ConformanceRelay.StartAsync();
        var app = (WebApplication)typeof(RelayHost)
            .GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(relay.Relay)!;
        Assert.Empty(app.Services.GetServices<ILoggerProvider>());

        var forbidden = new[]
        {
            "System.Console",
            "System.Diagnostics.Debug",
            "System.Diagnostics.Trace",
            "System.Diagnostics.EventLog",
            "System.Diagnostics.Tracing.EventSource",
            "System.Diagnostics.DiagnosticSource",
            "System.Diagnostics.Activity",
            "Microsoft.Extensions.Logging.ILogger",
            "Microsoft.Extensions.Logging.ILoggerFactory",
            "Microsoft.Extensions.Logging.LoggerExtensions",
        };
        Assert.All(forbidden, name => Assert.DoesNotContain(name, RelayAssembly.TypeReferences));
    }

    /// <summary>The two values of the pairing file, which are not the token but have no place in an event.</summary>
    private static IEnumerable<string> StoredSecrets(ConformanceRelay relay)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(relay.DataDirectory, "pairing.json")));
        yield return document.RootElement.GetProperty("tokenHash").GetString()!;
        yield return document.RootElement.GetProperty("protectedToken").GetString()!;
    }
}

/// <summary>What the relay assembly refers to, read from its metadata.</summary>
internal static class RelayAssembly
{
    private static readonly Lazy<(HashSet<string> Types, HashSet<string> Members)> References = new(Read);

    /// <summary>The full name of every type of another assembly that the relay refers to.</summary>
    public static IReadOnlySet<string> TypeReferences => References.Value.Types;

    /// <summary>"Type.Member" for every member of another assembly that the relay refers to.</summary>
    public static IReadOnlySet<string> MemberReferences => References.Value.Members;

    private static (HashSet<string> Types, HashSet<string> Members) Read()
    {
        using var file = File.OpenRead(typeof(RelayFactory).Assembly.Location);
        using var image = new PEReader(file);
        var metadata = image.GetMetadataReader();
        var types = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);

        string NameOf(TypeReferenceHandle handle)
        {
            var reference = metadata.GetTypeReference(handle);
            var space = metadata.GetString(reference.Namespace);
            var name = metadata.GetString(reference.Name);
            return space.Length == 0 ? name : space + "." + name;
        }

        foreach (var handle in metadata.TypeReferences)
        {
            types.Add(NameOf(handle));
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                members.Add(NameOf((TypeReferenceHandle)member.Parent) + "." + metadata.GetString(member.Name));
            }
        }

        return (types, members);
    }
}

/// <summary>
/// Test plan S-43: what the relay leaves on disk. The data folder holds the pairing file, the two schema
/// files, the three launcher-script copies and an empty <c>work\</c>, whatever happened to the requests;
/// and the relay has no code that could write anywhere else.
/// </summary>
public sealed class DiskFootprintTests
{
    private static readonly IReadOnlyList<string> Expected =
    [
        Path.Combine("lab", "LabCommon.ps1"),
        Path.Combine("lab", "Start-ASR-Lab.ps1"),
        Path.Combine("lab", "Stop-ASR-Lab.ps1"),
        "pairing.json",
        Path.Combine("schemas", "assist.schema.json"),
        Path.Combine("schemas", "summary.schema.json"),
    ];

    [Fact]
    public async Task Only_the_expected_files_exist_after_a_full_run()
    {
        using var data = new LiveCue.TestSupport.TempFolder();
        await using (var relay = await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path))
        {
            // A start alone writes all of it.
            Assert.Equal(Expected, relay.DataFiles());
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(data.Path, "work")));

            // A full run: every route, answers that succeed, fail, write too much and are cancelled, a
            // show, a reset and a stream.
            const string Canary = "disk-canary-51c2e9";
            foreach (var scenario in (string[])["ok", "fail", "no-output", "big"])
            {
                relay.Codex.ExecScenario = scenario;
                foreach (var route in Wire.HttpRoutes)
                {
                    using var response = await relay.SendAsync(relay.RequestFor(route));
                }

                using var answer = await relay.PostAsync("/v1/assist", ConformanceRelay.AnswerBody(transcript: Canary));
            }

            relay.Codex.ExecScenario = "hang";
            var run = relay.Codex.ExecReportFiles.Count;
            var hanging = relay.ExchangeAsync(HttpMethod.Post, "/v1/session-summary", ConformanceRelay.AnswerBody("request-0007", Canary));
            var report = await relay.Codex.ReadExecReportAsync(run);
            Assert.Contains(Canary, await File.ReadAllTextAsync(Path.Combine(report.WorkingFolder, "scratch.txt")), StringComparison.Ordinal);
            Assert.Equal(Path.Combine(data.Path, "work"), Path.GetDirectoryName(report.WorkingFolder));
            using (var cancel = await relay.DeleteAsync("/v1/requests/request-0007"))
            {
                Assert.Equal(200, (int)cancel.StatusCode);
            }

            Assert.Equal(409, (await hanging.WaitAsync(TimeSpan.FromSeconds(30))).Status);
            await relay.Relay.ShowPairingAsync(CancellationToken.None);
            await relay.Relay.ResetPairingAsync(CancellationToken.None);
            await relay.ReadTokenAsync();
            using (var phone = await relay.OpenReadyStreamAsync())
            {
                await phone.SendAudioAsync(2560);
                await phone.SendEndStreamAsync();
                await phone.ReadToEndAsync();
            }

            // Exactly the same files; work\ is empty; and no file anywhere holds the transcript or a token.
            Assert.Equal(Expected, relay.DataFiles());
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(data.Path, "work")));
            Assert.Equal(
                ["lab", "pairing.json", "schemas", "work"],
                Directory.GetFileSystemEntries(data.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            foreach (var file in relay.DataFiles())
            {
                var text = await File.ReadAllTextAsync(Path.Combine(data.Path, file));
                Assert.DoesNotContain(Canary, text, StringComparison.Ordinal);
                Assert.DoesNotContain(relay.Token, text, StringComparison.Ordinal);
            }
        }

        // After the stop: still exactly those.
        Assert.Equal(Expected, data.FileNames());
    }

    [Fact]
    public async Task A_start_writes_the_three_launcher_script_copies_and_the_schemas_from_the_embedded_bytes()
    {
        using var data = new LiveCue.TestSupport.TempFolder();

        // What a start finds: a launcher that was changed, a schema that was changed, and a leftover of a
        // run that was killed.
        Directory.CreateDirectory(Path.Combine(data.Path, "lab"));
        Directory.CreateDirectory(Path.Combine(data.Path, "schemas"));
        Directory.CreateDirectory(Path.Combine(data.Path, "work", "0123456789abcdef0123456789abcdef"));
        await File.WriteAllTextAsync(Path.Combine(data.Path, "lab", LabScripts.StartFileName), "Write-Output 'changed'");
        await File.WriteAllTextAsync(Path.Combine(data.Path, "lab", LabScripts.CommonFileName), "Write-Output 'changed'");
        await File.WriteAllTextAsync(Path.Combine(data.Path, "schemas", SchemaFiles.AssistFileName), "{}");
        await File.WriteAllTextAsync(Path.Combine(data.Path, "work", "0123456789abcdef0123456789abcdef", "output.json"), "left behind");

        await using var relay = await ConformanceRelay.StartAsync(options => options.DataDirectory = data.Path);

        Assert.Equal(3, LabScripts.FileNames.Count);
        foreach (var name in LabScripts.FileNames)
        {
            var copy = await File.ReadAllBytesAsync(LabScripts.PathFor(data.Path, name));
            Assert.True(copy.Length > 100, name + " is not a script.");
            Assert.Equal(LabScripts.Embedded(name).ToArray(), copy);
        }

        Assert.Equal(SchemaFiles.Read(SchemaFiles.AssistFileName), await File.ReadAllBytesAsync(SchemaFiles.PathFor(data.Path, AnswerKind.Assist)));
        Assert.Equal(SchemaFiles.Read(SchemaFiles.SummaryFileName), await File.ReadAllBytesAsync(SchemaFiles.PathFor(data.Path, AnswerKind.Summary)));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(data.Path, "work")));

        // Nothing is run for it: a start starts no process and makes no lab call.
        Assert.Empty(relay.Codex.Requests);
        Assert.Equal(0, relay.Lab.StartCalls + relay.Lab.StopCalls + relay.Lab.ProbeCalls);
    }

    [Fact]
    public void The_relay_has_no_code_that_writes_outside_its_data_folder()
    {
        // "Nothing was written under the OS temp folder" cannot be watched from inside a test run that
        // shares the temp folder with every other test. It is shown from the assembly instead: the relay
        // refers to no member that names, creates or buffers into a temporary file.
        var members = RelayAssembly.MemberReferences;
        foreach (var member in (string[])
        [
            "System.IO.Path.GetTempPath",
            "System.IO.Path.GetTempFileName",
            "System.IO.Directory.CreateTempSubdirectory",
            "System.Environment.GetFolderPath",
            "System.Environment.GetEnvironmentVariable",
            "System.Environment.ExpandEnvironmentVariables",
            "Microsoft.AspNetCore.Http.HttpRequestRewindExtensions.EnableBuffering",
        ])
        {
            Assert.DoesNotContain(member, members);
        }
    }
}
