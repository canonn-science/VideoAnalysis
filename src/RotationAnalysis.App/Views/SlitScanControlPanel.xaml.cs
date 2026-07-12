using System.Windows;
using System.Windows.Controls;
using RotationAnalysis.Core.VideoAnalysis.SlitScan;

namespace RotationAnalysis.App.Views;

/// <summary>Slit Scan's parameter controls, embedded directly in the tab rather than a modal
/// dialog - built from standard sliders/dropdowns (no custom canvas controls): Static/Sweep/
/// Rotational motion, optional width animation, sampling order, In/Out trim, and independent
/// output sizing. Freeform path motion, audio-reactivity, and a separate scrub-preview renderer
/// are out of scope for this pass.</summary>
public partial class SlitScanControlPanel : UserControl
{
    public SlitScanControlPanel()
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

    private void SlitPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitPositionValueText is not null)
        {
            SlitPositionValueText.Text = $"{e.NewValue:0}%";
        }
    }

    private void SlitWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitWidthValueText is not null)
        {
            SlitWidthValueText.Text = $"{e.NewValue:0} px";
        }
    }

    private void AnimateWidthCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (WidthAnimationPanel is not null)
        {
            WidthAnimationPanel.Visibility = AnimateWidthCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SlitWidthEndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitWidthEndValueText is not null)
        {
            SlitWidthEndValueText.Text = $"{e.NewValue:0} px";
        }
    }

    private void MotionModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (SweepPanel is null || RotationalPanel is null || StaticModeHint is null)
        {
            return; // fires once during InitializeComponent, before the rest of the tree exists
        }

        bool isSweep = ReferenceEquals(sender, SweepModeRadio);
        bool isRotational = ReferenceEquals(sender, RotationalModeRadio);

        StaticModeHint.Visibility = !isSweep && !isRotational ? Visibility.Visible : Visibility.Collapsed;
        SweepPanel.Visibility = isSweep ? Visibility.Visible : Visibility.Collapsed;
        RotationalPanel.Visibility = isRotational ? Visibility.Visible : Visibility.Collapsed;

        // Position doesn't apply to Rotational (which has its own Center/Radius controls).
        if (SlitPositionSlider is not null)
        {
            SlitPositionSlider.IsEnabled = !isRotational;
        }
    }

    private void SweepEndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SweepEndValueText is not null)
        {
            SweepEndValueText.Text = $"{e.NewValue:0}%";
        }
    }

    private void RotationCenterXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationCenterXValueText is not null)
        {
            RotationCenterXValueText.Text = $"{e.NewValue:0}%";
        }
    }

    private void RotationCenterYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationCenterYValueText is not null)
        {
            RotationCenterYValueText.Text = $"{e.NewValue:0}%";
        }
    }

    private void RotationRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationRadiusValueText is not null)
        {
            RotationRadiusValueText.Text = $"{e.NewValue:0}%";
        }
    }

    private void RotationRevolutionsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationRevolutionsValueText is not null)
        {
            RotationRevolutionsValueText.Text = $"{e.NewValue:0.0}";
        }
    }

    private void SamplingOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RandomSeedPanel is null)
        {
            return;
        }

        var tag = (SamplingOrderCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        RandomSeedPanel.Visibility = tag == "Random" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FrameIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FrameIntervalValueText is not null)
        {
            FrameIntervalValueText.Text = e.NewValue <= 1 ? "every frame" : $"every {e.NewValue:0} frames";
        }
    }

    private void InPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InPointValueText is null)
        {
            return;
        }
        InPointValueText.Text = $"{e.NewValue:0}%";
        if (OutPointSlider is not null && OutPointSlider.Value <= e.NewValue)
        {
            OutPointSlider.Value = Math.Min(e.NewValue + 1, OutPointSlider.Maximum);
        }
    }

    private void OutPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OutPointValueText is null)
        {
            return;
        }
        OutPointValueText.Text = $"{e.NewValue:0}%";
        if (InPointSlider is not null && InPointSlider.Value >= e.NewValue)
        {
            InPointSlider.Value = Math.Max(e.NewValue - 1, InPointSlider.Minimum);
        }
    }

    private void ScanSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScanSpeedValueText is not null)
        {
            ScanSpeedValueText.Text = $"{e.NewValue:0} px/frame";
        }
    }

    private void CustomOutputSizeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (OutputSizePanel is not null)
        {
            OutputSizePanel.Visibility = CustomOutputSizeCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void MotionDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlitAngleSlider is null || SamplingOrderCombo is null)
        {
            return; // fires once during InitializeComponent, before the rest of the tree exists
        }

        var tag = (MotionDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        switch (tag)
        {
            case "LeftToRight":
                SlitAngleSlider.Value = 90;
                SelectComboTag(SamplingOrderCombo, "Forward");
                break;
            case "RightToLeft":
                SlitAngleSlider.Value = 90;
                SelectComboTag(SamplingOrderCombo, "Reverse");
                break;
            case "TopToBottom":
                SlitAngleSlider.Value = 0;
                SelectComboTag(SamplingOrderCombo, "Forward");
                break;
            case "BottomToTop":
                SlitAngleSlider.Value = 0;
                SelectComboTag(SamplingOrderCombo, "Reverse");
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

    private static SlitScanEasing ParseEasing(ComboBox combo) => ((combo.SelectedItem as ComboBoxItem)?.Tag as string) switch
    {
        "EaseIn" => SlitScanEasing.EaseIn,
        "EaseOut" => SlitScanEasing.EaseOut,
        "EaseInOut" => SlitScanEasing.EaseInOut,
        _ => SlitScanEasing.Linear,
    };

    /// <summary>Reads the current control values into a <see cref="SlitScanParameters"/>, or
    /// returns null (after showing a validation message) if a text field holds an invalid value.</summary>
    public SlitScanParameters? BuildParameters()
    {
        int? outputWidth = null;
        int? outputHeight = null;
        if (CustomOutputSizeCheckBox.IsChecked == true)
        {
            if (!int.TryParse(OutputWidthTextBox.Text.Trim(), out var width) || width < 10
                || !int.TryParse(OutputHeightTextBox.Text.Trim(), out var height) || height < 10)
            {
                MessageBox.Show(Window.GetWindow(this), "Enter valid output dimensions (at least 10 pixels each).", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            outputWidth = width;
            outputHeight = height;
        }

        int randomSeed = 0;
        var samplingOrderTag = (SamplingOrderCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (samplingOrderTag == "Random" && !int.TryParse(RandomSeedTextBox.Text.Trim(), out randomSeed))
        {
            MessageBox.Show(Window.GetWindow(this), "Enter a valid integer random seed.", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return new SlitScanParameters
        {
            SlitAngleDegrees = SlitAngleSlider.Value,
            SlitPositionFraction = SlitPositionSlider.Value / 100.0,
            SlitWidthPixels = (int)SlitWidthSlider.Value,
            WidthIsAnimated = AnimateWidthCheckBox.IsChecked == true,
            SlitWidthEndPixels = (int)SlitWidthEndSlider.Value,
            WidthEasing = ParseEasing(WidthEasingCombo),

            MotionMode = SweepModeRadio.IsChecked == true
                ? SlitScanMotionMode.Sweep
                : RotationalModeRadio.IsChecked == true
                    ? SlitScanMotionMode.Rotational
                    : SlitScanMotionMode.Static,
            SweepEndPositionFraction = SweepEndSlider.Value / 100.0,
            SweepEasing = ParseEasing(SweepEasingCombo),
            RotationCenterXFraction = RotationCenterXSlider.Value / 100.0,
            RotationCenterYFraction = RotationCenterYSlider.Value / 100.0,
            RotationRadiusFraction = RotationRadiusSlider.Value / 100.0,
            RotationRevolutions = RotationRevolutionsSlider.Value,
            RotationDirection = ((RotationDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string) == "CounterClockwise"
                ? SlitScanRotationDirection.CounterClockwise
                : SlitScanRotationDirection.Clockwise,

            SamplingOrder = samplingOrderTag switch
            {
                "Reverse" => SlitScanSamplingOrder.Reverse,
                "PingPong" => SlitScanSamplingOrder.PingPong,
                "Random" => SlitScanSamplingOrder.Random,
                _ => SlitScanSamplingOrder.Forward,
            },
            RandomSeed = randomSeed,
            FrameSamplingInterval = (int)FrameIntervalSlider.Value,
            InPointFraction = InPointSlider.Value / 100.0,
            OutPointFraction = OutPointSlider.Value / 100.0,

            ScanSpeedPixelsPerFrame = (int)ScanSpeedSlider.Value,
            BlendMode = ((BlendModeCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Lighten" => SlitScanBlendMode.Lighten,
                "Average" => SlitScanBlendMode.Average,
                _ => SlitScanBlendMode.Normal,
            },

            CustomOutputSize = outputWidth is not null,
            OutputWidth = outputWidth ?? 1920,
            OutputHeight = outputHeight ?? 1080,
            Interpolation = ((InterpolationCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Nearest" => SlitScanInterpolation.Nearest,
                "Linear" => SlitScanInterpolation.Linear,
                _ => SlitScanInterpolation.Cubic,
            },
        };
    }

    /// <summary>Restores every control to its default value, so the user can try again from a
    /// clean slate without re-uploading the video.</summary>
    public void ResetToDefaults()
    {
        SelectComboTag(MotionDirectionCombo, "None");
        SlitAngleSlider.Value = 90;
        SlitPositionSlider.Value = 50;
        SlitWidthSlider.Value = 2;
        // Same no-op-if-already-false caveat as the radio buttons below - set explicitly.
        AnimateWidthCheckBox.IsChecked = false;
        WidthAnimationPanel.Visibility = Visibility.Collapsed;
        SlitWidthEndSlider.Value = 2;
        SelectComboTag(WidthEasingCombo, "Linear");

        // Setting IsChecked=true is a no-op (and won't fire the Checked handler) if it's already
        // checked, so the panel visibility is reset explicitly rather than relying on the event.
        StaticModeRadio.IsChecked = true;
        StaticModeHint.Visibility = Visibility.Visible;
        SweepPanel.Visibility = Visibility.Collapsed;
        RotationalPanel.Visibility = Visibility.Collapsed;
        SlitPositionSlider.IsEnabled = true;
        SweepEndSlider.Value = 50;
        SelectComboTag(SweepEasingCombo, "Linear");
        RotationCenterXSlider.Value = 50;
        RotationCenterYSlider.Value = 50;
        RotationRadiusSlider.Value = 50;
        RotationRevolutionsSlider.Value = 1.0;
        SelectComboTag(RotationDirectionCombo, "Clockwise");

        SelectComboTag(SamplingOrderCombo, "Forward");
        RandomSeedPanel.Visibility = Visibility.Collapsed;
        RandomSeedTextBox.Text = "0";
        FrameIntervalSlider.Value = 1;
        InPointSlider.Value = 0;
        OutPointSlider.Value = 100;

        ScanSpeedSlider.Value = 2;
        SelectComboTag(BlendModeCombo, "Normal");

        CustomOutputSizeCheckBox.IsChecked = false;
        OutputSizePanel.Visibility = Visibility.Collapsed;
        OutputWidthTextBox.Text = "1920";
        OutputHeightTextBox.Text = "1080";
        SelectComboTag(InterpolationCombo, "Cubic");
    }
}
