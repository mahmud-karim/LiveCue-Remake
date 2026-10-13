namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The four write commands of architecture 3.9 and nothing else (rules TS-2, TS-3, TS-6): the only way
/// to make a command is one of four factories, and each validates every value.
/// </summary>
public sealed class MappingCommandTests
{
    [Fact]
    public void The_four_forms_have_the_exact_arguments()
    {
        var applyPrivate = MappingCommand.ApplyPrivate(11443, 47841);
        var applyPublic = MappingCommand.ApplyPublic(10000, "/livecue", 47841);
        var removePrivate = MappingCommand.RemovePrivate(11443);
        var removePublic = MappingCommand.RemovePublic(10000, "/livecue");

        Assert.Equal(["serve", "--bg", "--https=11443", "http://127.0.0.1:47841"], applyPrivate.Arguments);
        Assert.Equal(
            ["funnel", "--bg", "--https=10000", "--set-path=/livecue", "http://127.0.0.1:47841"],
            applyPublic.Arguments);
        Assert.Equal(["serve", "--https=11443", "--set-path=/", "off"], removePrivate.Arguments);
        Assert.Equal(["funnel", "--https=10000", "--set-path=/livecue", "off"], removePublic.Arguments);

        Assert.Equal("tailscale serve --bg --https=11443 http://127.0.0.1:47841", applyPrivate.DisplayText);
        Assert.Equal(
            "tailscale funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:47841",
            applyPublic.DisplayText);
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", removePrivate.DisplayText);
        Assert.Equal("tailscale funnel --https=10000 --set-path=/livecue off", removePublic.DisplayText);

        Assert.Equal((MappingCommandKind.ApplyPrivate, 11443, "/", 47841), Shape(applyPrivate));
        Assert.Equal((MappingCommandKind.ApplyPublic, 10000, "/livecue", 47841), Shape(applyPublic));
        Assert.Equal((MappingCommandKind.RemovePrivate, 11443, "/", 0), Shape(removePrivate));
        Assert.Equal((MappingCommandKind.RemovePublic, 10000, "/livecue", 0), Shape(removePublic));
        Assert.Equal("http://127.0.0.1:47841", MappingCommand.TargetFor(47841));
    }

    [Fact]
    public void Factories_reject_bad_ports_prefixes_and_47831()
    {
        // The old relay's port is never a target (TS-6).
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPrivate(11443, 47831));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPublic(10000, "/livecue", 47831));

        // Ports out of range.
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPrivate(0, 47841));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPrivate(65536, 47841));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPrivate(11443, 1023));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPrivate(11443, 65536));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.RemovePrivate(0));

        // Only the ports Funnel can use.
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPublic(11443, "/livecue", 47841));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.RemovePublic(444, "/livecue"));

        // Only a prefix of the settings rule: no root, no upper case, no argument-like text.
        foreach (var prefix in (string?[])["/", "livecue", "/LiveCue", "/-x", "/a b", "/a/b", "/" + new string('a', 33), "/x --yes", string.Empty, null])
        {
            Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPublic(10000, prefix!, 47841));
            Assert.ThrowsAny<ArgumentException>(() => MappingCommand.RemovePublic(10000, prefix!));
        }
    }

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(10000)]
    public void Apply_private_refuses_a_funnel_port(int port)
    {
        // A tailnet-only listener is never created on a port on which Funnel can be switched on.
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPrivate(port, 47841));

        // Removing is always allowed.
        Assert.Equal(MappingCommandKind.RemovePrivate, MappingCommand.RemovePrivate(port).Kind);
    }

    [Theory]
    [InlineData("/livecue\n")]
    [InlineData("/livecue\r\n")]
    [InlineData("\n/livecue")]
    public void Factories_throw_for_a_prefix_that_ends_in_a_line_feed(string prefix)
    {
        // The prefix is a whole-text match: "$" would also match before a final line feed.
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.ApplyPublic(10000, prefix, 47841));
        Assert.ThrowsAny<ArgumentException>(() => MappingCommand.RemovePublic(10000, prefix));
    }

    [Theory]
    [InlineData(443)]
    [InlineData(8443)]
    [InlineData(10000)]
    public void No_command_form_holds_a_forbidden_word(int publicPort)
    {
        MappingCommand[] commands =
        [
            MappingCommand.ApplyPrivate(11443, 47841),
            MappingCommand.ApplyPublic(publicPort, "/livecue", 47841),
            MappingCommand.RemovePrivate(11443),
            MappingCommand.RemovePublic(publicPort, "/livecue"),
        ];

        Assert.All(commands, command =>
        {
            Assert.DoesNotContain("reset", command.Arguments);
            Assert.DoesNotContain("--yes", command.Arguments);
            Assert.DoesNotContain(command.Arguments, argument => argument.StartsWith("--tcp", StringComparison.Ordinal));
            Assert.DoesNotContain(command.Arguments, argument => argument.StartsWith("--tls-terminated-tcp", StringComparison.Ordinal));
            Assert.Contains(command.Arguments, argument => argument.StartsWith("--https=", StringComparison.Ordinal));
            Assert.True(
                !command.Arguments.Contains("off")
                || command.Arguments.Any(argument => argument.StartsWith("--set-path=", StringComparison.Ordinal)),
                "A remove form always names its path.");
        });
    }

    private static (MappingCommandKind Kind, int HttpsPort, string Path, int RelayPort) Shape(MappingCommand command) =>
        (command.Kind, command.HttpsPort, command.Path, command.RelayPort);
}
