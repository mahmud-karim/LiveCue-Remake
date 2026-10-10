using LiveCue.Relay.Processes;

namespace LiveCue.Hosting;

/// <summary>The hosting library, composed. Both executables build exactly one.</summary>
public sealed record HostingServices(
    ISettingsStore Settings,
    ISettingsValidator Validator,
    IPrerequisiteChecker Prerequisites,
    ICodexLocator Codex,
    ITailscaleCli Tailscale,
    IMappingManager Mappings,
    IEndpointResolver Endpoints,
    IRelaySupervisor Supervisor);

/// <summary>Replacements for tests and for the desktop's self-test. Null means production.</summary>
public sealed record HostingOverrides
{
    public TimeProvider? Clock { get; init; }

    public IProcessRunner? Processes { get; init; }

    public IHostFileSystem? Files { get; init; }

    public ISettingsStore? Settings { get; init; }

    public IPrerequisiteChecker? Prerequisites { get; init; }

    public ITailscaleCli? Tailscale { get; init; }

    public IMappingManager? Mappings { get; init; }

    public IRelayFactory? RelayFactory { get; init; }
}

public static class HostingFactory
{
    /// <summary>Does no I/O beyond resolving the Tailscale executable path lazily on first use.</summary>
    public static HostingServices Create(SupervisorOptions options, HostingOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        overrides ??= new HostingOverrides();

        var clock = overrides.Clock ?? TimeProvider.System;
        var processes = overrides.Processes ?? new ProcessRunner(clock);
        var files = overrides.Files ?? new HostFileSystem();
        var validator = new SettingsValidator(files);
        var settings = overrides.Settings ?? new SettingsStore(options.DataDirectory, files, validator);
        var codex = new CodexLocator(files);
        var signIn = new CodexSignInCheck(processes);
        var tailscale = overrides.Tailscale ?? new Tailscale.TailscaleCli(processes, files);
        var prerequisites = overrides.Prerequisites
            ?? new PrerequisiteChecker(settings, codex, signIn, tailscale, files);
        var endpoints = new EndpointResolver();
        var mappings = overrides.Mappings ?? new Tailscale.MappingManager(tailscale, settings, validator, clock);
        var relays = overrides.RelayFactory ?? new ProductionRelayFactory(clock, processes);
        var supervisor = new RelaySupervisor(options, settings, prerequisites, mappings, endpoints, relays, files, clock);

        return new HostingServices(settings, validator, prerequisites, codex, tailscale, mappings, endpoints, supervisor);
    }
}
