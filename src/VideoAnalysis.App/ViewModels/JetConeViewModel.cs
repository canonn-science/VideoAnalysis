using System.Collections.ObjectModel;
using System.IO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Domain;
using VideoAnalysis.Core.Spansh;
using VideoAnalysis.Core.Spansh.Models;
using VideoAnalysis.Core.Storage;
using VideoAnalysis.Core.VideoAnalysis;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Jet Cone Length's counterpart to <see cref="MainViewModel"/>/<see cref="StationViewModel"/>.
/// The video comes from the shared library selection (see <see cref="VideoLibraryViewModel.EntrySelected"/>),
/// same as Slit Scan/Long Exposure - but unlike those modes, a measurement still needs a specific
/// neutron star/white dwarf's full physical data (age, mass, orbital elements, ...) for the CSV,
/// which only Spansh has, so the system search and target picker (<see cref="JetTargetParser"/>)
/// stay, just auto-resolved from the selected video's tagged system where possible instead of
/// being the primary way to pick a video.</summary>
public sealed class JetConeViewModel : ObservableObject, IDisposable
{
    private readonly SpanshClient _spanshClient = new();
    private readonly JetLengthCsvStore _jetLengthStore = new();
    private readonly SecretStore _secretStore = new();

    public JetConeViewModel()
    {
        Measurements = new JetLengthMeasurementsViewModel(_jetLengthStore);
    }

    private string _systemQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _resolvedSystemName;
    private string? _videoFilePath;
    private JetConeRowViewModel? _selectedTarget;
    private JetConeAnalysisResult? _result;
    private double? _originalPrefillDistanceLs;
    private double? _distanceLs;
    private string? _distanceSourceLabel;

