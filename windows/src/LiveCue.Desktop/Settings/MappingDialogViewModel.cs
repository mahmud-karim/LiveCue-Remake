using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Settings;

/// <summary>
/// The set-up and remove confirmation of a Tailscale mapping (screens 6.2; W-design 5.9). It shows the
/// plan's command text verbatim, and it is the only code that calls
/// <see cref="IDesktopSession.ExecuteMappingAsync"/> (rule TS-9): nothing is written to Tailscale
/// without a click on a dialog that shows the exact command.
///
/// A command that worked closes the dialog. Every other result stays open with the mapping manager's
/// own text for it (W-design 4.15), so the command is still on screen beside what went wrong.
/// </summary>
internal sealed class MappingDialogViewModel : ObservableObject, IMappingDialogViewModel
{
    private readonly IDesktopSession _session;
    private readonly MappingPlan _plan;
    private readonly AsyncRelayCommand _confirm;
    private readonly RelayCommand _cancel;
    private bool _isBusy;
    private BannerModel? _failure;

    public MappingDialogViewModel(IDesktopSession session, MappingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(plan);
        _session = session;
        _plan = plan;
        _confirm = new AsyncRelayCommand(ConfirmAsync, () => !IsBusy);
        _cancel = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty), () => !IsBusy);
    }

    public event EventHandler? Closed;

    public MappingAction Action => _plan.Action;

    public bool IsDestructive => Action is MappingAction.RemovePrivate or MappingAction.RemovePublic;

    public string Title => IsDestructive ? SettingsText.RemoveTitle : SettingsText.SetUpTitle;

    public string Intro => SettingsText.DialogIntro;

    public string CommandText => _plan.Command.DisplayText;

    public string EffectText => _plan.EffectText;

    public string ConfirmLabel => IsDestructive ? SettingsText.Remove : SettingsText.SetUp;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _confirm.NotifyCanExecuteChanged();
                _cancel.NotifyCanExecuteChanged();
            }
        }
    }

    public BannerModel? Failure
    {
        get => _failure;
        private set => SetProperty(ref _failure, value);
    }

    public ICommand ConfirmCommand => _confirm;

    public ICommand CancelCommand => _cancel;

    private async Task ConfirmAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Failure = null;
        var result = await _session.ExecuteMappingAsync(_plan);
        IsBusy = false;

        if (result.Outcome == MappingOutcome.Done)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Rule TS-7: a change to somebody else's entries is said here too, not only in the activity.
        var text = MappingTexts.Failure(result, _plan);
        Failure = new BannerModel(
            BannerTone.Danger,
            null,
            result.OthersChanged ? text + Environment.NewLine + MappingTexts.OthersChanged : text);
    }
}
