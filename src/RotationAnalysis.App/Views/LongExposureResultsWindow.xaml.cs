using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RotationAnalysis.Core.Storage;
using RotationAnalysis.Core.VideoAnalysis.LongExposure;

namespace RotationAnalysis.App.Views;

/// <summary>Shows previews of every generated variation and lets the user save one or all of
/// them. Reused as-is by Slit Scan, which shares this save workflow per spec.</summary>
public partial class LongExposureResultsWindow : Window
{
    private static readonly string DefaultOutputRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RotationAnalysisLab", "LongExposure");

    private readonly string _systemName;
    private readonly string? _bodyOrStationName;

    /// <summary>Generic over any labeled set of generated images - Long Exposure passes its six
    /// variants, Slit Scan (which reuses this window's save workflow as-is per spec) passes its
    /// single output.</summary>
    public LongExposureResultsWindow(IReadOnlyList<(string DisplayName, byte[] Png)> images, string systemName, string? bodyOrStationName)
    {
        InitializeComponent();
        _systemName = systemName;
        _bodyOrStationName = bodyOrStationName;

        foreach (var (displayName, png) in images)
        {
            VariantList.Items.Add(new VariantThumbnail(displayName, png, ToBitmapImage(png)));
        }

        if (VariantList.Items.Count > 0)
        {
            VariantList.SelectedIndex = 0;
        }
    }

    public static LongExposureResultsWindow ForLongExposureResult(LongExposureResult result, string systemName, string? bodyOrStationName)
        => new(result.AllVariants.Select(v => (v.DisplayName, v.Png)).ToList(), systemName, bodyOrStationName);

    private static BitmapImage ToBitmapImage(byte[] pngBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void VariantList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SaveSelectedButton.IsEnabled = VariantList.SelectedItem is not null;
    }

    private void SaveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (VariantList.SelectedItem is not VariantThumbnail selected)
        {
            return;
        }

        var directory = LongExposureFileNamer.SuggestDirectory(DefaultOutputRoot, _systemName);
        Directory.CreateDirectory(directory);
        var fileName = LongExposureFileNamer.SuggestFileName(_systemName, _bodyOrStationName, null, selected.DisplayName, ".png");

        var dialog = new SaveFileDialog
        {
            Title = "Save Long Exposure Image",
            InitialDirectory = directory,
            FileName = fileName,
            Filter = "PNG Image (*.png)|*.png",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllBytes(dialog.FileName, selected.Png);
        }
    }

    private void SaveAllButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = LongExposureFileNamer.SuggestDirectory(DefaultOutputRoot, _systemName);

        var folderDialog = new OpenFolderDialog
        {
            Title = "Choose a folder to save all variations",
            InitialDirectory = Directory.Exists(directory) ? directory : DefaultOutputRoot,
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        var targetDirectory = folderDialog.FolderName;
        var items = VariantList.Items.Cast<VariantThumbnail>().ToList();
        var plannedPaths = items
            .Select(item => (Item: item, Path: Path.Combine(targetDirectory, LongExposureFileNamer.SuggestFileName(_systemName, _bodyOrStationName, null, item.DisplayName, ".png"))))
            .ToList();

        var conflicts = plannedPaths.Where(p => LongExposureFileNamer.WouldOverwrite(p.Path)).ToList();
        if (conflicts.Count > 0)
        {
            var names = string.Join("\n", conflicts.Select(c => Path.GetFileName(c.Path)));
            var result = MessageBox.Show(
                this,
                $"The following files already exist and will be overwritten:\n\n{names}\n\nContinue?",
                "Overwrite existing files?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var (item, path) in plannedPaths)
        {
            File.WriteAllBytes(path, item.Png);
        }

        MessageBox.Show(this, $"Saved {plannedPaths.Count} images to {targetDirectory}.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class VariantThumbnail
    {
        public VariantThumbnail(string displayName, byte[] png, BitmapImage thumbnail)
        {
            DisplayName = displayName;
            Png = png;
            Thumbnail = thumbnail;
        }

        public string DisplayName { get; }
        public byte[] Png { get; }
        public BitmapImage Thumbnail { get; }
    }
}
