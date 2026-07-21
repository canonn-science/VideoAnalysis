using System.Text;
using System.Text.Json;

namespace VideoAnalysis.Core.Journal;

/// <summary>Watches the Elite Dangerous journal directory for the active commander's name and
/// current location (system, body, docked station): an initial read of the most recent journal
/// file on <see cref="Start"/>, then live updates as the game appends to that file or starts a
/// new one each session. Only ever reads complete newline-terminated lines, so a session file
/// the game is mid-write to is picked up correctly on the next change notification rather than
/// having a half-written JSON line permanently skipped.</summary>
public sealed class JournalMonitor : IDisposable
{
    private const string JournalFilePattern = "Journal.*.log";

    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private string? _currentFilePath;
    private long _readOffset;
    private string? _lastKnownSystemName;
    private long? _lastKnownSystemId64;
    private string? _lastKnownBodyName;
    private string? _lastKnownStationName;
    private string? _lastKnownStationType;

    public string JournalDirectory { get; }

    /// <summary>The most recently observed current system, from <c>Location</c>, <c>FSDJump</c>,
    /// or <c>CarrierJump</c> events. Not currently consumed anywhere in the UI - exposed for future
    /// features (e.g. auto-filling a tab's system search) to read without needing to have
    /// subscribed to <see cref="SystemLocationChanged"/> before it first fired.</summary>
    public string? LastKnownSystemName
    {
        get { lock (_lock) { return _lastKnownSystemName; } }
    }

    /// <summary>The id64 (Elite's <c>SystemAddress</c>) of <see cref="LastKnownSystemName"/>, from
    /// the same <c>Location</c>/<c>FSDJump</c>/<c>CarrierJump</c> events - lets callers look a
    /// system up on Spansh by id64 directly instead of depending on its (sometimes laggy) name
    /// typeahead index.</summary>
    public long? LastKnownSystemId64
    {
        get { lock (_lock) { return _lastKnownSystemId64; } }
    }

    /// <summary>The most recently observed current body, from <c>ApproachBody</c>/<c>SupercruiseExit</c>
    /// events. Same "read without needing to have subscribed first" rationale as <see cref="LastKnownSystemName"/>.</summary>
    public string? LastKnownBodyName
    {
        get { lock (_lock) { return _lastKnownBodyName; } }
    }

    /// <summary>The most recently observed docked station name, from a <c>Docked</c> event -
    /// cleared (set to null) by an <c>Undocked</c> event.</summary>
    public string? LastKnownStationName
    {
        get { lock (_lock) { return _lastKnownStationName; } }
    }

    /// <summary>The most recently observed docked station type, from a <c>Docked</c> event -
    /// cleared alongside <see cref="LastKnownStationName"/>.</summary>
    public string? LastKnownStationType
    {
        get { lock (_lock) { return _lastKnownStationType; } }
    }

    /// <summary>Raised with the most recently observed commander name, on whatever thread the
    /// underlying <see cref="FileSystemWatcher"/> event fired on.</summary>
    public event Action<string>? CommanderNameChanged;

    /// <summary>Raised with the most recently observed current system name, on whatever thread the
    /// underlying <see cref="FileSystemWatcher"/> event fired on.</summary>
    public event Action<string>? SystemLocationChanged;

    /// <summary>Raised with the most recently observed current body name, on whatever thread the
    /// underlying <see cref="FileSystemWatcher"/> event fired on.</summary>
    public event Action<string>? BodyLocationChanged;

    /// <summary>Raised with the currently docked station name, or null when an <c>Undocked</c>
    /// event clears it, on whatever thread the underlying <see cref="FileSystemWatcher"/> event
    /// fired on.</summary>
    public event Action<string?>? StationChanged;

