using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Canonn;
using RotationAnalysis.Core.Diagnostics;
using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.App.ViewModels;

public sealed class MeasurementsViewModel : ObservableObject
{
    private readonly MeasurementCsvStore _store;
    private readonly Func<MeasurementRecord, CancellationToken, Task> _submitToCanonn;
    private readonly Func<string> _getCommanderName;
    private IReadOnlyList<CanonnSubmittedMeasurement> _remoteSubmitted = Array.Empty<CanonnSubmittedMeasurement>();

    public MeasurementsViewModel(
        MeasurementCsvStore store,
        Func<MeasurementRecord, CancellationToken, Task> submitToCanonn,
        Func<string> getCommanderName)
    {
        _store = store;
        _submitToCanonn = submitToCanonn;
        _getCommanderName = getCommanderName;
        RefreshCommand = new RelayCommand(Refresh);
        ShowOnDiskCommand = new RelayCommand(ShowOnDisk);
        Refresh();
    }

    public ObservableCollection<MeasurementRowViewModel> Records { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ShowOnDiskCommand { get; }

    /// <summary>Raised when a "Send to Canonn" action on a row fails; the view shows this in a dialog.</summary>
    public event Action<string>? SubmissionFailed;

    public void Refresh()
    {
        Records.Clear();
        foreach (var record in _store.ReadAll().OrderByDescending(r => r.Timestamp))
        {
            Records.Add(new MeasurementRowViewModel(record, SendToCanonnAsync));
        }
        ApplyRemoteSubmittedState(_remoteSubmitted);
    }

    /// <summary>Applies "already submitted" state from Canonn's published TSV. Rows already
    /// flagged locally (this app submitted them itself) are left alone - the remote check only
    /// ever adds matches, since the TSV can lag behind a submission that just happened.</summary>
    public void ApplyRemoteSubmittedState(IReadOnlyList<CanonnSubmittedMeasurement> submitted)
    {
        _remoteSubmitted = submitted;
        var commanderName = _getCommanderName();
        foreach (var row in Records)
        {
            if (!row.IsSubmitted && CanonnMatcher.IsSubmitted(row.Record, commanderName, submitted))
            {
                row.IsSubmitted = true;
            }
        }
    }

    private async Task SendToCanonnAsync(MeasurementRowViewModel row, CancellationToken ct)
    {
        row.IsSubmitting = true;
        row.SubmitError = null;
        try
        {
            await _submitToCanonn(row.Record, ct).ConfigureAwait(true);
            row.IsSubmitted = true;
            _store.SaveAll(Records.Select(r => r.Record));
        }
        catch (Exception ex)
        {
            AppLog.LogError("SubmitMeasurementToCanonn", ex);
            row.SubmitError = $"Send failed: {ex.Message}";
            // The row's error only shows up on hover, which is easy to miss, so also surface it
            // as a dialog - the view subscribes to this the same way it does VideoSelectionRequested.
            SubmissionFailed?.Invoke(row.SubmitError);
        }
        finally
        {
            row.IsSubmitting = false;
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