    public string SystemQuery
    {
        get => _systemQuery;
        set => SetField(ref _systemQuery, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string? ResolvedSystemName
    {
        get => _resolvedSystemName;
        set => SetField(ref _resolvedSystemName, value);
    }

    public string? VideoFilePath
    {
        get => _videoFilePath;
        set
        {
            if (SetField(ref _videoFilePath, value))
            {
                OnPropertyChanged(nameof(VideoFileName));
                OnPropertyChanged(nameof(HasVideo));
                OnPropertyChanged(nameof(CanAnalyze));
            }
        }
    }

    public string? VideoFileName => VideoFilePath is null ? null : Path.GetFileName(VideoFilePath);

    public bool HasVideo => VideoFilePath is not null;

    public ObservableCollection<SpanshSearchSystem> Suggestions { get; } = new();

    public ObservableCollection<JetConeRowViewModel> Targets { get; } = new();

    public JetConeRowViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetField(ref _selectedTarget, value))
            {
                if (value is not null)
                {
                    // Picking a target resolves whatever prompted the user in the first place (no
                    // body tagged, a stale/mismatched one, ...) - don't leave that message lingering.
                    ErrorMessage = null;
                }

                OnPropertyChanged(nameof(CanAnalyze));
            }
        }
    }

    /// <summary>Analyzing requires both a video (from the library selection) and a resolved
    /// system/body target (see <see cref="SubmitAsync"/>) - the Analyze button is disabled until
    /// both are in place instead of only failing with an error message after the fact.</summary>
    public bool CanAnalyze => HasVideo && SelectedTarget is not null;

    public JetLengthMeasurementsViewModel Measurements { get; }

    /// <summary>The most recently analyzed onset crop, shown inline for review rather than in a
    /// separate dialog - null before the first analysis (or after selecting a different video,
    /// since a stale result no longer corresponds to what's loaded).</summary>
    public JetConeAnalysisResult? Result
    {
        get => _result;
        set
        {
            if (SetField(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => Result is not null;

    /// <summary>The editable distance value shown in the inline confirmation form - prefilled from
    /// the local (or Claude) reading via <see cref="BeginReview"/>, but never saved without the
    /// user seeing and confirming it first.</summary>
    public double? DistanceLs
    {
        get => _distanceLs;
        set => SetField(ref _distanceLs, value);
    }

    public string? DistanceSourceLabel
    {
        get => _distanceSourceLabel;
        set => SetField(ref _distanceSourceLabel, value);
    }

    public bool HasClaudeApiKey => _secretStore.TryGetClaudeApiKey(out _);

    public void SetClaudeApiKey(string apiKey) => _secretStore.SetClaudeApiKey(apiKey);

    /// <summary>Calls the Claude vision fallback with the stored key. Throws if no key is
    /// configured - callers should check <see cref="HasClaudeApiKey"/> first.</summary>
    public async Task<ClaudeVisionDistanceReader.DistanceReading> ReadDistanceWithClaudeAsync(byte[] cropPngBytes, CancellationToken ct = default)
    {
        if (!_secretStore.TryGetClaudeApiKey(out var apiKey))
        {
            throw new InvalidOperationException("No Claude API key is configured.");
        }

        using var reader = new ClaudeVisionDistanceReader(apiKey);
        return await reader.ReadDistanceAsync(cropPngBytes, ct).ConfigureAwait(true);
    }

    public async Task RefreshSuggestionsAsync(string query, CancellationToken ct)
    {
        if (query.Length < 3)
        {
            Suggestions.Clear();
            return;
        }

        try
        {
            var response = await _spanshClient.SearchSystemsAsync(query, ct).ConfigureAwait(true);
            Suggestions.Clear();
            foreach (var system in response.MinMax)
            {
                Suggestions.Add(system);
            }
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke; ignore
        }
        catch (Exception ex)
        {
            AppLog.LogError("JetCone.RefreshSuggestions", ex);
            ErrorMessage = $"Search failed: {ex.Message}";
        }
    }

    /// <summary>Resolves a system and populates <see cref="Targets"/> with its neutron stars and
    /// white dwarfs. <paramref name="preferredBodyName"/> auto-selects the matching target (e.g.
    /// the selected library video's tagged body) once the list is populated, if present.</summary>
    public async Task SubmitAsync(SpanshSearchSystem? chosenSystem, string? preferredBodyName = null)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        Targets.Clear();
        SelectedTarget = null;
        IsBusy = true;
        try
        {
            var resolved = chosenSystem;
            if (resolved is null)
            {
                var query = SystemQuery.Trim();
                if (query.Length == 0)
                {
                    ErrorMessage = "Enter a system name.";
                    return;
                }

                var response = await _spanshClient.SearchSystemsAsync(query).ConfigureAwait(true);
                resolved = response.MinMax.FirstOrDefault(s => string.Equals(s.Name, query, StringComparison.OrdinalIgnoreCase));
                if (resolved is null)
                {
                    ErrorMessage = $"System \"{query}\" not found.";
                    return;
                }
            }

            var dump = await _spanshClient.GetDumpAsync(resolved.Id64).ConfigureAwait(true);
            if (dump is null)
            {
                ErrorMessage = $"System \"{resolved.Name}\" not found.";
                return;
            }

            var targets = JetTargetParser.ExtractTargets(dump);
            ResolvedSystemName = resolved.Name;
            SystemQuery = resolved.Name;
            foreach (var target in targets)
            {
                Targets.Add(new JetConeRowViewModel(target));
            }

            if (targets.Count == 0)
            {
                ErrorMessage = $"\"{resolved.Name}\" has no neutron stars or white dwarfs.";
            }
            else
            {
                if (preferredBodyName is not null)
                {
                    SelectedTarget = Targets.FirstOrDefault(t => t.BodyName == preferredBodyName);
                }

                // A measurement can't be saved without a target - prompt for one now rather than
                // letting the user discover it's missing only after analyzing the video.
                if (SelectedTarget is null)
                {
                    ErrorMessage = preferredBodyName is null
                        ? "This video isn't tagged with a body - pick the neutron star or white dwarf it shows from the dropdown below."
                        : $"This video's tagged body (\"{preferredBodyName}\") wasn't found in {resolved.Name} - pick the correct one from the dropdown below.";
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("JetCone.SystemLookup", ex);
            ErrorMessage = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<JetConeAnalysisResult> AnalyzeVideoAsync(string videoPath, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => JetConeAnalyzer.AnalyzeAsync(videoPath, progress, ct);

    /// <summary>A single representative frame (PNG bytes) for the "selected video" preview - null
    /// if there's no video yet or the frame couldn't be read.</summary>
    public Task<byte[]?> LoadPreviewFrameAsync(CancellationToken ct)
        => VideoFilePath is null
            ? Task.FromResult<byte[]?>(null)
            : VideoFrameReader.ReadRepresentativeFrameAsync(VideoFilePath, ct);

    /// <summary>Populates the inline review form from a completed analysis - call once per
    /// analysis, before showing the crop images/distance field.</summary>
    public void BeginReview(JetConeAnalysisResult result, double? prefillDistanceLs, string sourceLabel)
    {
        Result = result;
        _originalPrefillDistanceLs = prefillDistanceLs;
        DistanceLs = prefillDistanceLs;
        DistanceSourceLabel = sourceLabel;
    }

    /// <summary>True if the user changed <see cref="DistanceLs"/> from what was originally
    /// prefilled - the caller uses this to decide whether to offer setting up the Claude API key.</summary>
    public bool DistanceWasCorrected =>
        _originalPrefillDistanceLs is not double original || DistanceLs is not double current || Math.Abs(original - current) > 0.005;

    /// <summary>Clears the in-progress review state - called when a fresh video is selected so a
    /// stale crop/result doesn't linger on screen.</summary>
    public void ResetReview()
    {
        Result = null;
        _originalPrefillDistanceLs = null;
        DistanceLs = null;
        DistanceSourceLabel = null;
    }

    /// <summary>Requires <see cref="SelectedTarget"/> and <see cref="DistanceLs"/> to already be
    /// set - callers should only enable the Save action once both are available.</summary>
    public void SaveMeasurement()
    {
        if (SelectedTarget is not { } selectedTarget || DistanceLs is not double distance)
        {
            throw new InvalidOperationException("A target and confirmed distance are required to save a measurement.");
        }

        var target = selectedTarget.Target;
        _jetLengthStore.Append(new JetLengthRecord
        {
            SystemName = target.SystemName,
            BodyName = target.BodyName,
            Distance = distance,
            AbsoluteMagnitude = target.AbsoluteMagnitude,
            Age = target.Age,
            ArgOfPeriapsis = target.ArgOfPeriapsis,
            AscendingNode = target.AscendingNode,
            AxialTilt = target.AxialTilt,
            BodyId = target.BodyId,
            DistanceToArrival = target.DistanceToArrival,
            Luminosity = target.Luminosity,
            MainStar = target.MainStar,
            MeanAnomaly = target.MeanAnomaly,
            OrbitalEccentricity = target.OrbitalEccentricity,
            OrbitalInclination = target.OrbitalInclination,
            OrbitalPeriod = target.OrbitalPeriod,
            RotationalPeriod = target.RotationalPeriod,
            SemiMajorAxis = target.SemiMajorAxis,
            SolarMasses = target.SolarMasses,
            SolarRadius = target.SolarRadius,
            SpectralClass = target.SpectralClass,
            SurfaceTemperature = target.SurfaceTemperature,
            UpdateTime = target.UpdateTime,
        });
        Measurements.Refresh();
    }

    public string CsvPath => _jetLengthStore.CsvPath;

    public void Dispose()
    {
        _spanshClient.Dispose();
    }
}
