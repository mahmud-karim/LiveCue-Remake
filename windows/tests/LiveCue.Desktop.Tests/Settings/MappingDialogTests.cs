using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Settings;

/// <summary>
/// The mapping dialog over a <see cref="FakeSession"/> (test plan 2.8; screens 6.2; W-design 5.9; rule
/// TS-9): it shows the plan's own texts, runs the plan only when confirmed, and is the only code of the
/// desktop that can. The mapping manager behind the session is a fake: no command runs here.
/// </summary>
public sealed class MappingDialogTests
{
    private static readonly OwnedMapping PrivateEntry =
        new(ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841", DateTimeOffset.UnixEpoch);

    private static readonly OwnedMapping PublicEntry =
        new(ConnectionMode.Public, 10000, "/livecue", "http://127.0.0.1:47841", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Outcome_cases()
    {
        var session = new FakeSession();
        var setUp = session.PlanSetUp(out _)!;
        var dialog = new MappingDialogViewModel(session, setUp);
        var closed = 0;
        dialog.Closed += (_, _) => closed++;

        // The plan's texts, verbatim, before anything runs.
        Assert.Equal(MappingAction.ApplyPrivate, dialog.Action);
        Assert.Equal("Set up the Tailscale mapping?", dialog.Title);
        Assert.Equal("LiveCue will run:", dialog.Intro);
        Assert.Equal(setUp.Command.DisplayText, dialog.CommandText);
        Assert.Equal("tailscale serve --bg --https=11443 http://127.0.0.1:47841", dialog.CommandText);
        Assert.Equal(setUp.EffectText, dialog.EffectText);
        Assert.Equal("Set up", dialog.ConfirmLabel);
        Assert.False(dialog.IsDestructive);
        Assert.Null(dialog.Failure);
        Assert.False(dialog.IsBusy);
        Assert.Empty(session.ExecutedPlans);

        // Every outcome but Done stays open with the mapping manager's own text for it.
        (MappingOutcome Outcome, MappingRefusal Refusal, string Text)[] failures =
        [
            (MappingOutcome.CommandFailed, MappingRefusal.None, "Tailscale HTTPS setup failed. Check that Tailscale is connected."),
            (MappingOutcome.CommandTimedOut, MappingRefusal.None,
                "Tailscale did not finish in time. Run the command shown above in a terminal to see Tailscale's own instructions. The first use of HTTPS or Funnel on a tailnet must be approved in the Tailscale admin console."),
            (MappingOutcome.NotVerified, MappingRefusal.None,
                "Tailscale reported success, but the mapping is not in its configuration. Choose Check again."),
            (MappingOutcome.LedgerNotSaved, MappingRefusal.None,
                "The mapping changed, but LiveCue could not save its settings. LiveCue will treat the mapping as not its own."),
            (MappingOutcome.Refused, MappingRefusal.TailscaleNotInstalled, "Tailscale is not installed in Program Files."),
            (MappingOutcome.Refused, MappingRefusal.StatusUnavailable,
                "LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again."),
            (MappingOutcome.Refused, MappingRefusal.StatusUnreadable,
                "LiveCue could not read Tailscale's configuration, so it changed nothing. Check that Tailscale is running, then choose Check again."),
            (MappingOutcome.Refused, MappingRefusal.PlanOutOfDate,
                "The settings changed while this dialog was open. Close it and look at the command again."),
            (MappingOutcome.Refused, MappingRefusal.AlreadyPresent, "The connection is already set up."),
            (MappingOutcome.Refused, MappingRefusal.PrivatePortOccupied,
                "Port 11443 already has other Tailscale handlers. Choose another private HTTPS port."),
            (MappingOutcome.Refused, MappingRefusal.PrivatePortHasFunnel,
                "Port 11443 has Funnel turned on. A private listener there would turn Funnel off for the whole port. Choose another private HTTPS port."),
            (MappingOutcome.Refused, MappingRefusal.ForbiddenRelayPort,
                "Port 47831 belongs to the old LiveCue and is never used. Choose another relay port."),
        ];
        var runs = 0;
        foreach (var (outcome, refusal, text) in failures)
        {
            var result = new MappingResult(outcome, refusal, OthersChanged: false, session.Settings, After: null);
            session.MappingResult = result;
            dialog.ConfirmCommand.Execute(null);
            await Task.Yield();

            Assert.Equal(0, closed);
            Assert.Equal(new BannerModel(BannerTone.Danger, null, text), dialog.Failure);
            Assert.Equal(MappingTexts.Failure(result, setUp), dialog.Failure!.Body);
            Assert.False(dialog.IsBusy);
            Assert.Equal(++runs, session.ExecutedPlans.Count);
            Assert.Same(setUp, session.ExecutedPlans[^1]);

            // The command is still the one that was shown.
            Assert.Equal(setUp.Command.DisplayText, dialog.CommandText);
        }

        // Rule TS-7: other entries changed during the command. It is said here, after the outcome.
        session.MappingResult = new MappingResult(MappingOutcome.NotVerified, MappingRefusal.None, OthersChanged: true, session.Settings, null);
        dialog.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(
            "Tailscale reported success, but the mapping is not in its configuration. Choose Check again."
            + Environment.NewLine
            + "Warning: other entries of Tailscale's serve configuration changed during this command. LiveCue repaired nothing. Review them with: tailscale serve status",
            dialog.Failure?.Body);
        Assert.Equal(0, closed);

        // While the command runs the dialog is busy: no second run, no cancel. The old failure is gone.
        session.MappingResult = null;
        session.Gate = new TaskCompletionSource();
        var executed = session.ExecutedPlans.Count;
        dialog.ConfirmCommand.Execute(null);
        Assert.True(dialog.IsBusy);
        Assert.Null(dialog.Failure);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));
        Assert.False(dialog.CancelCommand.CanExecute(null));
        dialog.ConfirmCommand.Execute(null);
        Assert.Equal(executed + 1, session.ExecutedPlans.Count);

        // Done closes.
        session.Gate.SetResult();
        session.Gate = null;
        await Task.Yield();
        Assert.Equal(1, closed);
        Assert.False(dialog.IsBusy);
        Assert.Null(dialog.Failure);

        // Cancel closes and runs nothing.
        var cancelled = new MappingDialogViewModel(session, setUp);
        var cancels = 0;
        cancelled.Closed += (_, _) => cancels++;
        cancelled.CancelCommand.Execute(null);
        Assert.Equal(1, cancels);
        Assert.Equal(executed + 1, session.ExecutedPlans.Count);
    }

