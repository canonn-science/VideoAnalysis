using System.Windows;
using System.Windows.Controls;
using VideoAnalysis.App.Views;
using VideoAnalysis.Core.VideoAnalysis.SlitScan;
using Xunit;

namespace VideoAnalysis.App.Tests;

/// <summary>Covers the "Preset" picker added to <c>SlitScanControlPanel</c> (GitHub issue #38):
/// selecting a result preset should populate a full bundle of controls, leave everything else at
/// its default, and stay editable afterward. Does not exercise <c>SlitScanProcessor</c> itself -
/// that's covered separately and is comparatively slow since it processes real frames.</summary>
public class SlitScanPresetTests : IClassFixture<SlitScanPanelFixture>
{
    private readonly SlitScanPanelFixture _fixture;

    public SlitScanPresetTests(SlitScanPanelFixture fixture) => _fixture = fixture;

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (string?)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        throw new InvalidOperationException($"No ComboBoxItem with Tag '{tag}' found.");
    }

    private SlitScanParameters BuildWithPreset(string presetTag) => _fixture.Sta.Invoke(() =>
    {
        var panel = new SlitScanControlPanel();
        SelectByTag(panel.PresetCombo, presetTag);
        return panel.BuildParameters()!;
    });

    [Fact]
    public void Panorama_SetsSeamlessPushBroomBundle()
    {
        var p = BuildWithPreset("Panorama");
        Assert.Equal(SlitScanMotionMode.Static, p.MotionMode);
        Assert.Equal(90, p.SlitAngleDegrees);
        Assert.Equal(0.5, p.SlitPositionFraction, 3);
        Assert.Equal(2, p.SlitWidthPixels);
        Assert.Equal(2, p.ScanSpeedPixelsPerFrame);
        Assert.Equal(1, p.FrameSamplingInterval);
        Assert.False(p.CustomOutputSize);
    }

    [Fact]
    public void MovieBarcode_SetsFixedOutputSizeAndNarrowWidth()
    {
        var p = BuildWithPreset("MovieBarcode");
        Assert.Equal(SlitScanMotionMode.Static, p.MotionMode);
        Assert.Equal(1, p.SlitWidthPixels);
        Assert.Equal(1, p.ScanSpeedPixelsPerFrame);
        Assert.True(p.CustomOutputSize);
        Assert.Equal(1920, p.OutputWidth);
        Assert.Equal(1080, p.OutputHeight);
    }

    [Fact]
    public void PhotoFinish_MatchesPanoramaWidthSpeedRatio()
    {
        var p = BuildWithPreset("PhotoFinish");
        Assert.Equal(SlitScanMotionMode.Static, p.MotionMode);
        Assert.Equal(p.SlitWidthPixels, p.ScanSpeedPixelsPerFrame);
        Assert.Equal(2, p.SlitWidthPixels);
    }

    [Fact]
    public void StarTrailVortex_SetsSlowWideLockedAverageRotation()
    {
        var p = BuildWithPreset("StarTrailVortex");
        Assert.Equal(SlitScanMotionMode.Rotational, p.MotionMode);
        Assert.Equal(0.5, p.RotationCenterXFraction, 3);
        Assert.Equal(0.5, p.RotationCenterYFraction, 3);
        Assert.Equal(0.9, p.RotationRadiusFraction, 3);
        Assert.Equal(0.25, p.RotationRevolutions, 3);
        Assert.Equal(SlitScanBlendMode.Average, p.BlendMode);
    }

    [Fact]
    public void FastSpiral_SetsManyRevolutionsNarrowNormalBlend()
    {
        var p = BuildWithPreset("FastSpiral");
        Assert.Equal(SlitScanMotionMode.Rotational, p.MotionMode);
        Assert.Equal(6, p.RotationRevolutions, 3);
        Assert.Equal(1, p.SlitWidthPixels);
        Assert.Equal(SlitScanBlendMode.Normal, p.BlendMode);
    }

    [Fact]
    public void SlowVortex_SetsSubOneRevolutionWideAverageBlend()
    {
        var p = BuildWithPreset("SlowVortex");
        Assert.Equal(SlitScanMotionMode.Rotational, p.MotionMode);
        Assert.True(p.RotationRevolutions <= 1.0);
        Assert.Equal(10, p.SlitWidthPixels);
        Assert.Equal(SlitScanBlendMode.Average, p.BlendMode);
    }

    [Fact]
    public void PortraitWarp_SetsFullSweepWithEaseInOut()
    {
        var p = BuildWithPreset("PortraitWarp");
        Assert.Equal(SlitScanMotionMode.Sweep, p.MotionMode);
        Assert.Equal(0.0, p.SlitPositionFraction, 3);
        Assert.Equal(1.0, p.SweepEndPositionFraction, 3);
        Assert.Equal(SlitScanEasing.EaseInOut, p.SweepEasing);
        Assert.Equal(2, p.SlitWidthPixels);
    }

    [Fact]
    public void GhostTrail_SetsWideOverlappingSlowAverageBlend()
    {
        var p = BuildWithPreset("GhostTrail");
        Assert.Equal(SlitScanMotionMode.Static, p.MotionMode);
        Assert.Equal(12, p.SlitWidthPixels);
        Assert.Equal(1, p.ScanSpeedPixelsPerFrame);
        Assert.Equal(SlitScanBlendMode.Average, p.BlendMode);
        Assert.True(p.ScanSpeedPixelsPerFrame < p.SlitWidthPixels);
    }

    [Fact]
    public void Glitch_SetsSharpEasingAndAnimatedWidth()
    {
        var p = BuildWithPreset("Glitch");
        Assert.Equal(SlitScanMotionMode.Sweep, p.MotionMode);
        Assert.Equal(SlitScanEasing.EaseIn, p.SweepEasing);
        Assert.True(p.WidthIsAnimated);
        Assert.Equal(2, p.SlitWidthPixels);
        Assert.Equal(14, p.SlitWidthEndPixels);
        Assert.Equal(SlitScanEasing.EaseIn, p.WidthEasing);
    }

    [Fact]
    public void VerticalPanorama_MatchesPanoramaExceptAngle()
    {
        var panorama = BuildWithPreset("Panorama");
        var vertical = BuildWithPreset("VerticalPanorama");

        Assert.Equal(0, vertical.SlitAngleDegrees);
        Assert.Equal(panorama.SlitWidthPixels, vertical.SlitWidthPixels);
        Assert.Equal(panorama.ScanSpeedPixelsPerFrame, vertical.ScanSpeedPixelsPerFrame);
        Assert.Equal(panorama.MotionMode, vertical.MotionMode);
    }

    [Fact]
    public void SelectingNone_LeavesFieldsAtPlainDefaults()
    {
        var p = _fixture.Sta.Invoke(() =>
        {
            var panel = new SlitScanControlPanel();
            // PresetCombo already defaults to "None" - nothing to select.
            return panel.BuildParameters()!;
        });

        Assert.Equal(SlitScanMotionMode.Static, p.MotionMode);
        Assert.Equal(90, p.SlitAngleDegrees);
        Assert.Equal(2, p.SlitWidthPixels);
        Assert.Equal(2, p.ScanSpeedPixelsPerFrame);
        Assert.False(p.CustomOutputSize);
    }

    [Fact]
    public void PresetFieldsRemainEditableAfterSelection()
    {
        var p = _fixture.Sta.Invoke(() =>
        {
            var panel = new SlitScanControlPanel();
            SelectByTag(panel.PresetCombo, "Panorama");
            panel.SlitWidthSlider.Value = 25;
            return panel.BuildParameters()!;
        });

        Assert.Equal(25, p.SlitWidthPixels);
    }

    [Fact]
    public void ResetToDefaults_ClearsPresetSelectionBackToNone()
    {
        var (selectedTag, hintVisible) = _fixture.Sta.Invoke(() =>
        {
            var panel = new SlitScanControlPanel();
            SelectByTag(panel.PresetCombo, "FastSpiral");
            panel.ResetToDefaults();
            var tag = (panel.PresetCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            return (tag, panel.PresetHintText.Visibility == Visibility.Visible);
        });

        Assert.Equal("None", selectedTag);
        Assert.False(hintVisible);
    }
}
