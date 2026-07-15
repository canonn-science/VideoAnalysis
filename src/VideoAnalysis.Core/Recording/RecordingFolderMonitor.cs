namespace VideoAnalysis.Core.Recording;

/// <summary>Watches a user-configured set of folders for newly created video files (e.g. game
/// capture tool output) and reports when each one starts and finishes recording. Mirrors
/// <see cref="Journal.JournalMonitor"/>'s shape: one <see cref="FileSystemWatcher"/> per watched
/// directory, defensive construction (a folder that's missing or unreadable is skipped rather than
/// failing the whole monitor), <c>lock</c>-guarded shared state, and events that fire on whatever
/// thread the watcher/timer raised them on - callers are responsible for marshaling to the UI
/// thread. "Finished recording" isn't a filesystem event on its own, so completion is inferred by
/// polling each in-progress file's length until it's been stable across a few consecutive polls.</summary>
public sealed class RecordingFolderMonitor : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedFile> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _pollInterval;
    private readonly int _stableCyclesRequired;
    private Timer? _pollTimer;
    private List<string> _extensions;

    /// <summary>Raised the moment a new matching file is observed in a watched folder, on
    /// whatever thread the underlying <see cref="FileSystemWatcher"/> event fired on.</summary>
    public event Action<string>? RecordingDetected;

    /// <summary>Raised once a tracked file's size has stopped changing for
    /// <see cref="_stableCyclesRequired"/> consecutive polls, on the polling timer's thread.</summary>
    public event Action<string>? RecordingCompleted;

    /// <param name="extensions">File extensions to watch for, with or without a leading dot
    /// (e.g. "mp4" or ".mp4"). Matching is case-insensitive.</param>
    /// <param name="pollInterval">How often to check in-progress files for completion. Defaults to
    /// 3 seconds; tests pass a shorter interval so they don't wait on real-world timing.</param>
    /// <param name="stableCyclesRequired">Consecutive unchanged-length polls required before a file
    /// is considered finished. Defaults to 2 (~2 poll intervals of no growth).</param>
    public RecordingFolderMonitor(IEnumerable<string> extensions, TimeSpan? pollInterval = null, int? stableCyclesRequired = null)
    {
        _extensions = NormalizeExtensions(extensions);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
        _stableCyclesRequired = stableCyclesRequired ?? 2;
    }

    /// <summary>Replaces both the watched folder set and, if provided, the watched extensions -
    /// disposing any watchers no longer needed and creating new ones - so folder-list or
    /// extension-list changes in Configuration apply immediately without an app restart.</summary>
    public void SetWatchedFolders(IEnumerable<string> folders, IEnumerable<string>? extensions = null)
    {
        lock (_lock)
        {
            if (extensions is not null)
            {
                _extensions = NormalizeExtensions(extensions);
            }

            var desired = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);

            foreach (var stale in _watchers.Keys.Where(w => !desired.Contains(w)).ToList())
            {
                _watchers[stale].Dispose();
                _watchers.Remove(stale);
            }

            foreach (var folder in desired)
            {
                if (_watchers.ContainsKey(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    };
                    watcher.Created += OnFileCreated;
                    watcher.Renamed += OnFileRenamed;
                    watcher.EnableRaisingEvents = true;
                    _watchers[folder] = watcher;
                }
                catch (Exception)
                {
                    // Best-effort: an unwatchable folder (permissions, removable drive unplugged,
                    // etc.) shouldn't prevent the rest from being watched.
                }
            }

            EnsurePollTimer();
        }
    }

    /// <summary>Begins polling an already-in-progress file for completion without requiring a
    /// <see cref="FileSystemWatcher.Created"/> event - used to resume a placeholder library entry
    /// that was still recording when the app last closed.</summary>
    public void TrackExistingFile(string path)
    {
        lock (_lock)
        {
            if (!_tracked.ContainsKey(path))
            {
                _tracked[path] = new TrackedFile();
            }

            EnsurePollTimer();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Created -= OnFileCreated;
                watcher.Renamed -= OnFileRenamed;
                watcher.Dispose();
            }
            _watchers.Clear();
            _tracked.Clear();
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }

    private void EnsurePollTimer()
    {
        _pollTimer ??= new Timer(_ => PollTrackedFiles(), null, _pollInterval, _pollInterval);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e) => HandleCandidate(e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e) => HandleCandidate(e.FullPath);

    private void HandleCandidate(string path)
    {
        if (!MatchesExtension(path, _extensions))
        {
            return;
        }

        lock (_lock)
        {
            if (_tracked.ContainsKey(path))
            {
                return;
            }

            _tracked[path] = new TrackedFile();
        }

        RecordingDetected?.Invoke(path);
    }

    private void PollTrackedFiles()
    {
        List<string>? completed = null;

        lock (_lock)
        {
            foreach (var (path, tracked) in _tracked)
            {
                long length;
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists)
                    {
                        // The file vanished (moved/deleted mid-recording) - stop tracking it
                        // silently rather than reporting a false completion.
                        tracked.PendingRemoval = true;
                        continue;
                    }
                    length = info.Length;
                }
                catch (IOException)
                {
                    // Transient - the recorder may hold a brief exclusive lock; try again next poll.
                    continue;
                }

                if (length == tracked.LastLength && length > 0)
                {
                    tracked.StableCycles++;
                }
                else
                {
                    tracked.StableCycles = 0;
                    tracked.LastLength = length;
                }

                if (tracked.StableCycles >= _stableCyclesRequired)
                {
                    tracked.PendingRemoval = true;
                    (completed ??= new List<string>()).Add(path);
                }
            }

            foreach (var path in _tracked.Where(kv => kv.Value.PendingRemoval).Select(kv => kv.Key).ToList())
            {
                _tracked.Remove(path);
            }
        }

        if (completed is not null)
        {
            foreach (var path in completed)
            {
                RecordingCompleted?.Invoke(path);
            }
        }
    }

    /// <summary>True if <paramref name="path"/>'s extension matches any of <paramref name="extensions"/>,
    /// case-insensitively. Extensions may be supplied with or without a leading dot.</summary>
    public static bool MatchesExtension(string path, IEnumerable<string> extensions)
    {
        var pathExtension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(pathExtension))
        {
            return false;
        }

        return NormalizeExtensions(extensions).Contains(pathExtension.ToLowerInvariant());
    }

    private static List<string> NormalizeExtensions(IEnumerable<string> extensions) =>
        extensions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => (e.StartsWith('.') ? e : "." + e).Trim().ToLowerInvariant())
            .ToList();

    public void Dispose() => Stop();

    private sealed class TrackedFile
    {
        public long LastLength = -1;
        public int StableCycles;
        public bool PendingRemoval;
    }
}
