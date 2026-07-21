using System.Collections.ObjectModel;
using System.IO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Domain;
using VideoAnalysis.Core.Journal;
using VideoAnalysis.Core.Spansh;
using VideoAnalysis.Core.Spansh.Models;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Backs the video library's upload metadata modal: a Spansh system typeahead (same
/// pattern as the main tabs), followed by body/ring/station pickers populated from that system's
/// full dump once resolved. Body/station default from the current journal-derived location when
/// available (captured once, at construction) but stay freely editable - the user's choice always
/// wins, this is just a convenience prefill, not a live-updating binding.</summary>
public sealed class VideoUploadMetadataViewModel : ObservableObject
{
    private const string UnknownPlaceholder = "N/A";

    private readonly SpanshClient _spanshClient;
    private readonly JournalMonitor? _journalMonitor;
    private readonly List<(string Name, string? Type)> _stationOptions = new();

    private string _systemQuery = string.Empty;
    private SpanshSearchSystem? _selectedSystem;
    private SpanshDumpResponse? _dump;
    private string? _selectedBodyName;
    private string? _selectedRingName;
    private string? _selectedStationName;
    private bool _isBusy;
    private string? _errorMessage;

    public VideoUploadMetadataViewModel(
        SpanshClient spanshClient,
        JournalMonitor? journalMonitor = null,
        string? prefillSystemName = null,
        long? prefillSystemId64 = null,
        double? prefillSystemX = null,
        double? prefillSystemY = null,
        double? prefillSystemZ = null,
        string? prefillBodyName = null,
        string? prefillRingName = null)
    {
        _spanshClient = spanshClient;
        _journalMonitor = journalMonitor;

        if (prefillSystemName is not null && prefillSystemId64 is long id64)
        {
            _selectedSystem = new SpanshSearchSystem
            {
                Id64 = id64,
                Name = prefillSystemName,
                X = prefillSystemX ?? 0,
                Y = prefillSystemY ?? 0,
                Z = prefillSystemZ ?? 0,
            };
            _systemQuery = prefillSystemName;
            _selectedBodyName = prefillBodyName;
            _selectedRingName = prefillRingName;
            _ = LoadDumpAsync(_selectedSystem, resetSelections: false);
        }
        else if (journalMonitor?.LastKnownSystemName is { } liveSystemName)
        {
            if (journalMonitor.LastKnownSystemId64 is long liveId64)
            {
                // The journal's SystemAddress gives us the id64 directly - go straight to the
                // Spansh dump lookup instead of the name typeahead, which can lag behind for a
                // system visited (or renamed) very recently. Coordinates get filled in from the
                // dump itself once LoadDumpAsync completes.
                _selectedSystem = new SpanshSearchSystem { Id64 = liveId64, Name = liveSystemName };
                _systemQuery = liveSystemName;
                _ = LoadDumpAsync(_selectedSystem, resetSelections: false);
            }
            else
            {
                // No id64 available - fall back to text-only prefill; it still needs resolving
                // via typeahead/submit, same as the main tabs' system search.
                _systemQuery = liveSystemName;
            }
        }

        if (_selectedBodyName is null)
        {
            _selectedBodyName = journalMonitor?.LastKnownBodyName;
        }

        _selectedStationName = journalMonitor?.LastKnownStationName;
    }

    public string SystemQuery
    {
        get => _systemQuery;
        set
        {
            if (SetField(ref _systemQuery, value))
            {
                OnPropertyChanged(nameof(ResolvedSystemName));
                OnPropertyChanged(nameof(SuggestedFileBaseName));
            }
        }
    }

    public string? SelectedBodyName
    {
        get => _selectedBodyName;
        set
        {
            if (SetField(ref _selectedBodyName, value))
            {
                OnPropertyChanged(nameof(BodyNameDisplay));
                OnPropertyChanged(nameof(SuggestedFileBaseName));
                UpdateRingOptions(resetSelection: true);
            }
        }
    }

