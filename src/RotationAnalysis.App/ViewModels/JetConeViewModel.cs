using System.Collections.ObjectModel;
using System.IO;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Domain;
using RotationAnalysis.Core.Spansh;
using RotationAnalysis.Core.Spansh.Models;
using RotationAnalysis.Core.Storage;
using RotationAnalysis.Core.VideoAnalysis;

namespace RotationAnalysis.App.ViewModels;

/// <summary>Jet Cone Length's counterpart to <see cref="MainViewModel"/>/<see cref="StationViewModel"/>.
/// System search is the same Spansh-backed flow as the other modes; the object list is filtered to
/// neutron stars and white dwarfs (<see cref="JetTargetParser"/>) instead of rings or stations, and
/// there's no estimated-rotation/suggested-video-length concept - the user free-flies and records
/// themselves.</summary>
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

    public ObservableCollection<JetConeRowViewModel> Targets { get; } = new();

    public JetLengthMeasurementsViewModel Measurements { get; }

    /// <summary>Raised when the user clicks "Select Video…" on a target row; the view handles the file picker.</summary>
    public event Action<JetConeRowViewModel>? VideoSelectionRequested;

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

            var targets = JetTargetParser.ExtractTargets(dump);
            ResolvedSystemName = resolved.Name;
            foreach (var target in targets)
            {
                Targets.Add(new JetConeRowViewModel(target, row => VideoSelectionRequested?.Invoke(row)));
            }

            if (targets.Count == 0)
            {
                ErrorMessage = $"\"{resolved.Name}\" has no neutron stars or white dwarfs.";
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

    public void SaveMeasurement(JetConeRowViewModel row, double distanceLs)
    {
        var target = row.Target;
        _jetLengthStore.Append(new JetLengthRecord
        {
            SystemName = target.SystemName,
            BodyName = target.BodyName,
            Distance = distanceLs,
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