    public JournalMonitor(string? journalDirectory = null)
    {
        JournalDirectory = journalDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "Frontier Developments", "Elite Dangerous");
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_watcher is not null)
            {
                return;
            }

            if (!Directory.Exists(JournalDirectory))
            {
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(JournalDirectory, JournalFilePattern)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                };
                _watcher.Changed += OnJournalChanged;
                _watcher.Created += OnJournalCreated;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                // Best-effort: journal monitoring is an optional convenience, not a hard
                // dependency the rest of the app relies on.
                _watcher?.Dispose();
                _watcher = null;
                return;
            }
        }

        try
        {
            var latest = FindLatestJournalFile();
            if (latest is not null)
            {
                ReadFile(latest, forceSwitch: true);
            }
        }
        catch (Exception)
        {
            // Live updates from the watcher can still work even if this initial scan fails.
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_watcher is not null)
            {
                _watcher.Changed -= OnJournalChanged;
                _watcher.Created -= OnJournalCreated;
                _watcher.Dispose();
                _watcher = null;
            }

            _currentFilePath = null;
            _readOffset = 0;
        }
    }

    private void OnJournalCreated(object sender, FileSystemEventArgs e) => ReadFile(e.FullPath, forceSwitch: true);

    private void OnJournalChanged(object sender, FileSystemEventArgs e) => ReadFile(e.FullPath, forceSwitch: false);

    private string? FindLatestJournalFile()
    {
        return Directory.EnumerateFiles(JournalDirectory, JournalFilePattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    private void ReadFile(string path, bool forceSwitch)
    {
        string? lastName = null;
        string? lastSystem = null;
        long? lastSystemId64 = null;
        string? lastBody = null;
        var stationTouched = false;
        string? stationName = null;
        string? stationType = null;

        lock (_lock)
        {
            if (forceSwitch || _currentFilePath is null)
            {
                _currentFilePath = path;
                _readOffset = 0;
            }
            else if (!string.Equals(_currentFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                // A change notification for a journal file we're not currently tracking
                // (e.g. an older session file being touched) - ignore it.
                return;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var offset = Math.Min(_readOffset, stream.Length);
                stream.Seek(offset, SeekOrigin.Begin);

                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);

                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0)
                {
                    return;
                }

                var completeText = text[..(lastNewline + 1)];
                _readOffset = offset + Encoding.UTF8.GetByteCount(completeText);

                foreach (var line in completeText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedLine = line.TrimEnd('\r');
                    if (trimmedLine.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(trimmedLine);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("event", out var eventProp))
                        {
                            continue;
                        }

                        var eventName = eventProp.GetString();
                        switch (eventName)
                        {
                            case "Commander" when root.TryGetProperty("Name", out var nameProp):
                                lastName = nameProp.GetString() ?? lastName;
                                break;
                            case "LoadGame" when root.TryGetProperty("Commander", out var commanderProp):
                                lastName = commanderProp.GetString() ?? lastName;
                                break;
                            case "Location" or "FSDJump" or "CarrierJump" when root.TryGetProperty("StarSystem", out var systemProp):
                                lastSystem = systemProp.GetString() ?? lastSystem;
                                if (root.TryGetProperty("SystemAddress", out var addressProp) && addressProp.TryGetInt64(out var address))
                                {
                                    lastSystemId64 = address;
                                }
                                break;
                            case "ApproachBody" or "SupercruiseExit" when root.TryGetProperty("Body", out var bodyProp):
                                lastBody = bodyProp.GetString() ?? lastBody;
                                break;
                            case "Docked":
                                stationTouched = true;
                                stationName = root.TryGetProperty("StationName", out var snProp) ? snProp.GetString() : null;
                                stationType = root.TryGetProperty("StationType", out var stProp) ? stProp.GetString() : null;
                                break;
                            case "Undocked":
                                stationTouched = true;
                                stationName = null;
                                stationType = null;
                                break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed lines.
                    }
                }

                if (lastSystem is not null)
                {
                    _lastKnownSystemName = lastSystem;
                    _lastKnownSystemId64 = lastSystemId64;
                }

                if (lastBody is not null)
                {
                    _lastKnownBodyName = lastBody;
                }

                if (stationTouched)
                {
                    _lastKnownStationName = stationName;
                    _lastKnownStationType = stationType;
                }
            }
            catch (IOException)
            {
                // The game may hold a brief exclusive lock while rotating files; skip this
                // update; the next FileSystemWatcher event will retry.
            }
        }

        if (lastName is not null)
        {
            CommanderNameChanged?.Invoke(lastName);
        }

        if (lastSystem is not null)
        {
            SystemLocationChanged?.Invoke(lastSystem);
        }

        if (lastBody is not null)
        {
            BodyLocationChanged?.Invoke(lastBody);
        }

        if (stationTouched)
        {
            StationChanged?.Invoke(stationName);
        }
    }

    public static string? TryExtractCommanderName(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp))
            {
                return null;
            }

            return eventProp.GetString() switch
            {
                "Commander" when root.TryGetProperty("Name", out var nameProp) => nameProp.GetString(),
                "LoadGame" when root.TryGetProperty("Commander", out var commanderProp) => commanderProp.GetString(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? TryExtractSystemName(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp))
            {
                return null;
            }

            var eventName = eventProp.GetString();
            if (eventName is not ("Location" or "FSDJump" or "CarrierJump"))
            {
                return null;
            }

            return root.TryGetProperty("StarSystem", out var systemProp) ? systemProp.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Same event filter as <see cref="TryExtractSystemName"/>, but for the accompanying
    /// <c>SystemAddress</c> (the system's id64) - lets Spansh lookups skip its name typeahead index.</summary>
    public static long? TryExtractSystemId64(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp))
            {
                return null;
            }

            var eventName = eventProp.GetString();
            if (eventName is not ("Location" or "FSDJump" or "CarrierJump"))
            {
                return null;
            }

            return root.TryGetProperty("SystemAddress", out var addressProp) && addressProp.TryGetInt64(out var address)
                ? address
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? TryExtractBodyName(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp))
            {
                return null;
            }

            var eventName = eventProp.GetString();
            if (eventName is not ("ApproachBody" or "SupercruiseExit"))
            {
                return null;
            }

            return root.TryGetProperty("Body", out var bodyProp) ? bodyProp.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static (string? StationName, string? StationType)? TryExtractDockedStation(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp) || eventProp.GetString() != "Docked")
            {
                return null;
            }

            var stationName = root.TryGetProperty("StationName", out var nameProp) ? nameProp.GetString() : null;
            var stationType = root.TryGetProperty("StationType", out var typeProp) ? typeProp.GetString() : null;
            return (stationName, stationType);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsUndockedEvent(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            return root.TryGetProperty("event", out var eventProp) && eventProp.GetString() == "Undocked";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose() => Stop();
}
