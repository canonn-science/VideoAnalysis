using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RotationAnalysis.App.Views;

/// <summary>Shows the captured HUD crop and lets the user confirm or correct the distance value
/// before it's saved - the OCR prefill (local or Claude) is never trusted blindly.</summary>
public partial class JetConeReviewWindow : Window
{
    private readonly double? _originalPrefillValue;

    /// <summary>The value the user actually saved, once <see cref="ShowDialog"/> returns true.</summary>
    public double DistanceLs { get; private set; }

    /// <summary>True if the saved value differs from the OCR prefill - the caller uses this to
    /// decide whether to offer setting up the Claude API key fallback.</summary>
    public bool UserCorrectedValue { get; private set; }

    public JetConeReviewWindow(byte[] reticleCropPng, byte[] bottomLeftCropPng, double? prefillDistanceLs, string sourceLabel)
    {
        InitializeComponent();

        _originalPrefillValue = prefillDistanceLs;
        ReticleImage.Source = ToBitmapImage(reticleCropPng);
        BottomLeftImage.Source = ToBitmapImage(bottomLeftCropPng);

        DistanceTextBox.Text = prefillDistanceLs is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        SourceText.Text = sourceLabel;

        Loaded += (_, _) =>
        {
            DistanceTextBox.Focus();
            DistanceTextBox.SelectAll();
        };
    }

    private static BitmapImage? ToBitmapImage(byte[] pngBytes)
    {
        if (pngBytes.Length == 0)
        {
            return null;
        }

        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var text = DistanceTextBox.Text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            MessageBox.Show(this, "Enter a valid, non-negative distance in light seconds.", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DistanceLs = value;
        UserCorrectedValue = _originalPrefillValue is not double original || Math.Abs(original - value) > 0.005;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