    [Fact]
    public async Task A_removal_shows_its_own_texts()
    {
        var session = new FakeSession();

        var removePrivate = session.PlanRemoval(PrivateEntry);
        var dialog = new MappingDialogViewModel(session, removePrivate);
        Assert.Equal(MappingAction.RemovePrivate, dialog.Action);
        Assert.Equal("Remove the Tailscale mapping?", dialog.Title);
        Assert.Equal("LiveCue will run:", dialog.Intro);
        Assert.Equal("tailscale serve --https=11443 --set-path=/ off", dialog.CommandText);
        Assert.Equal(removePrivate.EffectText, dialog.EffectText);
        Assert.Equal("Remove", dialog.ConfirmLabel);
        Assert.True(dialog.IsDestructive);

        (MappingOutcome Outcome, MappingRefusal Refusal, string Text)[] failures =
        [
            (MappingOutcome.CommandFailed, MappingRefusal.None, "Tailscale could not remove the mapping. Check that Tailscale is connected."),
            (MappingOutcome.NotVerified, MappingRefusal.None,
                "Tailscale reported success, but LiveCue could not confirm that the mapping is gone. Choose Check again."),
            (MappingOutcome.Refused, MappingRefusal.NotPresent, "That mapping no longer exists in Tailscale. LiveCue removed it from its own list."),
            (MappingOutcome.Refused, MappingRefusal.NotInLedger, "This mapping was not created by LiveCue, so LiveCue will not remove it."),
            (MappingOutcome.Refused, MappingRefusal.LedgerMismatch,
                "The mapping no longer matches what LiveCue created, so LiveCue will not remove it."),
        ];
        var closed = 0;
        dialog.Closed += (_, _) => closed++;
        foreach (var (outcome, refusal, text) in failures)
        {
            session.MappingResult = new MappingResult(outcome, refusal, false, session.Settings, null);
            dialog.ConfirmCommand.Execute(null);
            await Task.Yield();
            Assert.Equal(new BannerModel(BannerTone.Danger, null, text), dialog.Failure);
            Assert.Equal(0, closed);
        }

        session.MappingResult = null;
        dialog.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(1, closed);
        Assert.All(session.ExecutedPlans, plan => Assert.Same(removePrivate, plan));

        // The public forms.
        var removePublic = new MappingDialogViewModel(session, session.PlanRemoval(PublicEntry));
        Assert.Equal(MappingAction.RemovePublic, removePublic.Action);
        Assert.Equal("tailscale funnel --https=10000 --set-path=/livecue off", removePublic.CommandText);
        Assert.True(removePublic.IsDestructive);

        session.Settings = RelaySettings.Default with { ConnectionMode = ConnectionMode.Public };
        var applyPublic = new MappingDialogViewModel(session, session.PlanSetUp(out _)!);
        Assert.Equal(MappingAction.ApplyPublic, applyPublic.Action);
        Assert.Equal("Set up the Tailscale mapping?", applyPublic.Title);
        Assert.Equal("tailscale funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:47841", applyPublic.CommandText);
        Assert.Equal("Set up", applyPublic.ConfirmLabel);
        Assert.False(applyPublic.IsDestructive);
        session.MappingResult = new MappingResult(MappingOutcome.Refused, MappingRefusal.PublicMountOccupied, false, session.Settings, null);
        applyPublic.ConfirmCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("The path /livecue on port 10000 is already used by something else. Choose another path.", applyPublic.Failure?.Body);
    }

