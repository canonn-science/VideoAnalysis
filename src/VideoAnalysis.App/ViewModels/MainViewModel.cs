using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Canonn;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Domain;
using VideoAnalysis.Core.Journal;
using VideoAnalysis.Core.Spansh;
using VideoAnalysis.Core.Spansh.Models;
using VideoAnalysis.Core.Storage;
using VideoAnalysis.Core.VideoAnalysis;

namespace VideoAnalysis.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const string DefaultCommanderName = "CMDR Your Name Here";

    private readonly SpanshClient _spanshClient = new();
    private readonly CanonnClient _canonnClient = new();
    private readonly MeasurementCsvStore _measurementStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly SecretStore _secretStore = new();
    private readonly JournalMonitor _journalMonitor = new();

    private string _systemQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _resolvedSystemName;
    private string _commanderName;
    private bool _monitorJournals;
    private bool _overrideUsername;
    private bool _hasClaudeApiKey;

    public MainViewModel()
    {
        var settings = _settingsStore.Load();
        _commanderName = settings.CommanderName ?? DefaultCommanderName;
        _monitorJournals = settings.MonitorJournals;
        _overrideUsername = settings.OverrideUsername;
        Measurements = new MeasurementsViewModel(_measurementStore, SubmitRecordToCanonnAsync, () => CommanderName);
        Stations = new StationViewModel();
        JetCone = new JetConeViewModel();
        LongExposure = new LongExposureViewModel();
        SlitScan = new SlitScanViewModel();
        _hasClaudeApiKey = _secretStore.TryGetClaudeApiKey(out _);
        _journalMonitor.CommanderNameChanged += OnJournalCommanderNameChanged;
        if (_monitorJournals)
        {
            _journalMonitor.Start();
        }
        _ = LoadSubmittedFromCanonnAsync();
    }

    public string SystemQuery
    {
        get => _systemQuery;
        set => SetField(ref _systemQuery, value);
    }

    public string CommanderName
    {
        get => _commanderName;
        set
        {
            if (SetField(ref _commanderName, value))
            {
                PersistSettings();
            }
        }
    }

    /// <summary>When enabled, watches the Elite Dangerous journal folder and keeps
    /// <see cref="CommanderName"/> in sync with the game, unless <see cref="OverrideUsername"/>
    /// is set.</summary>
    public bool MonitorJournals
    {
        get => _monitorJournals;
        set
        {
            if (SetField(ref _monitorJournals, value))
            {
                PersistSettings();
                if (value)
                {
                    _journalMonitor.Start();
                }
                else
                {
                    _journalMonitor.Stop();
                }
            }
        }
    }

    /// <summary>When set, <see cref="CommanderName"/> is edited manually and journal-detected
    /// names are ignored instead of overwriting it.</summary>
    public bool OverrideUsername
    {
        get => _overrideUsername;
        set
        {
            if (SetField(ref _overrideUsername, value))
            {
                PersistSettings();
            }
        }
    }

    private void PersistSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            CommanderName = _commanderName,
            MonitorJournals = _monitorJournals,
            OverrideUsername = _overrideUsername,
        });
    }

        void Apply()
        {
            if (!OverrideUsername)
            {
                CommanderName = name;
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.BeginInvoke((Action)Apply);
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

    public StationViewModel Stations { get; }

    public JetConeViewModel JetCone { get; }

    public LongExposureViewModel LongExposure { get; }

    public SlitScanViewModel SlitScan { get; }

    public bool HasClaudeApiKey
    {
        get => _hasClaudeApiKey;
        private set => SetField(ref _hasClaudeApiKey, value);
    }

    public void SetClaudeApiKey(string apiKey)
    {
        _secretStore.SetClaudeApiKey(apiKey);
        HasClaudeApiKey = true;
    }

    public void DeleteClaudeApiKey()
    {
        _secretStore.DeleteClaudeApiKey();
        HasClaudeApiKey = false;
    }

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
            AppLog.LogError("RefreshSuggestions", ex);
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
            AppLog.LogError("SystemLookup", ex);
            ErrorMessage = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<HorizontalVideoAnalysisResult> AnalyzeVideoAsync(string videoPath, double? seedPeriodSeconds, IProgress<VideoAnalysisProgress> progress, CancellationToken ct)
        => HorizontalVideoAnalyzer.AnalyzeAsync(videoPath, seedPeriodSeconds, progress, ct);

    public void SaveMeasurement(RingRowViewModel row, HorizontalVideoAnalysisResult result, string videoPath, bool submittedToCanonn = false)
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
            BodyName = ring.BodyName,
            BodyType = ring.BodyType ?? string.Empty,
            BodyMassEarthMasses = ring.BodyMassEarthMasses,
            RingName = ring.RingName,
            RingType = ring.MaterialType,
            RingMassKg = ring.RingMassKg,
            InnerRadius = ring.InnerRadiusMeters,
            OuterRadius = ring.OuterRadiusMeters,
            Width = ring.WidthMeters,
            EstimatedRotationSeconds = ring.EstimatedPeriodSeconds ?? double.NaN,
            ObservedRotationSeconds = result.ObservedPeriodSeconds,
            VideoFilename = Path.GetFileName(videoPath),
            Submitted = submittedToCanonn,
        });
        Measurements.Refresh();
    }

    /// <summary>Submits the measurement currently shown in the results dialog - independent of
    /// whether the user also chooses to save it to history.</summary>
    public Task SubmitMeasurementToCanonnAsync(RingRowViewModel row, HorizontalVideoAnalysisResult result, CancellationToken ct = default)
    {
        var ring = row.Ring;
        return _canonnClient.SubmitAsync(new CanonnSubmission
        {
            CommanderName = CommanderName,
            SystemName = ring.SystemName,
            Id64 = ring.SystemId64,
            X = ring.SystemX,
            Y = ring.SystemY,
            Z = ring.SystemZ,
            BodyName = ring.BodyName,
            BodyType = ring.BodyType,
            BodyRadiusKm = ring.BodyRadiusKm,
            BodyMassEarthMasses = ring.BodyMassEarthMasses,
            RingName = ring.RingName,
            RingType = ring.MaterialType,
            InnerRadiusKm = ring.InnerRadiusMeters / 1000.0,
            OuterRadiusKm = ring.OuterRadiusMeters / 1000.0,
            WidthKm = ring.WidthMeters / 1000.0,
            EstimatedPeriodSeconds = ring.EstimatedPeriodSeconds ?? double.NaN,
            ObservedPeriodSeconds = result.ObservedPeriodSeconds,
        }, ct);
    }

    private Task SubmitRecordToCanonnAsync(MeasurementRecord record, CancellationToken ct)
    {
        return _canonnClient.SubmitAsync(new CanonnSubmission
        {
            CommanderName = CommanderName,
            SystemName = record.SystemName,
            Id64 = record.Id64,
            X = record.X,
            Y = record.Y,
            Z = record.Z,
            BodyName = record.BodyName,
            BodyType = record.BodyType,
            BodyMassEarthMasses = record.BodyMassEarthMasses,
            RingName = record.RingName,
            RingType = record.RingType,
            InnerRadiusKm = record.InnerRadius / 1000.0,
            OuterRadiusKm = record.OuterRadius / 1000.0,
            WidthKm = record.Width / 1000.0,
            EstimatedPeriodSeconds = record.EstimatedRotationSeconds,
            ObservedPeriodSeconds = record.ObservedRotationSeconds,
        }, ct);
    }

    private async Task LoadSubmittedFromCanonnAsync()
    {
        try
        {
            var submitted = await _canonnClient.GetSubmittedMeasurementsAsync().ConfigureAwait(true);
            Measurements.ApplyRemoteSubmittedState(submitted);
        }
        catch (Exception ex)
        {
            AppLog.LogError("LoadCanonnSubmitted", ex);
            // Non-fatal: "already submitted" detection just falls back to the locally tracked flag.
        }
    }

    public void Dispose()
    {
        _spanshClient.Dispose();
        _canonnClient.Dispose();
        _journalMonitor.Dispose();
        Stations.Dispose();
        JetCone.Dispose();
        LongExposure.Dispose();
    }
}
