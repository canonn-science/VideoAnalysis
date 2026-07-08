using System.Windows;

namespace RotationAnalysis.App.Views;

public partial class VideoRenamePromptWindow : Window
{
    public VideoRenamePromptWindow(string suggestedFileName)
    {
        InitializeComponent();
        SuggestedNameRun.Text = suggestedFileName;
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void KeepButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