    [Fact]
    public void Only_the_dialog_can_execute()
    {
        // Rule TS-9, checked on the compiled desktop assembly itself: every place in its code that
        // refers to a method named ExecuteMappingAsync (a call, a delegate, a method group), whoever
        // declares that method.
        var references = MethodReferences(typeof(MappingDialogViewModel).Assembly, nameof(IDesktopSession.ExecuteMappingAsync));

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.Equal(typeof(MappingDialogViewModel).FullName, reference.Caller));
        Assert.Contains(references, reference => reference.Target == typeof(IDesktopSession).FullName);

        // And the mapping manager's own ExecuteAsync is reached from one place: the session, which
        // only the dialog can ask.
        var managerCalls = MethodReferences(typeof(MappingDialogViewModel).Assembly, nameof(IMappingManager.ExecuteAsync))
            .Where(reference => reference.Target == typeof(IMappingManager).FullName)
            .Select(reference => reference.Caller)
            .Distinct()
            .ToList();
        Assert.Equal(["LiveCue.Desktop.Session.DesktopSession"], managerCalls);

        // The scan sees what it should: a method that is known to be called from several types.
        var known = MethodReferences(typeof(MappingDialogViewModel).Assembly, nameof(IDesktopSession.CheckAgainAsync))
            .Select(reference => reference.Caller)
            .Distinct()
            .ToList();
        Assert.Contains(typeof(SettingsViewModel).FullName, known);
        Assert.Contains("LiveCue.Desktop.Live.LiveViewModel", known);
    }

    /// <summary>
    /// Every reference from the code of <paramref name="assembly"/> to a method named
    /// <paramref name="methodName"/>: the outermost type that holds the referring code (so the code of
    /// a lambda or of an async method counts for the type it was written in) and the type the method
    /// is referred to on.
    /// </summary>
    private static List<(string Caller, string Target)> MethodReferences(Assembly assembly, string methodName)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var found = new List<(string Caller, string Target)>();

        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            var caller = FullName(metadata, Outermost(metadata, typeHandle));
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                var code = image.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (code.RemainingBytes > 0)
                {
                    var first = code.ReadByte();
                    var opCode = first == 0xFE ? TwoByteOpCodes[code.ReadByte()] : OneByteOpCodes[first];
                    switch (opCode.OperandType)
                    {
                        case OperandType.InlineMethod or OperandType.InlineTok:
                            var token = MetadataTokens.EntityHandle(code.ReadInt32());
                            if (TargetOf(metadata, token) is { } target && target.Name == methodName)
                            {
                                found.Add((caller, target.Type));
                            }

                            break;

                        case OperandType.InlineSwitch:
                            var targets = code.ReadInt32();
                            code.Offset += 4 * targets;
                            break;

                        case OperandType.InlineNone:
                            break;

                        case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                            code.Offset += 1;
                            break;

                        case OperandType.InlineVar:
                            code.Offset += 2;
                            break;

                        case OperandType.InlineI8 or OperandType.InlineR:
                            code.Offset += 8;
                            break;

                        default:
                            code.Offset += 4;
                            break;
                    }
                }
            }
        }

        return found;
    }

    private static readonly OpCode[] OneByteOpCodes = OpCodeTable(twoBytes: false);

    private static readonly OpCode[] TwoByteOpCodes = OpCodeTable(twoBytes: true);

    private static OpCode[] OpCodeTable(bool twoBytes)
    {
        var table = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode && (opCode.Size == 2) == twoBytes)
            {
                table[opCode.Value & 0xFF] = opCode;
            }
        }

        return table;
    }

    /// <summary>The method a token names and the type it is named on; null for a token that names no method.</summary>
    private static (string Type, string Name)? TargetOf(MetadataReader metadata, EntityHandle token)
    {
        switch (token.Kind)
        {
            case HandleKind.MethodDefinition:
                var definition = metadata.GetMethodDefinition((MethodDefinitionHandle)token);
                return (FullName(metadata, definition.GetDeclaringType()), metadata.GetString(definition.Name));

            case HandleKind.MemberReference:
                var reference = metadata.GetMemberReference((MemberReferenceHandle)token);
                return (ParentName(metadata, reference.Parent), metadata.GetString(reference.Name));

            case HandleKind.MethodSpecification:
                return TargetOf(metadata, metadata.GetMethodSpecification((MethodSpecificationHandle)token).Method);

            default:
                return null;
        }
    }

    private static string ParentName(MetadataReader metadata, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeDefinition => FullName(metadata, (TypeDefinitionHandle)parent),
        HandleKind.TypeReference => FullName(metadata, metadata.GetTypeReference((TypeReferenceHandle)parent)),
        _ => string.Empty,
    };

    private static TypeDefinitionHandle Outermost(MetadataReader metadata, TypeDefinitionHandle type)
    {
        while (true)
        {
            var declaring = metadata.GetTypeDefinition(type).GetDeclaringType();
            if (declaring.IsNil)
            {
                return type;
            }

            type = declaring;
        }
    }

    private static string FullName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return FullName(metadata, declaring) + "+" + name;
        }

        var @namespace = metadata.GetString(type.Namespace);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }

    private static string FullName(MetadataReader metadata, TypeReference type)
    {
        var @namespace = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }
}
