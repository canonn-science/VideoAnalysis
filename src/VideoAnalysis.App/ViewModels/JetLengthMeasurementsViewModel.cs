using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using VideoAnalysis.App.Infrastructure;
using VideoAnalysis.Core.Diagnostics;
using VideoAnalysis.Core.Storage;

namespace VideoAnalysis.App.ViewModels;

/// <summary>Jet Cone Length's counterpart to <see cref="MeasurementsViewModel"/> - same
/// Refresh/Show on Disk/Send to Canonn behavior (per-row and batched across selected rows).
/// <c>jetlength.csv</c> has no timestamp column (it matches the existing NeutronJet tool's schema),
/// so rows are shown in reverse file order (most recently appended first) rather than sorted by a
/// recorded time.</summary>
public sealed class JetLengthMeasurementsViewModel : ObservableObject
{
    private readonly JetLengthCsvStore _store;
    private readonly Func<IReadOnlyList<JetLengthRecord>, CancellationToken, Task> _submitToCanonn;

    public JetLengthMeasurementsViewModel(
        JetLengthCsvStore store,
        Func<IReadOnlyList<JetLengthRecord>, CancellationToken, Task> submitToCanonn)
    {
        _store = store;
        _submitToCanonn = submitToCanonn;
        RefreshCommand = new RelayCommand(Refresh);
        ShowOnDiskCommand = new RelayCommand(ShowOnDisk);
        SendSelectedToCanonnCommand = new RelayCommand(
            async () => await SendSelectedToCanonnAsync(CancellationToken.None),
            () => Records.Any(r => r.IsSelected && r.CanSend));
        Refresh();
    }

    public ObservableCollection<JetLengthMeasurementRowViewModel> Records { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ShowOnDiskCommand { get; }
    public RelayCommand SendSelectedToCanonnCommand { get; }

    /// <summary>Raised when a "Send to Canonn" action fails; the view shows this in a dialog.</summary>
    public event Action<string>? SubmissionFailed;

    public void Refresh()
    {
        Records.Clear();
        foreach (var record in _store.ReadAll().AsEnumerable().Reverse())
        {
            Records.Add(new JetLengthMeasurementRowViewModel(record, SendOneToCanonnAsync));
        }
    }

    private async Task SendOneToCanonnAsync(JetLengthMeasurementRowViewModel row, CancellationToken ct)
    {
        await SendToCanonnAsync(new[] { row }, ct).ConfigureAwait(true);
    }

    private async Task SendSelectedToCanonnAsync(CancellationToken ct)
    {
        var rows = Records.Where(r => r.IsSelected && r.CanSend).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        await SendToCanonnAsync(rows, ct).ConfigureAwait(true);
    }

    /// <summary>Submits one or more rows as a single batched request, per Canonn's preference for
    /// history submissions. All rows in the batch move together: on success every row is marked
    /// submitted, on failure every row shows the same error (there's no per-row result in the
    /// endpoint's response to attribute a partial failure to one row over another).</summary>
    private async Task SendToCanonnAsync(IReadOnlyList<JetLengthMeasurementRowViewModel> rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            row.IsSubmitting = true;
            row.SubmitError = null;
        }

        try
        {
            await _submitToCanonn(rows.Select(r => r.Record).ToList(), ct).ConfigureAwait(true);
            foreach (var row in rows)
            {
                row.IsSubmitted = true;
            }
            _store.SaveAll(Records.Select(r => r.Record).Reverse());
        }
        catch (Exception ex)
        {
            AppLog.LogError("SubmitJetLengthMeasurementToCanonn", ex);
            var error = $"Send failed: {ex.Message}";
            foreach (var row in rows)
            {
                row.SubmitError = error;
            }
            SubmissionFailed?.Invoke(error);
        }
        finally
        {
            foreach (var row in rows)
            {
                row.IsSubmitting = false;
            }
        }
    }

    private void ShowOnDisk()
    {
        var directory = Path.GetDirectoryName(_store.CsvPath)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(_store.CsvPath))
        {
            Process.Start("explorer.exe", $"/select,\"{_store.CsvPath}\"");
        }
        else
        {
            Process.Start("explorer.exe", $"\"{directory}\"");
        }
    }
}
