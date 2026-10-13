namespace LiveCue.Relay.Answers;

/// <summary>
/// The argument list of <c>codex exec</c> (protocol 12.2), one list element per argument. Nothing here
/// depends on the request except the model and the reasoning effort, and both are the catalogue's own
/// text (protocol 7.3).
///
/// <see cref="Containment"/> is the fixed list of protocol 12.6, read from the help output and the
/// feature list of codex-cli 0.160.1. It is recorded, with what each switch turns off and with what
/// that version has no switch for, in docs/architecture/05-windows-architecture.md section 4.9. The two
/// are changed together, and the containment check of the test plan (L-4) is run again whenever the
/// installed Codex changes.
/// </summary>
internal static class CodexArguments
{
    /// <summary>The file Codex writes its last message to, inside the run's folder.</summary>
    public const string OutputFileName = "output.json";

    private const string Disable = "--disable";
    private const string Config = "-c";

    /// <summary>
    /// The containment arguments, in order. <c>--disable</c> is used for features because Codex refuses
    /// a name it does not know (the same switch written as <c>-c features.name=false</c> is accepted
    /// silently): a feature that a later Codex renames fails the run instead of coming back unnoticed.
    /// </summary>
    public static IReadOnlyList<string> Containment { get; } =
    [
        // The second command tool and what belongs to it. (0.160.1 accepts the first switch and keeps the
        // feature on; see the record in the design.)
        Disable, "unified_exec",
        Disable, "unified_exec_tty",
        Disable, "shell_snapshot",

        // Files and pictures.
        Disable, "view_image",
        Disable, "image_generation",

        // Tool servers and everything that brings tools or instructions from elsewhere.
        Disable, "apps",
        Disable, "plugins",
        Disable, "plugin_sharing",
        Disable, "remote_plugin",
        Disable, "recommended_plugins",
        Disable, "hooks",
        Disable, "skill_search",
        Disable, "skill_mcp_dependency_install",
        Disable, "tool_suggest",
        Disable, "tool_call_mcp_elicitation",
        Disable, "memories",

        // Other agents and other tools.
        Disable, "multi_agent",
        Disable, "multi_agent_v2",
        Disable, "goals",
        Disable, "sleep_tool",

        // The browser, the desktop and the desktop application's own features.
        Disable, "browser_use",
        Disable, "browser_use_external",
        Disable, "browser_use_full_cdp_access",
        Disable, "computer_use",
        Disable, "in_app_browser",
        Disable, "in_app_chat",
        Disable, "in_app_dictation",
        Disable, "in_app_local_automation",
        Disable, "in_app_updates",
        Disable, "realtime_conversation",

        // Other processes and other folders.
        Disable, "code_mode_host",
        Disable, "workspace_dependencies",
        Disable, "worktrees",
        Disable, "daemon_auto_start",

        // Settings that have no feature name.
        Config, "web_search=\"disabled\"",
        Config, "project_doc_max_bytes=0",
        Config, "apps._default.enabled=false",
        Config, "skills.bundled.enabled=false",
        Config, "allow_login_shell=false",
        Config, "shell_environment_policy.inherit=\"none\"",

        // The owner's own choices are in the configuration file that is not loaded, so the careful value
        // of each is passed: no usage data, no feedback upload, no telemetry export, no update check, no
        // prompt history.
        Config, "analytics.enabled=false",
        Config, "feedback.enabled=false",
        Config, "otel.exporter=\"none\"",
        Config, "otel.trace_exporter=\"none\"",
        Config, "otel.metrics_exporter=\"none\"",
        Config, "check_for_update_on_startup=false",
        Config, "history.persistence=\"none\"",
    ];

    /// <summary>
    /// The whole list for one run. The two <c>-c</c> values of the protocol hold their double quotes as
    /// characters of the argument: Codex reads the text after <c>=</c> as a value, here a string.
    /// </summary>
    /// <param name="schemaPath">Absolute path of the schema file of the run's kind.</param>
    /// <param name="workFolder">The run's own, empty folder: the output file goes there, and it is the
    /// working root Codex is told.</param>
    public static List<string> Build(string model, string reasoningEffort, string schemaPath, string workFolder) =>
    [
        "exec",
        "--ephemeral",
        "--ignore-user-config",
        "--ignore-rules",
        "--skip-git-repo-check",
        "--sandbox", "read-only",
        Disable, "shell_tool",
        .. Containment,
        "-m", model,
        Config, "model_reasoning_effort=\"" + reasoningEffort + "\"",
        Config, "service_tier=\"default\"",
        "--output-schema", schemaPath,
        "-o", Path.Combine(workFolder, OutputFileName),
        "-C", workFolder,
        "-",
    ];
}
