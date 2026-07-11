using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace RotationAnalysis.Core.Storage;

/// <summary>Parallel to <see cref="MeasurementCsvStore"/>/<see cref="StationMeasurementCsvStore"/>,
/// writing <c>jetlength.csv</c> with the same column set as the existing standalone
/// <c>S:\Canonn\NeutronJet\jetlength.csv</c> tool.</summary>
public sealed class JetLengthCsvStore
{
    public string CsvPath { get; }

    public JetLengthCsvStore(string? csvPath = null)
    {
        CsvPath = csvPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RotationAnalysisLab",
            "jetlength.csv");
    }

    public void Append(JetLengthRecord record)
    {
        var records = ReadAll();
        records.Add(record);
        SaveAll(records);
    }

    public void SaveAll(IEnumerable<JetLengthRecord> records)
    {
        var directory = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(CsvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteHeader<JetLengthRecord>();
        csv.NextRecord();
        foreach (var record in records)
        {
            csv.WriteRecord(record);
            csv.NextRecord();
        }
    }

    public List<JetLengthRecord> ReadAll()
    {
        if (!File.Exists(CsvPath))
        {
            return new List<JetLengthRecord>();
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
        };
        using var reader = new StreamReader(CsvPath);
        using var csv = new CsvReader(reader, config);
        return csv.GetRecords<JetLengthRecord>().ToList();
    }
}