    public string? SelectedRingName
    {
        get => _selectedRingName;
        set
        {
            if (SetField(ref _selectedRingName, value))
            {
                OnPropertyChanged(nameof(SuggestedFileBaseName));
            }
        }
    }

    public string? SelectedStationName
    {
        get => _selectedStationName;
        set
        {
            if (SetField(ref _selectedStationName, value))
            {
                OnPropertyChanged(nameof(StationNameDisplay));
            }
        }
    }

    /// <summary>What the Body combo box actually shows: the real value once known, or a literal
    /// "N/A" placeholder while it isn't - typing over the placeholder (or leaving it as-is)
    /// clears/keeps <see cref="SelectedBodyName"/> null rather than persisting the literal text.
    /// Picking an item from the dropdown hands WPF's editable ComboBox back that item's full
    /// "Name (Type)" <see cref="BodyOption.Display"/> text (there's no separate "value" for an
    /// editable ComboBox's selection, only its Text) - resolved back to the plain body name here
    /// so ring lookups and the persisted <see cref="VideoLibraryEntry.BodyName"/> keep matching a
    /// real Spansh body name instead of the decorated label. Free-typed text that doesn't match a
    /// known option's display is kept as-is.</summary>
    public string BodyNameDisplay
    {
        get => string.IsNullOrWhiteSpace(SelectedBodyName) ? UnknownPlaceholder : SelectedBodyName;
        set
        {
            if (IsPlaceholder(value))
            {
                SelectedBodyName = null;
                return;
            }

            var matched = BodyOptions.FirstOrDefault(o => string.Equals(o.Display, value, StringComparison.Ordinal));
            SelectedBodyName = matched?.Name ?? value;
        }
    }

