using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RotationAnalysis.App.Views;

public partial class VideoUploadPromptWindow : Window
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };

    public string? SelectedFilePath { get; private set; }

    public VideoUploadPromptWindow()
    {
        InitializeComponent();
    }

    private void DropTarget_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the ring rotation video",
            Filter = "Video files (*.mp4;*.mkv;*.avi;*.mov;*.wmv)|*.mp4;*.mkv;*.avi;*.mov;*.wmv|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            SelectedFilePath = dialog.FileName;
            DialogResult = true;
        }
    }

    private void DropTarget_DragEnter(object sender, DragEventArgs e)
    {
        if (TryGetVideoPath(e, out _))
        {
            DragOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
    }

    private void DropTarget_Drop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        if (TryGetVideoPath(e, out var path))
        {
            SelectedFilePath = path;
            DialogResult = true;
        }
    }

    private static bool TryGetVideoPath(DragEventArgs e, out string? path)
    {
        path = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var candidate = files.FirstOrDefault(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        if (candidate is null)
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
