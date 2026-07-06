using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using RotationAnalysis.App.Infrastructure;
using RotationAnalysis.Core.Storage;

namespace RotationAnalysis.App.ViewModels;

public sealed class MeasurementsViewModel : ObservableObject
{
    private readonly MeasurementCsvStore _store;

    public MeasurementsViewModel(MeasurementCsvStore store)
    {
        _store = store;
        RefreshCommand = new RelayCommand(Refresh);
        ShowOnDiskCommand = new RelayCommand(ShowOnDisk);
        Refresh();
    }

    public ObservableCollection<MeasurementRowViewModel> Records { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ShowOnDiskCommand { get; }

    public void Refresh()
    {
        Records.Clear();
        foreach (var record in _store.ReadAll().OrderByDescending(r => r.Timestamp))
        {
            Records.Add(new MeasurementRowViewModel(record));
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
