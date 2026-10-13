using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Settings;

/// <summary>
/// The Settings page (screens section 5; W-design 5.8).
///
///   Connection   a form (mode, ports, path) that is saved only by "Save", and beside it the state of
///                the Tailscale mapping of the mode that is saved, with the exact command that
///                "Set up connection…" would run
///   Assistant    what the last check found: Codex, its sign-in, its version and the containment note
///   PC speech    the lab folder in use and what the relay last said about the loaded model
///   About        version, licence, notices, source
///
/// Nothing here writes to Tailscale. Saving writes settings.json and then only reads Tailscale again;
/// "Set up connection…" and "Remove…" open the dialog that shows the command, and only that dialog
/// can run it (rule TS-9). A mapping LiveCue did not create is shown with its removal command as
/// text and has no button at all (rule TS-8). Use it on the UI thread.
/// </summary>
internal sealed class SettingsViewModel : ObservableObject, ISettingsViewModel
{
    private const string BundledLabFolderName = "asr";

    private static readonly StatusIndicator NotReported =
        new(StatusMark.Ring, StatusTone.Offline, SettingsText.NotReportedYet);

    private static readonly StatusIndicator NoModel = new(StatusMark.Ring, StatusTone.Offline, SettingsText.NoModel);

    private static readonly StatusIndicator NotCheckedYet =
        new(StatusMark.Ring, StatusTone.Offline, SettingsText.NotChecked);

    /// <summary>The form properties that can carry an error line, by the settings field they hold.</summary>
    private static readonly Dictionary<SettingsField, string> FieldProperties = new()
    {
        [SettingsField.RelayPort] = nameof(RelayPortText),
        [SettingsField.ConnectionMode] = nameof(Mode),
        [SettingsField.PrivateHttpsPort] = nameof(PrivateHttpsPortText),
        [SettingsField.PublicHttpsPort] = nameof(PublicHttpsPort),
        [SettingsField.PublicPathPrefix] = nameof(PublicPathPrefix),
        [SettingsField.CodexExecutablePath] = nameof(CodexPathText),
        [SettingsField.AsrLabFolder] = nameof(LabFolderText),
    };

    private readonly IDesktopSession _session;
    private readonly IDialogHost _dialogs;
    private readonly IClipboard _clipboard;
    private readonly IToastHost _toasts;
    private readonly HostingServices _hosting;
    private readonly RelayCommand _save;
    private readonly RelayCommand _discard;
    private readonly RelayCommand _setUpMapping;
    private readonly RelayCommand<object?> _removeMapping;
    private readonly AsyncRelayCommand _checkMapping;
    private readonly AsyncRelayCommand _checkAssistant;
    private readonly RelayCommand _markContainmentChecked;
    private readonly RelayCommand _copyAddress;

    private SettingsForm _form;
    private SettingsForm _baseline;
    private Dictionary<string, string> _errors = [];
    private MappingPlan? _setUpPlan;
    private IReadOnlyList<StaleMappingRow> _staleMappings = [];
    private IReadOnlyList<UnownedExposureRow> _unownedExposures = [];
    private string? _labFolderInUse;
    private StatusIndicator _loadedModel = NotReported;