    /// <summary>Same placeholder behavior as <see cref="BodyNameDisplay"/>, for the Station combo box.</summary>
    public string StationNameDisplay
    {
        get => string.IsNullOrWhiteSpace(SelectedStationName) ? UnknownPlaceholder : SelectedStationName;
        set => SelectedStationName = IsPlaceholder(value) ? null : value;
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), UnknownPlaceholder, StringComparison.OrdinalIgnoreCase);

    public bool HasRingOptions => RingNames.Count > 0;

    /// <summary>The system name as currently resolved (Spansh match) or, failing that, whatever's
    /// typed into the search box - used both for the video library entry and for naming a
    /// system-organized folder when renaming the uploaded file.</summary>
    public string? ResolvedSystemName => _selectedSystem?.Name ?? (SystemQuery.Trim().Length > 0 ? SystemQuery.Trim() : null);

    /// <summary>The name a rename-to-match suggestion should use: the most specific of ring/body/
    /// system that's currently populated - mirroring the same "name the file after the ring"
    /// convention <see cref="VideoAnalysis.Core.Storage.VideoFileNamer"/> already uses for the Ring
    /// Rotation completion flow, generalized to fall back to body or system when a video isn't
    /// ring-specific (e.g. Station Rotation, or a system with no rings at all).</summary>
    public string? SuggestedFileBaseName =>
        !string.IsNullOrWhiteSpace(SelectedRingName) ? SelectedRingName :
        !string.IsNullOrWhiteSpace(SelectedBodyName) ? SelectedBodyName :
        ResolvedSystemName;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public ObservableCollection<SpanshSearchSystem> Suggestions { get; } = new();

    public ObservableCollection<BodyOption> BodyOptions { get; } = new();

    public ObservableCollection<string> RingNames { get; } = new();

    public ObservableCollection<string> StationNames { get; } = new();

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
            AppLog.LogError("VideoUploadMetadataSuggestions", ex);
            ErrorMessage = $"Search failed: {ex.Message}";
        }
    }

    /// <summary>Tries to spot a system name in the uploaded file's name (see
    /// <see cref="FilenameSystemMatcher"/>) and, if found, pre-fills the system search - and its
    /// body/ring/station options - the same way a manual Spansh lookup would. Only call this when
    /// no other prefill (a Ring Rotation row, or the journal's last-known system) has already
    /// claimed a stronger answer; a filename match found here simply overrides that fallback.
    /// If the filename doesn't give up a system, falls back to <see cref="TryDetectSystemFromJournalHistoryAsync"/>.</summary>
    public async Task TryDetectSystemFromFilenameAsync(string filePath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = await _spanshClient.TryFindSystemInFilenameAsync(fileName).ConfigureAwait(true);
            if (match is not null)
            {
                SetSelectedSystem(match);
                SystemQuery = match.Name;
                await LoadDumpAsync(match, resetSelections: true).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoUploadMetadataFilenameMatch", ex);
        }

        await TryDetectSystemFromJournalHistoryAsync(filePath).ConfigureAwait(true);
    }

    /// <summary>Falls back to the file's creation time: replays journal history (see
    /// <see cref="JournalHistoryLookup"/>) to find where the commander was at that moment, and
    /// pre-fills the system/body/station from that, the same way a filename match would. Purely
    /// best-effort - a missing journal directory, no journal coverage for that time, or a
    /// system Spansh doesn't recognize just leaves the fields for the user to fill in by hand.</summary>
    public async Task TryDetectSystemFromJournalHistoryAsync(string filePath)
    {
        if (_journalMonitor is null)
        {
            return;
        }

        DateTime createdUtc;
        try
        {
            createdUtc = File.GetCreationTimeUtc(filePath);
        }
        catch (IOException)
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(() => JournalHistoryLookup.FindLocationAt(_journalMonitor.JournalDirectory, createdUtc))
                .ConfigureAwait(true);
            if (snapshot?.SystemName is not { } systemName)
            {
                return;
            }

            SpanshSearchSystem? resolved;
            if (snapshot.SystemId64 is long historyId64)
            {
                // Same reasoning as the live prefill above: the journal's own SystemAddress
                // skips the name typeahead's lag entirely.
                resolved = new SpanshSearchSystem { Id64 = historyId64, Name = systemName };
            }
            else
            {
                var response = await _spanshClient.SearchSystemsAsync(systemName).ConfigureAwait(true);
                resolved = response.MinMax.FirstOrDefault(s => string.Equals(s.Name, systemName, StringComparison.OrdinalIgnoreCase));
            }

            if (resolved is null)
            {
                return;
            }

            SetSelectedSystem(resolved);
            SystemQuery = resolved.Name;
            await LoadDumpAsync(resolved, resetSelections: true).ConfigureAwait(true);

            if (snapshot.BodyName is not null && BodyOptions.Any(o => o.Name == snapshot.BodyName))
            {
                SelectedBodyName = snapshot.BodyName;
            }

            if (snapshot.StationName is not null && StationNames.Contains(snapshot.StationName))
            {
                SelectedStationName = snapshot.StationName;
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoUploadMetadataJournalHistoryMatch", ex);
        }
    }

    public async Task SelectSystemAsync(SpanshSearchSystem? chosenSystem)
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

            IsBusy = true;
            try
            {
                var response = await _spanshClient.SearchSystemsAsync(query).ConfigureAwait(true);
                resolved = response.MinMax.FirstOrDefault(s => string.Equals(s.Name, query, StringComparison.OrdinalIgnoreCase));
                if (resolved is null)
                {
                    ErrorMessage = $"System \"{query}\" not found.";
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.LogError("VideoUploadMetadataSystemLookup", ex);
                ErrorMessage = $"Lookup failed: {ex.Message}";
                return;
            }
            finally
            {
                IsBusy = false;
            }
        }

        SetSelectedSystem(resolved);
        SystemQuery = resolved.Name;
        await LoadDumpAsync(resolved, resetSelections: true).ConfigureAwait(true);
    }

    private void SetSelectedSystem(SpanshSearchSystem system)
    {
        _selectedSystem = system;
        OnPropertyChanged(nameof(ResolvedSystemName));
        OnPropertyChanged(nameof(SuggestedFileBaseName));
    }

    /// <summary>Builds the library entry to persist, using whatever system/body/ring/station the
    /// user ended up with (resolved via Spansh, or just free-typed text, or the initial prefill).</summary>
    public VideoLibraryEntry BuildEntry(string filePath)
    {
        return new VideoLibraryEntry
        {
            FilePath = filePath,
            SystemName = _selectedSystem?.Name ?? (SystemQuery.Trim().Length > 0 ? SystemQuery.Trim() : null),
            SystemId64 = _selectedSystem?.Id64,
            SystemX = _selectedSystem?.X,
            SystemY = _selectedSystem?.Y,
            SystemZ = _selectedSystem?.Z,
            BodyName = string.IsNullOrWhiteSpace(SelectedBodyName) ? null : SelectedBodyName,
            RingName = string.IsNullOrWhiteSpace(SelectedRingName) ? null : SelectedRingName,
            StationName = string.IsNullOrWhiteSpace(SelectedStationName) ? null : SelectedStationName,
            StationType = _stationOptions.FirstOrDefault(o => o.Name == SelectedStationName).Type,
        };
    }

    private async Task LoadDumpAsync(SpanshSearchSystem system, bool resetSelections)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _dump = await _spanshClient.GetDumpAsync(system.Id64).ConfigureAwait(true);
            BodyOptions.Clear();
            if (_dump is not null)
            {
                // The dump is the authoritative source for this id64 - sync name/coords from it
                // rather than whatever placeholder the caller passed in (e.g. a journal-sourced
                // system built with X/Y/Z defaulted to 0 pending this lookup).
                _selectedSystem = new SpanshSearchSystem
                {
                    Id64 = system.Id64,
                    Name = _dump.System.Name,
                    X = _dump.System.Coords.X,
                    Y = _dump.System.Coords.Y,
                    Z = _dump.System.Coords.Z,
                };
                OnPropertyChanged(nameof(ResolvedSystemName));
                OnPropertyChanged(nameof(SuggestedFileBaseName));

                foreach (var body in _dump.System.Bodies)
                {
                    BodyOptions.Add(new BodyOption(body.Name, body.SubType ?? body.Type));
                }
            }

            if (resetSelections)
            {
                // A body/ring/station picked for a *different* system is meaningless here (and,
                // left in place, could still coincidentally match one of this system's own body
                // names) - clear it before re-deriving ring/station options from the new dump.
                SelectedBodyName = null;
            }

            UpdateStationOptions(resetSelections);
            UpdateRingOptions(resetSelections);
        }
        catch (Exception ex)
        {
            AppLog.LogError("VideoUploadMetadataDumpLookup", ex);
            ErrorMessage = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateStationOptions(bool resetSelection)
    {
        _stationOptions.Clear();
        StationNames.Clear();
        if (resetSelection)
        {
            SelectedStationName = null;
        }

        if (_dump is null)
        {
            return;
        }

        foreach (var station in _dump.System.Stations ?? new List<SpanshStation>())
        {
            AddStationOption(station);
        }

        foreach (var body in _dump.System.Bodies)
        {
            foreach (var station in body.Stations ?? new List<SpanshStation>())
            {
                AddStationOption(station);
            }
        }
    }

    private void AddStationOption(SpanshStation station)
    {
        _stationOptions.Add((station.Name, station.Type));
        StationNames.Add(station.Name);
    }

    private void UpdateRingOptions(bool resetSelection)
    {
        RingNames.Clear();
        if (resetSelection)
        {
            SelectedRingName = null;
        }

        var body = _dump?.System.Bodies.FirstOrDefault(b => b.Name == SelectedBodyName);
        if (body is not null)
        {
            foreach (var ring in body.Rings ?? new List<SpanshRingOrBelt>())
            {
                RingNames.Add(ring.Name);
            }

            foreach (var belt in body.Belts ?? new List<SpanshRingOrBelt>())
            {
                RingNames.Add(belt.Name);
            }
        }

        OnPropertyChanged(nameof(HasRingOptions));
    }
}
