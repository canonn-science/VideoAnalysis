using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Canonn;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Domain;
using VideoAnalysis.Core.Journal;
using VideoAnalysis.Core.Recording;
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
    private readonly JetConeCanonnClient _jetConeCanonnClient = new();
    private readonly MeasurementCsvStore _measurementStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly SecretStore _secretStore = new();
    private readonly JournalMonitor _journalMonitor = new();
    private readonly VideoLibraryStore _videoLibraryStore = new();
    private readonly RecordingFolderMonitor _recordingMonitor;

    private string _systemQuery = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _resolvedSystemName;
    private string _commanderName;
    private bool _monitorJournals;
    private bool _overrideUsername;
    private bool _organizeRenamedVideosBySystem;
    private string? _longExposureOutputDirectory;
    private bool _hasClaudeApiKey;
    private VideoLibraryEntryViewModel? _activeLibraryVideo;
    private bool _monitorVideoFolders;
    private List<string> _watchedVideoExtensions;
    private string _watchedVideoExtensionsText;
    private bool _promptOnNewRecording;
    private bool _autoAddWithoutPrompting;
    private bool _showRecordingBadge;

    public MainViewModel()
    {
        IsFirstRun = !_settingsStore.SettingsFileExists;
        var settings = _settingsStore.Load();
        _commanderName = settings.CommanderName ?? DefaultCommanderName;
        _monitorJournals = settings.MonitorJournals;
        _overrideUsername = settings.OverrideUsername;
        _organizeRenamedVideosBySystem = settings.OrganizeRenamedVideosBySystem;
        _longExposureOutputDirectory = settings.LongExposureOutputDirectory;
        _monitorVideoFolders = settings.MonitorVideoFolders;
        _watchedVideoExtensions = settings.WatchedVideoExtensions.Count > 0
            ? settings.WatchedVideoExtensions
            : new List<string> { ".mp4", ".mkv", ".mov" };
        _watchedVideoExtensionsText = string.Join(", ", _watchedVideoExtensions);
        _promptOnNewRecording = settings.PromptOnNewRecording;
        _autoAddWithoutPrompting = settings.AutoAddWithoutPrompting;
        _showRecordingBadge = settings.ShowRecordingBadge;

        // Null means defaults haven't been seeded yet - discover whatever capture-tool folders
        // actually exist on this machine and persist them below, so later launches don't need to
        // re-discover (and so a user who deliberately clears the list stays empty).
        var seedingDefaults = settings.WatchedVideoFolders is null;
        var initialFolders = settings.WatchedVideoFolders ?? DefaultWatchFolders.Discover();
        foreach (var folder in initialFolders)
        {
            WatchedVideoFolders.Add(CreateWatchedFolderRow(folder));
        }

        AddWatchedFolderCommand = new RelayCommand(() => AddWatchedFolderRequested?.Invoke());

        Measurements = new MeasurementsViewModel(_measurementStore, SubmitRecordToCanonnAsync, () => CommanderName);
        Stations = new StationViewModel();
        JetCone = new JetConeViewModel(SubmitJetLengthRecordsToCanonnAsync);
        LongExposure = new LongExposureViewModel();
        SlitScan = new SlitScanViewModel();
        VideoLibrary = new VideoLibraryViewModel(_videoLibraryStore, () => ShowRecordingBadge);
        VideoLibrary.EntrySelected += OnLibraryEntrySelected;
        _hasClaudeApiKey = _secretStore.TryGetClaudeApiKey(out _);
        _journalMonitor.CommanderNameChanged += OnJournalCommanderNameChanged;
        if (_monitorJournals)
        {
            _journalMonitor.Start();
        }

        _recordingMonitor = new RecordingFolderMonitor(_watchedVideoExtensions);
        _recordingMonitor.RecordingDetected += OnRecordingDetected;
        _recordingMonitor.RecordingCompleted += OnRecordingCompleted;
        if (_monitorVideoFolders)
        {
            StartRecordingMonitor();
        }

        if (seedingDefaults)
        {
            PersistSettings();
        }

        _ = LoadSubmittedFromCanonnAsync();
    }

    public string SystemQuery
    {
        get => _systemQuery;
        set => SetField(ref _systemQuery, value);
    }

    /// <summary>True when no settings file existed at startup - used to send the user to the
    /// Configuration tab first, since nothing has been set up yet.</summary>
    public bool IsFirstRun { get; }

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

    /// <summary>When enabled, a rename accepted from the "Add to Video Library" dialog moves the
    /// file into a subfolder named after its system, instead of leaving it alongside the original.</summary>
    public bool OrganizeRenamedVideosBySystem
    {
        get => _organizeRenamedVideosBySystem;
        set
        {
            if (SetField(ref _organizeRenamedVideosBySystem, value))
            {
                PersistSettings();
            }
        }
    }

    /// <summary>Null means "use the default Pictures\RotationAnalysisLab\LongExposure folder" -
    /// updated whenever a Long Exposure save goes somewhere other than the currently suggested
    /// folder, so later saves default there instead of reverting back to the original default.</summary>
    public string? LongExposureOutputDirectory
    {
        get => _longExposureOutputDirectory;
        set
        {
            if (SetField(ref _longExposureOutputDirectory, value))
            {
                PersistSettings();
            }
        }
    }

    /// <summary>Master toggle for the whole feature - the Configuration checkbox. Starting/
    /// stopping the monitor takes effect immediately, same as <see cref="MonitorJournals"/>.</summary>
    public bool MonitorVideoFolders
    {
        get => _monitorVideoFolders;
        set
        {
            if (SetField(ref _monitorVideoFolders, value))
            {
                PersistSettings();
                if (value)
                {
                    StartRecordingMonitor();
                }
                else
                {
                    _recordingMonitor.Stop();
                }
            }
        }
    }

    /// <summary>The folders watched for new recordings, in display/priority order (order is
    /// cosmetic only - every folder is watched regardless of position). Adding, removing, or
    /// reordering takes effect immediately via <see cref="RestartRecordingMonitorIfEnabled"/>.</summary>
    public ObservableCollection<WatchedFolderRowViewModel> WatchedVideoFolders { get; } = new();

    /// <summary>Comma-separated extensions bound to the Configuration textbox - parsed to/from
    /// <see cref="AppSettings.WatchedVideoExtensions"/> on every edit.</summary>
    public string WatchedVideoExtensionsText
    {
        get => _watchedVideoExtensionsText;
        set
        {
            if (SetField(ref _watchedVideoExtensionsText, value))
            {
                _watchedVideoExtensions = value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                PersistSettings();
                RestartRecordingMonitorIfEnabled();
            }
        }
    }

    public bool PromptOnNewRecording
    {
        get => _promptOnNewRecording;
        set
        {
            if (SetField(ref _promptOnNewRecording, value))
            {
                PersistSettings();
            }
        }
    }

    public bool AutoAddWithoutPrompting
    {
        get => _autoAddWithoutPrompting;
        set
        {
            if (SetField(ref _autoAddWithoutPrompting, value))
            {
                PersistSettings();
            }
        }
    }

    public bool ShowRecordingBadge
    {
        get => _showRecordingBadge;
        set
        {
            if (SetField(ref _showRecordingBadge, value))
            {
                PersistSettings();
                foreach (var entry in VideoLibrary.Entries)
                {
                    entry.NotifyEntryChanged();
                }
            }
        }
    }

    /// <summary>Opens a folder picker (handled by the view) and, on confirmation, adds the chosen
    /// folder via <see cref="AddWatchedFolder"/>.</summary>
    public event Action? AddWatchedFolderRequested;

    public RelayCommand AddWatchedFolderCommand { get; }

    public void AddWatchedFolder(string path)
    {
        if (WatchedVideoFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        WatchedVideoFolders.Add(CreateWatchedFolderRow(path));
        PersistSettings();
        RestartRecordingMonitorIfEnabled();
    }

    private WatchedFolderRowViewModel CreateWatchedFolderRow(string path) =>
        new(path, RemoveWatchedFolder, MoveWatchedFolderUp, MoveWatchedFolderDown);

    private void RemoveWatchedFolder(WatchedFolderRowViewModel row)
    {
        WatchedVideoFolders.Remove(row);
        PersistSettings();
        RestartRecordingMonitorIfEnabled();
    }

    private void MoveWatchedFolderUp(WatchedFolderRowViewModel row)
    {
        var index = WatchedVideoFolders.IndexOf(row);
        if (index > 0)
        {
            WatchedVideoFolders.Move(index, index - 1);
            PersistSettings();
        }
    }

    private void MoveWatchedFolderDown(WatchedFolderRowViewModel row)
    {
        var index = WatchedVideoFolders.IndexOf(row);
        if (index >= 0 && index < WatchedVideoFolders.Count - 1)
        {
            WatchedVideoFolders.Move(index, index + 1);
            PersistSettings();
        }
    }

    private void StartRecordingMonitor()
    {
        _recordingMonitor.SetWatchedFolders(WatchedVideoFolders.Select(f => f.Path), _watchedVideoExtensions);

        // Resume any placeholder left "Recording…" by a previous session that never saw it
        // finish (e.g. the app was closed mid-recording) - the monitor picks up polling it for
        // completion again without needing a fresh Created event.
        foreach (var entry in _videoLibraryStore.GetAll().Where(e => e.IsRecording))
        {
            _recordingMonitor.TrackExistingFile(entry.FilePath);
        }
    }

    private void RestartRecordingMonitorIfEnabled()
    {
        if (_monitorVideoFolders)
        {
            _recordingMonitor.SetWatchedFolders(WatchedVideoFolders.Select(f => f.Path), _watchedVideoExtensions);
        }
    }

    /// <summary>Raised (off the UI thread) when the monitor sees a new matching file and
    /// <see cref="PromptOnNewRecording"/> is on - the view shows a non-blocking notification;
    /// accepting it should call <see cref="VideoLibraryViewModel.AddPlaceholder"/>.</summary>
    public event Action<string>? RecordingPromptRequested;

    /// <summary>Raised (off the UI thread) once a recording finishes and its entry still has no
    /// system tagged - the view shows a non-blocking "tag it now?" notification.</summary>
    public event Action<VideoLibraryEntryViewModel>? RecordingFinalizedPromptRequested;

    private void OnRecordingDetected(string path)
    {
        void Apply()
        {
            if (_videoLibraryStore.FindByPath(path) is not null)
            {
                // Already known (e.g. a resumed placeholder, or a watcher edge-case duplicate) -
                // nothing new to prompt for.
                return;
            }

            if (AutoAddWithoutPrompting)
            {
                VideoLibrary.AddPlaceholder(path);
            }
            else if (PromptOnNewRecording)
            {
                RecordingPromptRequested?.Invoke(path);
            }
        }

        RunOnUiThread(Apply);
    }

    private void OnRecordingCompleted(string path)
    {
        async void Apply()
        {
            var entry = VideoLibrary.Entries.FirstOrDefault(e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (entry is null || !entry.IsRecording)
            {
                return;
            }

            await VideoLibrary.MarkRecordingCompleteAsync(entry).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(entry.Entry.SystemName))
            {
                RecordingFinalizedPromptRequested?.Invoke(entry);
            }
        }

        RunOnUiThread(Apply);
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private void PersistSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            CommanderName = _commanderName,
            MonitorJournals = _monitorJournals,
            OverrideUsername = _overrideUsername,
            OrganizeRenamedVideosBySystem = _organizeRenamedVideosBySystem,
            LongExposureOutputDirectory = _longExposureOutputDirectory,
            MonitorVideoFolders = _monitorVideoFolders,
            WatchedVideoFolders = WatchedVideoFolders.Select(f => f.Path).ToList(),
            WatchedVideoExtensions = _watchedVideoExtensions,
            PromptOnNewRecording = _promptOnNewRecording,
            AutoAddWithoutPrompting = _autoAddWithoutPrompting,
            ShowRecordingBadge = _showRecordingBadge,
        });
    }

    private void OnJournalCommanderNameChanged(string name)
    {
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

    public ObservableCollection<string> BodyNames { get; } = new();

    /// <summary>The ring/belt dropdown's actual choices - all of <see cref="Rings"/>, or just
    /// those belonging to <see cref="SelectedBodyName"/> once one is picked.</summary>
    public ObservableCollection<RingRowViewModel> RingChoices { get; } = new();

    public MeasurementsViewModel Measurements { get; }

    public StationViewModel Stations { get; }

    public JetConeViewModel JetCone { get; }

    public LongExposureViewModel LongExposure { get; }

    public SlitScanViewModel SlitScan { get; }

    public VideoLibraryViewModel VideoLibrary { get; }

    /// <summary>The library video currently active for analysis, or null if none is selected /
    /// its file has gone missing since selection. Ring Rotation's Analyze button uses this
    /// directly, gated by <see cref="CanAnalyzeRing"/> until it (and a ring) are both set.</summary>
    public VideoLibraryEntryViewModel? ActiveLibraryVideo
    {
        get => _activeLibraryVideo;
        private set
        {
            if (SetField(ref _activeLibraryVideo, value))
            {
                OnPropertyChanged(nameof(CanAnalyzeRing));
            }
        }
    }

    /// <summary>Exposed so the upload metadata modal can read current journal-derived values
    /// (system/body/station) to pre-fill from, without routing every value through this view model.</summary>
    public JournalMonitor JournalMonitor => _journalMonitor;

    /// <summary>Exposed so the upload metadata modal can reuse the same Spansh client rather than
    /// opening a second one.</summary>
    public SpanshClient SpanshClient => _spanshClient;

    private RingRowViewModel? _selectedRing;
    private string? _selectedBodyName;

    /// <summary>Bound to the Body dropdown - holds <see cref="RingRowViewModel.BodyDisplay"/>
    /// (name + subtype, e.g. "Eorl Scrua AA-A h670 2 (Neutron Star)"), not the bare name, so
    /// bodies sharing a name but not a type stay distinguishable. Narrows <see cref="RingChoices"/>
    /// to that body's own rings/belts - the user picks a specific ring from there, or
    /// <see cref="SelectedRing"/> fixes this back to match if they pick a ring directly instead.</summary>
    public string? SelectedBodyName
    {
        get => _selectedBodyName;
        set
        {
            if (_selectedBodyName == value)
            {
                return;
            }

            _selectedBodyName = value;
            OnPropertyChanged();

            // Clear the ring selection *before* rebuilding the filtered list below if it no
            // longer belongs to the newly chosen body - so there's never a moment where the
            // bound ComboBox's SelectedItem points at a ring that isn't (yet) in RingChoices.
            if (SelectedRing is not null && SelectedRing.BodyDisplay != value)
            {
                SelectedRing = null;
            }

            RingChoices.Clear();
            foreach (var ring in Rings.Where(r => value is null || r.BodyDisplay == value))
            {
                RingChoices.Add(ring);
            }
        }
    }

    /// <summary>Bound to the Ring / Belt dropdown's selection. Auto-set after a library video with
    /// a known <c>RingName</c> resolves its system, so the matching option is picked instead of
    /// requiring the user to find it themselves.</summary>
    public RingRowViewModel? SelectedRing
    {
        get => _selectedRing;
        set
        {
            if (SetField(ref _selectedRing, value))
            {
                if (value is not null)
                {
                    // Picking a ring resolves whatever prompted the user in the first place (no
                    // ring tagged, a stale/mismatched one, ...) - don't leave that message lingering.
                    ErrorMessage = null;

                    // Selecting a ring fixes the body choice to match it - set the field/notify
                    // directly rather than going through the SelectedBodyName setter, since that
                    // would rebuild (Clear + re-Add) RingChoices and risk momentarily dropping
                    // the very selection just made.
                    if (_selectedBodyName != value.BodyDisplay)
                    {
                        _selectedBodyName = value.BodyDisplay;
                        OnPropertyChanged(nameof(SelectedBodyName));
                    }
                }

                OnPropertyChanged(nameof(CanAnalyzeRing));
                OnPropertyChanged(nameof(HasSelectedRing));
            }
        }
    }

    /// <summary>Whether the selected ring's details summary should be shown.</summary>
    public bool HasSelectedRing => SelectedRing is not null;

    /// <summary>Analyzing requires both a non-missing library video and a resolved ring - the
    /// Analyze button is disabled until both are in place instead of only failing with an error
    /// message (or falling back to a file picker) after the fact.</summary>
    public bool CanAnalyzeRing => ActiveLibraryVideo is { IsFileMissing: false } && SelectedRing is not null;

    /// <summary>Called once a library entry has actually been removed, so every tab that may have
    /// cached its path (Ring Rotation via <see cref="ActiveLibraryVideo"/>, Station/Jet Cone/Long
    /// Exposure/Slit Scan via their own <c>VideoFilePath</c>) drops it instead of quietly keeping a
    /// removed - and possibly now-deleted - file as its active selection.</summary>
    public void ClearRemovedVideo(VideoLibraryEntryViewModel removed)
    {
        if (ActiveLibraryVideo == removed)
        {
            ActiveLibraryVideo = null;
        }

        if (string.Equals(Stations.VideoFilePath, removed.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            Stations.VideoFilePath = null;
        }

        if (string.Equals(JetCone.VideoFilePath, removed.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            JetCone.VideoFilePath = null;
        }

        if (string.Equals(LongExposure.VideoFilePath, removed.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            LongExposure.VideoFilePath = null;
        }

        if (string.Equals(SlitScan.VideoFilePath, removed.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            SlitScan.VideoFilePath = null;
        }
    }

    private void OnLibraryEntrySelected(VideoLibraryEntryViewModel entry)
    {
        ActiveLibraryVideo = entry;
        if (entry.Entry.SystemId64 is long id64)
        {
            var syntheticSystem = new SpanshSearchSystem
            {
                Id64 = id64,
                Name = entry.Entry.SystemName ?? string.Empty,
                X = entry.Entry.SystemX ?? 0,
                Y = entry.Entry.SystemY ?? 0,
                Z = entry.Entry.SystemZ ?? 0,
            };
            SystemQuery = syntheticSystem.Name;
            _ = SubmitAsync(syntheticSystem);
        }
else
{
    // Clear any previously resolved system/ring state so Analyze stays gated off until the user
    // resolves the system for this new, untagged video.
    SelectedRing = null;
    SelectedBodyName = null;
    Rings.Clear();
    BodyNames.Clear();
    RingChoices.Clear();
    ResolvedSystemName = null;

    ErrorMessage = "This video isn't tagged with a system - search for one above, then pick its ring below.";
}
    }

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
        BodyNames.Clear();
        RingChoices.Clear();
        SelectedRing = null;
        if (_selectedBodyName is not null)
        {
            _selectedBodyName = null;
            OnPropertyChanged(nameof(SelectedBodyName));
        }
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
                var row = new RingRowViewModel(ring);
                Rings.Add(row);
                RingChoices.Add(row);
                if (!BodyNames.Contains(row.BodyDisplay))
                {
                    BodyNames.Add(row.BodyDisplay);
                }
            }

            if (rings.Count == 0)
            {
                ErrorMessage = $"\"{resolved.Name}\" has no rings or belts.";
            }
            else
            {
                var wantedRingName = ActiveLibraryVideo?.Entry.RingName;
                var wantedBodyName = ActiveLibraryVideo?.Entry.BodyName;
                if (wantedRingName is not null)
                {
                    SelectedRing = Rings.FirstOrDefault(r => r.Ring.RingName == wantedRingName);
                }
                else if (wantedBodyName is not null && Rings.FirstOrDefault(r => r.BodyName == wantedBodyName) is { } wantedBodyRow)
                {
                    // No specific ring tagged, but the body is known - narrow the choices down to
                    // it and let the user pick the exact ring themselves.
                    SelectedBodyName = wantedBodyRow.BodyDisplay;
                }

                // A measurement can't be analyzed without a selected ring - prompt for one now
                // rather than letting the user discover it's missing only after loading a video.
                if (SelectedRing is null)
                {
                    ErrorMessage = wantedRingName is null
                        ? "This video isn't tagged with a ring - pick the one it shows from the dropdown below."
                        : $"This video's tagged ring (\"{wantedRingName}\") wasn't found in {resolved.Name} - pick the correct one from the dropdown below.";
                }
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
            BodyRadiusKm = ring.BodyRadiusKm,
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
            BodyRadiusKm = record.BodyRadiusKm,
            RingName = record.RingName,
            RingType = record.RingType,
            InnerRadiusKm = record.InnerRadius / 1000.0,
            OuterRadiusKm = record.OuterRadius / 1000.0,
            WidthKm = record.Width / 1000.0,
            EstimatedPeriodSeconds = record.EstimatedRotationSeconds,
            ObservedPeriodSeconds = record.ObservedRotationSeconds,
        }, ct);
    }

    private Task SubmitJetLengthRecordsToCanonnAsync(IReadOnlyList<JetLengthRecord> records, CancellationToken ct)
        => _jetConeCanonnClient.SubmitAsync(records, CommanderName, ct);

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
        _jetConeCanonnClient.Dispose();
        _journalMonitor.Dispose();
        _recordingMonitor.Dispose();
        Stations.Dispose();
        JetCone.Dispose();
    }
}
