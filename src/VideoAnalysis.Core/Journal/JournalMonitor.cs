using System.Text;
using System.Text.Json;

namespace VideoAnalysis.Core.Journal;

/// <summary>Watches the Elite Dangerous journal directory for the active commander's name and
/// current system location: an initial read of the most recent journal file on <see cref="Start"/>,
/// then live updates as the game appends to that file or starts a new one each session. Only ever
/// reads complete newline-terminated lines, so a session file the game is mid-write to is picked up
/// correctly on the next change notification rather than having a half-written JSON line
/// permanently skipped.</summary>
public sealed class JournalMonitor : IDisposable
{
    private const string JournalFilePattern = "Journal.*.log";

    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private string? _currentFilePath;
    private long _readOffset;
    private string? _lastKnownSystemName;

    public string JournalDirectory { get; }

    /// <summary>The most recently observed current system, from <c>Location</c>, <c>FSDJump</c>,
    /// or <c>CarrierJump</c> events. Not currently consumed anywhere in the UI - exposed for future
    /// features (e.g. auto-filling a tab's system search) to read without needing to have
    /// subscribed to <see cref="SystemLocationChanged"/> before it first fired.</summary>
    public string? LastKnownSystemName
    {
        get { lock (_lock) { return _lastKnownSystemName; } }
    }

    /// <summary>Raised with the most recently observed commander name, on whatever thread the
    /// underlying <see cref="FileSystemWatcher"/> event fired on.</summary>
    public event Action<string>? CommanderNameChanged;

    /// <summary>Raised with the most recently observed current system name, on whatever thread the
    /// underlying <see cref="FileSystemWatcher"/> event fired on.</summary>
    public event Action<string>? SystemLocationChanged;

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

                    var name = TryExtractCommanderName(trimmedLine);
                    if (name is not null)
                    {
                        lastName = name;
                    }

                    var system = TryExtractSystemName(trimmedLine);
                    if (system is not null)
                    {
                        lastSystem = system;
                    }
                }

                if (lastSystem is not null)
                {
                    _lastKnownSystemName = lastSystem;
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

    public void Dispose() => Stop();
}
