using System.Collections.ObjectModel;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Domain;
using RotationAnalysis.Core.Spansh;
using RotationAnalysis.Core.Spansh.Models;
using RotationAnalysis.Core.VideoAnalysis;
using RotationAnalysis.Core.VideoAnalysis.SlitScan;

namespace RotationAnalysis.App.ViewModels;

/// <summary>Slit Scan's counterpart to <see cref="LongExposureViewModel"/> - identical
/// system/object selection flow (reusing <see cref="LongExposureTargetParser"/>, since both modes
/// select "a body or station" the same way per spec), but generation takes a
/// <see cref="SlitScanParameters"/> the user configures in a parameter panel between selecting
/// the video and processing it.</summary>
public sealed class SlitScanViewModel : ObservableObject, IDisposable
{
    private readonly SpanshClient _spanshClient = new();

    private string _systemQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _resolvedSystemName;

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

    public ObservableCollection<SlitScanRowViewModel> Targets { get; } = new();

    /// <summary>Raised when the user clicks "Select Video…" on a target row; the view handles the file picker.</summary>
    public event Action<SlitScanRowViewModel>? VideoSelectionRequested;

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
            AppLog.LogError("SlitScan.RefreshSuggestions", ex);
            ErrorMessage = $"Search failed: {ex.Message}";
        }
    }

    public async Task SubmitAsync(SpanshSearchSystem? chosenSystem)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        Targets.Clear();
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

            var targets = LongExposureTargetParser.ExtractTargets(dump);
            ResolvedSystemName = resolved.Name;
            foreach (var target in targets)
            {
                Targets.Add(new SlitScanRowViewModel(target, row => VideoSelectionRequested?.Invoke(row)));
            }

            if (targets.Count == 0)
            {
                ErrorMessage = $"\"{resolved.Name}\" has no bodies or stations.";
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("SlitScan.SystemLookup", ex);
            ErrorMessage = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<SlitScanResult> GenerateAsync(string videoPath, SlitScanParameters parameters, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => SlitScanProcessor.GenerateAsync(videoPath, parameters, progress, ct);

    public void Dispose()
    {
        _spanshClient.Dispose();
    }
}
