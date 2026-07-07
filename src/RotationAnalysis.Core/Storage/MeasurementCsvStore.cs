using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

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

    /// <summary>Reads every existing row and rewrites the file with the new one appended.
    /// Measurement counts are small enough (one per recorded video) that this is simpler than
    /// maintaining a separate append path, and it means schema changes never leave the header
    /// out of sync with older rows written by a previous version of the app.</summary>
    public void Append(MeasurementRecord record)
    {
        var records = ReadAll();
        records.Add(record);
        SaveAll(records);
    }

    public void SaveAll(IEnumerable<MeasurementRecord> records)
    {
        var directory = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(CsvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteHeader<MeasurementRecord>();
        csv.NextRecord();
        foreach (var record in records)
        {
            csv.WriteRecord(record);
            csv.NextRecord();
        }
    }

    public List<MeasurementRecord> ReadAll()
    {
        if (!File.Exists(CsvPath))
        {
            return new List<MeasurementRecord>();
        }

        // Tolerate CSV files written by an older version of the app with fewer columns
        // (e.g. before "Body Name"/"submitted" existed) instead of throwing on the mismatch.
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
        };
        using var reader = new StreamReader(CsvPath);
        using var csv = new CsvReader(reader, config);
        return csv.GetRecords<MeasurementRecord>().ToList();
    }
}
