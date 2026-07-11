using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.App.ViewModels;

/// <summary>Jet Cone Length's counterpart to <see cref="MeasurementsViewModel"/>/
/// <see cref="StationMeasurementsViewModel"/> - same Refresh/Show on Disk behavior, no Canonn
/// column (no submission path for this mode, per spec). <c>jetlength.csv</c> has no timestamp
/// column (it matches the existing NeutronJet tool's schema exactly), so rows are shown in
/// reverse file order (most recently appended first) rather than sorted by a recorded time.</summary>
public sealed class JetLengthMeasurementsViewModel : ObservableObject
{
    private readonly JetLengthCsvStore _store;

    public JetLengthMeasurementsViewModel(JetLengthCsvStore store)
    {
        _store = store;
        RefreshCommand = new RelayCommand(Refresh);
        ShowOnDiskCommand = new RelayCommand(ShowOnDisk);
        Refresh();
    }

    public ObservableCollection<JetLengthMeasurementRowViewModel> Records { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ShowOnDiskCommand { get; }

    public void Refresh()
    {
        Records.Clear();
        foreach (var record in _store.ReadAll().AsEnumerable().Reverse())
        {
            Records.Add(new JetLengthMeasurementRowViewModel(record));
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
