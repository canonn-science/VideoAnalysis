using System.Collections.ObjectModel;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Domain;
using RotationAnalysis.Core.Spansh;
using RotationAnalysis.Core.Spansh.Models;
using RotationAnalysis.Core.Storage;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SpanshClient _spanshClient = new();
    private readonly MeasurementCsvStore _measurementStore = new();

    private string _systemQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _resolvedSystemName;

    public MainViewModel()
    {
        Measurements = new MeasurementsViewModel(_measurementStore);
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

    public ObservableCollection<SpanshSearchSystem> Suggestions { get; } = new();

    public ObservableCollection<RingRowViewModel> Rings { get; } = new();

    public MeasurementsViewModel Measurements { get; }

    /// <summary>Raised when the user clicks "Select Video…" on a ring row; the view handles the file picker.</summary>
    public event Action<RingRowViewModel>? VideoSelectionRequested;

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
            ErrorMessage = $"Search failed: {ex.Message}";
        }
    }

    public async Task SubmitAsync(SpanshSearchSystem? chosenSystem)
    {
        if (IsBusy)
        {
            // Guards against ModernWpf's AutoSuggestBox raising both SuggestionChosen and
            // QuerySubmitted for a single suggestion click, which would otherwise run this
            // method twice concurrently and duplicate every row in the Rings collection.
            return;
        }

        ErrorMessage = null;
        Rings.Clear();
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

            var rings = SystemParser.ExtractRings(dump);
            ResolvedSystemName = resolved.Name;
            foreach (var ring in rings)
            {
                Rings.Add(new RingRowViewModel(ring, row => VideoSelectionRequested?.Invoke(row)));
            }

            if (rings.Count == 0)
            {
                ErrorMessage = $"\"{resolved.Name}\" has no rings or belts.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<HorizontalVideoAnalysisResult> AnalyzeVideoAsync(string videoPath, double? seedPeriodSeconds, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => HorizontalVideoAnalyzer.AnalyzeAsync(videoPath, seedPeriodSeconds, progress, ct);

    public void SaveMeasurement(RingRowViewModel row, HorizontalVideoAnalysisResult result, string videoPath)
    {
        var ring = row.Ring;
        _measurementStore.Append(new MeasurementRecord
        {
            Timestamp = DateTime.UtcNow,
            SystemName = ring.SystemName,
            Id64 = ring.SystemId64,
            X = ring.SystemX,
            Y = ring.SystemY,
            Z = ring.SystemZ,
            RingName = ring.RingName,
            InnerRadius = ring.InnerRadiusMeters,
            OuterRadius = ring.OuterRadiusMeters,
            Width = ring.WidthMeters,
            EstimatedRotationSeconds = ring.EstimatedPeriodSeconds ?? double.NaN,
            ObservedRotationSeconds = result.ObservedPeriodSeconds,
            VideoFilename = videoPath,
        });
        Measurements.Refresh();
    }
}
