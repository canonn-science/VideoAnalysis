using System.Globalization;
using CsvHelper;

namespace RotationAnalysis.Core.Storage;

public sealed class MeasurementCsvStore
{
    public string CsvPath { get; }

    public MeasurementCsvStore(string? csvPath = null)
    {
        CsvPath = csvPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RotationAnalysisLab",
            "measurements.csv");
    }

    public void Append(MeasurementRecord record)
    {
        var directory = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool writeHeader = !File.Exists(CsvPath);
        using var stream = new FileStream(CsvPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        if (writeHeader)
        {
            csv.WriteHeader<MeasurementRecord>();
            csv.NextRecord();
        }

        csv.WriteRecord(record);
        csv.NextRecord();
    }

    public List<MeasurementRecord> ReadAll()
    {
        if (!File.Exists(CsvPath))
        {
            return new List<MeasurementRecord>();
        }

        using var reader = new StreamReader(CsvPath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<MeasurementRecord>().ToList();
    }
}
