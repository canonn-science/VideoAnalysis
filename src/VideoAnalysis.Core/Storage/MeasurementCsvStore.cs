using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace VideoAnalysis.Core.Storage;

public sealed class MeasurementCsvStore
{
    public string CsvPath { get; }

    public MeasurementCsvStore(string? csvPath = null)
    {
        CsvPath = csvPath ?? Path.Combine(StoragePaths.Root, "ring_period.csv");

        MigrateLegacyFileName();
        MigrateIfNeeded();
    }

    /// <summary>One-time rename of the file from its pre-rebrand name ("measurements.csv") to the
    /// current "ring_period.csv", now that the app covers multiple analysis modes and needs a
    /// mode-specific file name. A no-op if the legacy file doesn't exist or the current-named
    /// file is already there (never overwrites existing data).</summary>
    private void MigrateLegacyFileName()
    {
        if (File.Exists(CsvPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(CsvPath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var legacyPath = Path.Combine(directory, "measurements.csv");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        File.Move(legacyPath, CsvPath);
    }

    /// <summary>Transparently upgrades a CSV written by an older version of the app (missing any
    /// column up to and including "Body Radius", the newest one) to the current schema, by reading
    /// every row - which already tolerates missing columns via <see cref="ReadAll"/> - and
    /// rewriting the file with the current header. New columns come out empty for pre-existing
    /// rows, which is exactly what <see cref="SaveAll"/> does for a null/default field. Keying off
    /// the newest column (rather than the oldest missing one, e.g. "Body Type") means a file that's
    /// missing only "Body Radius" - because it was written after "Body Type" existed but before
    /// "Body Radius" did - still gets migrated.</summary>
    private void MigrateIfNeeded()
    {
        if (!File.Exists(CsvPath))
        {
            return;
        }

        string? headerLine;
        using (var reader = new StreamReader(CsvPath))
        {
            headerLine = reader.ReadLine();
        }

        if (headerLine is null || headerLine.Contains("Body Radius", StringComparison.Ordinal))
        {
            return;
        }

        SaveAll(ReadAll());
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
