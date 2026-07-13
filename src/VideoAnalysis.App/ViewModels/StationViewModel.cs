using System.Collections.ObjectModel;
using System.IO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Domain;
using VideoAnalysis.Core.Reference;
using VideoAnalysis.Core.Spansh;
using VideoAnalysis.Core.Spansh.Models;
using VideoAnalysis.Core.Storage;
using VideoAnalysis.Core.VideoAnalysis;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Station Rotation's counterpart to <see cref="MainViewModel"/>. Uses only
/// <see cref="SpanshClient"/> (same system search and same dump endpoint Ring Rotation already
/// uses) - no other data source. Owns its own video-analysis/save flow rather than extending
/// <see cref="MainViewModel"/>, mirroring how <see cref="MeasurementsViewModel"/> is already a
/// separate view model rather than bloating the main one. The video comes from the shared library
/// selection (see <see cref="VideoLibraryViewModel.EntrySelected"/>), same as Jet Cone/Slit Scan/
/// Long Exposure, with the system/station auto-resolved from the video's tagged system where
/// possible.</summary>
public sealed class StationViewModel : ObservableObject, IDisposable
{
    private readonly SpanshClient _spanshClient = new();
    private readonly GuardianBeaconClient _guardianBeaconClient = new();
    private readonly StationMeasurementCsvStore _measurementStore = new();

    private List<GuardianBeaconEntry>? _allBeacons;

    private string _systemQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _resolvedSystemName;
    private string? _videoFilePath;
    private StationRowViewModel? _selectedStation;

    public StationViewModel()
    {
        Measurements = new StationMeasurementsViewModel(_measurementStore);
    }

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

    public ObservableCollection<StationRowViewModel> Stations { get; } = new();

    public StationRowViewModel? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (SetField(ref _selectedStation, value))
            {
                if (value is not null)
                {
                    // Picking a station resolves whatever prompted the user in the first place
                    // (none tagged, a stale/mismatched one, ...) - don't leave that lingering.
                    ErrorMessage = null;
                }

                OnPropertyChanged(nameof(CanAnalyze));
                OnPropertyChanged(nameof(HasSelectedStation));
            }
        }
    }

    /// <summary>Whether the selected station's details summary should be shown.</summary>
    public bool HasSelectedStation => SelectedStation is not null;

    /// <summary>Analyzing requires both a video (from the library selection) and a resolved
    /// station - the Analyze button is disabled until both are in place instead of only failing
    /// with an error message (or falling back to a file picker) after the fact.</summary>
    public bool CanAnalyze => HasVideo && SelectedStation is not null;

    public StationMeasurementsViewModel Measurements { get; }

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
            AppLog.LogError("Station.RefreshSuggestions", ex);
            ErrorMessage = $"Search failed: {ex.Message}";
        }
    }

    /// <summary>Resolves a system and populates <see cref="Stations"/>. <paramref name="preferredStationName"/>
    /// auto-selects the matching row (e.g. the selected library video's tagged station) once the
    /// list is populated, if present.</summary>
    public async Task SubmitAsync(SpanshSearchSystem? chosenSystem, string? preferredStationName = null)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        Stations.Clear();
        SelectedStation = null;
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

            _allBeacons ??= await _guardianBeaconClient.GetBeaconsAsync().ConfigureAwait(true);

            var dump = await _spanshClient.GetDumpAsync(resolved.Id64).ConfigureAwait(true);
            if (dump is null)
            {
                ErrorMessage = $"System \"{resolved.Name}\" not found.";
                return;
            }

            var beaconsInSystem = _allBeacons
                .Where(b => string.Equals(b.SystemName, resolved.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var stations = StationParser.ExtractStations(dump, beaconsInSystem);

            ResolvedSystemName = resolved.Name;
            SystemQuery = resolved.Name;
            foreach (var station in stations)
            {
                Stations.Add(new StationRowViewModel(station));
            }

            if (stations.Count == 0)
            {
                ErrorMessage = $"\"{resolved.Name}\" has no stations, installations, or Guardian Beacons.";
            }
            else
            {
                if (preferredStationName is not null)
                {
                    SelectedStation = Stations.FirstOrDefault(s => s.StationName == preferredStationName);
                }

                // A measurement can't be analyzed without a selected station - prompt for one now
                // rather than letting the user discover it's missing only after loading a video.
                if (SelectedStation is null)
                {
                    ErrorMessage = preferredStationName is null
                        ? "This video isn't tagged with a station - pick the one it shows from the grid below."
                        : $"This video's tagged station (\"{preferredStationName}\") wasn't found in {resolved.Name} - pick the correct one from the grid below.";
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("Station.SystemLookup", ex);
            ErrorMessage = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<HorizontalVideoAnalysisResult> AnalyzeVideoAsync(string videoPath, double? seedPeriodSeconds, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => HorizontalVideoAnalyzer.AnalyzeAsync(videoPath, seedPeriodSeconds, progress, ct);

    public void SaveMeasurement(StationRowViewModel row, HorizontalVideoAnalysisResult result, string videoPath)
    {
        var station = row.Station;
        _measurementStore.Append(new StationMeasurementRecord
        {
            Timestamp = DateTime.UtcNow,
            SystemName = station.SystemName,
            StationName = station.StationName,
            Id64 = station.SystemId64,
            X = station.SystemX,
            Y = station.SystemY,
            Z = station.SystemZ,
            BodyName = station.BodyName ?? string.Empty,
            BodyType = station.BodyType ?? string.Empty,
            BodyMassEarthMasses = station.BodyMassEarthMasses,
            BodyRadiusKm = station.BodyRadiusKm,
            BodyInclinationDegrees = station.BodyInclinationDegrees,
            EstimatedRotationSeconds = station.EstimatedRotationSeconds ?? double.NaN,
            ObservedRotationSeconds = result.ObservedPeriodSeconds,
            MeasuredPeriodSeconds = result.ObservedPeriodSeconds,
            Submitted = false,
            VideoFilename = Path.GetFileName(videoPath),
        });
        Measurements.Refresh();
    }

    public void Dispose()
    {
        _spanshClient.Dispose();
        _guardianBeaconClient.Dispose();
    }
}
