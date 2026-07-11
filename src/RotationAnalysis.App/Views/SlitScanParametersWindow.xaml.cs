using System.Windows;
using System.Windows.Controls;
using RotationAnalysis.Core.VideoAnalysis.SlitScan;

namespace RotationAnalysis.App.Views;

/// <summary>Compact parameter panel for Slit Scan - deliberately simple sliders/dropdowns rather
/// than a wizard, per spec ("simple but enough for creative experimentation").</summary>
public partial class SlitScanParametersWindow : Window
{
    public SlitScanParameters? Parameters { get; private set; }

    public SlitScanParametersWindow()
    {
        InitializeComponent();
    }

    private void SlitAngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitAngleValueText is not null)
        {
            SlitAngleValueText.Text = $"{e.NewValue:0}°";
        }
    }

    private void SlitWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitWidthValueText is not null)
        {
            SlitWidthValueText.Text = $"{e.NewValue:0} px";
        }
    }

    private void SlitPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitPositionValueText is not null)
        {
            SlitPositionValueText.Text = $"{e.NewValue:0}%";
        }
    }

    private void ScanSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScanSpeedValueText is not null)
        {
            ScanSpeedValueText.Text = $"{e.NewValue:0} px/frame";
        }
    }

    private void FrameIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FrameIntervalValueText is not null)
        {
            FrameIntervalValueText.Text = e.NewValue <= 1 ? "every frame" : $"every {e.NewValue:0} frames";
        }
    }

    private void LimitWidthCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (MaxWidthTextBox is not null)
        {
            MaxWidthTextBox.IsEnabled = LimitWidthCheckBox.IsChecked == true;
        }
    }

    private void MotionDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlitAngleSlider is null || ScanDirectionCombo is null)
        {
            return; // fires once during InitializeComponent, before the rest of the tree exists
        }

        var tag = (MotionDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        switch (tag)
        {
            case "LeftToRight":
                SlitAngleSlider.Value = 90;
                SelectComboTag(ScanDirectionCombo, "Forward");
                break;
            case "RightToLeft":
                SlitAngleSlider.Value = 90;
                SelectComboTag(ScanDirectionCombo, "Reverse");
                break;
            case "TopToBottom":
                SlitAngleSlider.Value = 0;
                SelectComboTag(ScanDirectionCombo, "Forward");
                break;
            case "BottomToTop":
                SlitAngleSlider.Value = 0;
                SelectComboTag(ScanDirectionCombo, "Reverse");
                break;
        }
    }

    private static void SelectComboTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (string)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        int? maxWidth = null;
        if (LimitWidthCheckBox.IsChecked == true)
        {
            if (!int.TryParse(MaxWidthTextBox.Text.Trim(), out var parsed) || parsed < 10)
            {
                MessageBox.Show(this, "Enter a valid maximum width (at least 10 pixels).", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            maxWidth = parsed;
        }

        Parameters = new SlitScanParameters
        {
            SlitAngleDegrees = SlitAngleSlider.Value,
            SlitWidthPixels = (int)SlitWidthSlider.Value,
            SlitPositionFraction = SlitPositionSlider.Value / 100.0,
            ScanDirection = ((ScanDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string) == "Reverse"
                ? SlitScanDirection.Reverse
                : SlitScanDirection.Forward,
            ScanSpeedPixelsPerFrame = (int)ScanSpeedSlider.Value,
            FrameSamplingInterval = (int)FrameIntervalSlider.Value,
            MaxOutputWidth = maxWidth,
            BlendMode = ((BlendModeCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Lighten" => SlitScanBlendMode.Lighten,
                "Average" => SlitScanBlendMode.Average,
                _ => SlitScanBlendMode.Normal,
            },
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