    public SettingsViewModel(
        IDesktopSession session,
        IDialogHost dialogs,
        IClipboard clipboard,
        IToastHost toasts,
        IExternalLauncher launcher,
        HostingServices hosting,
        IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(hosting);
        ArgumentNullException.ThrowIfNull(ui);

        _session = session;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _toasts = toasts;
        _hosting = hosting;
        _baseline = SettingsForm.From(session.Settings);
        _form = _baseline;

        _save = new RelayCommand(Save, () => HasChanges && !HasErrors);
        _discard = new RelayCommand(Discard, () => HasChanges);
        _setUpMapping = new RelayCommand(OpenSetUpDialog, () => CanSetUpMapping);
        _removeMapping = new RelayCommand<object?>(OpenRemoveDialog, parameter => parameter is OwnedMapping entry && IsRemovable(entry));
        _checkMapping = new AsyncRelayCommand(session.CheckAgainAsync);
        _checkAssistant = new AsyncRelayCommand(session.CheckAgainAsync);
        _checkMapping.PropertyChanged += OnCheckCommandChanged;
        _checkAssistant.PropertyChanged += OnCheckCommandChanged;
        _markContainmentChecked = new RelayCommand(MarkContainmentChecked, () => CanMarkContainmentChecked);
        _copyAddress = new RelayCommand(CopyAddress, () => HasPcAddress);
        OpenSourceCommand = new RelayCommand(() => launcher.OpenUrl(AboutInfo.SourceUrl));
        OpenNoticesCommand = new RelayCommand(() => launcher.OpenFile(AboutInfo.NoticesPath));

        Revalidate();
        ReadMapping();
        ReadLabFolder();

        session.PropertyChanged += OnSessionChanged;
        session.RelayEventReceived += OnRelayEvent;
        session.Signal += OnSignal;
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    // ---- The form -------------------------------------------------------------------------------

    public ConnectionMode Mode
    {
        get => _form.Mode;
        set
        {
            if (_form.Mode == value)
            {
                return;
            }

            // A field the new mode does not show cannot keep an error nobody can see: it goes back to
            // its saved value.
            var form = _form with { Mode = value };
            if (value == ConnectionMode.Private && _errors.ContainsKey(nameof(PublicPathPrefix)))
            {
                form = form with { PublicPathPrefix = _baseline.PublicPathPrefix };
            }
            else if (value == ConnectionMode.Public && _errors.ContainsKey(nameof(PrivateHttpsPortText)))
            {
                form = form with { PrivateHttpsPortText = _baseline.PrivateHttpsPortText };
            }

            Show(form);
        }
    }

    public string ModeExplanation =>
        Mode == ConnectionMode.Public ? SettingsText.PublicExplanation : SettingsText.PrivateExplanation;

    /// <summary>True while the form's mode is Public Funnel: the page then shows that mode's port and path.</summary>
    public bool IsPublicMode => Mode == ConnectionMode.Public;

    public string RelayPortText
    {
        get => _form.RelayPortText;
        set => Edit(_form with { RelayPortText = value ?? string.Empty });
    }

    public string PrivateHttpsPortText
    {
        get => _form.PrivateHttpsPortText;
        set => Edit(_form with { PrivateHttpsPortText = value ?? string.Empty });
    }

    public int PublicHttpsPort
    {
        get => _form.PublicHttpsPort;
        set => Edit(_form with { PublicHttpsPort = value });
    }

    public string PublicPathPrefix
    {
        get => _form.PublicPathPrefix;
        set => Edit(_form with { PublicPathPrefix = value ?? string.Empty });
    }

    public string CodexPathText
    {
        get => _form.CodexPathText;
        set => Edit(_form with { CodexPathText = value ?? string.Empty });
    }

    public string LabFolderText
    {
        get => _form.LabFolderText;
        set => Edit(_form with { LabFolderText = value ?? string.Empty });
    }

    public bool HideFromScreenCapture
    {
        get => _form.HideFromScreenCapture;
        set => Edit(_form with { HideFromScreenCapture = value });
    }

    public bool HasChanges => _form != _baseline;

    public bool HasErrors => _errors.Count > 0;

    /// <summary>The error line under each field; null while the field's value is allowed.</summary>
    public string? RelayPortError => ErrorOf(nameof(RelayPortText));

    public string? PrivateHttpsPortError => ErrorOf(nameof(PrivateHttpsPortText));

    public string? PublicPathPrefixError => ErrorOf(nameof(PublicPathPrefix));

    public string? CodexPathError => ErrorOf(nameof(CodexPathText));

    public string? LabFolderError => ErrorOf(nameof(LabFolderText));

    public string PcAddress => FormAddress ?? SettingsText.AddressUnknown;

    /// <summary>False while the address cannot be told: the field then shows its placeholder and no copy button.</summary>
    public bool HasPcAddress => FormAddress is not null;

    public BannerModel? RestartNotice => _session.SettingsDifferFromRunning
        ? new BannerModel(BannerTone.Warning, null, SettingsText.RestartNotice)
        : null;

    public BannerModel? SettingsFileNotice =>
        _session.SettingsLoad.State is SettingsFileState.Invalid or SettingsFileState.Unreadable
            ? new BannerModel(BannerTone.Warning, null, SettingsText.SettingsFileNotice)
            : null;

    // ---- Tailscale mapping ----------------------------------------------------------------------

    public StatusIndicator MappingStatus
    {
        get
        {
            switch (Tailscale)
            {
                case TailscaleProblem.NotInstalled:
                    return new StatusIndicator(StatusMark.Alert, StatusTone.Danger, SettingsText.TailscaleNotInstalled);
                case TailscaleProblem.NotOnline:
                    return new StatusIndicator(StatusMark.Alert, StatusTone.Danger, SettingsText.TailscaleNotOnline);
                default:
                    break;
            }

            return SelectedMapping?.State switch
            {
                MappingState.Present => new(StatusMark.Dot, StatusTone.Success, SettingsText.MappingActive),
                MappingState.PresentNotOwned => new(StatusMark.Dot, StatusTone.Success, SettingsText.MappingActiveNotOwned),
                MappingState.Absent => new(StatusMark.Dot, StatusTone.Warning, SettingsText.MappingNotSetUp),
                MappingState.Blocked => new(StatusMark.Alert, StatusTone.Danger, SettingsText.MappingBlocked),
                MappingState.Unknown => new(StatusMark.Alert, StatusTone.Danger, SettingsText.TailscaleUnreadable),
                _ => new(StatusMark.Ring, StatusTone.Offline, SettingsText.MappingNotChecked),
            };
        }
    }

    public string? MappingCaption
    {
        get
        {
            switch (Tailscale)
            {
                case TailscaleProblem.NotInstalled:
                    return SettingsText.TailscaleNotInstalledFix;
                case TailscaleProblem.NotOnline:
                    return SettingsText.TailscaleNotOnlineFix;
                default:
                    break;
            }

            return SelectedMapping switch
            {
                { State: MappingState.Unknown } => SettingsText.TailscaleUnreadableFix,
                { State: MappingState.Blocked } blocked => NullIfEmpty(
                    MappingTexts.Refusal(blocked.BlockedBy, blocked.HttpsPort, blocked.Path)),
                { State: MappingState.PresentNotOwned } notOwned =>
                    MappingTexts.Refusal(MappingRefusal.NotInLedger, notOwned.HttpsPort, notOwned.Path),
                { State: MappingState.Absent } when HasChanges => SettingsText.SaveFirst,
                _ => null,
            };
        }
    }

    public string MappingCommandText => _setUpPlan?.Command.DisplayText ?? string.Empty;

    public string MappingEffectText => _setUpPlan?.EffectText ?? string.Empty;

    public bool CanSetUpMapping =>
        Tailscale == TailscaleProblem.None
        && SelectedMapping?.State == MappingState.Absent
        && !HasChanges
        && _setUpPlan is not null;

    public OwnedMapping? CurrentOwnedMapping
    {
        get
        {
            if (SelectedMapping is not { State: MappingState.Present } mapping)
            {
                return null;
            }

            var settings = _session.Settings;
            var target = MappingCommand.TargetFor(settings.RelayPort);
            return settings.OwnedMappings.FirstOrDefault(entry =>
                entry.Mode == mapping.Mode
                && entry.HttpsPort == mapping.HttpsPort
                && string.Equals(entry.Path, mapping.Path, StringComparison.Ordinal)
                && string.Equals(entry.Target, target, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<StaleMappingRow> StaleMappings => _staleMappings;

    public IReadOnlyList<UnownedExposureRow> UnownedExposures => _unownedExposures;

    public string? ExposureUnknownText =>
        _session.Connection is { StatusFailure: not (TailscaleReadFailure.None or TailscaleReadFailure.NotInstalled) }
            ? MappingTexts.ExposureUnknown
            : null;

    // ---- Assistant ------------------------------------------------------------------------------

    public StatusIndicator CodexPathStatus => StateOf(PrerequisiteId.CodexFound) switch
    {
        PrerequisiteState.Passed => new(StatusMark.Check, StatusTone.Success, SettingsText.Found),
        PrerequisiteState.Failed => new(StatusMark.Alert, StatusTone.Danger, SettingsText.NotFound),
        _ => NotCheckedYet,
    };

    public string? CodexPathDetail => StateOf(PrerequisiteId.CodexFound) switch
    {
        PrerequisiteState.Passed => _session.Prerequisites?.CodexExecutablePath,
        PrerequisiteState.Failed => SettingsText.CodexNotFoundFix,
        _ => null,
    };

    public StatusIndicator CodexLoginStatus => StateOf(PrerequisiteId.CodexSignedIn) switch
    {
        PrerequisiteState.Passed => new(StatusMark.Check, StatusTone.Success, SettingsText.SignedIn),
        PrerequisiteState.Failed => new(StatusMark.Alert, StatusTone.Danger, SettingsText.NotSignedIn),
        _ => NotCheckedYet,
    };

    /// <summary>The fix under "Codex sign-in" while Codex is not signed in; null otherwise.</summary>
    public string? CodexLoginDetail =>
        StateOf(PrerequisiteId.CodexSignedIn) == PrerequisiteState.Failed ? SettingsText.CodexNotSignedInFix : null;

    public StatusIndicator CodexVersionStatus
    {
        get
        {
            if (!IsCodexFound)
            {
                return new StatusIndicator(StatusMark.Ring, StatusTone.Offline, SettingsText.CodexVersionUnknown);
            }

            var version = _session.Prerequisites?.CodexVersion;
            return IsContainmentChecked(version)
                ? new StatusIndicator(StatusMark.Check, StatusTone.Success, version!)
                : new StatusIndicator(StatusMark.Alert, StatusTone.Warning, version ?? SettingsText.CodexVersionUnknown);
        }
    }

    public string? CodexVersionDetail
    {
        get
        {
            if (!IsCodexFound)
            {
                return null;
            }

            var version = _session.Prerequisites?.CodexVersion;
            return IsContainmentChecked(version)
                ? SettingsText.ContainmentChecked
                : PrerequisiteTexts.ContainmentWarning(version, _session.Settings.CodexContainmentCheckedVersion);
        }
    }

    public bool CanMarkContainmentChecked =>
        IsCodexFound && _session.Prerequisites?.CodexVersion is { } version && !IsContainmentChecked(version);

    public bool IsCheckingAssistant =>
        _checkAssistant.IsRunning || _checkMapping.IsRunning || _session.State == RelayUiState.Checking;

    // ---- PC speech models -----------------------------------------------------------------------

    public StatusIndicator LabFolderStatus => _labFolderInUse is null
        ? new StatusIndicator(StatusMark.Dot, StatusTone.Warning, SettingsText.LabNotSetUp)
        : new StatusIndicator(StatusMark.Check, StatusTone.Success, SettingsText.Found);

    public string? LabFolderDetail => _labFolderInUse;

    public StatusIndicator LoadedModelStatus => _loadedModel;

    public string SpeechCaption => _labFolderInUse is null ? SettingsText.SpeechSetupHint : SettingsText.SpeechCaption;

    // ---- About ----------------------------------------------------------------------------------

    public string Version => AboutInfo.Version;

    // ---- Commands -------------------------------------------------------------------------------

    public ICommand SaveCommand => _save;

    public ICommand DiscardCommand => _discard;

    public ICommand SetUpMappingCommand => _setUpMapping;

    public ICommand RemoveMappingCommand => _removeMapping;

    public ICommand CheckMappingCommand => _checkMapping;

    public ICommand CheckAssistantCommand => _checkAssistant;

    public ICommand MarkContainmentCheckedCommand => _markContainmentChecked;

    public ICommand CopyAddressCommand => _copyAddress;

    public ICommand OpenSourceCommand { get; }

    public ICommand OpenNoticesCommand { get; }

    /// <summary>The mapping of the mode that is saved, as last inspected; null before the first inspection.</summary>
    private MappingInspection? SelectedMapping => _session.Connection?.For(_session.Settings.ConnectionMode);

    /// <summary>What the last check said about Tailscale itself: without it no mapping can be set up.</summary>
    private TailscaleProblem Tailscale
    {
        get
        {
            if (StateOf(PrerequisiteId.TailscaleInstalled) == PrerequisiteState.Failed
                || _session.Connection is { StatusFailure: TailscaleReadFailure.NotInstalled })
            {
                return TailscaleProblem.NotInstalled;
            }

            return StateOf(PrerequisiteId.TailscaleOnline) == PrerequisiteState.Failed
                ? TailscaleProblem.NotOnline
                : TailscaleProblem.None;
        }
    }

    private bool IsCodexFound => StateOf(PrerequisiteId.CodexFound) == PrerequisiteState.Passed;

    /// <summary>The address the QR would advertise for the form as it stands; null when it cannot be told.</summary>
    private string? FormAddress =>
        _session.Prerequisites?.DnsName is { Length: > 0 } dnsName
            ? _hosting.Endpoints.Resolve(_form.ApplyTo(_session.Settings), dnsName)
            : null;

    public IEnumerable GetErrors(string? propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? _errors.Values.ToArray()
            : _errors.TryGetValue(propertyName, out var error) ? new[] { error } : Array.Empty<string>();

    /// <summary>
    /// The page was opened. A check runs only if none has run yet (screens 5.2): the rows otherwise
    /// show the most recent one.
    /// </summary>
    internal void OnOpened()
    {
        if (_session.Prerequisites is null && _checkAssistant.CanExecute(null) && !IsCheckingAssistant)
        {
            _checkAssistant.Execute(null);
        }
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private PrerequisiteState? StateOf(PrerequisiteId id) =>
        _session.Prerequisites?.Results.FirstOrDefault(result => result.Id == id)?.State;

    private bool IsContainmentChecked(string? version) =>
        version is not null
        && string.Equals(version, _session.Settings.CodexContainmentCheckedVersion, StringComparison.Ordinal);

    private string? ErrorOf(string propertyName) => _errors.GetValueOrDefault(propertyName);

    // ---- Form changes ---------------------------------------------------------------------------

    private void Edit(SettingsForm form, [CallerMemberName] string? propertyName = null)
    {
        if (form == _form)
        {
            return;
        }

        _form = form;
        OnPropertyChanged(propertyName);
        FormChanged();
    }

    /// <summary>Replaces the whole form: every field is told again.</summary>
    private void Show(SettingsForm form)
    {
        _form = form;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ModeExplanation));
        OnPropertyChanged(nameof(IsPublicMode));
        OnPropertyChanged(nameof(RelayPortText));
        OnPropertyChanged(nameof(PrivateHttpsPortText));
        OnPropertyChanged(nameof(PublicHttpsPort));
        OnPropertyChanged(nameof(PublicPathPrefix));
        OnPropertyChanged(nameof(CodexPathText));
        OnPropertyChanged(nameof(LabFolderText));
        OnPropertyChanged(nameof(HideFromScreenCapture));
        FormChanged();
    }

    private void FormChanged()
    {
        Revalidate();
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(PcAddress));
        OnPropertyChanged(nameof(HasPcAddress));
        OnPropertyChanged(nameof(MappingCaption));
        OnPropertyChanged(nameof(CanSetUpMapping));
        _save.NotifyCanExecuteChanged();
        _discard.NotifyCanExecuteChanged();
        _setUpMapping.NotifyCanExecuteChanged();
        _copyAddress.NotifyCanExecuteChanged();
    }

    /// <summary>The rules of the settings, applied to the form as it stands (W-design 4.14).</summary>
    private void Revalidate() => SetErrors(_hosting.Validator.Validate(_form.ApplyTo(_session.Settings)));

    private void SetErrors(IReadOnlyList<SettingsProblem> problems)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var problem in problems)
        {
            // The first problem of a field is the one it shows. A problem of something the page does
            // not edit has no field to show it on.
            if (FieldProperties.TryGetValue(problem.Field, out var propertyName))
            {
                errors.TryAdd(propertyName, SettingsText.FieldError(problem));
            }
        }

        var before = _errors;
        _errors = errors;
        var changed = before.Keys.Union(errors.Keys)
            .Where(name => before.GetValueOrDefault(name) != errors.GetValueOrDefault(name))
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        foreach (var name in changed)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(name));
        }

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(RelayPortError));
        OnPropertyChanged(nameof(PrivateHttpsPortError));
        OnPropertyChanged(nameof(PublicPathPrefixError));
        OnPropertyChanged(nameof(CodexPathError));
        OnPropertyChanged(nameof(LabFolderError));
        _save.NotifyCanExecuteChanged();
    }

    private void Save()
    {
        if (!HasChanges || HasErrors)
        {
            return;
        }

        var wanted = _form.ApplyTo(_session.Settings);
        var problems = _session.SaveSettings(wanted);
        if (problems.Count > 0)
        {
            SetErrors(problems);
            return;
        }

        if (SettingsForm.From(_session.Settings) != SettingsForm.From(wanted))
        {
            // Nothing was written (the session said why in the activity): the edits stay.
            return;
        }

        // Saved: the form is the saved settings again. Saving changes nothing in Tailscale; the state
        // of the mapping is only read again, because it is the state of the settings that are saved.
        _baseline = SettingsForm.From(_session.Settings);
        Show(_baseline);
        _ = _session.CheckAgainAsync();
    }

    private void Discard() => Show(_baseline);

    // ---- Mapping --------------------------------------------------------------------------------

    private void OpenSetUpDialog()
    {
        if (_session.PlanSetUp(out _) is { } plan)
        {
            _dialogs.Show(new MappingDialogViewModel(_session, plan));
        }
    }

    private void OpenRemoveDialog(object? parameter)
    {
        // A removal exists only for a mapping LiveCue created and that still matches what it created
        // (rule TS-8): the selected mode's own, or one of the stale ones.
        if (parameter is not OwnedMapping entry || !IsRemovable(entry))
        {
            return;
        }

        MappingPlan plan;
        try
        {
            plan = _session.PlanRemoval(entry);
        }
        catch (ArgumentException)
        {
            // A ledger entry edited by hand into something that has no removal command: there is
            // nothing to show, so there is nothing to confirm.
            return;
        }

        _dialogs.Show(new MappingDialogViewModel(_session, plan));
    }

    private bool IsRemovable(OwnedMapping entry) =>
        entry == CurrentOwnedMapping || _staleMappings.Any(row => row.Entry == entry);

    private void ReadMapping()
    {
        _setUpPlan = _session.PlanSetUp(out _);

        // A list whose rows did not change stays the same list: its warnings are not drawn, and not
        // announced, a second time.
        var connection = _session.Connection;
        List<StaleMappingRow> stale = connection is null
            ? []
            : connection.StaleOwned
                .Select(entry => new StaleMappingRow(entry, SettingsText.StaleMapping(entry.Mode, AddressOf(entry))))
                .ToList();
        if (!stale.SequenceEqual(_staleMappings))
        {
            _staleMappings = stale;
        }

        List<UnownedExposureRow> unowned = connection is null
            ? []
            : connection.UnownedExposure
                .Select(handler => new UnownedExposureRow(handler, MappingTexts.Exposure(handler), MappingTexts.ManualRemoval(handler)))
                .ToList();
        if (!unowned.SequenceEqual(_unownedExposures))
        {
            _unownedExposures = unowned;
        }
    }

    /// <summary>The address a mapping of ours was reached at, or where it is when the PC's name is unknown.</summary>
    private string AddressOf(OwnedMapping entry)
    {
        if (_session.Prerequisites?.DnsName is { Length: > 0 } dnsName)
        {
            var asSettings = entry.Mode == ConnectionMode.Public
                ? RelaySettings.Default with
                {
                    ConnectionMode = ConnectionMode.Public,
                    PublicHttpsPort = entry.HttpsPort,
                    PublicPathPrefix = entry.Path,
                }
                : RelaySettings.Default with { ConnectionMode = ConnectionMode.Private, PrivateHttpsPort = entry.HttpsPort };
            if (_hosting.Endpoints.Resolve(asSettings, dnsName) is { } address)
            {
                return address;
            }
        }

        return SettingsText.AddressWithoutName(entry.HttpsPort, entry.Path);
    }

    private void MappingChanged()
    {
        var stale = _staleMappings;
        var unowned = _unownedExposures;
        ReadMapping();
        OnPropertyChanged(nameof(MappingStatus));
        OnPropertyChanged(nameof(MappingCaption));
        OnPropertyChanged(nameof(MappingCommandText));
        OnPropertyChanged(nameof(MappingEffectText));
        OnPropertyChanged(nameof(CanSetUpMapping));
        OnPropertyChanged(nameof(CurrentOwnedMapping));
        if (!ReferenceEquals(stale, _staleMappings))
        {
            OnPropertyChanged(nameof(StaleMappings));
        }

        if (!ReferenceEquals(unowned, _unownedExposures))
        {
            OnPropertyChanged(nameof(UnownedExposures));
        }

        OnPropertyChanged(nameof(ExposureUnknownText));
        _setUpMapping.NotifyCanExecuteChanged();
        _removeMapping.NotifyCanExecuteChanged();
    }

    // ---- Assistant ------------------------------------------------------------------------------

    private void MarkContainmentChecked()
    {
        if (!CanMarkContainmentChecked || _session.Prerequisites?.CodexVersion is not { } version)
        {
            return;
        }

        // Built from the saved settings: what is typed in the form is neither saved nor lost.
        var problems = _session.SaveSettings(_session.Settings with { CodexContainmentCheckedVersion = version });
        if (problems.Count == 0)
        {
            _ = _session.CheckAgainAsync();
        }
    }

    private void AssistantChanged()
    {
        OnPropertyChanged(nameof(CodexPathStatus));
        OnPropertyChanged(nameof(CodexPathDetail));
        OnPropertyChanged(nameof(CodexLoginStatus));
        OnPropertyChanged(nameof(CodexLoginDetail));
        VersionChanged();
    }

    private void VersionChanged()
    {
        OnPropertyChanged(nameof(CodexVersionStatus));
        OnPropertyChanged(nameof(CodexVersionDetail));
        OnPropertyChanged(nameof(CanMarkContainmentChecked));
        _markContainmentChecked.NotifyCanExecuteChanged();
    }

    private void OnCheckCommandChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(AsyncRelayCommand.IsRunning))
        {
            OnPropertyChanged(nameof(IsCheckingAssistant));
        }
    }

    // ---- PC speech models -----------------------------------------------------------------------

    /// <summary>
    /// The lab folder in use. While a relay runs it is the one that relay was given. Otherwise it is
    /// the folder the next start would use: the saved one, or "asr" beside the app, if it passes the
    /// rules of the settings (a folder on a local drive that holds the two launcher scripts).
    /// </summary>
    private void ReadLabFolder()
    {
        if (_session.IsRelayRunning && _hosting.Supervisor.RunningOptions is { } running)
        {
            _labFolderInUse = running.LabFolder;
            return;
        }

        var saved = _session.Settings;
        var folder = saved.AsrLabFolder ?? Path.Combine(AppContext.BaseDirectory, BundledLabFolderName);
        var usable = _hosting.Validator
            .Validate(saved with { AsrLabFolder = folder })
            .All(problem => problem.Field != SettingsField.AsrLabFolder);
        _labFolderInUse = usable ? folder : null;
    }

    private void LabChanged()
    {
        ReadLabFolder();
        OnPropertyChanged(nameof(LabFolderStatus));
        OnPropertyChanged(nameof(LabFolderDetail));
        OnPropertyChanged(nameof(SpeechCaption));
    }

    private void OnRelayEvent(object? sender, RelayEvent relayEvent)
    {
        switch (relayEvent)
        {
            case LocalModelChanged { Status: var status } model:
                SetLoadedModel(status switch
                {
                    { State: LocalModelState.Starting, Model: { } name } =>
                        new StatusIndicator(StatusMark.Spinner, StatusTone.Live, SettingsText.ModelLoading(SettingsText.ModelLabel(name))),
                    { State: LocalModelState.Ready, Model: { } name } =>
                        new StatusIndicator(StatusMark.Dot, StatusTone.Success, SettingsText.ModelReady(SettingsText.ModelLabel(name))),
                    { State: LocalModelState.Error } =>
                        new StatusIndicator(StatusMark.Alert, StatusTone.Danger, string.IsNullOrEmpty(status.Message) ? model.Message : status.Message),
                    _ => NoModel,
                });
                break;

            case SpeechStreamChanged { Phase: SpeechStreamPhase.Ready } speech:
                SetLoadedModel(new StatusIndicator(
                    StatusMark.Dot, StatusTone.Live, SettingsText.ModelInUse(SettingsText.ModelLabel(speech.Model))));
                break;

            case SpeechStreamChanged { Phase: SpeechStreamPhase.Stopped or SpeechStreamPhase.Failed } speech:
                SetLoadedModel(new StatusIndicator(
                    StatusMark.Dot, StatusTone.Success, SettingsText.ModelReady(SettingsText.ModelLabel(speech.Model))));
                break;

            default:
                break;
        }
    }

    private void OnSignal(object? sender, SessionSignal signal)
    {
        // "What the relay last reported since it started": another relay has reported nothing yet.
        if (signal is SessionSignal.RelayStarting or SessionSignal.RelayEnded)
        {
            SetLoadedModel(NotReported);
        }
    }

    private void SetLoadedModel(StatusIndicator status)
    {
        if (_loadedModel != status)
        {
            _loadedModel = status;
            OnPropertyChanged(nameof(LoadedModelStatus));
        }
    }

    // ---- The session's state --------------------------------------------------------------------

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IDesktopSession.Settings):
                SavedSettingsChanged();
                break;

            case nameof(IDesktopSession.Connection):
                MappingChanged();
                break;

            case nameof(IDesktopSession.Prerequisites):
                AssistantChanged();
                MappingChanged();
                AddressChanged();
                LabChanged();
                break;

            case nameof(IDesktopSession.State):
                OnPropertyChanged(nameof(IsCheckingAssistant));
                break;

            case nameof(IDesktopSession.IsRelayRunning):
                LabChanged();
                break;

            case nameof(IDesktopSession.SettingsDifferFromRunning):
                OnPropertyChanged(nameof(RestartNotice));
                break;

            case nameof(IDesktopSession.SettingsLoad):
                OnPropertyChanged(nameof(SettingsFileNotice));
                break;

            case null or "":
                // "Everything may have changed."
                SavedSettingsChanged();
                AssistantChanged();
                AddressChanged();
                OnPropertyChanged(nameof(IsCheckingAssistant));
                OnPropertyChanged(nameof(RestartNotice));
                OnPropertyChanged(nameof(SettingsFileNotice));
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The saved settings changed: by "Save", by "Mark as checked", or by a mapping command that wrote
    /// its ledger. A form without unsaved edits follows them; one with edits keeps them.
    /// </summary>
    private void SavedSettingsChanged()
    {
        var hadChanges = HasChanges;
        _baseline = SettingsForm.From(_session.Settings);
        if (!hadChanges && _form != _baseline)
        {
            Show(_baseline);
        }
        else
        {
            FormChanged();
        }

        MappingChanged();
        VersionChanged();
        LabChanged();
    }

    private void AddressChanged()
    {
        OnPropertyChanged(nameof(PcAddress));
        OnPropertyChanged(nameof(HasPcAddress));
        _copyAddress.NotifyCanExecuteChanged();
    }

    private void CopyAddress()
    {
        if (FormAddress is { } address && _clipboard.TrySetText(address))
        {
            _toasts.Show(ShellText.Copied);
        }
    }

    private enum TailscaleProblem
    {
        None,
        NotInstalled,
        NotOnline,
    }
}
