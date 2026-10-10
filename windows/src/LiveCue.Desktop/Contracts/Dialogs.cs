using System.Windows.Input;
using LiveCue.Hosting;

namespace LiveCue.Desktop;

/// <summary>Screens 6.1. Initial focus is on Cancel; Enter does not confirm.</summary>
public interface IResetPairingDialogViewModel : IDialogViewModel
{
    string Body { get; }

    /// <summary>A failure of the reset, shown in the dialog; null otherwise.</summary>
    BannerModel? Failure { get; }

    ICommand ConfirmCommand { get; }
}

/// <summary>Screens 6.2: set up or remove one mapping. The only caller of ExecuteMappingAsync.</summary>
public interface IMappingDialogViewModel : IDialogViewModel
{
    MappingAction Action { get; }

    /// <summary>"LiveCue will run:".</summary>
    string Intro { get; }

    /// <summary>plan.Command.DisplayText, verbatim.</summary>
    string CommandText { get; }

    string EffectText { get; }

    /// <summary>"Set up" or "Remove".</summary>
    string ConfirmLabel { get; }

    /// <summary>True for the remove forms: the confirm button is the destructive style.</summary>
    bool IsDestructive { get; }

    /// <summary>Failure of the last attempt; the dialog stays open. Null otherwise.</summary>
    BannerModel? Failure { get; }

    ICommand ConfirmCommand { get; }
}
