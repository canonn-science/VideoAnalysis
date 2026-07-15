using System.Windows;

namespace VideoAnalysis.App.Views;

public enum RecordingNotificationChoice
{
    Dismissed,
    Primary,
    Secondary,
}

/// <summary>A small, non-modal (<see cref="Window.Show"/>, never <see cref="Window.ShowDialog"/>)
/// corner notification used for the folder-monitor prompts - "Recording started, add it?" and
/// "Recording finished, tag it now?". Deliberately chromeless (no title bar/close button) so it
/// reads as a passive toast rather than something that steals focus from an active recording; the
/// only way to dismiss it without choosing is <see cref="Choice"/> defaulting to
/// <see cref="RecordingNotificationChoice.Dismissed"/> if the window is closed some other way.</summary>
public partial class RecordingNotificationWindow : Window
{
    public RecordingNotificationChoice Choice { get; private set; } = RecordingNotificationChoice.Dismissed;

    /// <summary>Raised once, whichever way the window closes - button click or otherwise.</summary>
    public event Action<RecordingNotificationChoice>? Decided;

    private bool _decided;

    public RecordingNotificationWindow(string title, string message, string primaryButtonText, string secondaryButtonText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryButtonText;
        SecondaryButton.Content = secondaryButtonText;
        Closed += (_, _) => Finish(Choice);
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecordingNotificationChoice.Primary;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = RecordingNotificationChoice.Secondary;
        Close();
    }

    private void Finish(RecordingNotificationChoice choice)
    {
        if (_decided)
        {
            return;
        }

        _decided = true;
        Decided?.Invoke(choice);
    }
}
